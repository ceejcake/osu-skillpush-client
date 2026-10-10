// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Configuration;
using osu.Game.Extensions;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Scoring;

namespace osu.Game.Rulesets.Osu.Mods
{
    public partial class OsuModDifficultyAdjust : ModDifficultyAdjust
    {
        [SettingSource("Adjust circle size", "Take over this beatmap's CS, ignoring what other mods would do to it.", FIRST_SETTING_ORDER - 3)]
        public BindableBool AdjustCircleSize { get; } = new BindableBool();

        [SettingSource("Circle Size", "Override a beatmap's set CS.", FIRST_SETTING_ORDER - 2, SettingControlType = typeof(DifficultyAdjustSettingsControl))]
        public DifficultyBindable CircleSize { get; } = new DifficultyBindable
        {
            Precision = 0.1f,
            MinValue = 0,
            MaxValue = 10,
            ExtendedMaxValue = 11,
            ReadCurrentFromDifficulty = diff => diff.CircleSize,
        };

        [SettingSource("Adjust approach rate", "Hold the approach rate at the value below, whatever other mods are in use.", LAST_SETTING_ORDER + 1)]
        public BindableBool AdjustApproachRate { get; } = new BindableBool(true);

        [SettingSource("Approach Rate", "Override a beatmap's set AR.", LAST_SETTING_ORDER + 2, SettingControlType = typeof(ApproachRateSettingsControl))]
        public DifficultyBindable ApproachRate { get; } = new DifficultyBindable
        {
            Precision = 0.1f,
            MinValue = 0,
            MaxValue = 10,
            ExtendedMinValue = -10,
            ExtendedMaxValue = 11,
            ReadCurrentFromDifficulty = diff => diff.ApproachRate,
        };

        public override string ExtendedIconInformation
        {
            get
            {
                if (!IsExactlyOneSettingChanged(CircleSize, ApproachRate, OverallDifficulty, DrainRate))
                    return string.Empty;

                if (AdjustCircleSize.Value && !CircleSize.IsDefault) return format("CS", CircleSize);
                if (AdjustApproachRate.Value && !ApproachRate.IsDefault) return format("AR", ApproachRate);
                if (AdjustOverallDifficulty.Value && !OverallDifficulty.IsDefault) return format("OD", OverallDifficulty);
                if (AdjustDrainRate.Value && !DrainRate.IsDefault) return format("HP", DrainRate);

                return string.Empty;

                string format(string acronym, DifficultyBindable bindable)
                    => $"{acronym}{bindable.Value!.Value.ToStandardFormattedString(1)}";
            }
        }

        public override IEnumerable<(LocalisableString setting, LocalisableString value)> SettingDescription
        {
            get
            {
                if (AdjustCircleSize.Value && !CircleSize.IsDefault)
                    yield return ("Circle size", $"{CircleSize.Value:N1}");

                foreach (var setting in base.SettingDescription)
                    yield return setting;

                if (AdjustApproachRate.Value && !ApproachRate.IsDefault)
                    yield return ("Approach rate", $"{ApproachRate.Value:N1}");
            }
        }

        protected override void ApplySettings(BeatmapDifficulty difficulty)
        {
            base.ApplySettings(difficulty);

            if (AdjustCircleSize.Value && CircleSize.Value != null) difficulty.CircleSize = CircleSize.Value.Value;
            if (AdjustApproachRate.Value && ApproachRate.Value != null) difficulty.ApproachRate = ApproachRate.Value.Value;
        }

        protected override void CompensateForRate(BeatmapDifficulty difficulty, double rate)
        {
            base.CompensateForRate(difficulty, rate);

            // Only the settings which are turned on are held to the value they end up with, which is either the value
            // set below or, if none was set, whatever the beatmap and other mods produced.
            if (AdjustApproachRate.Value)
            {
                double preempt = IBeatmapDifficultyInfo.DifficultyRange(difficulty.ApproachRate, OsuHitObject.PREEMPT_RANGE);
                difficulty.ApproachRate = (float)IBeatmapDifficultyInfo.InverseDifficultyRange(preempt * rate, OsuHitObject.PREEMPT_RANGE);
            }

            if (AdjustOverallDifficulty.Value)
            {
                double greatHitWindow = IBeatmapDifficultyInfo.DifficultyRange(difficulty.OverallDifficulty, OsuHitWindows.GREAT_WINDOW_RANGE);
                difficulty.OverallDifficulty = (float)IBeatmapDifficultyInfo.InverseDifficultyRange(greatHitWindow * rate, OsuHitWindows.GREAT_WINDOW_RANGE);
            }
        }

        private partial class ApproachRateSettingsControl : DifficultyAdjustSettingsControl
        {
            protected override RoundedSliderBar<float> CreateSlider(BindableNumber<float> current) => new ApproachRateSlider();

            /// <summary>
            /// A slider bar with more detailed approach rate info for its given value
            /// </summary>
            public partial class ApproachRateSlider : RoundedSliderBar<float>
            {
                public override LocalisableString TooltipText =>
                    (Current as BindableNumber<float>)?.MinValue < 0
                        ? $"{base.TooltipText} ({getPreemptTime(Current.Value):0} ms)"
                        : base.TooltipText;

                private double getPreemptTime(float approachRate)
                {
                    var hitCircle = new HitCircle();
                    hitCircle.ApplyDefaults(new ControlPointInfo(), new BeatmapDifficulty { ApproachRate = approachRate });
                    return hitCircle.TimePreempt;
                }
            }
        }
    }
}
