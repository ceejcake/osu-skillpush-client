// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using System;
using osu.Framework.Allocation;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Overlays;
using osu.Game.Screens.Play;
using osuTK;
using osuTK.Input;

namespace osu.Game.Tests.Visual.Gameplay
{
    public partial class TestScenePracticeCheckpoint : PlayerTestScene
    {
        [Resolved]
        private SessionStatics statics { get; set; } = null!;

        [Cached]
        private readonly OnScreenDisplay onScreenDisplay = new OnScreenDisplay();

        public TestScenePracticeCheckpoint()
        {
            Add(onScreenDisplay);
        }

        protected override Ruleset CreatePlayerRuleset() => new OsuRuleset();

        // Restarting produces a new beatmap instance, but it is the same beatmap as far as the checkpoint is concerned.
        private static readonly Guid beatmap_id = Guid.NewGuid();

        protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
        {
            var beatmap = new Beatmap { BeatmapInfo = { Ruleset = ruleset, ID = beatmap_id } };

            for (int i = 0; i < 60; i++)
            {
                // Positions vary so that the beatmap has some difficulty for the strain graph to show.
                beatmap.HitObjects.Add(new HitCircle { StartTime = 1000 + i * 1000, Position = new Vector2(100 + i % 5 * 60, 100 + i % 3 * 60) });
            }

            return beatmap;
        }

        [SetUpSteps]
        public override void SetUpSteps()
        {
            AddStep("clear checkpoint", () => statics.SetValue<PracticeCheckpoint>(Static.PracticeCheckpoint, null));
            base.SetUpSteps();
        }

        [Test]
        public void TestCheckpointFollowsStrainGraphSeek()
        {
            AddUntilStep("gameplay running", () => !Player.GameplayClockContainer.IsPaused.Value && Player.GameplayClockContainer.CurrentTime > 0);

            AddStep("press F7", () => InputManager.Key(Key.F7));
            AddAssert("checkpoint set", () => checkpoint != null);

            AddStep("open strain graph", () => InputManager.Key(Key.Tab));

            AddUntilStep("graph ready", () => strainGraph.Alpha > 0 && strainGraph.Sections.Count > 1);

            AddStep("click late in the graph", () =>
            {
                var quad = strainGraph.ScreenSpaceDrawQuad;
                InputManager.MoveMouseTo(new Vector2(quad.TopLeft.X + quad.Width * 0.8f, quad.Centre.Y));
                InputManager.Click(MouseButton.Left);
            });

            AddUntilStep("checkpoint followed the seek", () => checkpoint!.Time, () => Is.GreaterThan(30000));

            AddStep("press F7 again", () => InputManager.Key(Key.F7));
            AddAssert("checkpoint cleared", () => checkpoint == null);
        }

        [Test]
        public void TestArrowSeekDoesNotAffectCheckpoint()
        {
            AddUntilStep("gameplay running", () => !Player.GameplayClockContainer.IsPaused.Value && Player.GameplayClockContainer.CurrentTime > 0);

            AddStep("press F7", () => InputManager.Key(Key.F7));
            AddAssert("checkpoint set", () => checkpoint != null);

            double checkpointTime = 0;
            AddStep("store checkpoint time", () => checkpointTime = checkpoint!.Time);

            AddStep("seek forward with arrow", () => InputManager.Key(Key.Right));
            AddWaitStep("wait a moment", 5);
            AddAssert("checkpoint unchanged", () => checkpoint!.Time, () => Is.EqualTo(checkpointTime));

            AddStep("seek backward with arrow", () => InputManager.Key(Key.Left));
            AddWaitStep("wait a moment", 5);
            AddAssert("checkpoint still unchanged", () => checkpoint!.Time, () => Is.EqualTo(checkpointTime));
        }

        [Test]
        public void TestExitingClearsCheckpoint()
        {
            AddUntilStep("gameplay running", () => !Player.GameplayClockContainer.IsPaused.Value && Player.GameplayClockContainer.CurrentTime > 0);

            AddStep("press F7", () => InputManager.Key(Key.F7));
            AddAssert("checkpoint set", () => checkpoint != null);

            AddStep("exit gameplay", () => Player.Exit());

            AddUntilStep("checkpoint cleared", () => checkpoint == null);
        }

        [Test]
        public void TestRestartingKeepsCheckpoint()
        {
            AddUntilStep("gameplay running", () => !Player.GameplayClockContainer.IsPaused.Value && Player.GameplayClockContainer.CurrentTime > 0);

            AddStep("press F7", () => InputManager.Key(Key.F7));
            AddAssert("checkpoint set", () => checkpoint != null);

            AddStep("restart", () => Player.Restart());

            AddWaitStep("wait for exit", 10);
            AddAssert("checkpoint kept", () => checkpoint != null);
        }

        [Test]
        public void TestGameplayStartsAtCheckpoint()
        {
            AddStep("exit player", () => Player.Exit());
            AddUntilStep("player exited", () => !Player.IsCurrentScreen());

            AddStep("set a checkpoint", () => statics.SetValue<PracticeCheckpoint>(Static.PracticeCheckpoint,
                new PracticeCheckpoint(Beatmap.Value.BeatmapInfo.ID, 30000)));

            AddStep("reload player", LoadPlayer);
            AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);

            AddUntilStep("gameplay began at the checkpoint", () => Player.GameplayClockContainer.CurrentTime, () => Is.GreaterThan(29000));
            AddAssert("play invalidated", () => Player.GameplayInvalidatedBySeeking);

            // The clock has to keep running after landing on the checkpoint, rather than sitting still.
            double timeOnArrival = 0;
            AddStep("store arrival time", () => timeOnArrival = Player.GameplayClockContainer.CurrentTime);
            AddUntilStep("gameplay is progressing", () => Player.GameplayClockContainer.CurrentTime, () => Is.GreaterThan(timeOnArrival + 1000));
            AddAssert("clock is running", () => Player.GameplayClockContainer.IsRunning);
        }

        [Test]
        public void TestCheckpointForAnotherBeatmapIsIgnored()
        {
            AddStep("exit player", () => Player.Exit());
            AddUntilStep("player exited", () => !Player.IsCurrentScreen());

            AddStep("set a checkpoint for another beatmap", () => statics.SetValue<PracticeCheckpoint>(Static.PracticeCheckpoint,
                new PracticeCheckpoint(Guid.NewGuid(), 30000)));

            AddStep("reload player", LoadPlayer);
            AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);

            AddUntilStep("gameplay running", () => !Player.GameplayClockContainer.IsPaused.Value);
            AddAssert("gameplay began at the start", () => Player.GameplayClockContainer.CurrentTime, () => Is.LessThan(10000));
            AddAssert("play not invalidated", () => !Player.GameplayInvalidatedBySeeking);
        }

        private PracticeCheckpoint? checkpoint => statics.Get<PracticeCheckpoint>(Static.PracticeCheckpoint);

        private StrainGraphOverlay.StrainGraph strainGraph => Player.ChildrenOfType<StrainGraphOverlay.StrainGraph>().Single();
    }
}
