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
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Screens.Select;
using osu.Game.Tests.Resources;

namespace osu.Game.Tests.Visual.SongSelect
{
    /// <summary>
    /// Star ratings under the current mods are only worked out, and only used, for the collection selected in song select.
    /// </summary>
    public partial class TestSceneFilterControlStarRatings : SongSelectComponentsTestScene
    {
        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        private RulesetStore rulesetStore = null!;
        private BeatmapManager beatmapManager = null!;
        private ModdedStarRatingCache cache = null!;

        private FilterControl filterControl = null!;
        private Container filterControlContainer = null!;

        private BeatmapInfo inCollection = null!;
        private BeatmapInfo notInCollection = null!;

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));

            dependencies.Cache(rulesetStore = new RealmRulesetStore(Realm));
            dependencies.Cache(beatmapManager = new BeatmapManager(LocalStorage, Realm, null, dependencies.Get<AudioManager>(), Resources, dependencies.Get<GameHost>(), Beatmap.Default));
            dependencies.CacheAs<Storage>(LocalStorage);
            dependencies.Cache(Realm);
            dependencies.Cache(cache = new ModdedStarRatingCache());

            return dependencies;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            // The filter control is replaced for each test, but the cache stays, as it lives alongside song select in the game.
            Add(cache);
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
                SelectedMods.Value = new Mod[] { new OsuModDoubleTime() };
                config.SetValue(OsuSetting.SongSelectCollectionFilter, string.Empty);
                config.SetValue(OsuSetting.ModdedStarRatingSorting, true);
            });

            AddStep("clear saved star ratings", () =>
            {
                if (LocalStorage.ExistsDirectory(@"cache/star-ratings"))
                    LocalStorage.DeleteDirectory(@"cache/star-ratings");
            });

            AddStep("import beatmap", () =>
            {
                beatmapManager.Import(TestResources.GetQuickTestBeatmapForImport()).WaitSafely();

                var osuBeatmaps = beatmapManager.GetAllUsableBeatmapSets().SelectMany(s => s.Beatmaps).Where(b => b.Ruleset.ShortName == OsuRuleset.SHORT_NAME).ToList();

                inCollection = osuBeatmaps[0];
                notInCollection = osuBeatmaps[1];
            });

            AddStep("create filter control", () =>
            {
                filterControlContainer.Child = filterControl = new FilterControl
                {
                    State = { Value = Visibility.Visible },
                    RelativeSizeAxes = Axes.X,
                };
            });
        }

        [Test]
        public void TestNotUsedWithoutCollection()
        {
            int countBefore = 0;
            AddStep("store calculation count", () => countBefore = cache.CalculationCount);

            AddWaitStep("give it time", 10);

            AddAssert("no star ratings in criteria", () => filterControl.CreateCriteria().StarRatings, () => Is.Null);
            AddAssert("nothing calculated", () => cache.CalculationCount, () => Is.EqualTo(countBefore));
        }

        [Test]
        public void TestOffByDefault()
        {
            AddAssert("off by default", () => config.GetBindable<bool>(OsuSetting.ModdedStarRatingSorting).Default, () => Is.False);
        }

        [Test]
        public void TestNotUsedWhenTurnedOff()
        {
            int countBefore = 0;
            AddStep("store calculation count", () => countBefore = cache.CalculationCount);

            AddStep("turn off", () => config.SetValue(OsuSetting.ModdedStarRatingSorting, false));
            selectCollection();
            AddWaitStep("give it time", 10);

            AddAssert("no star ratings in criteria", () => filterControl.CreateCriteria().StarRatings, () => Is.Null);
            AddAssert("nothing calculated", () => cache.CalculationCount, () => Is.EqualTo(countBefore));

            AddStep("turn on", () => config.SetValue(OsuSetting.ModdedStarRatingSorting, true));
            AddUntilStep("collection beatmap worked out", () => cache.Current.TryGetStarRating(inCollection, out _));
            AddAssert("star ratings in criteria", () => filterControl.CreateCriteria().StarRatings, () => Is.Not.Null);
        }

        [Test]
        public void TestCollectionIsWorkedOut()
        {
            selectCollection();

            AddUntilStep("collection beatmap worked out", () => cache.Current.TryGetStarRating(inCollection, out _));
            AddUntilStep("calculation finished", () => !cache.IsCalculating.Value);

            AddAssert("other beatmap left alone", () => cache.Current.TryGetStarRating(notInCollection, out _), () => Is.False);
            AddAssert("star ratings in criteria", () => filterControl.CreateCriteria().StarRatings, () => Is.Not.Null);
            AddAssert("criteria use the worked out rating", () =>
            {
                cache.Current.TryGetStarRating(inCollection, out double stars);
                return filterControl.CreateCriteria().GetStarRating(inCollection) == stars;
            });
        }

        private void selectCollection() => AddStep("select collection", () =>
        {
            var collection = new BeatmapCollection("test", new List<string> { inCollection.MD5Hash });
            Realm.Write(r => r.Add(collection));
            config.SetValue(OsuSetting.SongSelectCollectionFilter, collection.ID.ToString());
        });

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (rulesetStore.IsNotNull())
                rulesetStore.Dispose();
        }
    }
}
