// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Game.Input;
using osu.Game.Input.Bindings;
using osu.Game.Localisation;

namespace osu.Game.Overlays.OSD
{
    public partial class PracticeCheckpointToast : Toast
    {
        public PracticeCheckpointToast(double? checkpointTime)
            : base(ToastStrings.PracticeCheckpoint, checkpointTime == null
                ? ToastStrings.RestartFromBeginning
                : ToastStrings.RestartFromTime(TimeSpan.FromMilliseconds(Math.Max(0, checkpointTime.Value)).ToString(@"m\:ss")))
        {
        }

        [BackgroundDependencyLoader]
        private void load(RealmKeyBindingStore keyBindingStore)
        {
            ExtraText = keyBindingStore.GetBindingsStringFor(GlobalAction.TogglePracticeCheckpoint);
        }
    }
}
