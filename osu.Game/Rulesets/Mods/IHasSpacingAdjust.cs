// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Bindables;

namespace osu.Game.Rulesets.Mods
{
    /// <summary>
    /// A mod which adjusts the spacing between hit objects, and can therefore be controlled by the spacing adjust hotkeys.
    /// </summary>
    public interface IHasSpacingAdjust
    {
        /// <summary>
        /// The multiplier applied to the spacing between hit objects, where 1 is the beatmap's original spacing.
        /// </summary>
        BindableNumber<double> SpacingChange { get; }
    }
}
