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
using osu.Game.Configuration;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Overlays.Login;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Tests.Resources;

namespace osu.Game.Tests.Visual.Menus
{
    public partial class TestSceneLocalPerformance : OsuTestScene
    {
        [Cached]
        private readonly OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Purple);

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        private RulesetStore rulesetStore = null!;
        private BeatmapManager beatmapManager = null!;
        private ScoreManager scoreManager = null!;
        private BeatmapDifficultyCache difficultyCache = null!;
        private LocalPerformanceTracker tracker = null!;

        private Container panelContainer = null!;

        private BeatmapInfo beatmap = null!;

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));

            dependencies.Cache(rulesetStore = new RealmRulesetStore(Realm));
            dependencies.Cache(beatmapManager = new BeatmapManager(LocalStorage, Realm, null, dependencies.Get<AudioManager>(), Resources, dependencies.Get<GameHost>(), Beatmap.Default));
            dependencies.Cache(scoreManager = new ScoreManager(rulesetStore, () => beatmapManager, LocalStorage, Realm, API));
            dependencies.Cache(difficultyCache = new BeatmapDifficultyCache());
            dependencies.Cache(tracker = new LocalPerformanceTracker());
            dependencies.Cache(Realm);

            return dependencies;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Add(difficultyCache);
            Add(tracker);
            Add(panelContainer = new Container { RelativeSizeAxes = Axes.Both });
        }

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("reset settings", () => config.SetValue(OsuSetting.GuestPerformanceIncludesUnranked, false));
            AddStep("clear scores", () => scoreManager.Delete());

            AddStep("import ranked beatmap", () =>
            {
                beatmapManager.Import(TestResources.GetQuickTestBeatmapForImport()).WaitSafely();
                beatmap = beatmapManager.GetAllUsableBeatmapSets().First().Beatmaps.First(b => b.Ruleset.ShortName == OsuRuleset.SHORT_NAME);
                setStatus(BeatmapOnlineStatus.Ranked);
            });

            AddStep("show profile", () => panelContainer.Child = new GuestProfilePanel
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Width = 360,
            });
        }

        [Test]
        public void TestBestLocalScoreCounts()
        {
            ScoreInfo best = null!;
            ScoreInfo worse = null!;
            ScoreInfo otherPlayer = null!;

            AddStep("import scores", () =>
            {
                best = importScore(misses: 0);
                worse = importScore(misses: 10);
                otherPlayer = importScore(misses: 0, userId: 2);
            });

            refresh();

            AddUntilStep("local scores given pp", () => getPP(best) != null && getPP(worse) != null);
            AddAssert("imported score from another player left alone", () => getPP(otherPlayer), () => Is.Null);

            AddUntilStep("one beatmap counted", () => tracker.Summary.Value.CountedBeatmaps, () => Is.EqualTo(1));
            AddAssert("best score is the top play", () => tracker.Summary.Value.TopPlays.Single().Score.ID, () => Is.EqualTo(best.ID));
            AddAssert("total includes bonus", () => tracker.Summary.Value.TotalPerformance,
                () => Is.EqualTo(getPP(best)!.Value + (417.0 - 1.0 / 3.0) * (1 - 0.995)).Within(0.001));
            AddUntilStep("top play shown", () => this.ChildrenOfType<GuestProfilePanel>().Single().ChildrenOfType<Drawable>().Count(d => d.GetType().Name == "TopPlayRow"), () => Is.EqualTo(1));
        }

        [Test]
        public void TestUnrankedBeatmapOnlyCountsWhenAsked()
        {
            AddStep("make beatmap unranked", () => setStatus(BeatmapOnlineStatus.Pending));
            AddStep("import score", () => importScore(misses: 0));

            refresh();

            AddUntilStep("nothing counted", () => tracker.Summary.Value.PendingScores == 0 && tracker.Summary.Value.CountedBeatmaps == 0);

            AddStep("count unranked plays", () => config.SetValue(OsuSetting.GuestPerformanceIncludesUnranked, true));
            refresh();

            AddUntilStep("score counted", () => tracker.Summary.Value.CountedBeatmaps, () => Is.EqualTo(1));
        }

        [Test]
        public void TestRankedModsFollowOnlineRules()
        {
            AddAssert("no mods is ranked", () => LocalPerformanceTracker.IsRankedOnline(createScore(Array.Empty<Mod>())));
            AddAssert("hidden is ranked", () => LocalPerformanceTracker.IsRankedOnline(createScore(new Mod[] { new OsuModHidden() })));
            AddAssert("double time is ranked", () => LocalPerformanceTracker.IsRankedOnline(createScore(new Mod[] { new OsuModDoubleTime() })));
            AddAssert("customised rate is unranked", () => LocalPerformanceTracker.IsRankedOnline(createScore(new Mod[] { new OsuModDoubleTime { SpeedChange = { Value = 1.3 } } })), () => Is.False);
            AddAssert("bpm lock is unranked", () => LocalPerformanceTracker.IsRankedOnline(createScore(new Mod[] { new OsuModDoubleTime { LockBpm = { Value = true } } })), () => Is.False);
            AddAssert("cross screen is unranked", () => LocalPerformanceTracker.IsRankedOnline(createScore(new Mod[] { new OsuModCrossScreen() })), () => Is.False);
            AddAssert("failed is unranked", () => LocalPerformanceTracker.IsRankedOnline(createScore(Array.Empty<Mod>(), ScoreRank.F)), () => Is.False);
        }

        [Test]
        public void TestWeighting()
        {
            AddAssert("weights best scores", () =>
            {
                var entries = new[]
                {
                    new PerformanceEntry(Guid.NewGuid(), "a", 100, 1),
                    new PerformanceEntry(Guid.NewGuid(), "a", 300, 0.9), // best on its beatmap
                    new PerformanceEntry(Guid.NewGuid(), "b", 200, 0.8),
                };

                (double total, double accuracy, var counted) = LocalPerformanceSummary.Calculate(entries);

                double expectedBonus = (417.0 - 1.0 / 3.0) * (1 - Math.Pow(0.995, 2));

                return counted.Count == 2
                       && Math.Abs(total - (300 + 200 * 0.95 + expectedBonus)) < 0.001
                       && Math.Abs(accuracy - (0.9 + 0.8 * 0.95) / 1.95) < 0.0001;
            });
        }

        private void refresh() => AddStep("refresh", () => tracker.Refresh());

        private void setStatus(BeatmapOnlineStatus status) => Realm.Write(r => r.Find<BeatmapInfo>(beatmap.ID)!.Status = status);

        private double? getPP(ScoreInfo score) => Realm.Run(r => r.Find<ScoreInfo>(score.ID)?.PP);

        private ScoreInfo createScore(Mod[] mods, ScoreRank rank = ScoreRank.S) => new ScoreInfo
        {
            Rank = rank,
            BeatmapInfo = new BeatmapInfo { Status = BeatmapOnlineStatus.Ranked },
            Ruleset = new OsuRuleset().RulesetInfo,
            Mods = mods,
        };

        private ScoreInfo importScore(int misses, int userId = APIUser.SYSTEM_USER_ID)
        {
            int objects = beatmapManager.GetWorkingBeatmap(beatmap).Beatmap.HitObjects.Count;

            var score = new ScoreInfo
            {
                Rank = misses == 0 ? ScoreRank.X : ScoreRank.A,
                Accuracy = (double)(objects - misses) / objects,
                MaxCombo = objects - misses,
                TotalScore = 1000000,
                Date = DateTimeOffset.Now,
                Statistics = new Dictionary<HitResult, int>
                {
                    { HitResult.Great, objects - misses },
                    { HitResult.Miss, misses },
                },
                MaximumStatistics = new Dictionary<HitResult, int>
                {
                    { HitResult.Great, objects },
                },
                Ruleset = rulesetStore.GetRuleset(OsuRuleset.SHORT_NAME)!,
                BeatmapInfo = beatmap,
                BeatmapHash = beatmap.Hash,
                User = new APIUser { Id = userId, Username = "tester" },
            };

            return scoreManager.Import(score)!.Value;
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (rulesetStore.IsNotNull())
                rulesetStore.Dispose();
        }
    }
}
