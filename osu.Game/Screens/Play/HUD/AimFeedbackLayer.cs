// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.UI;
using osuTK.Graphics;

namespace osu.Game.Screens.Play.HUD
{
    /// <summary>
    /// Makes the sides of the screen glow when objects are being missed because of aim, in the same way as <see cref="FailingLayer"/>
    /// does for low health: red when the aim is lagging behind the objects, and blue when it is going past them.
    /// </summary>
    /// <remarks>
    /// Each miss adds to the glow of its colour, which fades away over time. A single miss is a faint pulse, and the glow only
    /// gets strong when misses come in quick succession, which is when the aim has started to go wrong.
    /// </remarks>
    public partial class AimFeedbackLayer : CompositeDrawable
    {
        private const float max_alpha = 0.4f;
        private const float gradient_size = 0.2f;

        /// <summary>
        /// How much a single miss adds to the glow, where 1 is full strength.
        /// </summary>
        public const float MISS_STRENGTH = 0.5f;

        /// <summary>
        /// How long it takes for the glow to fade to half its strength.
        /// </summary>
        private const double half_life = 450;

        private readonly Playfield? playfield;

        private readonly Bindable<bool> enabled = new Bindable<bool>();

        private Container behindGlow = null!;
        private Container aheadGlow = null!;

        /// <summary>
        /// How strong the glow for lagging behind is, from 0 to 1 (before fading).
        /// </summary>
        public float BehindStrength { get; private set; }

        /// <summary>
        /// How strong the glow for going too fast is, from 0 to 1 (before fading).
        /// </summary>
        public float AheadStrength { get; private set; }

        /// <param name="playfield">The playfield whose aim misses are shown, or null if the ruleset has none.</param>
        public AimFeedbackLayer(Playfield? playfield)
        {
            this.playfield = playfield;
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load(OsuColour colours, OsuConfigManager config)
        {
            InternalChildren = new Drawable[]
            {
                behindGlow = createGlow(colours.Red),
                aheadGlow = createGlow(colours.Blue),
            };

            config.BindWith(OsuSetting.AimFeedbackGlow, enabled);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (playfield != null)
                playfield.AimMiss += onAimMiss;

            enabled.BindValueChanged(e =>
            {
                if (!e.NewValue)
                    Reset();
            });
        }

        /// <summary>
        /// Clears the glow straight away.
        /// </summary>
        public void Reset()
        {
            BehindStrength = 0;
            AheadStrength = 0;
        }

        private void onAimMiss(HitObject hitObject, AimError error) => Schedule(() => AddMiss(error));

        /// <summary>
        /// Adds a miss to the glow of its colour.
        /// </summary>
        public void AddMiss(AimError error)
        {
            if (!enabled.Value)
                return;

            switch (error)
            {
                case AimError.Behind:
                    BehindStrength = Math.Min(1, BehindStrength + MISS_STRENGTH);
                    break;

                case AimError.Ahead:
                    AheadStrength = Math.Min(1, AheadStrength + MISS_STRENGTH);
                    break;
            }
        }

        protected override void Update()
        {
            base.Update();

            // Fades in real time, so that it behaves the same while paused or rewinding a replay.
            float decay = (float)Math.Pow(0.5, Clock.ElapsedFrameTime / half_life);

            BehindStrength *= decay;
            AheadStrength *= decay;

            behindGlow.Alpha = BehindStrength * max_alpha;
            aheadGlow.Alpha = AheadStrength * max_alpha;
        }

        private static Container createGlow(Color4 colour) => new Container
        {
            Alpha = 0,
            Blending = BlendingParameters.Additive,
            RelativeSizeAxes = Axes.Both,
            Colour = colour,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = ColourInfo.GradientHorizontal(Color4.White, Color4.White.Opacity(0)),
                    Width = gradient_size,
                },
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Width = gradient_size,
                    Colour = ColourInfo.GradientHorizontal(Color4.White.Opacity(0), Color4.White),
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                },
            }
        };

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (playfield.IsNotNull())
                playfield.AimMiss -= onAimMiss;
        }
    }
}
