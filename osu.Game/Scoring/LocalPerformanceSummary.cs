// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;

namespace osu.Game.Scoring
{
    /// <summary>
    /// The performance totals of the local player, worked out from their local scores in the same way as osu! does online.
    /// </summary>
    public class LocalPerformanceSummary
    {
        public static readonly LocalPerformanceSummary EMPTY = new LocalPerformanceSummary(0, 0, 0, Array.Empty<LocalTopPlay>(), 0);

        /// <summary>
        /// The number of best scores which contribute to the totals. Scores beyond this carry no weight online either.
        /// </summary>
        public const int MAXIMUM_COUNTED_SCORES = 1000;

        /// <summary>
        /// The number of top plays kept for display.
        /// </summary>
        public const int MAXIMUM_DISPLAYED_PLAYS = 100;

        private const double weighting_factor = 0.95;

        /// <summary>
        /// The total performance, including the bonus awarded for the number of beatmaps played.
        /// </summary>
        public double TotalPerformance { get; }

        /// <summary>
        /// The weighted accuracy of the counted scores, from 0 to 1.
        /// </summary>
        public double Accuracy { get; }

        /// <summary>
        /// The number of beatmaps with a score that counts towards the totals.
        /// </summary>
        public int CountedBeatmaps { get; }

        /// <summary>
        /// The best scores, highest performance first.
        /// </summary>
        public IReadOnlyList<LocalTopPlay> TopPlays { get; }

        /// <summary>
        /// The number of scores which are still waiting for their performance to be calculated.
        /// </summary>
        public int PendingScores { get; }

        public LocalPerformanceSummary(double totalPerformance, double accuracy, int countedBeatmaps, IReadOnlyList<LocalTopPlay> topPlays, int pendingScores)
        {
            TotalPerformance = totalPerformance;
            Accuracy = accuracy;
            CountedBeatmaps = countedBeatmaps;
            TopPlays = topPlays;
            PendingScores = pendingScores;
        }

        /// <summary>
        /// Weights the best score on each beatmap into totals.
        /// </summary>
        /// <param name="entries">Every eligible score. Only the best one on each beatmap is counted.</param>
        /// <returns>The totals, and the counted scores with their weights, highest performance first.</returns>
        public static (double totalPerformance, double accuracy, IReadOnlyList<(PerformanceEntry entry, double weight)> counted) Calculate(IEnumerable<PerformanceEntry> entries)
        {
            var best = entries.GroupBy(e => e.BeatmapKey)
                              .Select(g => g.MaxBy(e => e.Performance))
                              .OrderByDescending(e => e.Performance)
                              .Take(MAXIMUM_COUNTED_SCORES)
                              .ToArray();

            var counted = new List<(PerformanceEntry, double)>(best.Length);

            double totalPerformance = 0;
            double totalAccuracy = 0;
            double weight = 1;

            foreach (var entry in best)
            {
                totalPerformance += entry.Performance * weight;
                totalAccuracy += entry.Accuracy * weight;
                counted.Add((entry, weight));

                weight *= weighting_factor;
            }

            if (best.Length == 0)
                return (0, 0, counted);

            // A small bonus is given for each beatmap played, up to a limit.
            totalPerformance += (417.0 - 1.0 / 3.0) * (1 - Math.Pow(0.995, Math.Min(best.Length, MAXIMUM_COUNTED_SCORES)));

            // The weights sum to less than one, so the accuracy is scaled back up to a proportion.
            double accuracy = totalAccuracy / ((1 - Math.Pow(weighting_factor, best.Length)) / (1 - weighting_factor));

            return (totalPerformance, accuracy, counted);
        }
    }

    /// <summary>
    /// A score which is eligible to count towards the local player's performance.
    /// </summary>
    /// <param name="ScoreID">The ID of the score.</param>
    /// <param name="BeatmapKey">Identifies the beatmap the score was set on. Only one score per beatmap is counted.</param>
    /// <param name="Performance">The performance points awarded for the score.</param>
    /// <param name="Accuracy">The accuracy of the score, from 0 to 1.</param>
    public readonly record struct PerformanceEntry(Guid ScoreID, string BeatmapKey, double Performance, double Accuracy);

    /// <summary>
    /// One of the local player's best scores.
    /// </summary>
    /// <param name="Score">The score, detached from the database.</param>
    /// <param name="Performance">The performance points awarded for the score.</param>
    /// <param name="Weight">How much of <paramref name="Performance"/> counts towards the total.</param>
    public record LocalTopPlay(ScoreInfo Score, double Performance, double Weight);
}
