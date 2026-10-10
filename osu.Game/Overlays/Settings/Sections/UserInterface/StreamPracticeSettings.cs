// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Game.Beatmaps.StreamPractice;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterfaceV2;

namespace osu.Game.Overlays.Settings.Sections.UserInterface
{
    /// <summary>
    /// Options for the stream practice beatmaps generated from song select.
    /// </summary>
    public partial class StreamPracticeSettings : SettingsSubsection
    {
        protected override LocalisableString Header => "Stream practice maps";

        private readonly Bindable<StreamPracticePattern> pattern = new Bindable<StreamPracticePattern>();
        private readonly BindableBool burstLengthShown = new BindableBool();

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            config.BindWith(OsuSetting.StreamPracticePattern, pattern);

            Children = new Drawable[]
            {
                new SettingsItemV2(new FormEnumDropdown<StreamPracticePattern>
                {
                    Caption = "Pattern",
                    HintText = "Generate one from a beatmap by right clicking it in song select.",
                    Current = pattern,
                })
                {
                    Keywords = new[] { "stream", "burst", "practice", "generate" },
                },
                new SettingsItemV2(new FormDropdown<int>
                {
                    Caption = "Notes per burst",
                    HintText = "Each burst is followed by a rest of the same length.",
                    Items = StreamPracticeOptions.BURST_LENGTHS,
                    Current = config.GetBindable<int>(OsuSetting.StreamPracticeBurstLength),
                })
                {
                    Keywords = new[] { "stream", "burst", "practice", "generate" },
                    CanBeShown = { BindTarget = burstLengthShown },
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            pattern.BindValueChanged(p => burstLengthShown.Value = p.NewValue == StreamPracticePattern.Bursts, true);
        }
    }
}
