// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Collections;

namespace osu.Game.Tests.NonVisual
{
    [TestFixture]
    public class CollectionFolderManagerTest
    {
        private TemporaryNativeStorage storage = null!;
        private CollectionFolderManager folders = null!;

        [SetUp]
        public void SetUp()
        {
            storage = new TemporaryNativeStorage("collection-folders");
            folders = new CollectionFolderManager(storage);
        }

        [TearDown]
        public void TearDown() => storage.Dispose();

        [Test]
        public void TestNestedFolders()
        {
            var tournament = folders.CreateFolder("Tournament");
            var pool = folders.CreateFolder("Pool A", tournament.ID);

            Assert.That(folders.GetChildFolders(null).Select(f => f.Name), Is.EqualTo(new[] { "Tournament" }));
            Assert.That(folders.GetChildFolders(tournament.ID).Select(f => f.Name), Is.EqualTo(new[] { "Pool A" }));
            Assert.That(folders.GetPathName(pool.ID), Is.EqualTo("Tournament / Pool A"));

            // Same name in a different place is a different folder, same name in the same place is the same folder.
            Assert.That(folders.CreateFolder("Pool A").ID, Is.Not.EqualTo(pool.ID));
            Assert.That(folders.CreateFolder("pool a", tournament.ID).ID, Is.EqualTo(pool.ID));
        }

        [Test]
        public void TestCollectionsInsideFolders()
        {
            var tournament = folders.CreateFolder("Tournament");
            var pool = folders.CreateFolder("Pool A", tournament.ID);

            var direct = Guid.NewGuid();
            var nested = Guid.NewGuid();
            var outside = Guid.NewGuid();

            folders.AddToFolder(direct, tournament.ID);
            folders.AddToFolder(nested, pool.ID);

            Assert.That(folders.GetCollections(tournament.ID), Is.EquivalentTo(new[] { direct }));
            Assert.That(folders.GetCollections(tournament.ID, recursive: true), Is.EquivalentTo(new[] { direct, nested }));

            Assert.That(folders.IsDirectlyInside(outside, null), Is.True);
            Assert.That(folders.IsDirectlyInside(nested, null), Is.False);
            Assert.That(folders.IsDirectlyInside(nested, pool.ID), Is.True);
        }

        [Test]
        public void TestMoveCollection()
        {
            var a = folders.CreateFolder("A");
            var b = folders.CreateFolder("B");
            var c = folders.CreateFolder("C");
            var collection = Guid.NewGuid();

            folders.AddToFolder(collection, a.ID);
            folders.AddToFolder(collection, c.ID);

            folders.MoveCollection(collection, a.ID, b.ID);
            Assert.That(folders.GetFolders(collection).Select(f => f.Name), Is.EqualTo(new[] { "B", "C" }), "other folders are kept");

            folders.MoveCollection(collection, null, a.ID);
            Assert.That(folders.GetFolders(collection).Select(f => f.Name), Is.EqualTo(new[] { "A", "B", "C" }));

            folders.MoveCollection(collection, a.ID, null);
            folders.MoveCollection(collection, b.ID, null);
            folders.MoveCollection(collection, c.ID, null);
            Assert.That(folders.IsInAnyFolder(collection), Is.False);
        }

        [Test]
        public void TestMoveFolder()
        {
            var a = folders.CreateFolder("A");
            var b = folders.CreateFolder("B");
            var aChild = folders.CreateFolder("A child", a.ID);

            Assert.That(folders.MoveFolder(a.ID, b.ID), Is.True);
            Assert.That(folders.GetPathName(aChild.ID), Is.EqualTo("B / A / A child"));

            Assert.That(folders.CanMoveFolder(b.ID, aChild.ID), Is.False, "can't move into own subfolder");
            Assert.That(folders.CanMoveFolder(b.ID, b.ID), Is.False, "can't move into itself");
            Assert.That(folders.CanMoveFolder(a.ID, b.ID), Is.False, "already there");
            Assert.That(folders.MoveFolder(b.ID, aChild.ID), Is.False);
            Assert.That(folders.GetFolder(b.ID)!.ParentID, Is.Null);

            Assert.That(folders.MoveFolder(a.ID, null), Is.True);
            Assert.That(folders.GetChildFolders(null).Select(f => f.Name), Is.EqualTo(new[] { "A", "B" }));
        }

        [Test]
        public void TestDeleteFolderKeepsContents()
        {
            var parent = folders.CreateFolder("Parent");
            var deleted = folders.CreateFolder("Deleted", parent.ID);
            var child = folders.CreateFolder("Child", deleted.ID);

            var inDeleted = Guid.NewGuid();
            var topLevel = folders.CreateFolder("Top");
            var inTopLevelDeleted = Guid.NewGuid();

            folders.AddToFolder(inDeleted, deleted.ID);
            folders.AddToFolder(inTopLevelDeleted, topLevel.ID);

            folders.DeleteFolder(deleted.ID);

            Assert.That(folders.GetFolder(child.ID)!.ParentID, Is.EqualTo(parent.ID), "subfolder moves up");
            Assert.That(folders.IsInFolder(inDeleted, parent.ID), Is.True, "collection moves up");

            folders.DeleteFolder(topLevel.ID);
            Assert.That(folders.IsDirectlyInside(inTopLevelDeleted, null), Is.True);
        }

        [Test]
        public void TestRemoveMissingCollections()
        {
            var folder = folders.CreateFolder("Folder");
            var kept = Guid.NewGuid();
            var deleted = Guid.NewGuid();

            folders.AddToFolder(kept, folder.ID);
            folders.AddToFolder(deleted, folder.ID);

            folders.RemoveMissingCollections(new[] { kept });

            Assert.That(folders.GetCollections(folder.ID), Is.EquivalentTo(new[] { kept }));
        }

        [Test]
        public void TestSavedAndLoaded()
        {
            var tournament = folders.CreateFolder("Tournament");
            var pool = folders.CreateFolder("Pool A", tournament.ID);
            var collection = Guid.NewGuid();

            folders.AddToFolder(collection, pool.ID);
            folders.GetExpanded(tournament.ID).Value = true;

            var reloaded = new CollectionFolderManager(storage);

            Assert.That(reloaded.GetPathName(pool.ID), Is.EqualTo("Tournament / Pool A"));
            Assert.That(reloaded.IsInFolder(collection, pool.ID), Is.True);
            Assert.That(reloaded.GetExpanded(tournament.ID).Value, Is.True);
            Assert.That(reloaded.GetExpanded(pool.ID).Value, Is.False);
        }

        [Test]
        public void TestLegacyFormatMigrated()
        {
            var speedCollection = Guid.NewGuid();
            var oldestCollection = Guid.NewGuid();

            writeFile($@"{{
                ""Folders"": [ ""Speed"", ""Stamina"" ],
                ""Memberships"": {{ ""{speedCollection}"": [ ""Speed"", ""Stamina"" ] }},
                ""Assignments"": {{ ""{oldestCollection}"": ""Aim"" }},
                ""Expanded"": [ ""Speed"" ]
            }}");

            var loaded = new CollectionFolderManager(storage);

            Assert.That(loaded.GetChildFolders(null).Select(f => f.Name), Is.EqualTo(new[] { "Aim", "Speed", "Stamina" }));
            Assert.That(loaded.GetFolders(speedCollection).Select(f => f.Name), Is.EqualTo(new[] { "Speed", "Stamina" }));
            Assert.That(loaded.GetFolders(oldestCollection).Select(f => f.Name), Is.EqualTo(new[] { "Aim" }));
            Assert.That(loaded.GetExpanded(loaded.Folders.Single(f => f.Name == "Speed").ID).Value, Is.True);

            // Saving converts to the new format, which is then loaded the same.
            loaded.CreateFolder("Speed");

            var reloaded = new CollectionFolderManager(storage);
            Assert.That(reloaded.GetFolders(speedCollection).Select(f => f.Name), Is.EqualTo(new[] { "Speed", "Stamina" }));
        }

        [Test]
        public void TestCorruptedTreeRecovered()
        {
            var a = Guid.NewGuid();
            var b = Guid.NewGuid();
            var orphan = Guid.NewGuid();

            writeFile($@"{{
                ""FolderTree"": [
                    {{ ""ID"": ""{a}"", ""Name"": ""A"", ""ParentID"": ""{b}"" }},
                    {{ ""ID"": ""{b}"", ""Name"": ""B"", ""ParentID"": ""{a}"" }},
                    {{ ""ID"": ""{orphan}"", ""Name"": ""Orphan"", ""ParentID"": ""{Guid.NewGuid()}"" }}
                ]
            }}");

            var loaded = new CollectionFolderManager(storage);

            Assert.That(loaded.GetFolder(orphan)!.ParentID, Is.Null);
            Assert.That(loaded.GetPath(a).Count, Is.LessThanOrEqualTo(2), "loop is broken");
            Assert.That(loaded.GetChildFolders(null), Is.Not.Empty);
        }

        [Test]
        public void TestFolderFilterValue()
        {
            var id = Guid.NewGuid();

            Assert.That(CollectionFolderManager.TryParseFolderFilterValue(CollectionFolderManager.GetFolderFilterValue(id), out var parsed), Is.True);
            Assert.That(parsed, Is.EqualTo(id));

            Assert.That(CollectionFolderManager.TryParseFolderFilterValue(id.ToString(), out _), Is.False, "plain collection IDs aren't folders");
            Assert.That(CollectionFolderManager.TryParseFolderFilterValue(string.Empty, out _), Is.False);
        }

        private void writeFile(string json)
        {
            using (var stream = storage.CreateFileSafely("collection-folders.json"))
            using (var writer = new StreamWriter(stream))
                writer.Write(json);
        }
    }
}
