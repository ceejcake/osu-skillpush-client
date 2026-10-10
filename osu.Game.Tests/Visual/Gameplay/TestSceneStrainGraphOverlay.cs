// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play;
using osuTK;
using osuTK.Input;

namespace osu.Game.Tests.Visual.Gameplay
{
    public partial class TestSceneStrainGraphOverlay : PlayerTestScene
    {
        protected override Ruleset CreatePlayerRuleset() => new OsuRuleset();

        private StrainGraphOverlay overlay => Player.ChildrenOfType<StrainGraphOverlay>().Single();

        private StrainGraphOverlay.StrainGraph graph => Player.ChildrenOfType<StrainGraphOverlay.StrainGraph>().Single();

        private double timeBeforeSeek;

        private double lowestTimeObserved;

        private int missCount() => Player.ScoreProcessor.Statistics.GetValueOrDefault(HitResult.Miss);

        [Test]
        public void TestToggleFreezesGameplayAndClickSeeks()
        {
            AddUntilStep("gameplay running", () => !Player.GameplayClockContainer.IsPaused.Value && Player.GameplayClockContainer.CurrentTime > 0);

            AddStep("press tab", () => InputManager.Key(Key.Tab));

            AddUntilStep("overlay shown", () => overlay.State.Value == Visibility.Visible);
            AddUntilStep("gameplay frozen", () => Player.GameplayClockContainer.IsPaused.Value);
            AddUntilStep("strain graph populated", () => graph.Alpha > 0 && graph.Sections.Count > 1);

            AddStep("click late in the graph", () =>
            {
                timeBeforeSeek = Player.GameplayClockContainer.CurrentTime;

                var quad = graph.ScreenSpaceDrawQuad;
                InputManager.MoveMouseTo(new Vector2(quad.TopLeft.X + quad.Width * 0.8f, quad.Centre.Y));
                InputManager.Click(MouseButton.Left);
            });

            AddUntilStep("overlay hidden", () => overlay.State.Value == Visibility.Hidden);
            AddUntilStep("gameplay running again", () => !Player.GameplayClockContainer.IsPaused.Value);
            AddUntilStep("gameplay seeked forward", () => Player.GameplayClockContainer.CurrentTime > timeBeforeSeek + 2000);
            AddAssert("play invalidated", () => Player.GameplayInvalidatedBySeeking);

            // Skipped objects must not count against the player, whether or not they were judged during the seek.
            AddUntilStep("statistics reset", () => missCount() < 20 && Player.ScoreProcessor.JudgedHits < 20);
            AddAssert("did not fail from skipped objects", () => !Player.HealthProcessor.HasFailed);

            AddStep("press tab", () => InputManager.Key(Key.Tab));
            AddUntilStep("overlay shown", () => overlay.State.Value == Visibility.Visible);

            AddStep("click early in the graph", () =>
            {
                timeBeforeSeek = Player.GameplayClockContainer.CurrentTime;

                var quad = graph.ScreenSpaceDrawQuad;
                InputManager.MoveMouseTo(new Vector2(quad.TopLeft.X + quad.Width * 0.1f, quad.Centre.Y));
                InputManager.Click(MouseButton.Left);
            });

            AddUntilStep("gameplay seeked backward", () => Player.GameplayClockContainer.CurrentTime < timeBeforeSeek - 2000);
            AddUntilStep("gameplay running again", () => !Player.GameplayClockContainer.IsPaused.Value);
        }

        [Test]
        public void TestArrowKeysSeek()
        {
            AddUntilStep("gameplay running", () => !Player.GameplayClockContainer.IsPaused.Value && Player.GameplayClockContainer.CurrentTime > 0);

            AddStep("press right", () =>
            {
                timeBeforeSeek = Player.GameplayClockContainer.CurrentTime;
                InputManager.Key(Key.Right);
            });

            AddUntilStep("seeked forward five seconds", () => Player.GameplayClockContainer.CurrentTime, () => Is.EqualTo(timeBeforeSeek + 5000).Within(1000));
            AddAssert("play invalidated", () => Player.GameplayInvalidatedBySeeking);
            AddUntilStep("statistics reset", () => missCount() < 20 && Player.ScoreProcessor.JudgedHits < 20);

            AddStep("press left", () =>
            {
                timeBeforeSeek = Player.GameplayClockContainer.CurrentTime;
                lowestTimeObserved = double.MaxValue;
                InputManager.Key(Key.Left);
            });

            // Gameplay keeps running while the assertion is retried, so the dip in time is what's checked for.
            AddUntilStep("seeked backward five seconds", () =>
            {
                lowestTimeObserved = Math.Min(lowestTimeObserved, Player.GameplayClockContainer.CurrentTime);
                return lowestTimeObserved <= timeBeforeSeek - 4000;
            });

            AddUntilStep("gameplay still running", () => !Player.GameplayClockContainer.IsPaused.Value);
        }

        [Test]
        public void TestToggleBackResumesGameplay()
        {
            AddUntilStep("gameplay running", () => !Player.GameplayClockContainer.IsPaused.Value && Player.GameplayClockContainer.CurrentTime > 0);

            AddStep("press tab", () => InputManager.Key(Key.Tab));
            AddUntilStep("overlay shown", () => overlay.State.Value == Visibility.Visible);
            AddUntilStep("gameplay frozen", () => Player.GameplayClockContainer.IsPaused.Value);

            AddStep("press tab again", () => InputManager.Key(Key.Tab));
            AddUntilStep("overlay hidden", () => overlay.State.Value == Visibility.Hidden);
            AddUntilStep("gameplay running again", () => !Player.GameplayClockContainer.IsPaused.Value);
            AddAssert("play still valid", () => !Player.GameplayInvalidatedBySeeking);
        }
    }
}
