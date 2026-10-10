// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Overlays;
using osu.Game.Rulesets;
using osu.Game.Tests.Resources;
using osuTK.Input;
using CollectionDropdown = osu.Game.Screens.Select.CollectionDropdown;

namespace osu.Game.Tests.Visual.SongSelect
{
    public partial class TestSceneCollectionDropdownFolders : OsuManualInputManagerTestScene
    {
        private CollectionDropdown dropdown = null!;
        private CollectionFolderManager folders = null!;

        [Cached]
        private readonly OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Aquamarine);

        [BackgroundDependencyLoader]
        private void load(GameHost host)
        {
            Dependencies.Cache(new RealmRulesetStore(Realm));
            var beatmapManager = new BeatmapManager(LocalStorage, Realm, null, Audio, Resources, host, Beatmap.Default);
            Dependencies.Cache(beatmapManager);
            Dependencies.Cache(Realm);
            Dependencies.Cache(folders = new CollectionFolderManager(LocalStorage));

            beatmapManager.Import(TestResources.GetQuickTestBeatmapForImport()).WaitSafely();
        }

        [SetUp]
        public void SetUp() => Schedule(() =>
        {
            foreach (string folder in folders.Folders)
                folders.DeleteFolder(folder);

            Realm.Write(r =>
            {
                r.RemoveAll<BeatmapCollection>();

                var alpha = new BeatmapCollection("alpha");
                var bravo = new BeatmapCollection("bravo");
                r.Add(alpha);
                r.Add(bravo);
                r.Add(new BeatmapCollection("charlie"));

                folders.AddToFolder(alpha.ID, "Speed");
                folders.AddToFolder(bravo.ID, "Speed");
            });

            Child = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Child = dropdown = new CollectionDropdown
                {
                    Width = 300,
                    Y = 100,
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                },
            };
        });

        [Test]
        public void TestFolderCollapseAndSearch()
        {
            AddStep("expand dropdown", () =>
            {
                InputManager.MoveMouseTo(dropdown.ChildrenOfType<CollectionDropdown.ShearedDropdownHeader>().Single());
                InputManager.Click(MouseButton.Left);
            });

            assertPresent("Speed (2)", true);
            assertPresent("alpha", false);
            assertPresent("bravo", false);
            assertPresent("charlie", true);

            AddStep("click folder", () =>
            {
                InputManager.MoveMouseTo(getItem("Speed (2)"));
                InputManager.Click(MouseButton.Left);
            });

            assertPresent("alpha", true);
            assertPresent("bravo", true);
            AddAssert("menu still open", () => dropdown.ChildrenOfType<Menu>().First().State == MenuState.Open);

            AddStep("click folder again", () =>
            {
                InputManager.MoveMouseTo(getItem("Speed (2)"));
                InputManager.Click(MouseButton.Left);
            });

            assertPresent("alpha", false);

            AddStep("search 'alp'", () => dropdown.ChildrenOfType<CollectionDropdown.DropdownMenu>().Single().SearchTerm = "alp");

            assertPresent("alpha", true);
            assertPresent("Speed (2)", false);
            assertPresent("bravo", false);
            assertPresent("charlie", false);

            AddStep("search 'speed'", () => dropdown.ChildrenOfType<CollectionDropdown.DropdownMenu>().Single().SearchTerm = "speed");
            assertPresent("Speed (2)", false);
            assertPresent("alpha", false);

            AddStep("clear search", () => dropdown.ChildrenOfType<CollectionDropdown.DropdownMenu>().Single().SearchTerm = string.Empty);
            assertPresent("Speed (2)", true);
            assertPresent("alpha", false);
            assertPresent("charlie", true);
        }

        [Test]
        public void TestMovingCollectionUpdatesDropdown()
        {
            AddStep("move charlie to folder", () => folders.AddToFolder(Realm.Run(r => r.All<BeatmapCollection>().Single(c => c.Name == "charlie").ID), "Speed"));
            AddUntilStep("folder count updated", () => getItem("Speed (3)") != null);
        }

        [Test]
        public void TestCollectionInMultipleFolders()
        {
            AddStep("add alpha to second folder", () => folders.AddToFolder(Realm.Run(r => r.All<BeatmapCollection>().Single(c => c.Name == "alpha").ID), "Aim"));
            AddUntilStep("alpha listed twice", () => dropdown.ChildrenOfType<Menu.DrawableMenuItem>().Count(i => i.Item.Text.Value.ToString() == "alpha"), () => Is.EqualTo(2));
            AddUntilStep("aim folder listed", () => getItem("Aim (1)") != null);

            AddStep("expand dropdown", () =>
            {
                InputManager.MoveMouseTo(dropdown.ChildrenOfType<CollectionDropdown.ShearedDropdownHeader>().Single());
                InputManager.Click(MouseButton.Left);
            });

            AddStep("search 'alp'", () => dropdown.ChildrenOfType<CollectionDropdown.DropdownMenu>().Single().SearchTerm = "alp");
            AddUntilStep("alpha shown once", () => dropdown.ChildrenOfType<Menu.DrawableMenuItem>().Count(i => i.Item.Text.Value.ToString() == "alpha" && i.IsPresent), () => Is.EqualTo(1));
        }

        private Menu.DrawableMenuItem getItem(string text) =>
            dropdown.ChildrenOfType<Menu.DrawableMenuItem>().FirstOrDefault(i => i.Item.Text.Value.ToString() == text)!;

        private void assertPresent(string text, bool present) =>
            AddUntilStep($"'{text}' {(present ? "visible" : "hidden")}", () => getItem(text)?.IsPresent == present);
    }
}
