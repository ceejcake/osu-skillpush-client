// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using osu.Framework.Bindables;
using osu.Game.Configuration;
using osu.Game.Online.API;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Beatmaps
{
    /// <summary>
    /// Reduces a set of mods to what song select sorts star ratings by, so that changing anything else doesn't require the
    /// star ratings to be worked out again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Most of what is left out makes no difference to star rating at all: a mod such as No Fail, a setting such as the
    /// pitch adjustment of Double Time, or a Difficulty Adjust stat which is turned off.
    /// </para>
    /// <para>
    /// Hidden and Difficulty Adjust's approach rate are left out too, even though they do change star rating a little.
    /// They are changed often enough, and matter little enough to the order of beatmaps, that recalculating a whole
    /// collection each time isn't worth it.
    /// </para>
    /// </remarks>
    public static class StarRatingSortingMods
    {
        /// <summary>
        /// Difficulty Adjust stats which are left out. Drain rate never affects star rating; approach rate does, a little.
        /// </summary>
        private static readonly HashSet<string> ignored_stats = new HashSet<string>
        {
            nameof(ModDifficultyAdjust.DrainRate),
            "ApproachRate",
        };

        /// <summary>
        /// Creates copies of the given mods, without the ones and the settings which star ratings aren't sorted by.
        /// </summary>
        public static Mod[] Normalise(IEnumerable<Mod> mods) => mods.Select(m => normalise(m.DeepClone()))
                                                                    .OfType<Mod>()
                                                                    .OrderBy(m => m.Acronym)
                                                                    .ToArray();

        /// <summary>
        /// Identifies a set of mods returned by <see cref="Normalise"/>, settings included.
        /// </summary>
        public static string GetKey(IEnumerable<Mod> normalisedMods) => JsonConvert.SerializeObject(normalisedMods.Select(m => new APIMod(m)));

        /// <returns>The mod as it should be sorted by, or <see langword="null"/> if it should be left out.</returns>
        private static Mod? normalise(Mod mod)
        {
            switch (mod)
            {
                // Only decide whether a play fails, or how it is scored or presented.
                case ModNoFail:
                case ModFailCondition:
                case ModScoreV2:
                case ModMuted:
                case ModNoScope:
                // Only changes how a play is judged and scored, and how the beatmap looks and sounds.
                case ModClassic:
                    return null;

                case ModHidden:
                    return null;

                case ModDifficultyAdjust difficultyAdjust:
                    return normaliseDifficultyAdjust(difficultyAdjust);

                case ModEasyWithExtraLives easy:
                    easy.Retries.SetDefault();
                    return easy;
            }

            if (mod is ModRateAdjust rateAdjust)
            {
                // With the bpm lock on, the speed change is overwritten for each beatmap. With it off, the target isn't used.
                if (rateAdjust.LockBpm.Value)
                    rateAdjust.SpeedChange.SetDefault();
                else
                    rateAdjust.TargetBpm.SetDefault();

                switch (rateAdjust)
                {
                    case ModDoubleTime doubleTime:
                        doubleTime.AdjustPitch.SetDefault();
                        break;

                    case ModHalfTime halfTime:
                        halfTime.AdjustPitch.SetDefault();
                        break;
                }
            }

            if (mod is ModTimeRamp timeRamp)
                timeRamp.AdjustPitch.SetDefault();

            return mod;
        }

        /// <returns>The mod with only the stats in use, or <see langword="null"/> if none of it is.</returns>
        private static Mod? normaliseDifficultyAdjust(ModDifficultyAdjust mod)
        {
            bool inUse = false;

            // Doesn't change a value, only how far the sliders go.
            mod.ExtendedLimits.SetDefault();

            var settings = mod.GetSettingsSourceProperties().Select(s => s.Item2).ToList();
            var toggles = new HashSet<string>();

            foreach (var property in settings)
            {
                if (property.GetValue(mod) is not DifficultyBindable stat)
                    continue;

                // Each stat is turned on by a toggle named after it.
                string toggleName = $"Adjust{property.Name}";
                var toggle = mod.GetType().GetProperty(toggleName)?.GetValue(mod) as BindableBool;

                if (toggle != null)
                    toggles.Add(toggleName);

                // A stat which is turned on has an effect even without a value of its own, as it's then held at the
                // beatmap's value under a change of rate. Only one which is turned off can be left out.
                bool used = toggle?.Value ?? stat.Value != null;

                if (used && !ignored_stats.Contains(property.Name))
                {
                    inUse = true;
                    continue;
                }

                if (toggle != null)
                    toggle.Value = false;

                stat.SetDefault();
            }

            // Anything else it offers (catch's spicy patterns, for one) counts if it has been changed.
            foreach (var property in settings)
            {
                if (property.Name == nameof(ModDifficultyAdjust.ExtendedLimits) || toggles.Contains(property.Name))
                    continue;

                object? value = property.GetValue(mod);

                if (value is not DifficultyBindable && value is IHasDefaultValue setting && !setting.IsDefault)
                    inUse = true;
            }

            return inUse ? mod : null;
        }
    }
}
