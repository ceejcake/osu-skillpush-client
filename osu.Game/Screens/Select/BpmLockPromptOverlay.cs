// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.Select
{
    /// <summary>
    /// A prompt for typing in the BPM to lock beatmaps to, without having to open the mod select overlay.
    /// </summary>
    public partial class BpmLockPromptOverlay : OsuFocusedOverlayContainer
    {
        protected override bool DimMainContent => false;

        /// <summary>
        /// Invoked with the BPM the user entered, or <see langword="null"/> if they cleared it to remove the lock.
        /// </summary>
        public Action<int?> OnCommit;

        /// <summary>
        /// The BPM currently locked to, used to populate the text box when shown.
        /// </summary>
        public Func<int?> CurrentBpm;

        private PromptNumberBox numberBox;

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.Black.Opacity(0.6f),
                },
                new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Width = 320,
                    AutoSizeAxes = Axes.Y,
                    Masking = true,
                    CornerRadius = 10,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = new Color4(20, 20, 25, 255),
                        },
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Padding = new MarginPadding(20),
                            Spacing = new Vector2(0, 8),
                            Children = new Drawable[]
                            {
                                new OsuSpriteText
                                {
                                    Text = "Lock BPM",
                                    Font = OsuFont.Torus.With(size: 24, weight: FontWeight.SemiBold),
                                },
                                numberBox = new PromptNumberBox
                                {
                                    RelativeSizeAxes = Axes.X,
                                    PlaceholderText = "BPM",
                                    CommitOnFocusLost = false,
                                    // The text box consumes escape to release focus, so dismissal is handled by it directly.
                                    Dismiss = Hide,
                                },
                                new OsuSpriteText
                                {
                                    Text = "Enter to apply, empty to remove the lock.",
                                    Font = OsuFont.Torus.With(size: 14),
                                    Alpha = 0.7f,
                                },
                            }
                        },
                    }
                },
            };

            numberBox.OnCommit += (_, _) => commit();
        }

        protected override void PopIn()
        {
            this.FadeIn(150, Easing.OutQuint);

            int? current = CurrentBpm?.Invoke();

            numberBox.Text = current?.ToString() ?? string.Empty;

            // Focus is taken by this overlay when shown, and is handed to the text box so the user can type immediately.
            Schedule(() => GetContainingFocusManager()?.ChangeFocus(numberBox));
        }

        protected override void PopOut() => this.FadeOut(150, Easing.OutQuint);

        protected override void OnFocus(osu.Framework.Input.Events.FocusEvent e)
        {
            base.OnFocus(e);

            GetContainingFocusManager()?.ChangeFocus(numberBox);
        }

        protected override bool OnClick(osu.Framework.Input.Events.ClickEvent e)
        {
            // Clicking away from the prompt dismisses it.
            Hide();
            return true;
        }

        private partial class PromptNumberBox : OsuNumberBox
        {
            public Action Dismiss;

            protected override bool OnKeyDown(osu.Framework.Input.Events.KeyDownEvent e)
            {
                if (e.Key == osuTK.Input.Key.Escape)
                {
                    Dismiss?.Invoke();
                    return true;
                }

                return base.OnKeyDown(e);
            }
        }

        private void commit()
        {
            if (string.IsNullOrWhiteSpace(numberBox.Text))
                OnCommit?.Invoke(null);
            else if (int.TryParse(numberBox.Text, out int bpm))
                OnCommit?.Invoke(bpm);

            Hide();
        }
    }
}
