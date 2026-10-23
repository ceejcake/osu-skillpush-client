// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Osu.UI
{
    /// <summary>
    /// Shows an X where a press which missed its circle landed, so that over- or under-aiming is immediately visible.
    /// Toggled with <see cref="OsuSetting.MisaimIndicator"/>, with more detail shown when <see cref="OsuSetting.MisaimIndicatorDetails"/> is enabled.
    /// </summary>
    public partial class MisaimIndicatorLayer : Container
    {
        /// <summary>
        /// How long an indicator is fully visible for, before fading out.
        /// </summary>
        private const double hold_duration = 400;

        private const double fade_duration = 500;

        private readonly Bindable<bool> enabled = new Bindable<bool>();
        private readonly Bindable<bool> detailed = new Bindable<bool>();

        private IReadOnlyList<OsuHitObject> objects = Array.Empty<OsuHitObject>();

        public MisaimIndicatorLayer()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader(true)]
        private void load(OsuConfigManager? config)
        {
            config?.BindWith(OsuSetting.MisaimIndicator, enabled);
            config?.BindWith(OsuSetting.MisaimIndicatorDetails, detailed);
            enabled.BindValueChanged(e =>
            {
                if (!e.NewValue)
                    Clear();
            });
        }

        /// <summary>
        /// The objects of the beatmap being played, used to work out which way the cursor was travelling towards a circle.
        /// </summary>
        public void SetObjects(IEnumerable<OsuHitObject> hitObjects) => objects = hitObjects.OrderBy(h => h.StartTime).ToArray();

        /// <summary>
        /// Shows where a press landed relative to the circle it missed.
        /// </summary>
        /// <param name="circle">The circle the press was timed for.</param>
        /// <param name="screenSpacePressPosition">Where the cursor was at the time of the press.</param>
        public void ShowMisaim(OsuHitObject circle, Vector2 screenSpacePressPosition)
        {
            if (!enabled.Value)
                return;

            Vector2 pressPosition = ToLocalSpace(screenSpacePressPosition);

            Add(new MisaimIndicator(circle.StackedPosition, (float)circle.Radius, pressPosition, GetApproachDirection(circle), detailed.Value));
        }

        /// <summary>
        /// The object which has to be played before a circle, or null if it is the first.
        /// </summary>
        /// <remarks>
        /// Slider heads share their start time with the slider, so for a slider head this is the object before the slider.
        /// </remarks>
        public OsuHitObject? GetPreviousObject(OsuHitObject circle) => objects.LastOrDefault(h => h.StartTime < circle.StartTime);

        /// <summary>
        /// The direction the cursor was travelling to reach a circle: from where the previous object ended, towards the circle.
        /// </summary>
        /// <returns>The normalised direction, or null if it isn't known (e.g. for the first object, or one stacked on the previous).</returns>
        public Vector2? GetApproachDirection(OsuHitObject circle)
        {
            var previous = GetPreviousObject(circle);

            if (previous == null)
                return null;

            Vector2 travel = circle.StackedPosition - previous.StackedEndPosition;

            if (travel.Length < 1)
                return null;

            return travel.Normalized();
        }

        /// <summary>
        /// How far past the circle (positive) or short of it (negative) a press was, along the direction the cursor was travelling.
        /// </summary>
        public static float GetOveraim(Vector2 circlePosition, Vector2 pressPosition, Vector2 approachDirection) =>
            Vector2.Dot(pressPosition - circlePosition, approachDirection);

        /// <summary>
        /// An X where a press which missed its circle landed.
        /// Coloured by whether it was past the circle (overaim) or short of it (underaim), when that is known.
        /// With details enabled, also shows the circle, a line from its centre to the press, and how far over or under the press was.
        /// </summary>
        public partial class MisaimIndicator : CompositeDrawable
        {
            private const float line_thickness = 2;
            private const float cross_thickness = 3;
            private const float cross_size = 16;

            /// <summary>
            /// Whether the press was past the circle (true) or short of it (false), or null if the direction of travel isn't known.
            /// </summary>
            public readonly bool? Overaimed;

            /// <summary>
            /// Where the press landed, in playfield coordinates.
            /// </summary>
            public readonly Vector2 PressPosition;

            /// <summary>
            /// Whether the circle, line and distance are shown as well as the X.
            /// </summary>
            public readonly bool Detailed;

            private readonly Vector2 circlePosition;
            private readonly float radius;
            private readonly float? overaim;

            public MisaimIndicator(Vector2 circlePosition, float radius, Vector2 pressPosition, Vector2? approachDirection, bool detailed)
            {
                this.circlePosition = circlePosition;
                this.radius = radius;

                PressPosition = pressPosition;
                Detailed = detailed;

                overaim = approachDirection is Vector2 direction ? GetOveraim(circlePosition, pressPosition, direction) : null;
                Overaimed = overaim is float distance ? distance >= 0 : null;

                RelativeSizeAxes = Axes.Both;
            }

            [BackgroundDependencyLoader]
            private void load(OsuColour colours)
            {
                Color4 colour = Overaimed switch
                {
                    true => colours.YellowDark,
                    false => colours.BlueLight,
                    null => colours.Red,
                };

                if (Detailed)
                {
                    Vector2 offset = PressPosition - circlePosition;

                    string label = overaim is float distance
                        ? distance >= 0 ? $"over {distance:N0}" : $"under {-distance:N0}"
                        : $"off {offset.Length:N0}";

                    AddRangeInternal(new Drawable[]
                    {
                        // The circle which was missed.
                        new CircularContainer
                        {
                            Position = circlePosition,
                            Size = new Vector2(radius * 2),
                            Origin = Anchor.Centre,
                            Masking = true,
                            BorderThickness = line_thickness,
                            BorderColour = Color4.White,
                            Alpha = 0.6f,
                            Child = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Alpha = 0,
                                AlwaysPresent = true,
                            },
                        },
                        // From the centre of the circle to where the press landed.
                        new Box
                        {
                            Position = circlePosition,
                            Origin = Anchor.CentreLeft,
                            Size = new Vector2(offset.Length, line_thickness),
                            Rotation = MathHelper.RadiansToDegrees(MathF.Atan2(offset.Y, offset.X)),
                            Colour = colour,
                            Alpha = 0.8f,
                        },
                        new OsuSpriteText
                        {
                            Position = PressPosition + new Vector2(0, cross_size),
                            Origin = Anchor.TopCentre,
                            Text = label,
                            Font = OsuFont.GetFont(size: 14, weight: FontWeight.Bold),
                            Colour = colour,
                        },
                    });
                }

                // Where the press landed.
                AddInternal(new Container
                {
                    Position = PressPosition,
                    Origin = Anchor.Centre,
                    Size = new Vector2(cross_size),
                    Rotation = 45,
                    Colour = colour,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            RelativeSizeAxes = Axes.X,
                            Height = cross_thickness,
                        },
                        new Box
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            RelativeSizeAxes = Axes.Y,
                            Width = cross_thickness,
                        },
                    }
                });
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                this.Delay(hold_duration).FadeOut(fade_duration).Expire();
            }
        }
    }
}
