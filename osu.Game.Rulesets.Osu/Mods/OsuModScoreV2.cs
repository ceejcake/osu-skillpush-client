// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Localisation;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Osu.Mods
{
    /// <summary>
    /// Scores the play the way osu!stable's "Score V2" mod did.
    /// </summary>
    /// <remarks>
    /// Only the score calculation is taken from stable. The other half of that mod, slider ends counting towards
    /// accuracy, is how this game already works.
    /// <para>
    /// Judgements keep their usual values, so slider ticks and ends are worth what they are worth here rather than
    /// what stable would have given them.
    /// </para>
    /// </remarks>
    public class OsuModScoreV2 : ModScoreV2, IScoringFormula
    {
        public override LocalisableString Description => "Score the way osu!stable's Score V2 did.";
        public override ModType Type => ModType.Conversion;

        /// <summary>
        /// While Score V2 is the default (see <see cref="ScoringFormulaDefaults.ScoreV2ByDefault"/>), selecting it would do
        /// nothing, so <see cref="OsuModLazerScoring"/> is offered in its place.
        /// </summary>
        public override bool UserPlayable => !ScoringFormulaDefaults.ScoreV2ByDefault.Value;

        public override Type[] IncompatibleMods => new[] { typeof(OsuModLazerScoring) };

        /// <summary>
        /// The share of the score earned by keeping combo. The rest is earned by accuracy.
        /// </summary>
        private const double combo_portion = 700000;

        private const double accuracy_portion = 1000000 - combo_portion;

        /// <summary>
        /// How much each point of combo adds to the value of a judgement.
        /// </summary>
        private const double combo_bonus = 0.1;

        /// <summary>
        /// The exponent which makes the accuracy portion punishing at the top end.
        /// </summary>
        private const double accuracy_exponent = 10;

        /// <remarks>
        /// Unlike the default formula, the value actually judged is used rather than the best the object could have
        /// given: in Score V2, dropping combo is not the only cost of a 100.
        /// </remarks>
        public double GetComboScoreChange(ScoreProcessor processor, JudgementResult result)
            => processor.GetBaseScoreForResult(result.Type) * (1 + result.ComboAfterJudgement * combo_bonus);

        public double ComputeTotalScore(ScoreProcessor processor, double comboProgress, double accuracyProgress, double bonusPortion)
            => combo_portion * comboProgress
               + accuracy_portion * Math.Pow(processor.Accuracy.Value, accuracy_exponent) * accuracyProgress
               + bonusPortion;
    }
}
