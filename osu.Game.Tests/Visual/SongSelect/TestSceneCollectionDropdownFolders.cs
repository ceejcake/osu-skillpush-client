// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Configuration;
using osu.Game.Overlays;
using osu.Game.Rulesets;
using osu.Game.Tests.Resources;
using osuTK;
using osuTK.Input;
using CollectionDropdown = osu.Game.Screens.Select.CollectionDropdown;

namespace osu.Game.Tests.Visual.SongSelect
{
    public partial class TestSceneCollectionDropdownFolders : OsuManualInputManagerTestScene
    {
        private CollectionDropdown dropdown = null!;
        private CollectionFolderManager folders = null!;

        private CollectionFolder speed = null!;

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

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
            foreach (var folder in folders.Folders)
                folders.DeleteFolder(folder.ID);

            config.SetValue(OsuSetting.SongSelectCollectionFilter, string.Empty);

            speed = folders.CreateFolder("Speed");

            Realm.Write(r =>
            {
                r.RemoveAll<BeatmapCollection>();

                var alpha = new BeatmapCollection("alpha");
                var bravo = new BeatmapCollection("bravo");
                r.Add(alpha);
                r.Add(bravo);
                r.Add(new BeatmapCollection("charlie"));

                folders.AddToFolder(alpha.ID, speed.ID);
                folders.AddToFolder(bravo.ID, speed.ID);
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
            expandDropdown();

            assertPresent("Speed (2)", true);
            assertPresent("alpha", false);
            assertPresent("bravo", false);
            assertPresent("charlie", true);

            clickFolderIcon("Speed (2)");

            assertPresent("alpha", true);
            assertPresent("bravo", true);
            AddAssert("menu still open", () => dropdown.ChildrenOfType<Menu>().First().State == MenuState.Open);
            AddAssert("folder not selected", () => dropdown.Current.Value is AllBeatmapsCollectionFilterMenuItem);

            clickFolderIcon("Speed (2)");

            assertPresent("alpha", false);

            AddStep("search 'alp'", () => dropdown.ChildrenOfType<CollectionDropdown.DropdownMenu>().Single().SearchTerm = "alp");

            assertPresent("alpha", true);
            assertPresent("Speed (2)", false);
            assertPresent("bravo", false);
            assertPresent("charlie", false);

            AddStep("search 'speed'", () => dropdown.ChildrenOfType<CollectionDropdown.DropdownMenu>().Single().SearchTerm = "speed");
            assertPresent("Speed (2)", true);
            assertPresent("alpha", false);

            AddStep("clear search", () => dropdown.ChildrenOfType<CollectionDropdown.DropdownMenu>().Single().SearchTerm = string.Empty);
            assertPresent("Speed (2)", true);
            assertPresent("alpha", false);
            assertPresent("charlie", true);
        }

        [Test]
        public void TestSelectFolder()
        {
            expandDropdown();

            AddStep("click folder name", () =>
            {
                InputManager.MoveMouseTo(getItem("Speed (2)"));
                InputManager.Click(MouseButton.Left);
            });

            AddAssert("folder selected", () => dropdown.Current.Value is CollectionFolderFilterMenuItem folder && folder.FolderID == speed.ID);
            AddAssert("folder saved to config", () => config.Get<string>(OsuSetting.SongSelectCollectionFilter), () => Is.EqualTo(CollectionFolderManager.GetFolderFilterValue(speed.ID)));

            AddStep("rename folder", () => folders.RenameFolder(speed.ID, "Fast"));
            AddUntilStep("selection kept", () => dropdown.Current.Value is CollectionFolderFilterMenuItem folder && folder.FolderName == "Fast");

            AddStep("delete folder", () => folders.DeleteFolder(speed.ID));
            AddUntilStep("all beatmaps selected", () => dropdown.Current.Value is AllBeatmapsCollectionFilterMenuItem);
            AddAssert("config cleared", () => config.Get<string>(OsuSetting.SongSelectCollectionFilter), () => Is.Empty);
        }

        [Test]
        public void TestFolderSelectionRestored()
        {
            AddStep("select folder in config", () => config.SetValue(OsuSetting.SongSelectCollectionFilter, CollectionFolderManager.GetFolderFilterValue(speed.ID)));

            AddStep("recreate dropdown", () => Child = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Child = dropdown = new CollectionDropdown { Width = 300 },
            });

            AddUntilStep("folder selected", () => dropdown.Current.Value is CollectionFolderFilterMenuItem folder && folder.FolderID == speed.ID);
        }

        [Test]
        public void TestNestedFolders()
        {
            AddStep("move bravo into subfolder", () =>
            {
                var sub = folders.CreateFolder("Sub", speed.ID);
                folders.MoveCollection(collectionId("bravo"), speed.ID, sub.ID);
            });

            expandDropdown();

            AddUntilStep("speed counts nested collections", () => getItem("Speed (2)") != null);
            assertPresent("Sub (1)", false);

            clickFolderIcon("Speed (2)");
            assertPresent("Sub (1)", true);
            assertPresent("alpha", true);
            assertPresent("bravo", false);

            clickFolderIcon("Sub (1)");
            assertPresent("bravo", true);

            AddAssert("sub indented further than speed", () => folderIcon("Sub (1)").ScreenSpaceDrawQuad.Centre.X > folderIcon("Speed (2)").ScreenSpaceDrawQuad.Centre.X);

            clickFolderIcon("Speed (2)");
            assertPresent("Sub (1)", false);
            assertPresent("bravo", false);
        }

        [Test]
        public void TestMovingCollectionUpdatesDropdown()
        {
            AddStep("move charlie to folder", () => folders.AddToFolder(collectionId("charlie"), speed.ID));
            AddUntilStep("folder count updated", () => getItem("Speed (3)") != null);
        }

        [Test]
        public void TestCollectionInMultipleFolders()
        {
            AddStep("add alpha to second folder", () => folders.AddToFolder(collectionId("alpha"), folders.CreateFolder("Aim").ID));
            AddUntilStep("alpha listed twice", () => dropdown.ChildrenOfType<Menu.DrawableMenuItem>().Count(i => i.Item.Text.Value.ToString() == "alpha"), () => Is.EqualTo(2));
            AddUntilStep("aim folder listed", () => getItem("Aim (1)") != null);

            expandDropdown();

            AddStep("search 'alp'", () => dropdown.ChildrenOfType<CollectionDropdown.DropdownMenu>().Single().SearchTerm = "alp");
            AddUntilStep("alpha shown once", () => dropdown.ChildrenOfType<Menu.DrawableMenuItem>().Count(i => i.Item.Text.Value.ToString() == "alpha" && i.IsPresent), () => Is.EqualTo(1));
        }

        private Guid collectionId(string name) => Realm.Run(r => r.All<BeatmapCollection>().Single(c => c.Name == name).ID);

        private void expandDropdown() => AddStep("expand dropdown", () =>
        {
            InputManager.MoveMouseTo(dropdown.ChildrenOfType<CollectionDropdown.ShearedDropdownHeader>().Single());
            InputManager.Click(MouseButton.Left);
        });

        private void clickFolderIcon(string text) => AddStep($"click '{text}' folder icon", () =>
        {
            InputManager.MoveMouseTo(folderIcon(text));
            InputManager.Click(MouseButton.Left);
        });

        private SpriteIcon folderIcon(string text) =>
            getItem(text).ChildrenOfType<SpriteIcon>().First(i => i.Icon.Equals(FontAwesome.Solid.Folder) || i.Icon.Equals(FontAwesome.Solid.FolderOpen));

        private Menu.DrawableMenuItem getItem(string text) =>
            dropdown.ChildrenOfType<Menu.DrawableMenuItem>().FirstOrDefault(i => i.Item.Text.Value.ToString() == text)!;

        private void assertPresent(string text, bool present) =>
            AddUntilStep($"'{text}' {(present ? "visible" : "hidden")}", () => getItem(text)?.IsPresent == present);
    }
}
