// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Game.Configuration;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Tests.Visual.Navigation
{
    /// <summary>
    /// Making Score V2 the default swaps which of the Score V2 and Lazer Scoring mods can be selected, and drops the one
    /// which no longer makes sense from the selected mods.
    /// </summary>
    public partial class TestSceneScoreV2Default : OsuGameTestScene
    {
        private Mod scoreV2Mod => Game.AvailableMods.Value[ModType.Conversion].Single(m => m is OsuModScoreV2);
        private Mod lazerScoringMod => Game.AvailableMods.Value[ModType.Conversion].Single(m => m is OsuModLazerScoring);

        public override void SetUpSteps()
        {
            base.SetUpSteps();
            AddStep("default off", () => Game.LocalConfig.SetValue(OsuSetting.ScoreV2ByDefault, false));
        }

        [Test]
        public void TestEnablingDefaultSwapsMods()
        {
            AddAssert("score v2 offered", () => scoreV2Mod.UserPlayable, () => Is.True);
            AddAssert("lazer scoring not offered", () => lazerScoringMod.UserPlayable, () => Is.False);

            AddStep("select score v2", () => Game.SelectedMods.Value = new Mod[] { new OsuModScoreV2() });
            AddStep("enable default", () => Game.LocalConfig.SetValue(OsuSetting.ScoreV2ByDefault, true));

            AddAssert("setting reaches the mods", () => ScoringFormulaDefaults.ScoreV2ByDefault.Value, () => Is.True);
            AddAssert("score v2 no longer offered", () => scoreV2Mod.UserPlayable, () => Is.False);
            AddAssert("lazer scoring offered", () => lazerScoringMod.UserPlayable, () => Is.True);
            AddAssert("score v2 dropped from selection", () => Game.SelectedMods.Value, () => Is.Empty);

            AddStep("select lazer scoring and hidden", () => Game.SelectedMods.Value = new Mod[] { new OsuModLazerScoring(), new OsuModHidden() });
            AddStep("disable default", () => Game.LocalConfig.SetValue(OsuSetting.ScoreV2ByDefault, false));

            AddAssert("lazer scoring dropped, hidden kept", () => Game.SelectedMods.Value.Select(m => m.GetType()), () => Is.EqualTo(new[] { typeof(OsuModHidden) }));
        }
    }
}
