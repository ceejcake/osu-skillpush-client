// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Game.Screens.Play
{
    /// <summary>
    /// A point in a beatmap which gameplay restarts from, for practising a section of it repeatedly.
    /// </summary>
    /// <param name="BeatmapID">The beatmap the checkpoint was placed in.</param>
    /// <param name="Time">The time gameplay should begin at.</param>
    public record PracticeCheckpoint(Guid BeatmapID, double Time);
}
