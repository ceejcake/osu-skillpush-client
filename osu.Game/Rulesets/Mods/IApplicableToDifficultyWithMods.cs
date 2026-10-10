// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Game.Beatmaps;

namespace osu.Game.Rulesets.Mods
{
    /// <summary>
    /// An interface for <see cref="Mod"/>s that make adjustments to a beatmap's difficulty based on the other mods in use.
    /// </summary>
    /// <remarks>
    /// These are applied after all <see cref="IApplicableToDifficulty"/> mods, so a mod which overrides difficulty settings
    /// outright can rely on having the last word.
    /// </remarks>
    public interface IApplicableToDifficultyWithMods : IApplicableMod
    {
        /// <summary>
        /// Applies this <see cref="IApplicableToDifficultyWithMods"/> to a <see cref="BeatmapDifficulty"/>.
        /// </summary>
        /// <param name="mods">All mods in use, this one included.</param>
        /// <param name="difficulty">The difficulty to mutate.</param>
        void ApplyToDifficulty(IReadOnlyCollection<Mod> mods, BeatmapDifficulty difficulty);
    }
}
