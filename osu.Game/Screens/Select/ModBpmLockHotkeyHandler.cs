// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Rulesets.Mods;
using osu.Game.Utils;

namespace osu.Game.Screens.Select
{
    /// <summary>
    /// Applies a BPM lock to the selected rate adjusting mod, adding one if none is selected.
    /// </summary>
    public partial class ModBpmLockHotkeyHandler : Component
    {
        [Resolved]
        private Bindable<IReadOnlyList<Mod>> selectedMods { get; set; } = null!;

        /// <summary>
        /// The BPM currently locked to, if any.
        /// </summary>
        public int? CurrentBpm
        {
            get
            {
                var rateMod = selectedMods.Value.OfType<ModRateAdjust>().FirstOrDefault();

                return rateMod?.LockBpm.Value == true ? rateMod.TargetBpm.Value : null;
            }
        }

        /// <summary>
        /// Locks all beatmaps to the given BPM, or removes an existing lock when <paramref name="bpm"/> is <see langword="null"/>.
        /// </summary>
        /// <param name="bpm">The BPM to lock to.</param>
        /// <param name="availableMods">The mods available for selection, used when no rate adjusting mod is selected yet.</param>
        /// <returns>Whether the lock was changed.</returns>
        public bool SetBpmLock(int? bpm, IEnumerable<Mod> availableMods)
        {
            var selectedRateMod = selectedMods.Value.OfType<ModRateAdjust>().FirstOrDefault();

            if (bpm == null)
            {
                if (selectedRateMod?.LockBpm.Value != true)
                    return false;

                selectedRateMod.LockBpm.Value = false;
                return true;
            }

            if (selectedRateMod != null)
            {
                applyTo(selectedRateMod, bpm.Value);
                return true;
            }

            // Double time is the sensible default, given a lock is most often used to speed beatmaps up.
            if (availableMods.OfType<ModDoubleTime>().FirstOrDefault() is not ModRateAdjust targetMod)
                return false;

            applyTo(targetMod, bpm.Value);

            var intendedMods = selectedMods.Value.Append(targetMod).ToList();

            if (!ModUtils.CheckCompatibleSet(intendedMods))
                return false;

            selectedMods.Value = intendedMods;
            return true;
        }

        private static void applyTo(ModRateAdjust mod, int bpm)
        {
            mod.TargetBpm.Value = bpm;
            mod.LockBpm.Value = true;
        }
    }
}
