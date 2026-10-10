// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Graphics.Carousel;
using osu.Game.Scoring;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;
using osu.Game.Tests.Resources;

namespace osu.Game.Tests.Visual.SongSelect
{
    /// <summary>
    /// Filtering, sorting and grouping by star rating have to use the star ratings under the current mods, as displayed,
    /// rather than the ones stored with each beatmap.
    /// </summary>
    [TestFixture]
    public class BeatmapCarouselModdedStarRatingTest
    {
        private BeatmapInfo easy = null!;
        private BeatmapInfo hard = null!;
        private BeatmapInfo notWorkedOut = null!;

        private List<BeatmapInfo> beatmaps = null!;

        /// <summary>
        /// With mods, the stored order of <see cref="easy"/> and <see cref="hard"/> is reversed.
        /// </summary>
        private StarRatingSnapshot snapshot = null!;

        [SetUp]
        public void SetUp()
        {
            beatmaps = TestResources.CreateTestBeatmapSetInfo(3).Beatmaps.ToList();

            easy = beatmaps[0];
            hard = beatmaps[1];
            notWorkedOut = beatmaps[2];

            easy.StarRating = 2;
            hard.StarRating = 3;
            notWorkedOut.StarRating = 4.5;

            snapshot = new StarRatingSnapshot(new Dictionary<string, double>
            {
                [easy.MD5Hash] = 6,
                [hard.MD5Hash] = 5,
            });
        }

        [Test]
        public void TestRangeFilterUsesModdedStarRating()
        {
            var criteria = new FilterCriteria { StarRatings = snapshot };
            criteria.UserStarDifficulty.Min = 5.5;

            Assert.That(match(criteria), Is.EquivalentTo(new[] { easy }));
        }

        [Test]
        public void TestSearchFilterUsesModdedStarRating()
        {
            var criteria = new FilterCriteria { StarRatings = snapshot };
            FilterQueryParser.ApplyQueries(criteria, "stars<5.5");

            Assert.That(match(criteria), Is.EquivalentTo(new[] { hard, notWorkedOut }));
        }

        [Test]
        public void TestBeatmapNotWorkedOutFallsBackToStoredStarRating()
        {
            var criteria = new FilterCriteria { StarRatings = snapshot };
            criteria.UserStarDifficulty.Min = 4;
            criteria.UserStarDifficulty.Max = 4.9;

            Assert.That(match(criteria), Is.EquivalentTo(new[] { notWorkedOut }));
        }

        [Test]
        public void TestWithoutSnapshotStoredStarRatingIsUsed()
        {
            var criteria = new FilterCriteria();
            criteria.UserStarDifficulty.Min = 2.5;

            Assert.That(match(criteria), Is.EquivalentTo(new[] { hard, notWorkedOut }));
        }

        [Test]
        public async Task TestSortingUsesModdedStarRating()
        {
            var sorter = new BeatmapCarouselFilterSorting(() => new FilterCriteria { Sort = SortMode.Difficulty, StarRatings = snapshot });

            var results = (await sorter.Run(items(), CancellationToken.None)).Select(i => i.Model).ToList();

            Assert.That(results, Is.EqualTo(new[] { notWorkedOut, hard, easy }));
        }

        [Test]
        public async Task TestGroupingUsesModdedStarRating()
        {
            var grouping = new BeatmapCarouselFilterGrouping
            {
                GetCriteria = () => new FilterCriteria { Group = GroupMode.Difficulty, Sort = SortMode.Difficulty, StarRatings = snapshot },
                GetCollections = () => new List<BeatmapCollection>(),
                GetLocalUserTopRanks = _ => new Dictionary<Guid, ScoreRank>(),
                GetFavouriteBeatmapSets = () => new HashSet<int>(),
            };

            var results = await grouping.Run(items(), CancellationToken.None);

            Assert.That(groupOf(results, easy), Is.EqualTo(6));
            Assert.That(groupOf(results, hard), Is.EqualTo(5));
            Assert.That(groupOf(results, notWorkedOut), Is.EqualTo(4));
        }

        [Test]
        public void TestCriteriaKnowWhetherStarRatingMatters()
        {
            Assert.That(new FilterCriteria { Sort = SortMode.Title }.DependsOnStarRating, Is.False);
            Assert.That(new FilterCriteria { Sort = SortMode.Difficulty }.DependsOnStarRating, Is.True);
            Assert.That(new FilterCriteria { Group = GroupMode.Difficulty }.DependsOnStarRating, Is.True);

            var ranged = new FilterCriteria();
            ranged.UserStarDifficulty.Max = 5;
            Assert.That(ranged.DependsOnStarRating, Is.True);

            var searched = new FilterCriteria();
            FilterQueryParser.ApplyQueries(searched, "stars>3");
            Assert.That(searched.DependsOnStarRating, Is.True);
        }

        private List<CarouselItem> items() => beatmaps.Select(b => new CarouselItem(b)).ToList();

        private IEnumerable<BeatmapInfo> match(FilterCriteria criteria) => beatmaps.Where(b => BeatmapCarouselFilterMatching.CheckCriteriaMatch(b, criteria));

        /// <returns>The star rating of the group the beatmap was put in.</returns>
        private static int groupOf(List<CarouselItem> results, BeatmapInfo beatmap)
        {
            var grouped = results.Select(i => i.Model).OfType<GroupedBeatmap>().Single(g => g.Beatmap == beatmap);

            return grouped.Group!.Order;
        }
    }
}
