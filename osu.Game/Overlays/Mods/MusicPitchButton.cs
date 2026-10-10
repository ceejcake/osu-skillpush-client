// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.UserInterface;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osuTK;

namespace osu.Game.Overlays.Mods
{
    /// <summary>
    /// Shows the pitch of the music, and opens a slider to change it. Only the pitch is changed, not the speed.
    /// </summary>
    /// <remarks>
    /// A setting rather than a mod, so that it can be used at any speed, with or without a rate changing mod, and has no
    /// effect on scores.
    /// </remarks>
    public partial class MusicPitchButton : ShearedButton, IHasPopover
    {
        private readonly BindableDouble pitch = new BindableDouble(1);

        public MusicPitchButton()
        {
            Width = 130;
            Height = 42;
            TooltipText = "Change the pitch of the music without changing its speed";
        }

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            config.BindWith(OsuSetting.MusicPitch, pitch);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            Action = this.ShowPopover;
            pitch.BindValueChanged(p => Text = $"Pitch {p.NewValue:0.00}x", true);
        }

        public Popover GetPopover() => new MusicPitchPopover(pitch);

        private partial class MusicPitchPopover : OsuPopover
        {
            public MusicPitchPopover(BindableDouble pitch)
            {
                Child = new FillFlowContainer
                {
                    Width = 280,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(7),
                    Children = new Drawable[]
                    {
                        new FormSliderBar<double>
                        {
                            RelativeSizeAxes = Axes.X,
                            Caption = "Music pitch",
                            HintText = "Changes how high or low the music sounds, without changing its speed.",
                            Current = pitch,
                            KeyboardStep = 0.05f,
                            LabelFormat = v => $"{v:0.00}x",
                        },
                        new RoundedButton
                        {
                            RelativeSizeAxes = Axes.X,
                            Text = "Reset",
                            Action = () => pitch.SetDefault(),
                        },
                    }
                };
            }
        }
    }
}
