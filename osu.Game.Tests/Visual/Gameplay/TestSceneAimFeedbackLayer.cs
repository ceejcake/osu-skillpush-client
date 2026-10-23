// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Testing;
using osu.Game.Configuration;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Play.HUD;

namespace osu.Game.Tests.Visual.Gameplay
{
    /// <summary>
    /// The sides of the screen glow red when objects are missed because the aim is lagging behind, and blue when it is going past them.
    /// A single miss is a faint pulse; misses in quick succession build up to a full glow, which then fades.
    /// </summary>
    public partial class TestSceneAimFeedbackLayer : OsuTestScene
    {
        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        private TestPlayfield playfield = null!;
        private AimFeedbackLayer layer = null!;

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("enable setting", () => config.SetValue(OsuSetting.AimFeedbackGlow, true));
            AddStep("create layer", () => Child = layer = new AimFeedbackLayer(playfield = new TestPlayfield()));
            AddUntilStep("layer loaded", () => layer.IsLoaded);
        }

        [Test]
        public void TestMissesBuildUpGlowOfTheirColour()
        {
            // The headless test clock can jump far ahead between steps, which would fade the glow away before it is checked,
            // so strengths are read in the same step as the misses are added.
            float afterOne = 0, afterThree = 0, ahead = 0;

            AddStep("miss from lagging behind", () => playfield.Miss(AimError.Behind));
            AddUntilStep("reaches the layer", () => layer.BehindStrength, () => Is.GreaterThan(0));
            AddAssert("no blue glow", () => layer.AheadStrength, () => Is.Zero);

            AddStep("one miss, then three in a row", () =>
            {
                layer.Reset();
                layer.AddMiss(AimError.Behind);
                afterOne = layer.BehindStrength;
                layer.AddMiss(AimError.Behind);
                layer.AddMiss(AimError.Behind);
                afterThree = layer.BehindStrength;
                ahead = layer.AheadStrength;
            });

            AddAssert("single miss is a partial glow", () => afterOne, () => Is.EqualTo(AimFeedbackLayer.MISS_STRENGTH));
            AddAssert("misses in a row build to full glow", () => afterThree, () => Is.EqualTo(1));
            AddAssert("other colour untouched", () => ahead, () => Is.Zero);

            AddUntilStep("glow fades away", () => layer.BehindStrength, () => Is.LessThan(0.05f));

            AddStep("miss from going too fast", () => playfield.Miss(AimError.Ahead));
            AddUntilStep("blue glow reaches the layer", () => layer.AheadStrength, () => Is.GreaterThan(0));
        }

        [Test]
        public void TestDisabledSettingShowsNothing()
        {
            AddStep("disable setting", () => config.SetValue(OsuSetting.AimFeedbackGlow, false));
            AddStep("misses", () =>
            {
                playfield.Miss(AimError.Behind);
                playfield.Miss(AimError.Ahead);
            });
            AddWaitStep("give it time", 3);
            AddAssert("no glow", () => layer.BehindStrength + layer.AheadStrength, () => Is.Zero);
            AddStep("add misses directly", () =>
            {
                layer.AddMiss(AimError.Behind);
                layer.AddMiss(AimError.Ahead);
            });
            AddAssert("still no glow", () => layer.BehindStrength + layer.AheadStrength, () => Is.Zero);
            AddStep("enable setting", () => config.SetValue(OsuSetting.AimFeedbackGlow, true));
        }

        private partial class TestPlayfield : Playfield
        {
            public void Miss(AimError error) => TriggerAimMiss(new HitCircle(), error);
        }
    }
}
