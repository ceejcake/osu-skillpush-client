// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Extensions;
using osu.Framework.Utils;
using osu.Game.Graphics;
using osu.Game.Utils;

namespace osu.Game.Rulesets.Mods
{
    public abstract class ModDifficultyAdjust : Mod, IApplicableToDifficultyWithMods
    {
        public override string Name => @"Difficulty Adjust";

        public override LocalisableString Description => @"Override a beatmap's difficulty settings.";

        public override string Acronym => "DA";

        public override ModType Type => ModType.Conversion;

        public override IconUsage? Icon => OsuIcon.ModDifficultyAdjust;

        public override bool RequiresConfiguration => true;

        public override bool ValidForFreestyleAsRequiredMod => true;

        // This mod has the final say on every difficulty setting it overrides, so it can sit alongside anything else.
        public override Type[] IncompatibleMods => Array.Empty<Type>();

        // Each setting is preceded by the toggle which turns it on, so the orders leave room for both.
        protected const int FIRST_SETTING_ORDER = 3;

        protected const int LAST_SETTING_ORDER = 5;

        [SettingSource("Adjust HP drain", "Take over this beatmap's HP, ignoring what other mods would do to it.", FIRST_SETTING_ORDER - 1)]
        public BindableBool AdjustDrainRate { get; } = new BindableBool();

        [SettingSource("HP Drain", "Override a beatmap's set HP.", FIRST_SETTING_ORDER, SettingControlType = typeof(DifficultyAdjustSettingsControl))]
        public DifficultyBindable DrainRate { get; } = new DifficultyBindable
        {
            Precision = 0.1f,
            MinValue = 0,
            MaxValue = 10,
            ExtendedMaxValue = 11,
            ReadCurrentFromDifficulty = diff => diff.DrainRate,
        };

        [SettingSource("Adjust accuracy", "Take over this beatmap's OD, ignoring what other mods would do to it.", LAST_SETTING_ORDER - 1)]
        public BindableBool AdjustOverallDifficulty { get; } = new BindableBool();

        [SettingSource("Accuracy", "Override a beatmap's set OD.", LAST_SETTING_ORDER, SettingControlType = typeof(DifficultyAdjustSettingsControl))]
        public virtual DifficultyBindable OverallDifficulty { get; } = new DifficultyBindable
        {
            Precision = 0.1f,
            MinValue = 0,
            MaxValue = 10,
            ExtendedMaxValue = 11,
            ReadCurrentFromDifficulty = diff => diff.OverallDifficulty,
        };

        [SettingSource("Extended Limits", "Adjust difficulty beyond sane limits.")]
        public BindableBool ExtendedLimits { get; } = new BindableBool(true);

        protected ModDifficultyAdjust()
        {
            foreach (var (_, property) in this.GetOrderedSettingsSourceProperties())
            {
                if (property.GetValue(this) is DifficultyBindable diffAdjustBindable)
                    diffAdjustBindable.ExtendedLimits.BindTo(ExtendedLimits);
            }
        }

        public override string ExtendedIconInformation
        {
            get
            {
                if (!IsExactlyOneSettingChanged(OverallDifficulty, DrainRate))
                    return string.Empty;

                if (AdjustOverallDifficulty.Value && !OverallDifficulty.IsDefault) return format("OD", OverallDifficulty);
                if (AdjustDrainRate.Value && !DrainRate.IsDefault) return format("HP", DrainRate);

                return string.Empty;

                string format(string acronym, DifficultyBindable bindable) => $"{acronym}{bindable.Value!.Value.ToStandardFormattedString(1)}";
            }
        }

        protected bool IsExactlyOneSettingChanged(params DifficultyBindable[] difficultySettings)
        {
            DifficultyBindable? changedSetting = null;

            foreach (var setting in difficultySettings)
            {
                if (setting.IsDefault)
                    continue;

                if (changedSetting != null)
                    return false;

                changedSetting = setting;
            }

            return changedSetting != null;
        }

        public override IEnumerable<(LocalisableString setting, LocalisableString value)> SettingDescription
        {
            get
            {
                if (AdjustDrainRate.Value && !DrainRate.IsDefault)
                    yield return ("HP drain", $"{DrainRate.Value:N1}");

                if (AdjustOverallDifficulty.Value && !OverallDifficulty.IsDefault)
                    yield return ("Accuracy", $"{OverallDifficulty.Value:N1}");
            }
        }

        public void ApplyToDifficulty(IReadOnlyCollection<Mod> mods, BeatmapDifficulty difficulty)
        {
            ApplySettings(difficulty);

            // The settings are read as the difficulty which should be played at, so the effect other mods have on the
            // rate of play is cancelled out. Setting AR10 with double time active means AR10 to play against, not AR10
            // sped up to AR11.4.
            double rate = ModUtils.CalculateRateWithMods(mods);

            if (!Precision.AlmostEquals(rate, 1))
                CompensateForRate(difficulty, rate);
        }

        /// <summary>
        /// Adjusts the settings which are affected by the rate of play, so that they are experienced as they were set.
        /// </summary>
        /// <param name="difficulty">The difficulty to mutate.</param>
        /// <param name="rate">The rate of play resulting from all active mods.</param>
        protected virtual void CompensateForRate(BeatmapDifficulty difficulty, double rate)
        {
        }

        /// <summary>
        /// Apply the settings which are turned on to the provided beatmap.
        /// </summary>
        /// <param name="difficulty">The beatmap to have settings applied.</param>
        protected virtual void ApplySettings(BeatmapDifficulty difficulty)
        {
            if (AdjustDrainRate.Value && DrainRate.Value != null) difficulty.DrainRate = DrainRate.Value.Value;
            if (AdjustOverallDifficulty.Value && OverallDifficulty.Value != null) difficulty.OverallDifficulty = OverallDifficulty.Value.Value;
        }
    }
}
