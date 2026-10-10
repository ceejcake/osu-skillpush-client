// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Extensions;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Framework.Bindables;
using osu.Game.Configuration;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Screens.Play;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Taiko;
using osu.Game.Tests.Resources;

namespace osu.Game.Tests.Visual.SongSelect
{
    public partial class TestSceneModdedStarRatingCache : OsuTestScene
    {
        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        private RulesetStore rulesetStore = null!;
        private NotificationOverlay notificationOverlay = null!;
        private readonly TestPlayInfo playInfo = new TestPlayInfo();
        private BeatmapManager beatmapManager = null!;
        private BeatmapDifficultyCache difficultyCache = null!;

        private ModdedStarRatingCache cache = null!;
        private ModdedStarRatingCache.Tracking? tracking;

        /// <summary>
        /// The beatmap in the collection being tracked.
        /// </summary>
        private BeatmapInfo beatmap = null!;

        /// <summary>
        /// A beatmap from the same set which is not in that collection.
        /// </summary>
        private BeatmapInfo other = null!;

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));

            dependencies.Cache(rulesetStore = new RealmRulesetStore(Realm));
            dependencies.Cache(beatmapManager = new BeatmapManager(LocalStorage, Realm, null, dependencies.Get<AudioManager>(), Resources, dependencies.Get<GameHost>(), Beatmap.Default));
            dependencies.Cache(difficultyCache = new BeatmapDifficultyCache());
            dependencies.CacheAs<Storage>(LocalStorage);
            dependencies.CacheAs<INotificationOverlay>(notificationOverlay = new NotificationOverlay());
            dependencies.CacheAs<ILocalUserPlayInfo>(playInfo);
            dependencies.Cache(Realm);

            return dependencies;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Add(difficultyCache);
            Add(notificationOverlay);
        }

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("stop tracking", () => tracking?.Dispose());
            AddStep("resume calculation", () => playInfo.PlayingState.Value = LocalUserPlayingState.NotPlaying);
            AddStep("clear notifications", () => notificationOverlay.AllNotifications.ToList().ForEach(n => n.Close(false)));
            AddStep("reset ruleset and mods", () =>
            {
                Ruleset.Value = new OsuRuleset().RulesetInfo;
                SelectedMods.Value = Array.Empty<Mod>();
                config.SetValue(OsuSetting.ShowConvertedBeatmaps, false);
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

                beatmap = osuBeatmaps[0];
                other = osuBeatmaps[1];
            });

            createCache();
        }

        [Test]
        public void TestNothingWorkedOutWithoutMods()
        {
            startTracking();
            waitForCalculation();

            AddAssert("nothing calculated", () => cache.CalculationCount, () => Is.Zero);
            AddAssert("stored star rating used", () => cache.Current.GetStarRating(beatmap), () => Is.EqualTo(beatmap.StarRating));
        }

        [Test]
        public void TestStarRatingsMatchThoseDisplayed()
        {
            AddStep("select double time", () => SelectedMods.Value = new Mod[] { new OsuModDoubleTime() });

            startTracking();
            waitForRating();

            assertMatchesDisplayed(() => new Mod[] { new OsuModDoubleTime() });
            AddAssert("higher than without mods", () => cache.Current.GetStarRating(beatmap),
                () => Is.GreaterThan(difficultyCache.GetDifficultyAsync(beatmap, new OsuRuleset().RulesetInfo, Array.Empty<Mod>()).GetResultSafely()!.Value.Stars));
        }

        [Test]
        public void TestChangingModSettingStartsOver()
        {
            AddStep("select double time", () => SelectedMods.Value = new Mod[] { new OsuModDoubleTime() });

            startTracking();
            waitForRating();

            double firstRating = 0;
            AddStep("store rating", () => firstRating = cache.Current.GetStarRating(beatmap));

            AddStep("speed up further", () => ((OsuModDoubleTime)SelectedMods.Value.Single()).SpeedChange.Value = 2);

            AddUntilStep("rating went up", () => cache.Current.TryGetStarRating(beatmap, out double stars) && stars > firstRating);
            assertMatchesDisplayed(() => new Mod[] { new OsuModDoubleTime { SpeedChange = { Value = 2 } } });
        }

        [Test]
        public void TestChangesStarRatingsArentSortedByDontStartOver()
        {
            AddStep("select double time", () => SelectedMods.Value = new Mod[] { new OsuModDoubleTime() });

            startTracking();
            waitForRating();

            int countBefore = 0;
            double ratingBefore = 0;
            AddStep("store state", () =>
            {
                countBefore = cache.CalculationCount;
                ratingBefore = cache.Current.GetStarRating(beatmap);
            });

            AddStep("adjust pitch", () => ((OsuModDoubleTime)SelectedMods.Value.Single()).AdjustPitch.Value = true);
            AddStep("add hidden", () => SelectedMods.Value = SelectedMods.Value.Append(new OsuModHidden()).ToArray());
            AddStep("add difficulty adjust AR", () => SelectedMods.Value = SelectedMods.Value.Append(new OsuModDifficultyAdjust { ApproachRate = { Value = 10 } }).ToArray());
            AddStep("add no fail", () => SelectedMods.Value = SelectedMods.Value.Append(new OsuModNoFail()).ToArray());

            AddWaitStep("give it time", 10);

            AddAssert("nothing recalculated", () => cache.CalculationCount, () => Is.EqualTo(countBefore));
            AddAssert("rating kept", () => cache.Current.GetStarRating(beatmap), () => Is.EqualTo(ratingBefore));
        }

        [Test]
        public void TestChangingModsDropsOldRatings()
        {
            AddStep("select double time", () => SelectedMods.Value = new Mod[] { new OsuModDoubleTime() });

            startTracking();
            waitForRating();

            double doubleTimeRating = 0;
            AddStep("store rating", () => doubleTimeRating = cache.Current.GetStarRating(beatmap));

            AddStep("select half time", () => SelectedMods.Value = new Mod[] { new OsuModHalfTime() });

            // Double time's star ratings must never be used for half time, even briefly.
            AddAssert("double time ratings dropped", () => !cache.Current.TryGetStarRating(beatmap, out double stars) || stars != doubleTimeRating);

            waitForRating();
            assertMatchesDisplayed(() => new Mod[] { new OsuModHalfTime() });
        }

        [Test]
        public void TestSavedRatingsAreReused()
        {
            AddStep("select double time", () => SelectedMods.Value = new Mod[] { new OsuModDoubleTime() });

            startTracking();
            waitForRating();

            double firstRating = 0;
            AddStep("store rating", () => firstRating = cache.Current.GetStarRating(beatmap));
            AddUntilStep("ratings saved", () => LocalStorage.GetStorageForDirectory(@"cache/star-ratings").GetFiles(string.Empty).Any());

            AddStep("stop tracking", () => tracking?.Dispose());

            // As on the next launch of the game.
            createCache();
            startTracking();
            waitForRating();

            AddAssert("nothing calculated", () => cache.CalculationCount, () => Is.Zero);
            AddAssert("same rating", () => cache.Current.GetStarRating(beatmap), () => Is.EqualTo(firstRating));
        }

        [Test]
        public void TestNothingWorkedOutWithoutCollection()
        {
            AddStep("select double time", () => SelectedMods.Value = new Mod[] { new OsuModDoubleTime() });
            AddStep("start tracking nothing", () => tracking = cache.BeginTracking());
            AddWaitStep("give it time", 10);

            AddAssert("nothing calculated", () => cache.CalculationCount, () => Is.Zero);
        }

        [Test]
        public void TestOnlyCollectionIsWorkedOut()
        {
            AddStep("select double time", () => SelectedMods.Value = new Mod[] { new OsuModDoubleTime() });

            startTracking();
            waitForRating();

            AddAssert("only one calculated", () => cache.CalculationCount, () => Is.EqualTo(1));
            AddAssert("other beatmap left alone", () => cache.Current.TryGetStarRating(other, out _), () => Is.False);
        }

        [Test]
        public void TestSwitchingCollectionKeepsWhatWasWorkedOut()
        {
            AddStep("select double time", () => SelectedMods.Value = new Mod[] { new OsuModDoubleTime() });

            startTracking();
            waitForRating();

            AddStep("switch to other collection", () => tracking!.SetBeatmaps(new[] { other.MD5Hash }));

            AddUntilStep("other beatmap worked out", () => cache.Current.TryGetStarRating(other, out _));
            waitForCalculation();

            AddAssert("first beatmap kept", () => cache.Current.TryGetStarRating(beatmap, out _));
            AddAssert("each calculated once", () => cache.CalculationCount, () => Is.EqualTo(2));
        }

        [Test]
        public void TestProgressShownInNotification()
        {
            int completionsBefore = 0;
            AddStep("store completion count", () => completionsBefore = completionCount());

            AddStep("select double time", () => SelectedMods.Value = new Mod[] { new OsuModDoubleTime() });

            createCache(showNotificationsImmediately: true);
            holdCalculation();
            startTrackingBoth();

            AddUntilStep("progress shown", () => progressNotification()?.Text.ToString() ?? string.Empty, () => Does.Contain("with DT").And.Contain("0 of 2"));
            AddAssert("in progress", () => progressNotification()!.State, () => Is.EqualTo(ProgressNotificationState.Active));

            AddStep("release calculation", () => playInfo.PlayingState.Value = LocalUserPlayingState.NotPlaying);
            waitForCalculation();

            AddAssert("completed", () => notificationOverlay.AllNotifications.OfType<ProgressNotification>().Any(n => n.Ongoing), () => Is.False);
            AddUntilStep("reported as finished", completionCount, () => Is.GreaterThan(completionsBefore));
            AddAssert("ratings worked out", () => cache.Current.TryGetStarRating(beatmap, out _) && cache.Current.TryGetStarRating(other, out _));
        }

        [Test]
        public void TestShortPassShowsNoNotification()
        {
            int countBefore = 0;
            AddStep("store notification count", () => countBefore = notificationOverlay.AllNotifications.Count());

            AddStep("select double time", () => SelectedMods.Value = new Mod[] { new OsuModDoubleTime() });

            startTracking();
            waitForRating();

            AddAssert("no notification", () => notificationOverlay.AllNotifications.Count(), () => Is.EqualTo(countBefore));
        }

        [Test]
        public void TestStoppingFromNotification()
        {
            AddStep("select double time", () => SelectedMods.Value = new Mod[] { new OsuModDoubleTime() });

            createCache(showNotificationsImmediately: true);
            holdCalculation();
            startTrackingBoth();

            AddUntilStep("progress shown", () => progressNotification(), () => Is.Not.Null);
            AddStep("close notification", () => progressNotification()!.Close(false));

            AddStep("release calculation", () => playInfo.PlayingState.Value = LocalUserPlayingState.NotPlaying);
            waitForCalculation();
            AddWaitStep("give it time", 10);

            AddAssert("stopped", () => cache.IsCalculating.Value, () => Is.False);
            AddAssert("not started again", () => cache.CalculationCount, () => Is.Zero);

            // Anything changing picks it back up.
            AddStep("change collection", () => tracking!.SetBeatmaps(new[] { beatmap.MD5Hash }));
            AddUntilStep("worked out", () => cache.Current.TryGetStarRating(beatmap, out _));
        }

        [Test]
        public void TestSupersededPassIsClosedQuietly()
        {
            int completionsBefore = 0;
            AddStep("store completion count", () => completionsBefore = completionCount());

            AddStep("select double time", () => SelectedMods.Value = new Mod[] { new OsuModDoubleTime() });

            createCache(showNotificationsImmediately: true);
            holdCalculation();
            startTrackingBoth();

            ProgressNotification? first = null;
            AddUntilStep("progress shown", () => (first = progressNotification()) != null);

            AddStep("select half time", () => SelectedMods.Value = new Mod[] { new OsuModHalfTime() });

            AddUntilStep("new progress shown", () => progressNotification()?.Text.ToString() ?? string.Empty, () => Does.Contain("with HT"));
            AddUntilStep("first no longer in progress", () => first!.Ongoing, () => Is.False);
            AddAssert("first not reported as finished", completionCount, () => Is.EqualTo(completionsBefore));
        }

        [Test]
        public void TestNothingWorkedOutWhileNotTracked()
        {
            AddStep("select double time", () => SelectedMods.Value = new Mod[] { new OsuModDoubleTime() });
            AddWaitStep("give it time", 10);

            AddAssert("nothing calculated", () => cache.CalculationCount, () => Is.Zero);
        }

        [Test]
        public void TestConvertedBeatmapsFollowRuleset()
        {
            // Without mods, a converted beatmap still has a different star rating to the one stored with it.
            AddStep("show converted beatmaps", () => config.SetValue(OsuSetting.ShowConvertedBeatmaps, true));
            AddStep("select taiko", () => Ruleset.Value = new TaikoRuleset().RulesetInfo);

            startTracking();
            waitForRating();

            AddAssert("matches the displayed star rating", () => cache.Current.GetStarRating(beatmap),
                () => Is.EqualTo(difficultyCache.GetDifficultyAsync(beatmap, new TaikoRuleset().RulesetInfo, Array.Empty<Mod>()).GetResultSafely()!.Value.Stars).Within(0.0001));
            AddAssert("differs from the stored star rating", () => cache.Current.GetStarRating(beatmap), () => Is.Not.EqualTo(beatmap.StarRating));
        }

        private void createCache(bool showNotificationsImmediately = false)
        {
            AddStep("create cache", () =>
            {
                if (cache.IsNotNull())
                    Remove(cache, true);

                Add(cache = showNotificationsImmediately ? new ImmediateNotificationCache() : new ModdedStarRatingCache());
            });

            AddUntilStep("cache loaded", () => cache.IsLoaded);
        }

        /// <summary>
        /// Tracks a collection holding only <see cref="beatmap"/>.
        /// </summary>
        /// <summary>
        /// Holds the calculation where it is, by pretending gameplay is underway.
        /// </summary>
        private void holdCalculation() => AddStep("hold calculation", () => playInfo.PlayingState.Value = LocalUserPlayingState.Playing);

        private int completionCount() => notificationOverlay.AllNotifications.OfType<ProgressCompletionNotification>().Count();

        private ProgressNotification? progressNotification() => notificationOverlay.AllNotifications.OfType<ProgressNotification>().LastOrDefault(n => n.Ongoing);

        private void startTrackingBoth() => AddStep("start tracking both beatmaps", () =>
        {
            tracking = cache.BeginTracking();
            tracking.SetBeatmaps(new[] { beatmap.MD5Hash, other.MD5Hash });
        });

        private void startTracking() => AddStep("start tracking", () =>
        {
            tracking = cache.BeginTracking();
            tracking.SetBeatmaps(new[] { beatmap.MD5Hash });
        });

        private void waitForCalculation() => AddUntilStep("calculation finished", () => !cache.IsCalculating.Value);

        private void waitForRating()
        {
            AddUntilStep("rating worked out", () => cache.Current.TryGetStarRating(beatmap, out _));
            waitForCalculation();
        }

        private void assertMatchesDisplayed(Func<Mod[]> mods)
        {
            // What song select displays for a beatmap comes from the difficulty cache.
            AddAssert("matches the displayed star rating", () => cache.Current.GetStarRating(beatmap),
                () => Is.EqualTo(difficultyCache.GetDifficultyAsync(beatmap, new OsuRuleset().RulesetInfo, mods()).GetResultSafely()!.Value.Stars).Within(0.0001));
        }

        private partial class ImmediateNotificationCache : ModdedStarRatingCache
        {
            protected override double NotificationDelay => 0;
        }

        private class TestPlayInfo : ILocalUserPlayInfo
        {
            public Bindable<LocalUserPlayingState> PlayingState { get; } = new Bindable<LocalUserPlayingState>();

            IBindable<LocalUserPlayingState> ILocalUserPlayInfo.PlayingState => PlayingState;
        }

        protected override void Dispose(bool isDisposing)
        {
            tracking?.Dispose();
            base.Dispose(isDisposing);

            if (rulesetStore.IsNotNull())
                rulesetStore.Dispose();
        }
    }
}
