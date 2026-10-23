// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Bindables;

namespace osu.Game.Rulesets.Scoring
{
    /// <summary>
    /// How plays are scored when no mod says otherwise.
    /// </summary>
    /// <remarks>
    /// Static, because mods and rulesets are created in many places which have no access to the game's configuration,
    /// yet which mods can be selected depends on this. The game binds it to the configuration on startup.
    /// </remarks>
    public static class ScoringFormulaDefaults
    {
        /// <summary>
        /// Whether plays are scored the way osu!stable's Score V2 did (where the ruleset offers it), unless a mod asks for
        /// the standard scoring instead.
        /// </summary>
        public static readonly Bindable<bool> ScoreV2ByDefault = new Bindable<bool>();
    }
}
