// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Configuration;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Screens.Play;

namespace osu.Game.Tests.Visual.Gameplay
{
    /// <summary>
    /// The music's pitch can be changed from the mod select overlay, without it changing the speed of play.
    /// </summary>
    public partial class TestSceneMusicPitch : PlayerTestScene
    {
        private double pitch = 1;

        protected override Ruleset CreatePlayerRuleset() => new OsuRuleset();

        [SetUpSteps]
        public override void SetUpSteps()
        {
            // Has to be in place before the player loads.
            AddStep("set pitch", () => LocalConfig.SetValue(OsuSetting.MusicPitch, pitch));
            base.SetUpSteps();
        }

        [TearDownSteps]
        public void TearDownSteps() => AddStep("reset pitch", () => LocalConfig.SetValue(OsuSetting.MusicPitch, 1.0));

        [Test]
        public void TestPitchDoesNotChangeSpeed()
        {
            pitch = 1.5;

            AddUntilStep("gameplay running", () => Player.GameplayClockContainer.IsRunning);
            AddAssert("music pitched up", () => Player.GameplayClockContainer.AdjustmentsFromMods.AggregateFrequency.Value, () => Is.EqualTo(1.5).Within(1e-9));
            AddAssert("played at normal speed", () => Player.GameplayClockContainer.GetTrueGameplayRate(), () => Is.EqualTo(1).Within(1e-9));
        }

        [Test]
        public void TestPitchWithDoubleTime()
        {
            pitch = 0.8;

            AddStep("load player with double time", () => LoadPlayer(new Mod[] { new OsuModDoubleTime() }));
            AddUntilStep("gameplay running", () => Player.IsLoaded && Player.GameplayClockContainer.IsRunning);
            AddAssert("music pitched down", () => Player.GameplayClockContainer.AdjustmentsFromMods.AggregateFrequency.Value, () => Is.EqualTo(0.8).Within(1e-9));
            AddAssert("played at double time's speed", () => Player.GameplayClockContainer.GetTrueGameplayRate(), () => Is.EqualTo(1.5).Within(1e-9));
        }

        [Test]
        public void TestChangeDuringPlayIsHeard()
        {
            pitch = 1;

            AddUntilStep("gameplay running", () => Player.GameplayClockContainer.IsRunning);
            AddStep("raise pitch", () => LocalConfig.SetValue(OsuSetting.MusicPitch, 1.25));
            AddAssert("music pitched up", () => Player.GameplayClockContainer.AdjustmentsFromMods.AggregateFrequency.Value, () => Is.EqualTo(1.25).Within(1e-9));
            AddAssert("played at normal speed", () => Player.GameplayClockContainer.GetTrueGameplayRate(), () => Is.EqualTo(1).Within(1e-9));
        }
    }
}
