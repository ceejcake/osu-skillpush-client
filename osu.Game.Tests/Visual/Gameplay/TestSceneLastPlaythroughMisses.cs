// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Screens.Play;
using osuTK;
using osuTK.Input;

namespace osu.Game.Tests.Visual.Gameplay
{
    /// <summary>
    /// The practice strain graph shows where the misses were in the last full playthrough of the beatmap,
    /// which is kept across restarts and partial plays until the beatmap is played through again, or a different beatmap is played.
    /// </summary>
    public partial class TestSceneLastPlaythroughMisses : PlayerTestScene
    {
        [Resolved]
        private SessionStatics statics { get; set; } = null!;

        // Restarting produces a new beatmap instance, but it is the same beatmap as far as the misses are concerned.
        private static readonly Guid beatmap_id = Guid.NewGuid();

        private static readonly double[] object_times = { 1000, 1500, 2000, 2500 };

        protected override Ruleset CreatePlayerRuleset() => new OsuRuleset();

        protected override bool HasCustomSteps => true;

        protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
        {
            var beatmap = new Beatmap { BeatmapInfo = { Ruleset = ruleset, ID = beatmap_id } };

            for (int i = 0; i < object_times.Length; i++)
                beatmap.HitObjects.Add(new HitCircle { StartTime = object_times[i], Position = new Vector2(100 + i * 80, 100 + i % 2 * 100) });

            return beatmap;
        }

        private LastPlaythroughMisses? last => statics.Get<LastPlaythroughMisses>(Static.LastPlaythroughMisses);

        private StrainGraphOverlay.StrainGraph graph => Player.ChildrenOfType<StrainGraphOverlay.StrainGraph>().Single();

        [Test]
        public void TestFullPlaythroughRecordsMisses()
        {
            AddStep("clear misses", () => statics.SetValue<LastPlaythroughMisses>(Static.LastPlaythroughMisses, null));
            CreateTest();

            // Nothing is pressed, so every object is missed.
            AddUntilStep("play through", () => Player.ScoreProcessor.HasCompleted.Value);
            AddUntilStep("misses recorded", () => last?.MissTimes, () => Is.EqualTo(object_times));
            AddAssert("for this beatmap", () => last!.BeatmapID, () => Is.EqualTo(beatmap_id));
        }

        [Test]
        public void TestMissesShownOnGraph()
        {
            AddStep("set misses of last playthrough", () => statics.SetValue(Static.LastPlaythroughMisses, new LastPlaythroughMisses(beatmap_id, new[] { 1500.0, 2500.0 })));
            CreateTest();

            AddUntilStep("gameplay running", () => !Player.GameplayClockContainer.IsPaused.Value && Player.GameplayClockContainer.CurrentTime > 0);
            AddStep("open strain graph", () => InputManager.Key(Key.Tab));
            AddUntilStep("graph ready", () => graph.Alpha > 0 && graph.Sections.Count > 1);

            AddAssert("misses on graph", () => graph.MissTimes, () => Is.EqualTo(new[] { 1500.0, 2500.0 }));
            AddAssert("misses explained", () => Player.ChildrenOfType<OsuSpriteText>().Any(t => t.Text.ToString() == "Red lines: the 2 misses in your last full playthrough."));
        }

        [Test]
        public void TestSeekedPlayDoesNotReplaceMisses()
        {
            AddStep("set misses of last playthrough", () => statics.SetValue(Static.LastPlaythroughMisses, new LastPlaythroughMisses(beatmap_id, new[] { 1234.0 })));
            CreateTest();

            AddUntilStep("gameplay running", () => !Player.GameplayClockContainer.IsPaused.Value && Player.GameplayClockContainer.CurrentTime > 0);
            AddStep("seek forwards", () => InputManager.Key(Key.Right));

            AddUntilStep("play finished", () => Player.GameplayState.HasPassed);
            AddAssert("misses kept", () => last?.MissTimes, () => Is.EqualTo(new[] { 1234.0 }));
        }

        [Test]
        public void TestRestartedPlayDoesNotReplaceMisses()
        {
            AddStep("set misses of last playthrough", () => statics.SetValue(Static.LastPlaythroughMisses, new LastPlaythroughMisses(beatmap_id, new[] { 1234.0 })));
            CreateTest();

            AddUntilStep("first object missed", () => Player.ScoreProcessor.JudgedHits, () => Is.GreaterThan(0));
            AddStep("restart", () => Player.Restart());
            AddUntilStep("player exited", () => !Player.IsCurrentScreen());

            AddAssert("misses kept", () => last?.MissTimes, () => Is.EqualTo(new[] { 1234.0 }));
        }

        [Test]
        public void TestDifferentBeatmapClearsMisses()
        {
            AddStep("set misses of another beatmap", () => statics.SetValue(Static.LastPlaythroughMisses, new LastPlaythroughMisses(Guid.NewGuid(), new[] { 1234.0 })));
            CreateTest();

            AddUntilStep("misses cleared", () => last, () => Is.Null);
        }
    }
}
