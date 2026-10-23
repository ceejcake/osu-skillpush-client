// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Framework.Allocation;
using osu.Framework.Graphics.Containers;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Graphics.Sprites;
using osu.Game.Replays;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Replays;
using osu.Game.Rulesets.Osu.UI;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.Osu.Tests
{
    /// <summary>
    /// Presses which are timed to hit a circle but miss it (e.g. because the cursor is off it) are reported, so that they can be shown on hit error meters.
    /// </summary>
    public partial class TestSceneMisaimedPresses : RateAdjustedBeatmapTestScene
    {
        private const double time_circle = 1500;

        private static readonly Vector2 position_circle = OsuPlayfield.BASE_SIZE / 2;
        private static readonly Vector2 position_away = Vector2.Zero;

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        private ScoreAccessibleReplayPlayer currentPlayer = null!;
        private List<double> misaimedPresses = null!;
        private List<AimError> aimMisses = null!;

        [Test]
        public void TestMisaimedPressReported()
        {
            performTest(new List<ReplayFrame>
            {
                new OsuReplayFrame { Time = time_circle - 30, Position = position_away, Actions = { OsuAction.LeftButton } },
                new OsuReplayFrame { Time = time_circle - 20, Position = position_away },
            });

            AddAssert("misaimed press reported", () => misaimedPresses, () => Has.Count.EqualTo(1));
            AddAssert("at the time of the press", () => misaimedPresses.Single(), () => Is.EqualTo(-30).Within(20));
            AddAssert("circle missed", () => currentPlayer.ScoreProcessor.Statistics.GetValueOrDefault(HitResult.Miss), () => Is.EqualTo(1));
        }

        [Test]
        public void TestHitNotReported()
        {
            performTest(new List<ReplayFrame>
            {
                new OsuReplayFrame { Time = time_circle, Position = position_circle, Actions = { OsuAction.LeftButton } },
                new OsuReplayFrame { Time = time_circle + 10, Position = position_circle },
            });

            AddAssert("nothing reported", () => misaimedPresses, () => Is.Empty);
            AddAssert("circle hit", () => currentPlayer.ScoreProcessor.Statistics.GetValueOrDefault(HitResult.Great), () => Is.EqualTo(1));
        }

        [Test]
        public void TestPressOutsideHitWindowsNotReported()
        {
            performTest(new List<ReplayFrame>
            {
                new OsuReplayFrame { Time = time_circle - 1000, Position = position_away, Actions = { OsuAction.LeftButton } },
                new OsuReplayFrame { Time = time_circle - 990, Position = position_away },
            });

            AddAssert("nothing reported", () => misaimedPresses, () => Is.Empty);
        }

        [Test]
        public void TestMisaimThenHitBothShown()
        {
            performTest(new List<ReplayFrame>
            {
                new OsuReplayFrame { Time = time_circle - 40, Position = position_away, Actions = { OsuAction.LeftButton } },
                new OsuReplayFrame { Time = time_circle - 30, Position = position_away },
                new OsuReplayFrame { Time = time_circle + 10, Position = position_circle, Actions = { OsuAction.RightButton } },
                new OsuReplayFrame { Time = time_circle + 20, Position = position_circle },
            });

            AddAssert("misaimed press reported", () => misaimedPresses, () => Has.Count.EqualTo(1));
            AddAssert("circle still hit", () => currentPlayer.ScoreProcessor.Statistics.GetValueOrDefault(HitResult.Great), () => Is.EqualTo(1));
        }

        [TestCase(300, true, false, null)]
        [TestCase(200, false, false, null)]
        [TestCase(300, true, true, "over 44")]
        [TestCase(200, false, true, "under 56")]
        public void TestIndicatorShowsOverOrUnderAim(float pressX, bool overaimed, bool details, string? expectedLabel)
        {
            const double time_first = 1000;
            var positionFirst = new Vector2(100, 192);

            AddStep("enable indicator", () => config.SetValue(OsuSetting.MisaimIndicator, true));
            AddStep(details ? "enable details" : "disable details", () => config.SetValue(OsuSetting.MisaimIndicatorDetails, details));

            // The cursor travels right from the first circle to the second, so pressing right of the second circle is overaiming.
            performTest(new List<ReplayFrame>
            {
                new OsuReplayFrame { Time = time_first, Position = positionFirst, Actions = { OsuAction.LeftButton } },
                new OsuReplayFrame { Time = time_first + 10, Position = positionFirst },
                new OsuReplayFrame { Time = time_circle, Position = new Vector2(pressX, position_circle.Y), Actions = { OsuAction.LeftButton } },
                new OsuReplayFrame { Time = time_circle + 10, Position = new Vector2(pressX, position_circle.Y) },
            }, new HitCircle { StartTime = time_first, Position = positionFirst }, holdAtEnd: true);

            AddAssert("misaimed press reported", () => misaimedPresses, () => Has.Count.EqualTo(1));
            AddUntilStep("one X shown", () => indicators().Length, () => Is.EqualTo(1));
            AddAssert(overaimed ? "overaimed" : "underaimed", () => indicators().Single().Overaimed, () => Is.EqualTo(overaimed));
            AddAssert("X where the press was", () => indicators().Single().PressPosition.X, () => Is.EqualTo(pressX).Within(1));

            if (details)
                AddAssert("distance shown", () => labels(), () => Is.EqualTo(new[] { expectedLabel }));
            else
            {
                AddAssert("no distance shown", () => labels(), () => Is.Empty);
                AddAssert("no circle or line drawn", () => indicators().Single().ChildrenOfType<CircularContainer>(), () => Is.Empty);
            }

            AddStep("disable indicator", () => config.SetValue(OsuSetting.MisaimIndicator, false));
            AddUntilStep("indicator removed", () => indicators(), () => Is.Empty);
        }

        [Test]
        public void TestIndicatorHiddenWhenDisabled()
        {
            AddStep("disable indicator", () => config.SetValue(OsuSetting.MisaimIndicator, false));

            performTest(new List<ReplayFrame>
            {
                new OsuReplayFrame { Time = time_circle, Position = position_away, Actions = { OsuAction.LeftButton } },
                new OsuReplayFrame { Time = time_circle + 10, Position = position_away },
            }, holdAtEnd: true);

            AddAssert("misaimed press still reported", () => misaimedPresses, () => Has.Count.EqualTo(1));
            AddAssert("no indicator", () => indicators(), () => Is.Empty);
        }

        /// <summary>
        /// The cursor travels right from a first circle to a second one at the middle of the playfield, and stays where given
        /// without pressing. Being left of the second circle is lagging behind, right of it is going past it.
        /// </summary>
        [TestCase(200, 192, AimError.Behind)]
        [TestCase(320, 192, AimError.Ahead)]
        public void TestMissFromAimIsClassified(float cursorX, float cursorY, AimError expected)
        {
            aimTest(new[]
            {
                new OsuReplayFrame { Time = time_circle - 200, Position = new Vector2(cursorX, cursorY) },
                new OsuReplayFrame { Time = time_circle + 400, Position = new Vector2(cursorX, cursorY) },
            });

            AddAssert("miss classified", () => aimMisses, () => Is.EqualTo(new[] { expected }));
        }

        [TestCase(256, 300, TestName = "TestMissMostlySidewaysIsNotClassified")]
        [TestCase(260, 195, TestName = "TestMissWithCursorOnCircleIsNotClassified")]
        public void TestMissNotCausedByLagOrOveraim(float cursorX, float cursorY)
        {
            aimTest(new[]
            {
                new OsuReplayFrame { Time = time_circle - 200, Position = new Vector2(cursorX, cursorY) },
                new OsuReplayFrame { Time = time_circle + 400, Position = new Vector2(cursorX, cursorY) },
            });

            AddAssert("circle missed", () => currentPlayer.ScoreProcessor.Statistics.GetValueOrDefault(HitResult.Miss), () => Is.EqualTo(1));
            AddAssert("not classified", () => aimMisses, () => Is.Empty);
        }

        [Test]
        public void TestMisaimedPressDecidesClassification()
        {
            // Pressed short of the circle, then carried on past it before the circle was missed. The press is what counts.
            aimTest(new[]
            {
                new OsuReplayFrame { Time = time_circle, Position = new Vector2(200, 192), Actions = { OsuAction.LeftButton } },
                new OsuReplayFrame { Time = time_circle + 10, Position = new Vector2(200, 192) },
                new OsuReplayFrame { Time = time_circle + 60, Position = new Vector2(340, 192) },
                new OsuReplayFrame { Time = time_circle + 400, Position = new Vector2(340, 192) },
            });

            AddAssert("misaimed press reported", () => misaimedPresses, () => Has.Count.EqualTo(1));
            AddAssert("classified once, from the press", () => aimMisses, () => Is.EqualTo(new[] { AimError.Behind }));
        }

        [Test]
        public void TestHitIsNotClassified()
        {
            aimTest(new[]
            {
                new OsuReplayFrame { Time = time_circle, Position = position_circle, Actions = { OsuAction.LeftButton } },
                new OsuReplayFrame { Time = time_circle + 10, Position = position_circle },
            });

            AddAssert("nothing classified", () => aimMisses, () => Is.Empty);
        }

        /// <summary>
        /// Plays a first circle (hit) to the left of a second circle in the middle of the playfield, followed by the given frames.
        /// </summary>
        private void aimTest(OsuReplayFrame[] frames)
        {
            const double time_first = 1000;
            var positionFirst = new Vector2(100, 192);

            var allFrames = new List<ReplayFrame>
            {
                new OsuReplayFrame { Time = time_first, Position = positionFirst, Actions = { OsuAction.LeftButton } },
                new OsuReplayFrame { Time = time_first + 10, Position = positionFirst },
            };

            allFrames.AddRange(frames);

            performTest(allFrames, new HitCircle { StartTime = time_first, Position = positionFirst });

            AddAssert("first circle hit", () => currentPlayer.ScoreProcessor.Statistics.GetValueOrDefault(HitResult.Great), () => Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void TestClassificationMaths()
        {
            var circle = new Vector2(256, 192);

            AddAssert("short is behind", () => AimMissDetector.Classify(circle, 30, new Vector2(200, 192), Vector2.UnitX), () => Is.EqualTo(AimError.Behind));
            AddAssert("past is ahead", () => AimMissDetector.Classify(circle, 30, new Vector2(300, 200), Vector2.UnitX), () => Is.EqualTo(AimError.Ahead));
            AddAssert("on the circle is neither", () => AimMissDetector.Classify(circle, 30, new Vector2(270, 192), Vector2.UnitX), () => Is.Null);
            AddAssert("sideways is neither", () => AimMissDetector.Classify(circle, 30, new Vector2(266, 280), Vector2.UnitX), () => Is.Null);
        }

        [Test]
        public void TestOveraimMaths()
        {
            AddAssert("past the circle is over", () => MisaimIndicatorLayer.GetOveraim(new Vector2(100, 100), new Vector2(130, 110), Vector2.UnitX), () => Is.EqualTo(30));
            AddAssert("short of the circle is under", () => MisaimIndicatorLayer.GetOveraim(new Vector2(100, 100), new Vector2(100, 80), Vector2.UnitY), () => Is.EqualTo(-20));
        }

        private MisaimIndicatorLayer.MisaimIndicator[] indicators() =>
            currentPlayer.ChildrenOfType<MisaimIndicatorLayer.MisaimIndicator>().ToArray();

        private string[] labels() => indicators().SelectMany(i => i.ChildrenOfType<OsuSpriteText>()).Select(t => t.Text.ToString()).ToArray();

        private void performTest(List<ReplayFrame> frames, HitCircle? before = null, bool holdAtEnd = false)
        {
            AddStep("load player", () =>
            {
                Beatmap.Value = CreateWorkingBeatmap(new Beatmap<OsuHitObject>
                {
                    HitObjects = before == null
                        ? new List<OsuHitObject> { new HitCircle { StartTime = time_circle, Position = position_circle } }
                        : new List<OsuHitObject> { before, new HitCircle { StartTime = time_circle, Position = position_circle } },
                    BeatmapInfo =
                    {
                        Difficulty = new BeatmapDifficulty { OverallDifficulty = 5 },
                        Ruleset = new OsuRuleset().RulesetInfo
                    },
                });

                misaimedPresses = new List<double>();
                aimMisses = new List<AimError>();

                var p = new ScoreAccessibleReplayPlayer(new Score { Replay = new Replay { Frames = frames } });
                p.OnLoadComplete += _ =>
                {
                    var playfield = p.ChildrenOfType<OsuPlayfield>().Single();
                    playfield.MisaimedPress += (_, offset) => misaimedPresses.Add(offset);
                    playfield.AimMiss += (_, error) => aimMisses.Add(error);
                };

                LoadScreen(currentPlayer = p);
            });

            AddUntilStep("Beatmap at 0", () => Beatmap.Value.Track.CurrentTime == 0);
            AddUntilStep("Wait until player is loaded", () => currentPlayer.IsCurrentScreen());

            if (holdAtEnd)
            {
                // Stop just after the press, while the indicator is still showing.
                AddUntilStep("Wait for press", () => currentPlayer.GameplayClockContainer.CurrentTime > time_circle + 50);
                AddStep("Pause", () => currentPlayer.GameplayClockContainer.Stop());
            }
            else
                AddUntilStep("Wait for completion", () => currentPlayer.ScoreProcessor.HasCompleted.Value);
        }

        private partial class ScoreAccessibleReplayPlayer : ReplayPlayer
        {
            public new ScoreProcessor ScoreProcessor => base.ScoreProcessor;

            public new GameplayClockContainer GameplayClockContainer => base.GameplayClockContainer;

            protected override bool PauseOnFocusLost => false;

            public ScoreAccessibleReplayPlayer(Score score)
                : base(score, new PlayerConfiguration
                {
                    AllowPause = false,
                    ShowResults = false,
                })
            {
            }
        }
    }
}
