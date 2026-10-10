// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;

namespace osu.Game.Beatmaps
{
    /// <summary>
    /// The star ratings of the beatmap library under one ruleset and set of mods, as far as they have been worked out.
    /// </summary>
    /// <remarks>
    /// Never changes once created, so it can be read from any thread for the length of a filter operation.
    /// </remarks>
    public sealed class StarRatingSnapshot
    {
        public static readonly StarRatingSnapshot EMPTY = new StarRatingSnapshot(new Dictionary<string, double>());

        /// <summary>
        /// Star ratings keyed by <see cref="BeatmapInfo.MD5Hash"/>.
        /// </summary>
        private readonly IReadOnlyDictionary<string, double> ratings;

        /// <summary>
        /// The number of beatmaps with a worked out star rating.
        /// </summary>
        public int Count => ratings.Count;

        public StarRatingSnapshot(IReadOnlyDictionary<string, double> ratings)
        {
            this.ratings = ratings;
        }

        /// <summary>
        /// The star rating of a beatmap under this snapshot's ruleset and mods.
        /// </summary>
        /// <remarks>
        /// A beatmap which hasn't been worked out yet, or which never needs to be (one played in its own ruleset with no
        /// mods), falls back to its stored star rating.
        /// </remarks>
        public double GetStarRating(BeatmapInfo beatmap) => TryGetStarRating(beatmap, out double stars) ? stars : beatmap.StarRating;

        public bool TryGetStarRating(BeatmapInfo beatmap, out double stars) => ratings.TryGetValue(beatmap.MD5Hash, out stars);
    }
}
