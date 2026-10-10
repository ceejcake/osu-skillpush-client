// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Beatmaps;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Tests.Beatmaps;
using osuTK;

namespace osu.Game.Rulesets.Osu.Tests.Mods
{
    [TestFixture]
    public class OsuModMirrorRotationTest
    {
        private const float tolerance = 0.01f;

        [Test]
        public void TestClockwiseFillsPlayfield()
        {
            // Top left goes to top right, top right to bottom right, and so on round.
            assertPosition(rotate(new Vector2(0, 0), OsuModMirror.MirrorRotation.Clockwise), new Vector2(512, 0));
            assertPosition(rotate(new Vector2(512, 0), OsuModMirror.MirrorRotation.Clockwise), new Vector2(512, 384));
            assertPosition(rotate(new Vector2(512, 384), OsuModMirror.MirrorRotation.Clockwise), new Vector2(0, 384));
            assertPosition(rotate(new Vector2(0, 384), OsuModMirror.MirrorRotation.Clockwise), new Vector2(0, 0));
            assertPosition(rotate(new Vector2(256, 192), OsuModMirror.MirrorRotation.Clockwise), new Vector2(256, 192));
        }

        [Test]
        public void TestCounterclockwiseFillsPlayfield()
        {
            assertPosition(rotate(new Vector2(0, 0), OsuModMirror.MirrorRotation.Counterclockwise), new Vector2(0, 384));
            assertPosition(rotate(new Vector2(512, 0), OsuModMirror.MirrorRotation.Counterclockwise), new Vector2(0, 0));
            assertPosition(rotate(new Vector2(512, 384), OsuModMirror.MirrorRotation.Counterclockwise), new Vector2(512, 0));
            assertPosition(rotate(new Vector2(0, 384), OsuModMirror.MirrorRotation.Counterclockwise), new Vector2(512, 384));
        }

        [TestCase(OsuModMirror.MirrorRotation.Clockwise)]
        [TestCase(OsuModMirror.MirrorRotation.Counterclockwise)]
        public void TestHorizontalJumpBecomesVertical(OsuModMirror.MirrorRotation rotation)
        {
            // A jump across the whole width becomes one across the whole height, and the other way around.
            Vector2 horizontal = rotate(new Vector2(512, 192), rotation) - rotate(new Vector2(0, 192), rotation);
            Vector2 vertical = rotate(new Vector2(256, 384), rotation) - rotate(new Vector2(256, 0), rotation);

            Assert.That(horizontal.X, Is.EqualTo(0).Within(tolerance));
            Assert.That(Math.Abs(horizontal.Y), Is.EqualTo(384).Within(tolerance));
            Assert.That(vertical.Y, Is.EqualTo(0).Within(tolerance));
            Assert.That(Math.Abs(vertical.X), Is.EqualTo(512).Within(tolerance));
        }

        [Test]
        public void TestNoRotationByDefault()
        {
            var mod = new OsuModMirror { Reflection = { Value = OsuModMirror.MirrorType.None } };
            var circle = new HitCircle { Position = new Vector2(100, 50) };

            mod.ApplyToHitObject(circle);

            assertPosition(circle.Position, new Vector2(100, 50));
        }

        [Test]
        public void TestRotationAppliesAfterFlip()
        {
            var mod = new OsuModMirror { Reflection = { Value = OsuModMirror.MirrorType.Horizontal }, Rotation = { Value = OsuModMirror.MirrorRotation.Clockwise } };
            var circle = new HitCircle { Position = new Vector2(0, 0) };

            mod.ApplyToHitObject(circle);

            // Flipped to the top right, then turned to the bottom right.
            assertPosition(circle.Position, new Vector2(512, 384));
        }

        [TestCase(OsuModMirror.MirrorRotation.Clockwise)]
        [TestCase(OsuModMirror.MirrorRotation.Counterclockwise)]
        public void TestSlidersKeepTheirTiming(OsuModMirror.MirrorRotation rotation)
        {
            var original = playable(createBeatmap());
            var rotated = playable(createBeatmap(), new OsuModMirror { Reflection = { Value = OsuModMirror.MirrorType.None }, Rotation = { Value = rotation } });

            var originalSliders = original.HitObjects.OfType<Slider>().ToArray();
            var rotatedSliders = rotated.HitObjects.OfType<Slider>().ToArray();

            Assert.That(rotatedSliders, Has.Length.EqualTo(originalSliders.Length));

            for (int i = 0; i < originalSliders.Length; i++)
            {
                var before = originalSliders[i];
                var after = rotatedSliders[i];

                Assert.That(after.EndTime, Is.EqualTo(before.EndTime).Within(0.001), $"slider {i} end time");
                Assert.That(after.NestedHitObjects.Select(h => h.GetType()), Is.EqualTo(before.NestedHitObjects.Select(h => h.GetType())), $"slider {i} nested objects");

                for (int n = 0; n < before.NestedHitObjects.Count; n++)
                    Assert.That(after.NestedHitObjects[n].StartTime, Is.EqualTo(before.NestedHitObjects[n].StartTime).Within(0.001), $"slider {i} nested {n} time");

                // The whole shape turns with the slider: the end is where the original's end would be turned to.
                assertPosition(after.Position, rotate(before.Position, rotation));
                assertPosition(after.EndPosition, rotate(before.EndPosition, rotation), 1);
                assertPosition(after.Position + after.Path.PositionAt(0), after.Position);

                foreach (var nested in after.NestedHitObjects.OfType<OsuHitObject>())
                {
                    Assert.That(nested.Position.X, Is.InRange(-1, 513), $"slider {i} nested in playfield");
                    Assert.That(nested.Position.Y, Is.InRange(-1, 385), $"slider {i} nested in playfield");
                }
            }
        }

        [Test]
        public void TestSliderLengthFollowsDirection()
        {
            // Horizontal sliders get shorter (and slower), vertical ones longer (and faster).
            var rotated = playable(createBeatmap(), new OsuModMirror { Reflection = { Value = OsuModMirror.MirrorType.None }, Rotation = { Value = OsuModMirror.MirrorRotation.Clockwise } });
            var sliders = rotated.HitObjects.OfType<Slider>().ToArray();

            Assert.That(sliders[0].Path.Distance, Is.EqualTo(200 * 0.75).Within(0.01));
            Assert.That(sliders[1].Path.Distance, Is.EqualTo(150 / 0.75).Within(0.01));
        }

        [Test]
        public void TestTimingSurvivesStrictTracking()
        {
            var original = playable(createBeatmap(), new OsuModStrictTracking());
            var rotated = playable(createBeatmap(),
                new OsuModMirror { Reflection = { Value = OsuModMirror.MirrorType.None }, Rotation = { Value = OsuModMirror.MirrorRotation.Clockwise } },
                new OsuModStrictTracking());

            Assert.That(rotated.HitObjects.OfType<Slider>().Select(s => s.EndTime), Is.EqualTo(original.HitObjects.OfType<Slider>().Select(s => s.EndTime)).Within(0.001));
        }

        [Test]
        public void TestStarRatingFollowsRotation()
        {
            // Jumps across the width shrink to the height once turned, and jumps across the height grow to the width.
            // The star rating shown at song select comes from the same calculation.
            var rotation = new OsuModMirror { Reflection = { Value = OsuModMirror.MirrorType.None }, Rotation = { Value = OsuModMirror.MirrorRotation.Clockwise } };

            double horizontal = starRating(createJumps(new Vector2(56, 192), new Vector2(456, 192)));
            double horizontalTurned = starRating(createJumps(new Vector2(56, 192), new Vector2(456, 192)), rotation);
            double vertical = starRating(createJumps(new Vector2(256, 42), new Vector2(256, 342)));
            double verticalTurned = starRating(createJumps(new Vector2(256, 42), new Vector2(256, 342)), rotation);

            Assert.That(horizontalTurned, Is.LessThan(horizontal));
            Assert.That(verticalTurned, Is.GreaterThan(vertical));

            // 400 across becomes 300 down, and 300 down becomes 400 across.
            Assert.That(horizontalTurned, Is.EqualTo(vertical).Within(0.05));
            Assert.That(verticalTurned, Is.EqualTo(horizontal).Within(0.05));
        }

        private static double starRating(IBeatmap beatmap, params Mod[] mods)
            => new OsuRuleset().CreateDifficultyCalculator(new TestWorkingBeatmap(beatmap)).Calculate(mods).StarRating;

        private static IBeatmap createJumps(Vector2 first, Vector2 second)
        {
            var beatmap = new OsuBeatmap { BeatmapInfo = { Ruleset = new OsuRuleset().RulesetInfo } };

            for (int i = 0; i < 64; i++)
                beatmap.HitObjects.Add(new HitCircle { StartTime = 1000 + i * 250, Position = i % 2 == 0 ? first : second });

            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            return beatmap;
        }

        private static Vector2 rotate(Vector2 position, OsuModMirror.MirrorRotation rotation)
        {
            var mod = new OsuModMirror { Reflection = { Value = OsuModMirror.MirrorType.None }, Rotation = { Value = rotation } };
            var circle = new HitCircle { Position = position };

            mod.ApplyToHitObject(circle);
            return circle.Position;
        }

        private static void assertPosition(Vector2 actual, Vector2 expected, float within = tolerance)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(within));
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(within));
        }

        private static IBeatmap playable(IBeatmap beatmap, params Mod[] mods)
            => new TestWorkingBeatmap(beatmap).GetPlayableBeatmap(new OsuRuleset().RulesetInfo, mods);

        private static IBeatmap createBeatmap()
        {
            var beatmap = new OsuBeatmap
            {
                BeatmapInfo = { Ruleset = new OsuRuleset().RulesetInfo },
                HitObjects =
                {
                    // Horizontal, with a repeat.
                    new Slider
                    {
                        StartTime = 1000,
                        Position = new Vector2(100, 100),
                        RepeatCount = 1,
                        Path = new SliderPath(new[] { new PathControlPoint(Vector2.Zero, PathType.LINEAR), new PathControlPoint(new Vector2(200, 0)) }, 200),
                    },
                    // Vertical.
                    new Slider
                    {
                        StartTime = 3000,
                        Position = new Vector2(300, 100),
                        Path = new SliderPath(new[] { new PathControlPoint(Vector2.Zero, PathType.LINEAR), new PathControlPoint(new Vector2(0, 150)) }, 150),
                    },
                    // A circular arc, which stops being circular once stretched.
                    new Slider
                    {
                        StartTime = 5000,
                        Position = new Vector2(150, 250),
                        Path = new SliderPath(new[]
                        {
                            new PathControlPoint(Vector2.Zero, PathType.PERFECT_CURVE),
                            new PathControlPoint(new Vector2(100, -80)),
                            new PathControlPoint(new Vector2(200, 0)),
                        }),
                    },
                    // A curve cut short of its full length.
                    new Slider
                    {
                        StartTime = 7000,
                        Position = new Vector2(100, 200),
                        Path = new SliderPath(new[]
                        {
                            new PathControlPoint(Vector2.Zero, PathType.BEZIER),
                            new PathControlPoint(new Vector2(150, -150)),
                            new PathControlPoint(new Vector2(300, 100)),
                        }, 180),
                    },
                }
            };

            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            return beatmap;
        }
    }
}
