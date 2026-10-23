// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Extensions;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Configuration;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Osu;
using osu.Game.Screens.Select;
using osu.Game.Tests.Resources;

namespace osu.Game.Tests.Visual.SongSelect
{
    /// <summary>
    /// Selecting a folder in song select shows all beatmaps inside the collections of that folder, including those in subfolders.
    /// </summary>
    public partial class TestSceneFilterControlCollectionFolders : SongSelectComponentsTestScene
    {
        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        private RulesetStore rulesetStore = null!;
        private BeatmapManager beatmapManager = null!;
        private CollectionFolderManager folders = null!;

        private FilterControl filterControl = null!;
        private Container filterControlContainer = null!;

        private BeatmapInfo inParent = null!;
        private BeatmapInfo inChild = null!;
        private BeatmapInfo outside = null!;

        private CollectionFolder parentFolder = null!;
        private CollectionFolder childFolder = null!;
        private Guid outsideCollection;

        private int criteriaChangedCount;

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));

            dependencies.Cache(rulesetStore = new RealmRulesetStore(Realm));
            dependencies.Cache(beatmapManager = new BeatmapManager(LocalStorage, Realm, null, dependencies.Get<AudioManager>(), Resources, dependencies.Get<GameHost>(), Beatmap.Default));
            dependencies.Cache(Realm);
            dependencies.Cache(folders = new CollectionFolderManager(LocalStorage));

            return dependencies;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Add(filterControlContainer = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
            });
        }

        [SetUpSteps]
        public override void SetUpSteps()
        {
            base.SetUpSteps();

            AddStep("reset", () =>
            {
                Ruleset.Value = new OsuRuleset().RulesetInfo;
                config.SetValue(OsuSetting.SongSelectCollectionFilter, string.Empty);

                foreach (var folder in folders.Folders)
                    folders.DeleteFolder(folder.ID);
            });

            AddStep("set up folders", () =>
            {
                beatmapManager.Import(TestResources.GetQuickTestBeatmapForImport()).WaitSafely();

                var beatmaps = beatmapManager.GetAllUsableBeatmapSets().SelectMany(s => s.Beatmaps).Where(b => b.Ruleset.ShortName == OsuRuleset.SHORT_NAME).ToList();

                inParent = beatmaps[0];
                inChild = beatmaps[1];
                outside = beatmaps[2];

                parentFolder = folders.CreateFolder("Parent");
                childFolder = folders.CreateFolder("Child", parentFolder.ID);

                var parentCollection = new BeatmapCollection("parent", new List<string> { inParent.MD5Hash });
                var childCollection = new BeatmapCollection("child", new List<string> { inChild.MD5Hash });
                var otherCollection = new BeatmapCollection("outside", new List<string> { outside.MD5Hash });

                Realm.Write(r =>
                {
                    r.RemoveAll<BeatmapCollection>();
                    r.Add(parentCollection);
                    r.Add(childCollection);
                    r.Add(otherCollection);
                });

                folders.AddToFolder(parentCollection.ID, parentFolder.ID);
                folders.AddToFolder(childCollection.ID, childFolder.ID);
                outsideCollection = otherCollection.ID;
            });

            AddStep("create filter control", () =>
            {
                filterControlContainer.Child = filterControl = new FilterControl
                {
                    State = { Value = Visibility.Visible },
                    RelativeSizeAxes = Axes.X,
                };

                filterControl.CriteriaChanged += _ => criteriaChangedCount++;
            });
        }

        [Test]
        public void TestFolderIncludesSubfolders()
        {
            selectFolder(() => parentFolder);
            assertFiltered(() => new[] { inParent, inChild });

            selectFolder(() => childFolder);
            assertFiltered(() => new[] { inChild });
        }

        [Test]
        public void TestFilterUpdatesWhenFolderContentsChange()
        {
            selectFolder(() => childFolder);

            int countBefore = 0;
            AddStep("store criteria change count", () => countBefore = criteriaChangedCount);

            AddStep("move outside collection into folder", () => folders.MoveCollection(outsideCollection, null, childFolder.ID));

            AddUntilStep("filter re-run", () => criteriaChangedCount, () => Is.GreaterThan(countBefore));
            assertFiltered(() => new[] { inChild, outside });
        }

        [Test]
        public void TestDeletedFolderNotFiltered()
        {
            selectFolder(() => parentFolder);
            AddStep("delete folder", () => folders.DeleteFolder(parentFolder.ID));
            AddAssert("not filtered by collection", () => filterControl.CreateCriteria().CollectionBeatmapMD5Hashes, () => Is.Null);
        }

        private void selectFolder(Func<CollectionFolder> folder) =>
            AddStep("select folder", () => config.SetValue(OsuSetting.SongSelectCollectionFilter, CollectionFolderManager.GetFolderFilterValue(folder().ID)));

        private void assertFiltered(Func<BeatmapInfo[]> expected) =>
            AddAssert("filtered to folder beatmaps", () => filterControl.CreateCriteria().CollectionBeatmapMD5Hashes, () => Is.EquivalentTo(expected().Select(b => b.MD5Hash)));

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (rulesetStore.IsNotNull())
                rulesetStore.Dispose();
        }
    }
}
