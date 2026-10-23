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
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays;
using osu.Game.Overlays.Dialog;
using osuTK;
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

        private CollectionFolder speed = null!;
        private CollectionFolder stamina = null!;

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
            foreach (var folder in folders.Folders)
                folders.DeleteFolder(folder.ID);

            Realm.Write(r =>
            {
                r.RemoveAll<BeatmapCollection>();
                var collection = new BeatmapCollection("speed maps");
                r.Add(collection);
                collectionId = collection.ID;
            });

            speed = folders.CreateFolder("Speed");
            stamina = folders.CreateFolder("Stamina");

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
                var texts = popover?.ChildrenOfType<SpriteText>().Where(t => t.IsPresent && t.DrawWidth > 0).Select(t => t.Text.ToString()).ToArray();
                return texts != null && texts.Contains("Speed") && texts.Contains("Stamina");
            });

            AddStep("click 'Speed'", () => clickRow("Speed"));
            AddAssert("in Speed", () => folders.IsInFolder(collectionId, speed.ID));
            AddAssert("collection still shown while popover open", () => collectionItem.IsPresent);

            AddStep("click 'Stamina'", () => clickRow("Stamina"));
            AddAssert("in both folders", () => folders.GetFolders(collectionId).Select(f => f.Name).SequenceEqual(new[] { "Speed", "Stamina" }));

            AddStep("click 'Speed' again", () => clickRow("Speed"));
            AddAssert("only in Stamina", () => folders.GetFolders(collectionId).Select(f => f.Name).SequenceEqual(new[] { "Stamina" }));

            AddUntilStep("item label updated", () => dialog.ChildrenOfType<SpriteText>().Any(t => t.Text.ToString() == "in Stamina"));

            // The first press only releases focus from the popover's text box.
            AddStep("close popover", () =>
            {
                InputManager.Key(Key.Escape);
                InputManager.Key(Key.Escape);
            });
            AddUntilStep("popover closed", () => popover?.State.Value != Visibility.Visible);
            AddUntilStep("collection hidden from top level", () => !collectionItem.IsPresent);
        }

        [Test]
        public void TestNestedFoldersInPopover()
        {
            AddStep("nest stamina in speed", () => folders.MoveFolder(stamina.ID, speed.ID));
            AddUntilStep("wait for items", () => dialog.ChildrenOfType<DrawableCollectionListItem.FolderButton>().Any());

            AddStep("click folder button", () =>
            {
                InputManager.MoveMouseTo(dialog.ChildrenOfType<DrawableCollectionListItem.FolderButton>().First());
                InputManager.Click(MouseButton.Left);
            });

            AddUntilStep("subfolder listed", () => popover?.ChildrenOfType<SpriteText>().Any(t => t.Text.ToString() == "Stamina") == true);
            AddAssert("subfolder indented", () => popoverText("Stamina").ScreenSpaceDrawQuad.TopLeft.X > popoverText("Speed").ScreenSpaceDrawQuad.TopLeft.X);
        }

        [Test]
        public void TestNavigateFolders()
        {
            AddUntilStep("folder tiles shown", () => tile("Speed") != null && tile("Stamina") != null);
            AddUntilStep("collection shown at top level", () => collectionItem.IsPresent);

            AddStep("open Speed", () =>
            {
                InputManager.MoveMouseTo(tile("Speed")!);
                InputManager.Click(MouseButton.Left);
            });

            AddUntilStep("path shows Speed", () => breadcrumbs().SequenceEqual(new[] { "All collections", "Speed" }));
            AddUntilStep("collection not in Speed hidden", () => !collectionItem.IsPresent);
            AddUntilStep("no folder tiles", () => tile("Stamina") == null);

            AddStep("go back via path", () =>
            {
                InputManager.MoveMouseTo(segment(null));
                InputManager.Click(MouseButton.Left);
            });

            AddUntilStep("back at top level", () => breadcrumbs().SequenceEqual(new[] { "All collections" }));
            AddUntilStep("collection shown again", () => collectionItem.IsPresent);
        }

        [Test]
        public void TestDragFolderIntoFolder()
        {
            AddUntilStep("folder tiles shown", () => tile("Speed") != null && tile("Stamina") != null);

            drag(() => tile("Stamina")!, () => tile("Speed")!);

            AddAssert("stamina inside speed", () => folders.GetFolder(stamina.ID)!.ParentID, () => Is.EqualTo(speed.ID));
            AddUntilStep("stamina no longer at top level", () => tile("Stamina") == null);

            AddStep("open Speed", () =>
            {
                InputManager.MoveMouseTo(tile("Speed")!);
                InputManager.Click(MouseButton.Left);
            });

            AddUntilStep("stamina inside", () => tile("Stamina") != null);

            drag(() => tile("Stamina")!, () => segment(null));

            AddAssert("stamina back at top level", () => folders.GetFolder(stamina.ID)!.ParentID, () => Is.Null);

            AddStep("go back via path", () =>
            {
                InputManager.MoveMouseTo(segment(null));
                InputManager.Click(MouseButton.Left);
            });

            drag(() => tile("Speed")!, () => tile("Speed")!);
            AddAssert("can't move into itself", () => folders.GetFolder(speed.ID)!.ParentID, () => Is.Null);
        }

        [Test]
        public void TestDragCollectionIntoFolder()
        {
            AddUntilStep("wait for items", () => dialog.ChildrenOfType<CollectionDragHandle>().Any(h => h.IsPresent));

            drag(() => dialog.ChildrenOfType<CollectionDragHandle>().First(), () => tile("Speed")!);

            AddAssert("collection in Speed", () => folders.GetFolders(collectionId).Select(f => f.Name).SequenceEqual(new[] { "Speed" }));
            AddUntilStep("collection hidden from top level", () => !collectionItem.IsPresent);

            AddStep("open Speed", () =>
            {
                InputManager.MoveMouseTo(tile("Speed")!);
                InputManager.Click(MouseButton.Left);
            });

            AddUntilStep("collection shown in Speed", () => collectionItem.IsPresent);

            drag(() => dialog.ChildrenOfType<CollectionDragHandle>().First(), () => segment(null));

            AddAssert("collection moved out", () => folders.IsInAnyFolder(collectionId), () => Is.False);
            AddUntilStep("collection hidden from Speed", () => !collectionItem.IsPresent);
        }

        [Test]
        public void TestCreateInsideCurrentFolder()
        {
            AddUntilStep("folder tiles shown", () => tile("Speed") != null);

            AddStep("open Speed", () =>
            {
                InputManager.MoveMouseTo(tile("Speed")!);
                InputManager.Click(MouseButton.Left);
            });

            AddStep("click new folder", () =>
            {
                InputManager.MoveMouseTo(dialog.ChildrenOfType<CollectionFolderBreadcrumbs.NewFolderButton>().Single());
                InputManager.Click(MouseButton.Left);
            });

            AddUntilStep("name popover shown", () => dialog.ChildrenOfType<FolderNamePopover>().Any());
            AddStep("type name and confirm", () =>
            {
                dialog.ChildrenOfType<FolderNamePopover>().Single().ChildrenOfType<OsuTextBox>().Single().Text = "Bursts";
                InputManager.Key(Key.Enter);
            });

            AddAssert("folder created inside Speed", () => folders.GetChildFolders(speed.ID).Select(f => f.Name).SequenceEqual(new[] { "Bursts" }));
            AddUntilStep("tile shown", () => tile("Bursts") != null);

            AddStep("create collection", () =>
            {
                var textBox = dialog.ChildrenOfType<DrawableCollectionListItem>().Single(i => !i.Model.IsManaged).ChildrenOfType<OsuTextBox>().Single();

                InputManager.MoveMouseTo(textBox);
                InputManager.Click(MouseButton.Left);
                textBox.Text = "new speed collection";
                InputManager.Key(Key.Enter);
            });

            AddUntilStep("collection created inside Speed", () =>
            {
                var created = Realm.Run(r => r.All<BeatmapCollection>().FirstOrDefault(c => c.Name == "new speed collection")?.ID);
                return created != null && folders.IsInFolder(created.Value, speed.ID);
            });
        }

        [Test]
        public void TestSearchShowsFoldersAnywhere()
        {
            AddStep("nest stamina in speed", () => folders.MoveFolder(stamina.ID, speed.ID));
            AddUntilStep("stamina hidden at top level", () => tile("Stamina") == null);

            AddStep("search 'stam'", () => dialog.ChildrenOfType<BasicSearchTextBox>().Single().Current.Value = "stam");
            AddUntilStep("stamina shown", () => tile("Stamina") != null);
            AddUntilStep("speed hidden", () => tile("Speed") == null);

            AddStep("open stamina", () =>
            {
                InputManager.MoveMouseTo(tile("Stamina")!);
                InputManager.Click(MouseButton.Left);
            });

            AddUntilStep("path shows stamina", () => breadcrumbs().SequenceEqual(new[] { "All collections", "Speed", "Stamina" }));
            AddAssert("search cleared", () => dialog.ChildrenOfType<BasicSearchTextBox>().Single().Current.Value, () => Is.Empty);
        }

        private void drag(Func<Drawable> from, Func<Drawable> to)
        {
            AddStep("start drag", () =>
            {
                InputManager.MoveMouseTo(from());
                InputManager.PressButton(MouseButton.Left);
            });

            AddStep("move away", () => InputManager.MoveMouseTo(from(), new Vector2(0, 30)));
            AddStep("move to target", () => InputManager.MoveMouseTo(to()));
            AddStep("drop", () => InputManager.ReleaseButton(MouseButton.Left));
        }

        private DrawableCollectionListItem collectionItem => dialog.ChildrenOfType<DrawableCollectionListItem>().Single(i => i.Model.IsManaged && i.Model.ID == collectionId);

        private DrawableCollectionFolder? tile(string name) => dialog.ChildrenOfType<DrawableCollectionFolder>().FirstOrDefault(t => t.Folder.Name == name);

        private CollectionFolderBreadcrumbs.Segment segment(Guid? folderId) => dialog.ChildrenOfType<CollectionFolderBreadcrumbs.Segment>().Single(s => s.FolderID == folderId);

        private string[] breadcrumbs() =>
            dialog.ChildrenOfType<CollectionFolderBreadcrumbs.Segment>().SelectMany(s => s.ChildrenOfType<SpriteText>()).Select(t => t.Text.ToString()).ToArray();

        private CollectionFolderPopover? popover => dialog.ChildrenOfType<CollectionFolderPopover>().SingleOrDefault();

        private SpriteText popoverText(string text) => popover!.ChildrenOfType<SpriteText>().First(t => t.Text.ToString() == text);

        private void clickRow(string folder)
        {
            InputManager.MoveMouseTo(popoverText(folder).FindClosestParent<OsuClickableContainer>());
            InputManager.Click(MouseButton.Left);
        }
    }
}
