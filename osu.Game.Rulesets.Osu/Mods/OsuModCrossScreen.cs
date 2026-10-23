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
using osu.Game.Rulesets.Objects.Types;
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
    /// <remarks>
    /// The map is first laid out as a chain of scaled jumps, which keeps every jump's direction and the mapper's shapes exactly
    /// (a star stays a star, a back-and-forth stays symmetric). A chain of stretched jumps drifts, though, and ends up pinned
    /// against the edges of the playfield, so the chain is then slid back, a little at a time, so that each part of it is centred
    /// where that part of the original map was centred.
    /// </remarks>
    public class OsuModCrossScreen : Mod, IApplicableToBeatmap, IHasSpacingAdjust
    {
        public override string Name => "Cross Screen";
        public override string Acronym => "XS";
        public override LocalisableString Description => "Use the whole screen. Jumps get bigger, patterns stay intact.";
        public override ModType Type => ModType.Conversion;
        public override IconUsage? Icon => FontAwesome.Solid.ExpandArrowsAlt;

        public override Type[] IncompatibleMods => new[] { typeof(OsuModRandom), typeof(OsuModTargetPractice) };

        /// <summary>
        /// The largest gap between two objects, as a fraction of a beat, for them to be fully part of the same pattern.
        /// Covers 1/4 and 1/3 rhythms, but not 1/2.
        /// </summary>
        private const double pattern_gap_in_beats = 0.375;

        /// <summary>
        /// Bounds applied to the gap derived from <see cref="pattern_gap_in_beats"/>, for beatmaps with extreme tempos.
        /// </summary>
        private const double minimum_pattern_gap = 60;

        private const double maximum_pattern_gap = 250;

        /// <summary>
        /// Up to this movement, two objects close enough in time are fully part of the same pattern.
        /// Between this and <see cref="jump_distance"/> they are partly a pattern and partly a jump, so that a stream whose
        /// spacing wavers around the boundary isn't scaled differently from note to note.
        /// </summary>
        private const float pattern_distance = 100;

        private const float jump_distance = 160;

        /// <summary>
        /// The distance below which two objects are considered stacked, and are therefore always kept together.
        /// </summary>
        private const float stack_distance_threshold = 3;

        /// <summary>
        /// How far either side of an object the surrounding objects are looked at to find the centre it is pushed out from.
        /// </summary>
        private const double centre_window_in_beats = 2;

        private const double minimum_centre_window = 500;

        private const double maximum_centre_window = 2500;

        /// <summary>
        /// How far from the edge of the playfield objects are kept. Mappers rarely put an object right on the edge.
        /// </summary>
        private const float playfield_margin = 10;

        /// <summary>
        /// A jump is not allowed to end up shorter than this share of its original length when it is squeezed into the playfield,
        /// so that no two objects are pushed on top of each other and no jump becomes noticeably smaller than the mapper made it.
        /// </summary>
        private const float minimum_jump_share = 0.85f;

        /// <summary>
        /// The share of <see cref="centre_window_in_beats"/> over which every surrounding object counts in full. Beyond it,
        /// objects count less and less, so that the centre doesn't lurch when an object enters or leaves the window.
        /// </summary>
        private const double centre_window_flat_share = 0.8;

        /// <summary>
        /// Jumps already faster than this share of the beatmap's own jumps are not scaled by the full intensity, so that a
        /// beatmap's handful of fastest jumps can't become impossible.
        /// </summary>
        private const double fast_jump_percentile = 0.95;

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

        /// <summary>
        /// Whether sliders grow along with the spacing around them, as they would if the mapper had mapped at a higher
        /// slider velocity. Left off, sliders keep their exact shape and size while the gaps between them grow.
        /// </summary>
        [SettingSource("Scale sliders", "Grow sliders along with the spacing around them, rather than keeping them at their original size")]
        public BindableBool ScaleSliders { get; } = new BindableBool();

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

            float intensity = (float)SpacingIntensity.Value;
            float intraPatternFactor = 1 + (float)((SpacingIntensity.Value - 1) * PatternSpread.Value);

            // How much each object belongs to the same pattern as the one before it: 1 for a note in a stream, 0 for a jump.
            float[] patternness = new float[hitObjects.Count];
            // The factor each movement is scaled by, from the pattern factor up to the full intensity.
            float[] movementFactor = new float[hitObjects.Count];

            for (int i = 1; i < hitObjects.Count; i++)
            {
                patternness[i] = getPatternness(osuBeatmap, i);
                movementFactor[i] = intraPatternFactor + (intensity - intraPatternFactor) * (1 - patternness[i]);
            }

            if (ScaleSliders.Value)
                scaleSliders(hitObjects, movementFactor);

            // Positions are read from these snapshots, since objects are moved in place as we go.
            var originalStartPositions = hitObjects.Select(h => h.Position).ToArray();
            var originalEndPositions = hitObjects.Select(h => h.EndPosition).ToArray();

            var patterns = getPatterns(hitObjects, patternness).ToList();

            limitFastJumps(hitObjects, originalStartPositions, originalEndPositions, patterns, movementFactor, intensity);

            Vector2[] lowestPosition = new Vector2[patterns.Count];
            Vector2[] highestPosition = new Vector2[patterns.Count];
            Vector2[][] offsets = new Vector2[patterns.Count][];

            // Where every object would be if each jump were simply scaled from wherever the previous one landed.
            var chained = new Vector2[hitObjects.Count];

            for (int p = 0; p < patterns.Count; p++)
            {
                (int start, int end) = patterns[p];

                // The pattern is laid out on its own first, so that the playfield can never reshape it.
                offsets[p] = layOutPattern(hitObjects, originalStartPositions, originalEndPositions, movementFactor, start, end);
                (Vector2 minimum, Vector2 maximum) = getBounds(hitObjects, offsets[p], start, end);

                // The positions the pattern's first object may take while the whole pattern stays inside the playfield.
                lowestPosition[p] = new Vector2(playfield_margin) - minimum;
                highestPosition[p] = OsuPlayfield.BASE_SIZE - new Vector2(playfield_margin) - maximum;

                Vector2 anchor = originalStartPositions[start];

                if (start > 0)
                {
                    Vector2 previousEnd = chained[start - 1] + (originalEndPositions[start - 1] - originalStartPositions[start - 1]);
                    Vector2 jump = anchor - originalEndPositions[start - 1];

                    // A spinner sits in the middle regardless, so the jumps into and out of it aren't really jumps.
                    bool spinnerInvolved = hitObjects[start] is Spinner || hitObjects[start - 1] is Spinner;

                    anchor = previousEnd + (spinnerInvolved ? jump : jump * movementFactor[start]);
                }

                for (int i = start; i < end; i++)
                    chained[i] = anchor + offsets[p][i - start];
            }

            for (int p = 0; p < patterns.Count; p++)
            {
                (int start, int end) = patterns[p];

                // Spinners are always centred, and moving them would achieve nothing.
                if (hitObjects[start] is Spinner)
                    continue;

                // The chain is slid so that the objects around this one are centred where they were in the original map.
                Vector2 originalCentre = getLocalCentre(osuBeatmap, originalStartPositions, start);
                Vector2 chainedCentre = getLocalCentre(osuBeatmap, chained, start);

                Vector2 position = fitIntoPlayfield(chained[start] - chainedCentre + originalCentre, originalCentre, lowestPosition[p], highestPosition[p]);

                // Squeezing a pattern into the playfield can pull it onto the end of the previous one. A jump which has lost
                // much of its length is retaken from where the previous pattern actually ended instead, so that nothing lands
                // on top of anything else and no jump ends up smaller than the mapper made it.
                if (start > 0 && hitObjects[start - 1] is not Spinner)
                {
                    Vector2 from = hitObjects[start - 1].EndPosition;
                    Vector2 intended = (originalStartPositions[start] - originalEndPositions[start - 1]) * movementFactor[start];

                    if (intended.Length > stack_distance_threshold && (position - from).Length < intended.Length * minimum_jump_share)
                        position = retakeJump(from, intended, position, lowestPosition[p], highestPosition[p]);
                }

                for (int i = start; i < end; i++)
                    hitObjects[i].Position = position + offsets[p][i - start];
            }
        }

        /// <summary>
        /// Positions the objects of a pattern relative to its first object, spreading each movement inside it by its own
        /// factor without changing the shape of the pattern or of any slider in it.
        /// </summary>
        /// <remarks>
        /// The spread is reduced for a pattern which would otherwise grow larger than the playfield.
        /// </remarks>
        private static Vector2[] layOutPattern(IReadOnlyList<OsuHitObject> hitObjects, Vector2[] startPositions, Vector2[] endPositions, float[] movementFactor, int start, int end)
        {
            float reduction = 1;

            while (true)
            {
                var offsets = new Vector2[end - start];

                for (int i = start + 1; i < end; i++)
                {
                    // The vector across the previous object (for a slider, from its head to its tail) is never scaled.
                    Vector2 acrossPrevious = endPositions[i - 1] - startPositions[i - 1];
                    Vector2 gap = startPositions[i] - endPositions[i - 1];

                    float factor = 1 + (movementFactor[i] - 1) * reduction;

                    offsets[i - start] = offsets[i - 1 - start] + acrossPrevious + gap * factor;
                }

                (Vector2 minimum, Vector2 maximum) = getBounds(hitObjects, offsets, start, end);

                Vector2 available = OsuPlayfield.BASE_SIZE - new Vector2(2 * playfield_margin);

                if (reduction <= 0 || (maximum.X - minimum.X <= available.X && maximum.Y - minimum.Y <= available.Y))
                    return offsets;

                // Back off towards the pattern's original size until it fits on screen again.
                reduction /= 2;

                if (reduction < 0.01f)
                    reduction = 0;
            }
        }

        /// <summary>
        /// Splits the beatmap into patterns: runs of objects which are played as one movement of the cursor,
        /// such as a triple, a burst, a stream, or a stack.
        /// </summary>
        /// <returns>The start (inclusive) and end (exclusive) index of each pattern.</returns>
        private static IEnumerable<(int start, int end)> getPatterns(IReadOnlyList<OsuHitObject> hitObjects, float[] patternness)
        {
            int start = 0;

            for (int i = 1; i <= hitObjects.Count; i++)
            {
                if (i < hitObjects.Count && patternness[i] >= 0.5f)
                    continue;

                yield return (start, i);

                start = i;
            }
        }

        /// <summary>
        /// How much the object at <paramref name="index"/> belongs to the same pattern as the one before it:
        /// 1 when they are clearly played as one movement, 0 when the movement between them is clearly a jump.
        /// </summary>
        private static float getPatternness(OsuBeatmap beatmap, int index)
        {
            var previous = beatmap.HitObjects[index - 1];
            var current = beatmap.HitObjects[index];

            if (previous is Spinner || current is Spinner)
                return 0;

            float distance = (current.Position - previous.EndPosition).Length;

            // Stacks are kept together no matter how far apart in time they are, as pulling them apart would be very obvious.
            if (distance <= stack_distance_threshold)
                return 1;

            float byDistance = 1 - Math.Clamp((distance - pattern_distance) / (jump_distance - pattern_distance), 0, 1);

            // The gap is measured from the point the previous object is finished with, which is the tail of a slider.
            double gap = current.StartTime - previous.GetEndTime();

            if (gap <= 0)
                return byDistance;

            // What counts as "played in one movement" depends on the tempo of the beatmap, not on an absolute time.
            double beatLength = beatmap.ControlPointInfo.TimingPointAt(current.StartTime).BeatLength;
            double patternGap = Math.Clamp(beatLength * pattern_gap_in_beats, minimum_pattern_gap, maximum_pattern_gap);

            float byTime = 1 - (float)Math.Clamp((gap - patternGap) / (patternGap * 0.5), 0, 1);

            return Math.Min(byDistance, byTime);
        }

        /// <summary>
        /// The point objects around <paramref name="index"/> are centred on: the average of the given positions of the objects
        /// played within a couple of beats either side.
        /// </summary>
        /// <remarks>
        /// Every object in the window counts the same (bar a soft edge), so that neighbouring objects see nearly the same centre
        /// and the chain is slid by nearly the same amount for both, keeping the jump between them as it was laid out.
        /// </remarks>
        private static Vector2 getLocalCentre(OsuBeatmap beatmap, Vector2[] positions, int index)
        {
            var hitObjects = beatmap.HitObjects;
            double time = hitObjects[index].StartTime;

            double beatLength = beatmap.ControlPointInfo.TimingPointAt(time).BeatLength;
            double window = Math.Clamp(beatLength * centre_window_in_beats, minimum_centre_window, maximum_centre_window);

            Vector2 total = Vector2.Zero;
            double totalWeight = 0;

            for (int i = index; i >= 0 && time - hitObjects[i].StartTime <= window; i--)
                accumulate(i);

            for (int i = index + 1; i < hitObjects.Count && hitObjects[i].StartTime - time <= window; i++)
                accumulate(i);

            return totalWeight > 0 ? total / (float)totalWeight : positions[index];

            void accumulate(int i)
            {
                if (hitObjects[i] is Spinner)
                    return;

                double distance = Math.Abs(hitObjects[i].StartTime - time) / window;
                double weight = distance <= centre_window_flat_share ? 1 : (1 - distance) / (1 - centre_window_flat_share);

                total += positions[i] * (float)weight;
                totalWeight += weight;
            }
        }

        /// <summary>
        /// Keeps the fastest jumps of the beatmap from being scaled by the full intensity, so that they stay within what the
        /// beatmap itself asks of the player, scaled by the intensity.
        /// </summary>
        private static void limitFastJumps(IReadOnlyList<OsuHitObject> hitObjects, Vector2[] startPositions, Vector2[] endPositions, List<(int start, int end)> patterns, float[] movementFactor, float intensity)
        {
            var velocities = new List<(int index, double velocity)>();

            foreach ((int start, _) in patterns)
            {
                if (start == 0 || hitObjects[start] is Spinner || hitObjects[start - 1] is Spinner)
                    continue;

                double time = hitObjects[start].StartTime - hitObjects[start - 1].GetEndTime();

                if (time <= 0)
                    continue;

                velocities.Add((start, (startPositions[start] - endPositions[start - 1]).Length / time));
            }

            if (velocities.Count < 20)
                return;

            var sorted = velocities.Select(v => v.velocity).OrderBy(v => v).ToList();
            double ceiling = sorted[(int)((sorted.Count - 1) * fast_jump_percentile)] * intensity;

            foreach ((int index, double velocity) in velocities)
            {
                if (velocity * movementFactor[index] > ceiling)
                    movementFactor[index] = Math.Max(1, (float)(ceiling / velocity));
            }
        }

        /// <summary>
        /// Retakes a jump from where the previous pattern actually ended. If the jump runs out of playfield in that direction,
        /// it bounces off the edge instead (the part of it heading into the edge is turned around), as the alternative is for
        /// the two objects to end up on top of each other.
        /// </summary>
        /// <returns>Whichever of the retaken, bounced and originally placed positions gives the longest jump.</returns>
        private static Vector2 retakeJump(Vector2 from, Vector2 intended, Vector2 fallback, Vector2 lowestPosition, Vector2 highestPosition)
        {
            Vector2 best = fallback;

            void consider(Vector2 candidate)
            {
                if ((candidate - from).Length > (best - from).Length)
                    best = candidate;
            }

            consider(fitIntoPlayfield(from + intended, from, lowestPosition, highestPosition));

            Vector2 lowest = Vector2.ComponentMin(lowestPosition, highestPosition);
            Vector2 highest = Vector2.ComponentMax(lowestPosition, highestPosition);
            Vector2 target = from + intended;

            Vector2 bounced = new Vector2(
                target.X < lowest.X || target.X > highest.X ? -intended.X : intended.X,
                target.Y < lowest.Y || target.Y > highest.Y ? -intended.Y : intended.Y);

            if (bounced != intended)
                consider(fitIntoPlayfield(from + bounced, from, lowestPosition, highestPosition));

            return best;
        }

        /// <summary>
        /// Pulls a position back towards <paramref name="centre"/>, if needed, until the pattern anchored on it fits inside
        /// the playfield. Pulling straight back keeps the direction it was pushed out in.
        /// </summary>
        private static Vector2 fitIntoPlayfield(Vector2 position, Vector2 centre, Vector2 lowestPosition, Vector2 highestPosition)
        {
            Vector2 lowest = Vector2.ComponentMin(lowestPosition, highestPosition);
            Vector2 highest = Vector2.ComponentMax(lowestPosition, highestPosition);

            float share = Math.Min(axisShare(position.X, centre.X, lowest.X, highest.X), axisShare(position.Y, centre.Y, lowest.Y, highest.Y));

            position = centre + (position - centre) * share;

            // The centre itself may be out of range when a pattern is larger than the playfield, in which case the pattern
            // is put as close to its intended position as possible.
            return Vector2.Clamp(position, lowest, highest);
        }

        /// <returns>The largest share in [0, 1] of the way from <paramref name="from"/> to <paramref name="to"/> which stays within range.</returns>
        private static float axisShare(float to, float from, float min, float max)
        {
            if (to > max && to > from)
                return Math.Clamp((max - from) / (to - from), 0, 1);

            if (to < min && to < from)
                return Math.Clamp((min - from) / (to - from), 0, 1);

            return 1;
        }

        /// <summary>
        /// Grows each slider by the factor of the movement leading into it, as if it had been mapped at a higher slider velocity.
        /// The slider still takes the same time, so it is simply travelled faster.
        /// </summary>
        private static void scaleSliders(IReadOnlyList<OsuHitObject> hitObjects, float[] movementFactor)
        {
            for (int i = 0; i < hitObjects.Count; i++)
            {
                if (hitObjects[i] is not Slider slider)
                    continue;

                float factor = movementFactor[Math.Max(i, 1)];

                if (factor <= 1 || slider.Path.ControlPoints.Count < 2)
                    continue;

                var controlPoints = slider.Path.ControlPoints.Select(point => new PathControlPoint(point.Position * factor, point.Type)).ToArray();

                slider.ReshapePath(new SliderPath(controlPoints, slider.Path.ExpectedDistance.Value * factor));
            }
        }

        /// <returns>The extents of the pattern's geometry relative to its first object, slider paths and stacking included.</returns>
        private static (Vector2 minimum, Vector2 maximum) getBounds(IReadOnlyList<OsuHitObject> hitObjects, Vector2[] offsets, int start, int end)
        {
            var minimum = new Vector2(float.MaxValue);
            var maximum = new Vector2(float.MinValue);

            for (int i = start; i < end; i++)
            {
                Vector2 offset = offsets[i - start] + hitObjects[i].StackOffset;

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
    }
}
