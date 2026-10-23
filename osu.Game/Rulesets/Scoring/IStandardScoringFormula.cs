// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Scoring
{
    /// <summary>
    /// A <see cref="Mod"/> which asks for the standard total score formula, for when Score V2 would otherwise be used by
    /// default (see <see cref="ScoringFormulaDefaults.ScoreV2ByDefault"/>).
    /// </summary>
    public interface IStandardScoringFormula : IApplicableMod
    {
    }
}
