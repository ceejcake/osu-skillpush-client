// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Input;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osuTK;

namespace osu.Game.Rulesets.Osu.UI
{
    /// <summary>
    /// Works out whether a missed circle was missed because the aim was lagging behind it or had already moved on from it.
    /// </summary>
    /// <remarks>
    /// This is about timing rather than position: it looks at when the cursor was on the circle, compared to when the circle was
    /// pressed for (or should have been, if it wasn't). Where the cursor was relative to the circle doesn't matter, so overshooting
    /// a circle at the right time isn't mistaken for being ahead, and moving on towards the next object early is, whichever way
    /// that object is.
    /// </remarks>
    public partial class AimMissDetector : Component
    {
        /// <summary>
        /// How close (in circle radii from its centre) the cursor has to come to a circle it never touched for when it came closest
        /// to count. Any further away and it was aiming somewhere else entirely.
        /// </summary>
        private const float near_distance = 2;

        /// <summary>
        /// How long the cursor's path is remembered for.
        /// </summary>
        private const double history_length = 3000;

        private readonly Func<OsuHitObject, OsuHitObject?> getPreviousObject;
        private readonly Func<Vector2, Vector2> toPlayfieldSpace;

        private readonly List<(double time, Vector2 position)> cursorHistory = new List<(double, Vector2)>();

        private readonly Dictionary<HitObject, double> misaimedPresses = new Dictionary<HitObject, double>();

        private InputManager? inputManager;

        /// <param name="getPreviousObject">Gets the object which has to be played before a circle, if there is one.</param>
        /// <param name="toPlayfieldSpace">Converts a screen space position to playfield coordinates.</param>
        public AimMissDetector(Func<OsuHitObject, OsuHitObject?> getPreviousObject, Func<Vector2, Vector2> toPlayfieldSpace)
        {
            this.getPreviousObject = getPreviousObject;
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
        /// Remembers when a press which missed a circle happened, which is when the player meant to hit it.
        /// </summary>
        public void AddMisaimedPress(HitObject hitObject) => misaimedPresses.TryAdd(hitObject, Time.Current);

        /// <summary>
        /// Works out why a circle was missed.
        /// </summary>
        /// <returns>Which way the aim was off, or null if the miss wasn't because of aim lagging behind or being ahead.</returns>
        public AimError? Classify(OsuHitObject circle)
        {
            double targetTime = misaimedPresses.Remove(circle, out double pressTime) ? pressTime : circle.StartTime;

            // Missed by pressing too early, which is down to tapping rather than aim.
            if (Time.Current < circle.StartTime)
                return null;

            // Only the path since the previous object, so that passing over this circle on the way to an earlier one doesn't count.
            double from = getPreviousObject(circle)?.GetEndTime() ?? double.MinValue;
            var path = cursorHistory.Where(h => h.time >= from).ToArray();

            double tolerance = circle.HitWindows?.WindowFor(HitResult.Great) ?? 0;

            return Classify(path, circle.StackedPosition, (float)circle.Radius, targetTime, tolerance);
        }

        /// <summary>
        /// Works out whether the cursor reached a circle too late or had already left it, from the path it took.
        /// </summary>
        /// <param name="path">Where the cursor was over time, in order.</param>
        /// <param name="circlePosition">Where the circle is.</param>
        /// <param name="radius">The radius of the circle.</param>
        /// <param name="targetTime">When the circle was pressed for, or should have been.</param>
        /// <param name="tolerance">How far off (in milliseconds) the cursor can be and still count as on time.</param>
        /// <returns>Which way the aim was off, or null if it was on time (so the miss was just off target) or nowhere near the circle.</returns>
        public static AimError? Classify(IReadOnlyList<(double time, Vector2 position)> path, Vector2 circlePosition, float radius, double targetTime, double tolerance)
        {
            if (path.Count == 0)
                return null;

            float[] distances = path.Select(p => Vector2.Distance(p.position, circlePosition)).ToArray();
            float closestDistance = distances.Min();

            if (closestDistance > radius * near_distance)
            {
                // Still on its way there when the circle was missed, or aiming somewhere else entirely.
                return isApproaching(path, distances) ? AimError.Behind : null;
            }

            // When the cursor was on the circle, or if it never was, when it came closest (which can be for a while if it stopped short).
            float onTargetDistance = closestDistance <= radius ? radius : closestDistance + 1;

            // Of those times, the one closest to when the circle should have been hit.
            double onTargetTime = path.Where((_, i) => distances[i] <= onTargetDistance)
                                      .Select(p => p.time)
                                      .MinBy(t => Math.Abs(t - targetTime));

            double offset = onTargetTime - targetTime;

            if (Math.Abs(offset) <= tolerance)
                return null;

            return offset > 0 ? AimError.Behind : AimError.Ahead;
        }

        /// <summary>
        /// Whether the cursor was still getting closer to the circle at the end of the path.
        /// </summary>
        private static bool isApproaching(IReadOnlyList<(double time, Vector2 position)> path, float[] distances)
        {
            const double approach_check_duration = 50;

            int last = path.Count - 1;
            int earlier = last;

            while (earlier > 0 && path[earlier].time > path[last].time - approach_check_duration)
                earlier--;

            return distances[last] < distances[earlier] - 1;
        }
    }
}
