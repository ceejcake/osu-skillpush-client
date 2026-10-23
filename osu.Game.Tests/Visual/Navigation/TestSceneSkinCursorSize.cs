// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Game.Configuration;
using osu.Game.Skinning;

namespace osu.Game.Tests.Visual.Navigation
{
    public partial class TestSceneSkinCursorSize : OsuGameTestScene
    {
        private SkinManager skins => Game.Dependencies.Get<SkinManager>();

        [Test]
        public void TestCursorSizeFollowsSkin()
        {
            // Bindable bindings are weak, so make sure nothing the game relies on is only weakly held.
            AddStep("collect garbage", () =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            });

            AddStep("set size 0.7 on argon",() => Game.LocalConfig.SetValue(OsuSetting.GameplayCursorSize, 0.7f));

            AddStep("switch to classic", () => skins.SetSkinFromConfiguration(SkinInfo.CLASSIC_SKIN.ToString()));
            AddStep("set size 1.4 on classic", () => Game.LocalConfig.SetValue(OsuSetting.GameplayCursorSize, 1.4f));

            AddStep("switch to argon", () => skins.SetSkinFromConfiguration(SkinInfo.ARGON_SKIN.ToString()));
            AddAssert("size is 0.7", () => Game.LocalConfig.Get<float>(OsuSetting.GameplayCursorSize), () => Is.EqualTo(0.7f));

            AddStep("switch to classic", () => skins.SetSkinFromConfiguration(SkinInfo.CLASSIC_SKIN.ToString()));
            AddAssert("size is 1.4", () => Game.LocalConfig.Get<float>(OsuSetting.GameplayCursorSize), () => Is.EqualTo(1.4f));
        }
    }
}
