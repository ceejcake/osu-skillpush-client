// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Utils;
using osu.Game.Overlays;
using osu.Game.Overlays.OSD;
using osu.Game.Rulesets.Mods;
using osu.Game.Utils;

namespace osu.Game.Screens.Select
{
    /// <summary>
    /// Handles the hotkeys which adjust the spacing of any selected <see cref="IHasSpacingAdjust"/> mod,
    /// in the same way that <see cref="ModSpeedHotkeyHandler"/> handles rate adjustment.
    /// </summary>
    public partial class ModSpacingHotkeyHandler : Component
    {
        /// <summary>
        /// The spacing a mod is given when the hotkey turns it on.
        /// </summary>
        private const double initial_spacing = 2;

        [Resolved]
        private Bindable<IReadOnlyList<Mod>> selectedMods { get; set; } = null!;

        [Resolved]
        private OnScreenDisplay? onScreenDisplay { get; set; }

        public bool ChangeSpacing(double delta, IEnumerable<Mod> availableMods)
        {
            var selectedSpacingMod = selectedMods.Value.OfType<IHasSpacingAdjust>().FirstOrDefault();

            double targetSpacing = (selectedSpacingMod?.SpacingChange.Value ?? 1) + delta;

            // Returning to the original spacing is expressed by removing the mod entirely.
            if (targetSpacing <= 1 || Precision.AlmostEquals(targetSpacing, 1, 0.005))
            {
                if (selectedSpacingMod == null)
                    return false;

                selectedMods.Value = selectedMods.Value.Where(m => m is not IHasSpacingAdjust).ToList();
                onScreenDisplay?.Display(new SpacingChangeToast(1));
                return true;
            }

            if (selectedSpacingMod != null)
            {
                selectedSpacingMod.SpacingChange.Value = clamp(selectedSpacingMod, targetSpacing);
                onScreenDisplay?.Display(new SpacingChangeToast(selectedSpacingMod.SpacingChange.Value));
                return true;
            }

            if (availableMods.OfType<IHasSpacingAdjust>().FirstOrDefault() is not Mod targetMod)
                return false;

            var spacingAdjust = (IHasSpacingAdjust)targetMod;
            // Turning the mod on starts from a spacing worth playing, rather than one step above the original.
            spacingAdjust.SpacingChange.Value = clamp(spacingAdjust, initial_spacing);

            var intendedMods = selectedMods.Value.Append(targetMod).ToList();

            if (!ModUtils.CheckCompatibleSet(intendedMods))
                return false;

            selectedMods.Value = intendedMods;
            onScreenDisplay?.Display(new SpacingChangeToast(spacingAdjust.SpacingChange.Value));
            return true;
        }

        private static double clamp(IHasSpacingAdjust mod, double value)
            => Math.Clamp(value, mod.SpacingChange.MinValue, mod.SpacingChange.MaxValue);
    }
}
