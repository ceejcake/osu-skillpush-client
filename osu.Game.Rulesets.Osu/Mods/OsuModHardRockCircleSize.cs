// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Utils;

namespace osu.Game.Rulesets.Osu.Mods
{
    /// <summary>
    /// Hard Rock's circle size increase and vertical flip, without its changes to the other difficulty settings.
    /// </summary>
    /// <remarks>
    /// This deliberately doesn't derive from <see cref="ModHardRock"/>, so that it is never treated as Hard Rock
    /// (e.g. by score and difficulty calculation, or the classic mod hotkeys).
    /// </remarks>
    public class OsuModHardRockCircleSize : Mod, IApplicableToDifficulty, IApplicableToHitObject, IHasUpsideDownIcon
    {
        /// <summary>
        /// The same ratio Hard Rock increases circle size by.
        /// </summary>
        private const float circle_size_ratio = 1.3f;

        public override string Name => "HR but only the CS";
        public override string Acronym => "HRC";
        public override IconUsage? Icon => OsuIcon.ModHardRock;
        public override ModType Type => ModType.DifficultyIncrease;
        public override LocalisableString Description => "Hard Rock's smaller circles and flipped map, with the other settings left alone.";
        public override bool Ranked => false;
        public override Type[] IncompatibleMods => new[] { typeof(ModHardRock), typeof(ModMirror) };

        public void ApplyToDifficulty(BeatmapDifficulty difficulty)
        {
            difficulty.CircleSize = Math.Min(difficulty.CircleSize * circle_size_ratio, 10.0f);
        }

        public void ApplyToHitObject(HitObject hitObject)
        {
            OsuHitObjectGenerationUtils.ReflectVerticallyAlongPlayfield((OsuHitObject)hitObject);
        }
    }
}
