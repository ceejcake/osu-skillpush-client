// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Osu.Mods
{
    /// <summary>
    /// Scores the play the usual way, for when Score V2 has been made the default in the gameplay settings.
    /// The opposite of <see cref="OsuModScoreV2"/>: only one of the two can be selected at a time, depending on that setting.
    /// </summary>
    public class OsuModLazerScoring : Mod, IStandardScoringFormula, IHasUpsideDownIcon
    {
        public override string Name => "Lazer Scoring";
        public override string Acronym => "LZ";
        public override IconUsage? Icon => OsuIcon.ModScoreV2;
        public override ModType Type => ModType.Conversion;
        public override LocalisableString Description => "Score the usual way rather than with Score V2, which is the default in your gameplay settings.";
        public override bool UserPlayable => ScoringFormulaDefaults.ScoreV2ByDefault.Value;
        public override bool ValidForMultiplayer => false;
        public override bool ValidForMultiplayerAsFreeMod => false;
        public override Type[] IncompatibleMods => new[] { typeof(ModScoreV2) };
    }
}
