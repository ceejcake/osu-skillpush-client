// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Testing;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays.Settings.Sections;
using osu.Game.Skinning;

namespace osu.Game.Tests.Visual.Navigation
{
    public partial class TestSceneFavouriteSkins : OsuGameTestScene
    {
        private FormDropdown<Live<SkinInfo>>? skinDropdown => Game.Settings.ChildrenOfType<FormDropdown<Live<SkinInfo>>>().SingleOrDefault();

        [Test]
        public void TestFavouriteSkinShownFirst()
        {
            AddStep("open settings", () => Game.Settings.Show());
            AddUntilStep("wait for skin dropdown", () => skinDropdown != null);
            AddUntilStep("argon is first", () => skinDropdown!.Items.First().ID == SkinInfo.ARGON_SKIN);

            AddStep("click heart", () => getMenuItem(SkinInfo.CLASSIC_SKIN).ChildrenOfType<IconButton>().Single().TriggerClick());

            AddAssert("classic is favourite", () => SkinSection.ParseFavouriteSkins(Game.LocalConfig.Get<string>(OsuSetting.FavouriteSkins)).Contains(SkinInfo.CLASSIC_SKIN));
            AddUntilStep("classic is first", () => skinDropdown!.Items.First().ID == SkinInfo.CLASSIC_SKIN);
            AddAssert("current skin unchanged", () => Game.Dependencies.Get<SkinManager>().CurrentSkinInfo.Value.ID == SkinInfo.ARGON_SKIN);

            AddStep("click heart again", () => getMenuItem(SkinInfo.CLASSIC_SKIN).ChildrenOfType<IconButton>().Single().TriggerClick());

            AddUntilStep("argon is first again", () => skinDropdown!.Items.First().ID == SkinInfo.ARGON_SKIN);
        }

        private Menu.DrawableMenuItem getMenuItem(System.Guid skinId) =>
            skinDropdown!.ChildrenOfType<Menu.DrawableMenuItem>().Single(i => ((DropdownMenuItem<Live<SkinInfo>>)i.Item).Value.ID == skinId);
    }
}
