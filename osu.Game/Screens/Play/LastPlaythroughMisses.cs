// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;

namespace osu.Game.Screens.Play
{
    /// <summary>
    /// Where the misses were in the last full playthrough of a beatmap, shown on the practice strain graph.
    /// Kept across restarts (and partial or seeked plays) until the beatmap is played through again, or a different beatmap is played.
    /// </summary>
    /// <param name="BeatmapID">The beatmap which was played.</param>
    /// <param name="MissTimes">The times of the objects which were missed, in beatmap time.</param>
    public record LastPlaythroughMisses(Guid BeatmapID, IReadOnlyList<double> MissTimes);
}
