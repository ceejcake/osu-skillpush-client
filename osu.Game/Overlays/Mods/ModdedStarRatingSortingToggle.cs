// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterface;

namespace osu.Game.Overlays.Mods
{
    /// <summary>
    /// Turns filtering and sorting by star ratings with mods applied on or off.
    /// </summary>
    /// <remarks>
    /// Off by default, as the star ratings have to be worked out again every time a mod or one of its settings changes.
    /// </remarks>
    public partial class ModdedStarRatingSortingToggle : ShearedToggleButton
    {
        public ModdedStarRatingSortingToggle()
        {
            Text = "Sort with mods";
            TooltipText = "Filter and sort the selected collection by star ratings with your mods applied. "
                          + "Each beatmap in the collection is worked out again whenever the mods change.";
            Width = 160;
            Height = 42;
        }

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            config.BindWith(OsuSetting.ModdedStarRatingSorting, Active);
        }
    }
}
