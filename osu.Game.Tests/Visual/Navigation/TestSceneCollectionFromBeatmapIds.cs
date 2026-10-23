// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Tests.Resources;
using osuTK.Input;

namespace osu.Game.Tests.Visual.Navigation
{
    /// <summary>
    /// A collection can be made from a pasted list of beatmap IDs, with the missing beatmaps downloaded.
    /// </summary>
    public partial class TestSceneCollectionFromBeatmapIds : OsuGameTestScene
    {
        private const int remote_set_id = 987001;
        private const int remote_beatmap_id = 987002;
        private const int unknown_beatmap_id = 987003;

        private TestImporter importer = null!;
        private BeatmapInfo localBeatmap = null!;

        public override void SetUpSteps()
        {
            base.SetUpSteps();

            AddStep("import a beatmap", () =>
            {
                var set = TestResources.CreateTestBeatmapSetInfo(2);
                Game.BeatmapManager.Import(set);
                localBeatmap = set.Beatmaps.First();
            });

            AddStep("add importer", () => Game.Add(importer = new TestImporter()));
            AddUntilStep("importer loaded", () => importer.IsLoaded);
        }

        [Test]
        public void TestCreateCollection()
        {
            AddStep("create from pasted IDs", () => importer.ImportBeatmapIds(" pasted ",
                $"{localBeatmap.OnlineID}\r\nhttps://osu.ppy.sh/beatmapsets/{remote_set_id}#osu/{remote_beatmap_id}, {unknown_beatmap_id}\n{localBeatmap.OnlineID}"));

            AddUntilStep("collection created", () => hashes("pasted"), () => Is.Not.Null);

            AddAssert("only missing beatmaps looked up", () => importer.Lookups.OrderBy(id => id), () => Is.EqualTo(new[] { remote_beatmap_id, unknown_beatmap_id }));
            AddAssert("has the local beatmap", () => hashes("pasted"), () => Does.Contain(localBeatmap.MD5Hash));

            AddUntilStep("missing set downloaded", () => importer.Downloads.ToArray(), () => Is.EqualTo(new[] { remote_set_id }));

            // The mirror's copy isn't the listed version, so its checksum is only known once it has been imported.
            // The listed version can never be found locally, so it is replaced, keeping the collection's size accurate.
            AddUntilStep("downloaded version replaces listed version", () => hashes("pasted"), () => Does.Contain(importer.DownloadedHash).And.Not.Contain("listed-checksum"));
            AddAssert("nothing else added", () => hashes("pasted"), () => Has.Length.EqualTo(2));
        }

        [Test]
        public void TestAddsToExistingCollection()
        {
            AddStep("create collection", () => Game.Realm.Write(r => r.Add(new BeatmapCollection("existing", new[] { "already there" }))));
            AddStep("add local beatmap", () => importer.ImportBeatmapIds("existing", localBeatmap.OnlineID.ToString()));

            AddUntilStep("added", () => hashes("existing"), () => Is.EquivalentTo(new[] { "already there", localBeatmap.MD5Hash }));
            AddAssert("no lookups needed", () => importer.Lookups, () => Is.Empty);
            AddAssert("no downloads needed", () => importer.Downloads, () => Is.Empty);
        }

        [Test]
        public void TestUpdateWithDuplicates()
        {
            AddStep("create collection", () => Game.Realm.Write(r => r.Add(new BeatmapCollection("existing", new[] { localBeatmap.MD5Hash }))));

            AddStep("paste list with duplicates", () => importer.ImportBeatmapIds("existing",
                $"{localBeatmap.OnlineID} {remote_beatmap_id}\n{localBeatmap.OnlineID}\nhttps://osu.ppy.sh/b/{remote_beatmap_id} {remote_beatmap_id}"));

            AddUntilStep("missing set downloaded once", () => importer.Downloads.ToArray(), () => Is.EqualTo(new[] { remote_set_id }));
            AddUntilStep("missing beatmap added once", () => hashes("existing"), () => Is.EquivalentTo(new[] { localBeatmap.MD5Hash, importer.DownloadedHash }));
            AddAssert("looked up once", () => importer.Lookups.ToArray(), () => Is.EqualTo(new[] { remote_beatmap_id }));
        }

        [Test]
        public void TestAddFromManageCollectionsDialog()
        {
            AddStep("create collection", () => Game.Realm.Write(r => r.Add(new BeatmapCollection("from dialog"))));
            AddUntilStep("wait for manage collections to load", () => Game.ChildrenOfType<ManageCollectionsDialog>().Any());
            AddStep("open manage collections", () => Game.ChildrenOfType<ManageCollectionsDialog>().Single().Show());

            AddUntilStep("wait for button", () => addButton() != null);
            AddStep("click add beatmap ids", () =>
            {
                InputManager.MoveMouseTo(addButton()!);
                InputManager.Click(MouseButton.Left);
            });

            AddUntilStep("popover shown", () => Game.ChildrenOfType<AddBeatmapIdsPopover>().Any());

            // Only beatmaps which are available locally, so that the game's own importer doesn't need to go online.
            AddStep("paste and confirm", () =>
            {
                Game.ChildrenOfType<AddBeatmapIdsPopover>().Single().ChildrenOfType<AddBeatmapIdsPopover.PastedListTextBox>().Single().Text =
                    $"{localBeatmap.OnlineID} {localBeatmap.OnlineID}";
                InputManager.Key(Key.Enter);
            });

            AddUntilStep("added once", () => hashes("from dialog"), () => Is.EqualTo(new[] { localBeatmap.MD5Hash }));
            AddUntilStep("popover closed", () => !Game.ChildrenOfType<AddBeatmapIdsPopover>().Any(p => p.State.Value == Visibility.Visible));
        }

        private DrawableCollectionListItem.AddBeatmapIdsButton? addButton() =>
            Game.ChildrenOfType<DrawableCollectionListItem>()
                .FirstOrDefault(i => i.Model.IsManaged && i.Model.PerformRead(c => c.Name) == "from dialog")?
                .ChildrenOfType<DrawableCollectionListItem.AddBeatmapIdsButton>().SingleOrDefault();

        [Test]
        public void TestNothingCreatedWithoutNameOrIds()
        {
            AddStep("no name", () => importer.ImportBeatmapIds(" ", localBeatmap.OnlineID.ToString()));
            AddStep("no ids", () => importer.ImportBeatmapIds("empty", "not an id"));
            AddAssert("no collections", () => Game.Realm.Run(r => r.All<BeatmapCollection>().Count()), () => Is.Zero);
        }

        private string[]? hashes(string name) => Game.Realm.Run(r => r.All<BeatmapCollection>().FirstOrDefault(c => c.Name == name)?.BeatmapMD5Hashes.ToArray());

        private partial class TestImporter : OsuCollectorImporter
        {
            public readonly ConcurrentQueue<int> Lookups = new ConcurrentQueue<int>();
            public readonly ConcurrentQueue<int> Downloads = new ConcurrentQueue<int>();

            public string DownloadedHash { get; private set; } = string.Empty;

            [Resolved]
            private BeatmapManager beatmaps { get; set; } = null!;

            protected override bool UseMirrors => true;

            protected override Task<BeatmapLookup?> LookupBeatmap(int beatmapId)
            {
                Lookups.Enqueue(beatmapId);

                return Task.FromResult(beatmapId == remote_beatmap_id
                    ? new BeatmapLookup { ID = beatmapId, BeatmapSetID = remote_set_id, Checksum = "listed-checksum" }
                    : null);
            }

            protected override Task<bool> DownloadFromMirror(int setId, bool noVideo) => Task.Run(() =>
            {
                Downloads.Enqueue(setId);

                var set = TestResources.CreateTestBeatmapSetInfo(1);
                set.OnlineID = setId;
                set.Beatmaps[0].OnlineID = remote_beatmap_id;
                DownloadedHash = set.Beatmaps[0].MD5Hash;

                return beatmaps.Import(set) != null;
            });
        }
    }
}
