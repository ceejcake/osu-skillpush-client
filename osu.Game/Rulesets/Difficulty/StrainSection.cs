// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Game.Rulesets.Difficulty
{
    /// <summary>
    /// The difficulty of one section of a beatmap, as calculated by <see cref="DifficultyCalculator.CalculateStrainSections"/>.
    /// </summary>
    /// <param name="Time">The time at which this section ends, in beatmap time.</param>
    /// <param name="Strain">The strain of this section, normalised to the range [0, 1] across the beatmap.</param>
    public readonly record struct StrainSection(double Time, double Strain);
}
