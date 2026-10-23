// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Beatmaps;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.UI;
using osuTK;

namespace osu.Game.Rulesets.Osu.Tests.Mods
{
    [TestFixture]
    public class OsuModCrossScreenTest
    {
        /// <summary>
        /// 100 BPM, which puts a 1/4 rhythm at 150ms apart.
        /// </summary>
        private const double slow_beat_length = 600;

        [Test]
        public void TestSpacedMovementIsScaled()
        {
            var beatmap = createBeatmap(
                new HitCircle { StartTime = 0, Position = new Vector2(100, 100) },
                new HitCircle { StartTime = 900, Position = new Vector2(180, 100) });

            applyMod(beatmap, 2);

            var objects = beatmap.HitObjects;

            // 80px doubled is 160px, and both objects move: they are pushed apart from the middle of what's being played.
            Assert.That((objects[1].Position - objects[0].Position - new Vector2(160, 0)).Length, Is.EqualTo(0).Within(0.01));
            Assert.That(((objects[0].Position + objects[1].Position) / 2 - new Vector2(140, 100)).Length, Is.EqualTo(0).Within(0.01));
        }

        [Test]
        public void TestSlowMapTripleStaysIntact()
        {
            // At 100 BPM a 1/4 triple is 150ms apart, which is still one movement of the cursor.
            var beatmap = createBeatmap(
                new HitCircle { StartTime = 0, Position = new Vector2(100, 100) },
                new HitCircle { StartTime = 150, Position = new Vector2(140, 100) },
                new HitCircle { StartTime = 300, Position = new Vector2(180, 100) });

            applyMod(beatmap, 3, patternSpread: 0);

            var objects = beatmap.HitObjects;

            Assert.That((objects[1].Position - objects[0].Position).Length, Is.EqualTo(40).Within(0.01));
            Assert.That((objects[2].Position - objects[1].Position).Length, Is.EqualTo(40).Within(0.01));
        }

        [Test]
        public void TestPatternSpreadWidensPatternUniformly()
        {
            var beatmap = createBeatmap(
                new HitCircle { StartTime = 0, Position = new Vector2(100, 100) },
                new HitCircle { StartTime = 150, Position = new Vector2(140, 100) },
                new HitCircle { StartTime = 300, Position = new Vector2(160, 130) });

            // A spacing intensity of 3 with half of it applied inside patterns leaves a factor of 2 within the pattern.
            applyMod(beatmap, 3, patternSpread: 0.5);

            var objects = beatmap.HitObjects;

            Assert.That((objects[1].Position - objects[0].Position - new Vector2(80, 0)).Length, Is.EqualTo(0).Within(0.01));
            Assert.That((objects[2].Position - objects[1].Position - new Vector2(40, 60)).Length, Is.EqualTo(0).Within(0.01));
        }

        [Test]
        public void TestGapBetweenPatternsIsScaledInFull()
        {
            var beatmap = createBeatmap(
                new HitCircle { StartTime = 0, Position = new Vector2(200, 200) },
                new HitCircle { StartTime = 150, Position = new Vector2(230, 200) },
                // A 1/1 gap: the start of a new pattern.
                new HitCircle { StartTime = 750, Position = new Vector2(290, 200) },
                new HitCircle { StartTime = 900, Position = new Vector2(320, 200) });

            applyMod(beatmap, 2, patternSpread: 0);

            var objects = beatmap.HitObjects;

            Assert.That((objects[1].Position - objects[0].Position).Length, Is.EqualTo(30).Within(0.01), "pattern was not left alone");
            Assert.That((objects[2].Position - objects[1].Position).Length, Is.EqualTo(120).Within(0.01), "gap between patterns was not scaled");
            Assert.That((objects[3].Position - objects[2].Position).Length, Is.EqualTo(30).Within(0.01), "pattern was not left alone");
        }

        [Test]
        public void TestStackedObjectsStayStacked()
        {
            var beatmap = createBeatmap(
                new HitCircle { StartTime = 0, Position = new Vector2(100, 100) },
                new HitCircle { StartTime = 1000, Position = new Vector2(250, 200) },
                new HitCircle { StartTime = 2000, Position = new Vector2(250, 200) });

            applyMod(beatmap, 2);

            var objects = beatmap.HitObjects;

            Assert.That((objects[2].Position - objects[1].Position).Length, Is.EqualTo(0).Within(0.01));
        }

        [Test]
        public void TestDefaultIntensityDoesNotMoveObjects()
        {
            var positions = new[] { new Vector2(100, 100), new Vector2(250, 200), new Vector2(400, 100) };

            var beatmap = createBeatmap(
                new HitCircle { StartTime = 0, Position = positions[0] },
                new HitCircle { StartTime = 1000, Position = positions[1] },
                new HitCircle { StartTime = 2000, Position = positions[2] });

            applyMod(beatmap, 1);

            for (int i = 0; i < positions.Length; i++)
                Assert.That((beatmap.HitObjects[i].Position - positions[i]).Length, Is.EqualTo(0).Within(0.01));
        }

        [Test]
        public void TestMovementRunningOutOfPlayfieldIsNotTurned()
        {
            // Almost straight down, from close enough to the bottom that the full movement can't be taken.
            var beatmap = createBeatmap(
                new HitCircle { StartTime = 0, Position = new Vector2(256, 100) },
                new HitCircle { StartTime = 1000, Position = new Vector2(276, 250) });

            applyMod(beatmap, 3);

            var movement = beatmap.HitObjects[1].Position - beatmap.HitObjects[0].Position;

            // The jump has to give up the length it can't fit, rather than swinging sideways to keep it.
            Assert.That(angleOf(movement), Is.EqualTo(angleOf(new Vector2(20, 150))).Within(0.5));
            Assert.That(movement.Length, Is.LessThan(450), "the jump can't be longer than the playfield is tall");
            Assert.That(movement.Length, Is.GreaterThan(300), "the jump should use most of the playfield's height");
            Assert.That(beatmap.HitObjects[0].Position.Y, Is.InRange(10, OsuPlayfield.BASE_SIZE.Y - 10));
            Assert.That(beatmap.HitObjects[1].Position.Y, Is.InRange(10, OsuPlayfield.BASE_SIZE.Y - 10));
        }

        [Test]
        public void TestShapeOfJumpPatternIsPreserved()
        {
            // A five pointed star, drawn as jumps around the playfield. Scaled up, it should still be the same star, only bigger.
            var centre = new Vector2(256, 192);

            var positions = Enumerable.Range(0, 6)
                                      .Select(i => i * 4 * Math.PI / 5)
                                      .Select(angle => centre + new Vector2((float)Math.Sin(angle) * 110, -(float)Math.Cos(angle) * 110))
                                      .ToArray();

            var beatmap = createBeatmap(positions.Select((position, index) => (OsuHitObject)new HitCircle { StartTime = index * 160, Position = position }).ToArray());

            applyMod(beatmap, 1.5);

            // Everything is pushed out from the middle of what is being played, which is the average position of the objects.
            var middle = positions.Aggregate(Vector2.Zero, (total, position) => total + position) / positions.Length;

            for (int i = 0; i < positions.Length; i++)
                Assert.That((beatmap.HitObjects[i].Position - (middle + (positions[i] - middle) * 1.5f)).Length, Is.EqualTo(0).Within(0.5), $"point {i} of the star moved out of shape");

            for (int i = 1; i < positions.Length; i++)
            {
                var movement = beatmap.HitObjects[i].Position - beatmap.HitObjects[i - 1].Position;

                Assert.That(angleOf(movement), Is.EqualTo(angleOf(positions[i] - positions[i - 1])).Within(0.5), $"the angle of jump {i} was changed");
            }
        }

        [Test]
        public void TestProgressionAcrossPlayfieldDoesNotPinToEdge()
        {
            // A long run of jumps which sweeps steadily across the playfield. Chained scaling would run into the right edge
            // half way through and leave every later object stuck there.
            var hitObjects = new List<OsuHitObject>();

            for (int i = 0; i < 12; i++)
                hitObjects.Add(new HitCircle { StartTime = i * 500, Position = new Vector2(60 + i * 36, 192 + (i % 2 == 0 ? -50 : 50)) });

            var originalPositions = hitObjects.Select(h => h.Position).ToArray();
            var beatmap = createBeatmap(hitObjects.ToArray());

            applyMod(beatmap, 2);

            var objects = beatmap.HitObjects;

            Assert.That(objects.Count(o => o.Position.X >= OsuPlayfield.BASE_SIZE.X - 10), Is.LessThanOrEqualTo(1), "objects were pinned to the right edge");

            for (int i = 1; i < objects.Count; i++)
            {
                float original = (originalPositions[i] - originalPositions[i - 1]).Length;
                Assert.That((objects[i].Position - objects[i - 1].Position).Length, Is.GreaterThan(original * 0.85f), $"jump {i} became smaller than the mapper made it");
            }
        }

        [Test]
        public void TestMovementNearPatternBoundaryIsScaledPartWay()
        {
            // 130px is half way between a movement which is clearly part of a pattern and one which is clearly a jump,
            // so it gets half of the difference between the two factors rather than snapping to one of them.
            var beatmap = createBeatmap(
                new HitCircle { StartTime = 0, Position = new Vector2(126, 192) },
                new HitCircle { StartTime = 150, Position = new Vector2(256, 192) });

            applyMod(beatmap, 3, patternSpread: 0);

            Assert.That((beatmap.HitObjects[1].Position - beatmap.HitObjects[0].Position).Length, Is.EqualTo(260).Within(0.5));
        }

        [Test]
        public void TestSlidersCanBeScaledWithSpacing()
        {
            var slider = new Slider
            {
                StartTime = 1000,
                Position = new Vector2(200, 200),
                Path = new SliderPath(PathType.LINEAR, new[] { Vector2.Zero, new Vector2(80, 40) }),
            };

            var beatmap = createBeatmap(
                new HitCircle { StartTime = 0, Position = new Vector2(100, 100) },
                slider);

            double duration = slider.Duration;

            var mod = new OsuModCrossScreen { SpacingIntensity = { Value = 2 }, ScaleSliders = { Value = true } };
            mod.ApplyToBeatmap(beatmap);

            Assert.That((slider.EndPosition - slider.Position - new Vector2(160, 80)).Length, Is.EqualTo(0).Within(0.5), "the slider wasn't scaled with the jump into it");
            Assert.That(slider.Duration, Is.EqualTo(duration).Within(0.01), "the slider should take the same time as before");
        }

        private static double angleOf(Vector2 movement) => Math.Atan2(movement.Y, movement.X) * 180 / Math.PI;

        [Test]
        public void TestObjectsAlwaysStayInsidePlayfield()
        {
            var beatmap = createBeatmap(
                new HitCircle { StartTime = 0, Position = new Vector2(20, 20) },
                new HitCircle { StartTime = 700, Position = new Vector2(200, 60) },
                new HitCircle { StartTime = 1400, Position = new Vector2(480, 40) },
                new HitCircle { StartTime = 2100, Position = new Vector2(500, 350) },
                new HitCircle { StartTime = 2800, Position = new Vector2(60, 360) },
                new HitCircle { StartTime = 3500, Position = new Vector2(256, 192) });

            applyMod(beatmap, 3);

            foreach (var hitObject in beatmap.HitObjects)
            {
                Assert.That(hitObject.Position.X, Is.InRange(0, OsuPlayfield.BASE_SIZE.X));
                Assert.That(hitObject.Position.Y, Is.InRange(0, OsuPlayfield.BASE_SIZE.Y));
            }
        }

        [Test]
        public void TestSliderIsKeptInsidePlayfield()
        {
            var slider = new Slider
            {
                StartTime = 1000,
                Position = new Vector2(300, 250),
                Path = new SliderPath(PathType.LINEAR, new[] { Vector2.Zero, new Vector2(150, 60) }),
            };

            var beatmap = createBeatmap(
                new HitCircle { StartTime = 0, Position = new Vector2(100, 100) },
                slider);

            applyMod(beatmap, 3);

            foreach (var pathPosition in slider.Path.CalculatedPath)
            {
                var position = slider.Position + pathPosition;

                Assert.That(position.X, Is.InRange(0, OsuPlayfield.BASE_SIZE.X));
                Assert.That(position.Y, Is.InRange(0, OsuPlayfield.BASE_SIZE.Y));
            }
        }

        [Test]
        public void TestSliderIsNeverReshaped()
        {
            var slider = new Slider
            {
                StartTime = 1000,
                Position = new Vector2(200, 200),
                Path = new SliderPath(PathType.LINEAR, new[] { Vector2.Zero, new Vector2(80, 40) }),
            };

            var beatmap = createBeatmap(
                new HitCircle { StartTime = 0, Position = new Vector2(100, 100) },
                slider,
                new HitCircle { StartTime = 2000, Position = new Vector2(400, 300) });

            var originalPath = slider.Path.CalculatedPath.ToArray();

            applyMod(beatmap, 3);

            Assert.That(slider.Path.CalculatedPath, Is.EqualTo(originalPath));
        }

        [Test]
        public void TestBurstEndingInSliderIsKeptTogether()
        {
            var beatmap = createBurstEndingInSlider(gapAfterSlider: 50);

            var slider = (Slider)beatmap.HitObjects[0];

            applyMod(beatmap, 3, patternSpread: 0);

            Assert.That((beatmap.HitObjects[1].Position - slider.EndPosition).Length, Is.EqualTo(60).Within(1));
        }

        [Test]
        public void TestSpacedMovementAfterSliderIsScaled()
        {
            var beatmap = createBurstEndingInSlider(gapAfterSlider: 700);

            var slider = (Slider)beatmap.HitObjects[0];

            applyMod(beatmap, 2);

            Assert.That((beatmap.HitObjects[1].Position - slider.EndPosition).Length, Is.EqualTo(120).Within(0.5));
        }

        [Test]
        public void TestHigherIntensityNeverDecreasesSpacing()
        {
            // A tight pattern in the middle of the playfield, which has room to grow at every intensity.
            var positions = new[]
            {
                new Vector2(226, 132),
                new Vector2(286, 162),
                new Vector2(236, 222),
                new Vector2(296, 192),
                new Vector2(246, 142),
            };

            double previousTotal = 0;

            foreach (double intensity in new[] { 1.0, 1.5, 2.0, 2.5, 3.0 })
            {
                double total = totalSpacing(positions, intensity);

                Assert.That(total, Is.GreaterThan(previousTotal), $"spacing did not grow at {intensity}x");
                previousTotal = total;
            }
        }

        [Test]
        public void TestSpacingNeverFallsBelowTheOriginal()
        {
            // A beatmap which already uses the whole playfield, so most of its jumps run out of room when scaled up.
            var positions = new[]
            {
                new Vector2(100, 100),
                new Vector2(300, 200),
                new Vector2(150, 300),
                new Vector2(400, 120),
                new Vector2(200, 250),
            };

            double original = totalSpacing(positions, 1);

            foreach (double intensity in new[] { 1.5, 2.0, 2.5, 3.0 })
                Assert.That(totalSpacing(positions, intensity), Is.GreaterThan(original), $"spacing fell below the beatmap's own at {intensity}x");
        }

        private static double totalSpacing(Vector2[] positions, double intensity)
        {
            var beatmap = createBeatmap(positions.Select((position, index) => (OsuHitObject)new HitCircle { StartTime = index * 700, Position = position }).ToArray());

            applyMod(beatmap, intensity);

            double total = 0;

            for (int i = 1; i < beatmap.HitObjects.Count; i++)
                total += (beatmap.HitObjects[i].Position - beatmap.HitObjects[i - 1].Position).Length;

            return total;
        }

        private static OsuBeatmap createBurstEndingInSlider(double gapAfterSlider)
        {
            var beatmap = new OsuBeatmap();

            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = slow_beat_length });

            var slider = new Slider
            {
                StartTime = 0,
                Position = new Vector2(100, 150),
                Path = new SliderPath(PathType.LINEAR, new[] { Vector2.Zero, new Vector2(30, 0) }),
            };

            slider.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);

            var circle = new HitCircle
            {
                StartTime = slider.EndTime + gapAfterSlider,
                Position = slider.EndPosition + new Vector2(60, 0),
            };

            circle.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);

            beatmap.HitObjects = new List<OsuHitObject> { slider, circle };

            return beatmap;
        }

        private static void applyMod(OsuBeatmap beatmap, double intensity, double? patternSpread = null)
        {
            var mod = new OsuModCrossScreen();

            mod.SpacingIntensity.Value = intensity;

            if (patternSpread != null)
                mod.PatternSpread.Value = patternSpread.Value;

            mod.ApplyToBeatmap(beatmap);
        }

        private static OsuBeatmap createBeatmap(params OsuHitObject[] hitObjects)
        {
            var beatmap = new OsuBeatmap { HitObjects = new List<OsuHitObject>(hitObjects) };

            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = slow_beat_length });

            foreach (var hitObject in beatmap.HitObjects)
                hitObject.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);

            return beatmap;
        }
    }
}
