// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osu.Game.Collections;
using osu.Game.Graphics.Containers;
using osu.Game.Overlays;
using osu.Game.Overlays.Dialog;
using osuTK.Input;

namespace osu.Game.Tests.Visual.SongSelect
{
    public partial class TestSceneManageCollectionFolders : OsuManualInputManagerTestScene
    {
        [Cached]
        private OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Aquamarine);

        private CollectionFolderManager folders = null!;
        private ManageCollectionsDialog dialog = null!;
        private Guid collectionId;

        [BackgroundDependencyLoader]
        private void load()
        {
            Dependencies.Cache(Realm);
            Dependencies.Cache(folders = new CollectionFolderManager(LocalStorage));
            Dependencies.CacheAs<IDialogOverlay>(new DialogOverlay());
        }

        [SetUp]
        public void SetUp() => Schedule(() =>
        {
            foreach (string folder in folders.Folders)
                folders.DeleteFolder(folder);

            Realm.Write(r =>
            {
                r.RemoveAll<BeatmapCollection>();
                var collection = new BeatmapCollection("speed maps");
                r.Add(collection);
                collectionId = collection.ID;
            });

            folders.CreateFolder("Speed");
            folders.CreateFolder("Stamina");

            Child = dialog = new ManageCollectionsDialog();
            dialog.Show();
        });

        [Test]
        public void TestFolderPopover()
        {
            AddUntilStep("wait for items", () => dialog.ChildrenOfType<DrawableCollectionListItem.FolderButton>().Any());

            AddStep("click folder button", () =>
            {
                InputManager.MoveMouseTo(dialog.ChildrenOfType<DrawableCollectionListItem.FolderButton>().First());
                InputManager.Click(MouseButton.Left);
            });

            AddUntilStep("folder names visible", () =>
            {
                var texts = dialog.ChildrenOfType<CollectionFolderPopover>().SingleOrDefault()?.ChildrenOfType<SpriteText>().Where(t => t.IsPresent && t.DrawWidth > 0).Select(t => t.Text.ToString()).ToArray();
                return texts != null && texts.Contains("Speed") && texts.Contains("Stamina");
            });

            AddStep("click 'Speed'", () => clickRow("Speed"));
            AddAssert("in Speed", () => folders.IsInFolder(collectionId, "Speed"));

            AddStep("click 'Stamina'", () => clickRow("Stamina"));
            AddAssert("in both folders", () => folders.GetFolders(collectionId).SequenceEqual(new[] { "Speed", "Stamina" }));

            AddStep("click 'Speed' again", () => clickRow("Speed"));
            AddAssert("only in Stamina", () => folders.GetFolders(collectionId).SequenceEqual(new[] { "Stamina" }));

            AddUntilStep("item label updated", () => dialog.ChildrenOfType<SpriteText>().Any(t => t.Text.ToString() == "in Stamina"));
        }

        private void clickRow(string folder)
        {
            var text = dialog.ChildrenOfType<CollectionFolderPopover>().Single().ChildrenOfType<SpriteText>().First(t => t.Text.ToString() == folder);
            InputManager.MoveMouseTo(text.FindClosestParent<OsuClickableContainer>());
            InputManager.Click(MouseButton.Left);
        }
    }
}
