// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Game.Rulesets.UI
{
    /// <summary>
    /// Which way the cursor was off when an object was missed because of aim.
    /// </summary>
    public enum AimError
    {
        /// <summary>
        /// The cursor hadn't reached the object yet: the aim was lagging behind.
        /// </summary>
        Behind,

        /// <summary>
        /// The cursor had gone past the object: the aim was moving too fast.
        /// </summary>
        Ahead,
    }
}
