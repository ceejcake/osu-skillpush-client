// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Scoring
{
    /// <summary>
    /// An alternative way of turning judgements into a total score, provided by a <see cref="Mod"/>.
    /// </summary>
    /// <remarks>
    /// Only the total score is affected. Judgements, accuracy and rank are worked out as they always are.
    /// <para>
    /// A ruleset whose <see cref="ScoreProcessor"/> overrides <see cref="ScoreProcessor.GetComboScoreChange"/> without
    /// calling the base implementation will ignore the combo weighting given here, so a mod implementing this should
    /// only be offered by rulesets it has been checked against.
    /// </para>
    /// </remarks>
    public interface IScoringFormula : IApplicableMod
    {
        /// <summary>
        /// How much the combo portion of the score grows for a judgement.
        /// </summary>
        double GetComboScoreChange(ScoreProcessor processor, JudgementResult result);

        /// <summary>
        /// The total score, before mod multipliers are applied.
        /// </summary>
        /// <param name="processor">The score processor, for reading the current accuracy and judgement counts.</param>
        /// <param name="comboProgress">How much of the maximum combo portion has been earned, from 0 to 1.</param>
        /// <param name="accuracyProgress">How many of the accuracy-affecting judgements have been made, from 0 to 1.</param>
        /// <param name="bonusPortion">The score earned from bonus judgements, such as spinner spins.</param>
        double ComputeTotalScore(ScoreProcessor processor, double comboProgress, double accuracyProgress, double bonusPortion);
    }
}
