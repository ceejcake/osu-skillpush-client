// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Input;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.UI;
using osuTK;

namespace osu.Game.Rulesets.Osu.UI
{
    /// <summary>
    /// Works out whether a missed circle was missed because the aim was lagging behind it or going past it.
    /// </summary>
    /// <remarks>
    /// The cursor position used is where a misaimed press landed if there was one, and otherwise where the cursor was at the
    /// moment the circle should have been hit. It is judged along the direction the cursor was travelling to reach the circle.
    /// </remarks>
    public partial class AimMissDetector : Component
    {
        /// <summary>
        /// How much further along the direction of travel than sideways the cursor has to be off, for a miss to count as lagging
        /// behind or going past. A miss which is mostly sideways is neither.
        /// </summary>
        private const float minimum_along_share = 0.75f;

        /// <summary>
        /// How long the cursor's path is remembered for.
        /// </summary>
        private const double history_length = 3000;

        private readonly Func<OsuHitObject, Vector2?> getApproachDirection;
        private readonly Func<Vector2, Vector2> toPlayfieldSpace;

        private readonly List<(double time, Vector2 position)> cursorHistory = new List<(double, Vector2)>();

        private readonly Dictionary<HitObject, Vector2> misaimedPresses = new Dictionary<HitObject, Vector2>();

        private InputManager? inputManager;

        /// <param name="getApproachDirection">Gets the direction the cursor travels in to reach a circle, if known.</param>
        /// <param name="toPlayfieldSpace">Converts a screen space position to playfield coordinates.</param>
        public AimMissDetector(Func<OsuHitObject, Vector2?> getApproachDirection, Func<Vector2, Vector2> toPlayfieldSpace)
        {
            this.getApproachDirection = getApproachDirection;
            this.toPlayfieldSpace = toPlayfieldSpace;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            inputManager = GetContainingInputManager();
        }

        protected override void Update()
        {
            base.Update();

            if (inputManager == null)
                return;

            double time = Time.Current;

            // Rewinding (seeking, or a replay going backwards) makes the remembered path wrong.
            if (cursorHistory.Count > 0 && time < cursorHistory[^1].time)
            {
                cursorHistory.Clear();
                misaimedPresses.Clear();
            }

            cursorHistory.Add((time, toPlayfieldSpace(inputManager.CurrentState.Mouse.Position)));

            int expired = cursorHistory.FindIndex(h => h.time >= time - history_length);

            if (expired > 0)
                cursorHistory.RemoveRange(0, expired);
        }

        /// <summary>
        /// Remembers where a press which missed a circle landed, which is a better guess at where the player was aiming than
        /// wherever the cursor happens to be when the circle is eventually missed.
        /// </summary>
        public void AddMisaimedPress(HitObject hitObject, Vector2 screenSpacePosition) => misaimedPresses.TryAdd(hitObject, toPlayfieldSpace(screenSpacePosition));

        /// <summary>
        /// Works out why a circle was missed.
        /// </summary>
        /// <returns>Which way the aim was off, or null if the miss wasn't because of aim lagging behind or going past.</returns>
        public AimError? Classify(OsuHitObject circle)
        {
            Vector2? position = misaimedPresses.Remove(circle, out var pressPosition) ? pressPosition : cursorPositionAt(circle.StartTime);

            if (position == null || getApproachDirection(circle) is not Vector2 direction)
                return null;

            return Classify(circle.StackedPosition, (float)circle.Radius, position.Value, direction);
        }

        /// <summary>
        /// Works out which way the cursor was off a circle, along the direction it was travelling.
        /// </summary>
        /// <returns>Which way the aim was off, or null if the cursor was on the circle or mostly off to the side of it.</returns>
        public static AimError? Classify(Vector2 circlePosition, float radius, Vector2 cursorPosition, Vector2 direction)
        {
            Vector2 offset = cursorPosition - circlePosition;

            if (offset.Length <= radius)
                return null;

            float along = Vector2.Dot(offset, direction);
            float sideways = Math.Abs(offset.X * direction.Y - offset.Y * direction.X);

            if (Math.Abs(along) < sideways * minimum_along_share)
                return null;

            return along < 0 ? AimError.Behind : AimError.Ahead;
        }

        private Vector2? cursorPositionAt(double time)
        {
            if (cursorHistory.Count == 0)
                return null;

            return cursorHistory.MinBy(h => Math.Abs(h.time - time)).position;
        }
    }
}
