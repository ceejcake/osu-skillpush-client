// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Beatmaps;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.UI;
using osuTK;

namespace osu.Game.Rulesets.Osu.Mods
{
    /// <summary>
    /// Mod that spreads the <see cref="HitObject"/>s of a beatmap across the playfield, by scaling up the movement
    /// between patterns much more than the movement inside them.
    /// </summary>
    public class OsuModCrossScreen : Mod, IApplicableToBeatmap, IHasSpacingAdjust
    {
        public override string Name => "Cross Screen";
        public override string Acronym => "XS";
        public override LocalisableString Description => "Use the whole screen. Jumps get bigger, patterns stay intact.";
        public override ModType Type => ModType.Conversion;
        public override IconUsage? Icon => FontAwesome.Solid.ExpandArrowsAlt;

        public override Type[] IncompatibleMods => new[] { typeof(OsuModRandom), typeof(OsuModTargetPractice) };

        /// <summary>
        /// The largest gap between two objects, as a fraction of a beat, for them to still be considered part of the same
        /// pattern. Covers 1/4 and 1/3 rhythms, but not 1/2.
        /// </summary>
        private const double pattern_gap_in_beats = 0.375;

        /// <summary>
        /// Bounds applied to the gap derived from <see cref="pattern_gap_in_beats"/>, for beatmaps with extreme tempos.
        /// </summary>
        private const double minimum_pattern_gap = 60;

        private const double maximum_pattern_gap = 250;

        /// <summary>
        /// The largest movement between two objects for them to still be considered part of the same pattern.
        /// Movements longer than this are jumps, even when played at stream speed.
        /// </summary>
        private const float pattern_distance_threshold = 130;

        /// <summary>
        /// The distance below which two objects are considered stacked, and are therefore always kept together.
        /// </summary>
        private const float stack_distance_threshold = 3;


        /// <summary>
        /// The length a movement is scaled towards, but never past. Roughly the longest jump the playfield can hold.
        /// </summary>
        private const float maximum_movement = 460;

        /// <summary>
        /// The share of a movement which has to survive being fitted into the playfield for its direction to be kept
        /// exactly. Below this, the movement is turned by up to <see cref="fallback_rotation_degrees"/> instead.
        /// </summary>
        private const float acceptable_length_share = 0.5f;

        private const int fallback_rotation_degrees = 30;

        private const int rotation_step_degrees = 5;

        [SettingSource("Spacing intensity", "How much the spacing between objects is multiplied by", SettingControlType = typeof(MultiplierSettingsSlider))]
        public BindableNumber<double> SpacingIntensity { get; } = new BindableDouble(1.5)
        {
            MinValue = 1,
            MaxValue = 3,
            Precision = 0.05,
        };

        /// <summary>
        /// How much of the spacing increase is applied inside a pattern. A pattern is played as a single movement of the
        /// cursor, so stretching it as much as the jumps between patterns would make it unplayable at its original tempo.
        /// </summary>
        [SettingSource("Pattern spread", "How much of the spacing increase also applies inside patterns such as streams and triples",
            SettingControlType = typeof(SettingsPercentageSlider<double>))]
        public BindableNumber<double> PatternSpread { get; } = new BindableDouble(0.35)
        {
            MinValue = 0,
            MaxValue = 1,
            Precision = 0.05,
        };

        BindableNumber<double> IHasSpacingAdjust.SpacingChange => SpacingIntensity;

        public void ApplyToBeatmap(IBeatmap beatmap)
        {
            if (beatmap is not OsuBeatmap osuBeatmap)
                return;

            // Avoid touching the objects at all when nothing would change.
            if (SpacingIntensity.Value == SpacingIntensity.MinValue)
                return;

            var hitObjects = osuBeatmap.HitObjects;

            if (hitObjects.Count == 0)
                return;

            // Positions are read from these snapshots, since objects are moved in place as we go.
            var originalStartPositions = hitObjects.Select(h => h.Position).ToArray();
            var originalEndPositions = hitObjects.Select(h => h.EndPosition).ToArray();

            float intensity = (float)SpacingIntensity.Value;
            float intraPatternFactor = 1 + (float)((SpacingIntensity.Value - 1) * PatternSpread.Value);

            foreach ((int start, int end) in getPatterns(osuBeatmap, originalStartPositions, originalEndPositions))
            {
                // Spinners are always centred, and moving them would achieve nothing.
                if (hitObjects[start] is Spinner)
                    continue;

                // The pattern is laid out on its own first, so that the playfield can never reshape it.
                Vector2[] offsets = layOutPattern(hitObjects, originalStartPositions, originalEndPositions, start, end, intraPatternFactor);
                (Vector2 minimum, Vector2 maximum) = getBounds(hitObjects, offsets, start, end);

                // The positions the pattern's first object may take while the whole pattern stays inside the playfield.
                Vector2 lowestPosition = -minimum;
                Vector2 highestPosition = OsuPlayfield.BASE_SIZE - maximum;

                Vector2 position = originalStartPositions[start];

                // Every pattern but the first is placed relative to where the previous one ended.
                if (start > 0)
                {
                    Vector2 from = hitObjects[start - 1].EndPosition;
                    Vector2 movement = scaleMovement(originalStartPositions[start] - originalEndPositions[start - 1], intensity);

                    position = from + fitMovement(movement, from, lowestPosition, highestPosition);
                }

                // A pattern which is larger than the playfield (or which starts outside of it) can't be placed perfectly,
                // so it is put as close to its intended position as possible.
                position = new Vector2(
                    Math.Clamp(position.X, MathF.Min(lowestPosition.X, highestPosition.X), MathF.Max(lowestPosition.X, highestPosition.X)),
                    Math.Clamp(position.Y, MathF.Min(lowestPosition.Y, highestPosition.Y), MathF.Max(lowestPosition.Y, highestPosition.Y)));

                for (int i = start; i < end; i++)
                    hitObjects[i].Position = position + offsets[i - start];
            }
        }

        /// <summary>
        /// Positions the objects of a pattern relative to its first object, spreading them apart by
        /// <paramref name="intraPatternFactor"/> without changing the shape of the pattern or of any slider in it.
        /// </summary>
        /// <remarks>
        /// The spread is reduced for a pattern which would otherwise grow larger than the playfield.
        /// </remarks>
        private static Vector2[] layOutPattern(IReadOnlyList<OsuHitObject> hitObjects, Vector2[] startPositions, Vector2[] endPositions, int start, int end, float intraPatternFactor)
        {
            float factor = intraPatternFactor;

            while (true)
            {
                var offsets = new Vector2[end - start];

                for (int i = start + 1; i < end; i++)
                {
                    // The vector across the previous object (for a slider, from its head to its tail) is never scaled.
                    Vector2 acrossPrevious = endPositions[i - 1] - startPositions[i - 1];
                    Vector2 gap = startPositions[i] - endPositions[i - 1];

                    offsets[i - start] = offsets[i - 1 - start] + acrossPrevious + gap * factor;
                }

                (Vector2 minimum, Vector2 maximum) = getBounds(hitObjects, offsets, start, end);

                if (factor <= 1 || (maximum.X - minimum.X <= OsuPlayfield.BASE_SIZE.X && maximum.Y - minimum.Y <= OsuPlayfield.BASE_SIZE.Y))
                    return offsets;

                // Back off towards the pattern's original size until it fits on screen again.
                factor = 1 + (factor - 1) / 2;

                if (factor < 1.001f)
                    factor = 1;
            }
        }

        /// <summary>
        /// Splits the beatmap into patterns: runs of objects which are played as one movement of the cursor,
        /// such as a triple, a burst, a stream, or a stack.
        /// </summary>
        /// <returns>The start (inclusive) and end (exclusive) index of each pattern.</returns>
        private static IEnumerable<(int start, int end)> getPatterns(OsuBeatmap beatmap, Vector2[] startPositions, Vector2[] endPositions)
        {
            int start = 0;

            for (int i = 1; i <= beatmap.HitObjects.Count; i++)
            {
                if (i < beatmap.HitObjects.Count && continuesPattern(beatmap, startPositions, endPositions, i))
                    continue;

                yield return (start, i);

                start = i;
            }
        }

        /// <returns>Whether the object at <paramref name="index"/> belongs to the same pattern as the one before it.</returns>
        private static bool continuesPattern(OsuBeatmap beatmap, Vector2[] startPositions, Vector2[] endPositions, int index)
        {
            var previous = beatmap.HitObjects[index - 1];
            var current = beatmap.HitObjects[index];

            if (previous is Spinner || current is Spinner)
                return false;

            float distance = (startPositions[index] - endPositions[index - 1]).Length;

            // Stacks are kept together no matter how far apart in time they are, as pulling them apart would be very obvious.
            if (distance <= stack_distance_threshold)
                return true;

            if (distance > pattern_distance_threshold)
                return false;

            // The gap is measured from the point the previous object is finished with, which is the tail of a slider.
            double gap = current.StartTime - previous.GetEndTime();

            if (gap <= 0)
                return true;

            // What counts as "played in one movement" depends on the tempo of the beatmap, not on an absolute time.
            double beatLength = beatmap.ControlPointInfo.TimingPointAt(current.StartTime).BeatLength;

            return gap <= Math.Clamp(beatLength * pattern_gap_in_beats, minimum_pattern_gap, maximum_pattern_gap);
        }

        /// <summary>
        /// Lengthens a movement, without ever turning it or making it shorter than it already was.
        /// </summary>
        /// <remarks>
        /// The lengthening tails off as a movement approaches <see cref="maximum_movement"/>. A jump which already
        /// crosses the screen has nowhere left to go, so multiplying it outright only asks for a length the playfield
        /// cannot give, which is then cut back to whatever fits — making every setting above a low one play the same.
        /// </remarks>
        private static Vector2 scaleMovement(Vector2 movement, float intensity)
        {
            float length = movement.Length;

            if (length < float.Epsilon)
                return movement;

            float scaled = Math.Max(length, maximum_movement * MathF.Tanh(length * intensity / maximum_movement));

            return movement * (scaled / length);
        }

        /// <summary>
        /// Shortens a movement, if needed, so that it arrives at a position from which the whole pattern fits inside
        /// the playfield.
        /// </summary>
        /// <remarks>
        /// The direction of the movement is left alone. Turning a jump to buy room would keep its length at the cost of
        /// the shape of the pattern it belongs to — a star whose vertical jumps are laid flat is no longer a star. A jump
        /// which is already as long as the playfield allows in its own direction is therefore left at that length.
        /// <para>
        /// The exception is a movement which barely fits at all, which happens when the object it starts from has itself
        /// been pushed flush against an edge: a jump mostly down the screen, with a slight lean into that edge, would then
        /// have to collapse to nothing. A limited turn is allowed to save those.
        /// </para>
        /// </remarks>
        private static Vector2 fitMovement(Vector2 movement, Vector2 from, Vector2 lowestPosition, Vector2 highestPosition)
        {
            Vector2 minimum = lowestPosition - from;
            Vector2 maximum = highestPosition - from;

            if (minimum.X > maximum.X || minimum.Y > maximum.Y)
                return Vector2.Zero;

            float factor = fittingFactor(movement, minimum, maximum);

            if (factor >= acceptable_length_share)
                return movement * factor;

            Vector2 best = movement * factor;

            for (int degrees = rotation_step_degrees; degrees <= fallback_rotation_degrees; degrees += rotation_step_degrees)
            {
                foreach (int signedDegrees in new[] { degrees, -degrees })
                {
                    Vector2 candidate = rotate(movement, signedDegrees * MathF.PI / 180);

                    float candidateFactor = fittingFactor(candidate, minimum, maximum);

                    // Turned as little as it takes to get a worthwhile jump back.
                    if (candidateFactor >= acceptable_length_share)
                        return candidate * candidateFactor;

                    if (candidate.Length * candidateFactor > best.Length)
                        best = candidate * candidateFactor;
                }
            }

            return best;
        }

        /// <returns>The largest factor in [0, 1] which keeps <paramref name="movement"/> inside the given box.</returns>
        private static float fittingFactor(Vector2 movement, Vector2 minimum, Vector2 maximum) => Math.Min(
            axisFactor(movement.X, minimum.X, maximum.X),
            axisFactor(movement.Y, minimum.Y, maximum.Y));

        private static Vector2 rotate(Vector2 vector, float radians)
        {
            float sin = MathF.Sin(radians);
            float cos = MathF.Cos(radians);

            return new Vector2(vector.X * cos - vector.Y * sin, vector.X * sin + vector.Y * cos);
        }

        /// <returns>The extents of the pattern's geometry relative to its first object, slider paths included.</returns>
        private static (Vector2 minimum, Vector2 maximum) getBounds(IReadOnlyList<OsuHitObject> hitObjects, Vector2[] offsets, int start, int end)
        {
            var minimum = new Vector2(float.MaxValue);
            var maximum = new Vector2(float.MinValue);

            for (int i = start; i < end; i++)
            {
                Vector2 offset = offsets[i - start];

                minimum = Vector2.ComponentMin(minimum, offset);
                maximum = Vector2.ComponentMax(maximum, offset);

                if (hitObjects[i] is not Slider slider)
                    continue;

                foreach (var pathPosition in slider.Path.CalculatedPath)
                {
                    minimum = Vector2.ComponentMin(minimum, offset + pathPosition);
                    maximum = Vector2.ComponentMax(maximum, offset + pathPosition);
                }
            }

            return (minimum, maximum);
        }

        /// <returns>The largest factor in [0, 1] which keeps <paramref name="movement"/> within the given range.</returns>
        private static float axisFactor(float movement, float min, float max)
        {
            if (movement > 0)
                return max <= 0 ? 0 : Math.Min(1, max / movement);

            if (movement < 0)
                return min >= 0 ? 0 : Math.Min(1, min / movement);

            return 1;
        }
    }
}
