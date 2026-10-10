// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Overlays.Settings;

namespace osu.Game.Rulesets.Mods
{
    public abstract class ModRateAdjust : Mod, IApplicableToRate, IApplicableAfterBeatmapConversion
    {
        public sealed override bool ValidForFreestyleAsRequiredMod => true;
        public sealed override bool ValidForMultiplayerAsFreeMod => false;

        public abstract BindableNumber<double> SpeedChange { get; }

        /// <summary>
        /// The slowest a bpm lock will play a beatmap, whichever way this mod usually changes the rate.
        /// </summary>
        public const double LOCKED_MINIMUM_RATE = 0.5;

        /// <summary>
        /// The fastest a bpm lock will play a beatmap, whichever way this mod usually changes the rate.
        /// </summary>
        public const double LOCKED_MAXIMUM_RATE = 2;

        /// <summary>
        /// How finely a bpm lock sets the rate, so that it lands on the target rather than up to a percent either side.
        /// </summary>
        private const double locked_rate_precision = 0.0001;

        [SettingSource("Lock BPM", "Set the speed change for each beatmap so that it plays at a fixed BPM", SettingControlType = typeof(SettingsCheckbox))]
        public BindableBool LockBpm { get; } = new BindableBool();

        [SettingSource("Target BPM", "The BPM to play every beatmap at, when the BPM lock is enabled")]
        public BindableNumber<int> TargetBpm { get; } = new BindableInt(180)
        {
            MinValue = 60,
            MaxValue = 400,
        };

        /// <summary>
        /// Whether the rate is set to something other than this mod's default.
        /// </summary>
        protected bool RateIsCustomised => !SpeedChange.IsDefault || LockBpm.Value;

        /// <remarks>
        /// This runs directly after conversion, before anything reads the rate of play: difficulty mods which
        /// compensate for it, and the hit objects themselves, both need the final rate rather than the one the
        /// speed change happened to be left at.
        /// </remarks>
        public void ApplyToBeatmap(IBeatmap beatmap)
        {
            if (!LockBpm.Value)
                return;

            double beatLength = beatmap.GetMostCommonBeatLength();

            if (beatLength <= 0)
                return;

            LockTo(60000 / beatLength);
        }

        /// <summary>
        /// Sets the rate a bpm lock gives a beatmap of the given BPM, if the lock is on.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The speed change setting is overwritten rather than ignored, so that it always shows the rate actually being
        /// played at, and anything working out the rate from it gets the right one.
        /// </para>
        /// <para>
        /// A lock has to be able to slow a beatmap down with double time, or speed one up with half time, so the rate is
        /// allowed anywhere from <see cref="LOCKED_MINIMUM_RATE"/> to <see cref="LOCKED_MAXIMUM_RATE"/> rather than only in
        /// the range this mod usually offers. That only happens to the copy of the mod applied to a beatmap; the one in the
        /// mod select overlay is never locked to a beatmap, so its slider keeps its usual range.
        /// </para>
        /// </remarks>
        /// <param name="beatmapBpm">The beatmap's most common BPM.</param>
        public void LockTo(double beatmapBpm)
        {
            if (!LockBpm.Value || !double.IsFinite(beatmapBpm) || beatmapBpm <= 0)
                return;

            allowLockedRates();
            SpeedChange.Value = Math.Clamp(TargetBpm.Value / beatmapBpm, LOCKED_MINIMUM_RATE, LOCKED_MAXIMUM_RATE);
        }

        private void allowLockedRates()
        {
            SpeedChange.MinValue = Math.Min(SpeedChange.MinValue, LOCKED_MINIMUM_RATE);
            SpeedChange.MaxValue = Math.Max(SpeedChange.MaxValue, LOCKED_MAXIMUM_RATE);
            SpeedChange.Precision = Math.Min(SpeedChange.Precision, locked_rate_precision);
        }

        /// <remarks>
        /// A mod recreated from its settings (a stored score's, for one) may have a rate a bpm lock chose outside this mod's
        /// usual range. The settings are copied one at a time, and the speed change can come before the lock, so whether
        /// the lock is on can't be relied upon here. Instead, a rate which wouldn't fit is let in as one only a lock could
        /// have chosen.
        /// </remarks>
        internal override void CopyAdjustedSetting(IBindable target, object source)
        {
            if (target == SpeedChange && source is not IBindable)
            {
                var stored = new BindableDouble { MinValue = LOCKED_MINIMUM_RATE, MaxValue = LOCKED_MAXIMUM_RATE, Precision = locked_rate_precision };
                stored.Parse(source, CultureInfo.InvariantCulture);

                bool fitsUsualRange = stored.Value >= SpeedChange.MinValue && stored.Value <= SpeedChange.MaxValue
                                                                         && Math.Abs(Math.Round(stored.Value / SpeedChange.Precision) * SpeedChange.Precision - stored.Value) < 1e-9;

                if (!fitsUsualRange)
                    allowLockedRates();
            }

            base.CopyAdjustedSetting(target, source);
        }

        public abstract void ApplyToTrack(IAdjustableAudioComponent track);

        public virtual void ApplyToSample(IAdjustableAudioComponent sample)
        {
            sample.AddAdjustment(AdjustableProperty.Frequency, SpeedChange);
        }

        public double ApplyToRate(double time, double rate) => rate * SpeedChange.Value;

        public override Type[] IncompatibleMods => new[] { typeof(ModTimeRamp), typeof(ModAdaptiveSpeed), typeof(ModRateAdjust) };

        public override IEnumerable<(LocalisableString setting, LocalisableString value)> SettingDescription
        {
            get
            {
                if (LockBpm.Value)
                {
                    yield return ("Locked BPM", FormattableString.Invariant($@"{TargetBpm.Value:N0}"));

                    yield break;
                }

                if (!SpeedChange.IsDefault)
                    yield return ("Speed change", FormattableString.Invariant($@"{SpeedChange.Value:N2}x"));
            }
        }

        public override string ExtendedIconInformation
        {
            get
            {
                if (LockBpm.Value)
                    return FormattableString.Invariant($"{TargetBpm.Value:N0}bpm");

                return SpeedChange.IsDefault ? string.Empty : FormattableString.Invariant($"{SpeedChange.Value:N2}x");
            }
        }
    }
}
