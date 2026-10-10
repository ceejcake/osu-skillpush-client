// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Osu.Objects.Drawables
{
    public partial class DrawableOsuJudgement : DrawableJudgement
    {
        internal Color4 AccentColour { get; private set; }

        internal SkinnableLighting Lighting { get; private set; } = null!;

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        private Vector2? screenSpacePosition;

        private readonly Bindable<bool> hitErrorJudgements = new Bindable<bool>();

        [BackgroundDependencyLoader]
        private void load()
        {
            config.BindWith(OsuSetting.HitErrorJudgements, hitErrorJudgements);

            AddInternal(Lighting = new SkinnableLighting
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Blending = BlendingParameters.Additive,
                Depth = float.MaxValue,
                Alpha = 0
            });
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Run immediately, as the body is first created before this class has had its configuration injected.
            // Judgements are also pooled and live for the length of a play, so a change has to reach the ones already created.
            hitErrorJudgements.BindValueChanged(_ => InvalidateJudgementBody(), true);
        }

        public override void Apply(JudgementResult result, DrawableHitObject? judgedObject)
        {
            base.Apply(result, judgedObject);

            if (judgedObject is not DrawableOsuHitObject osuObject)
                return;

            AccentColour = osuObject.AccentColour.Value;

            switch (osuObject)
            {
                case DrawableSlider slider:
                    screenSpacePosition = slider.TailCircle.ToScreenSpace(slider.TailCircle.OriginPosition);
                    break;

                default:
                    screenSpacePosition = osuObject.ToScreenSpace(osuObject.OriginPosition);
                    break;
            }

            Scale = new Vector2(osuObject.HitObject.Scale);
        }

        protected override void PrepareForUse()
        {
            base.PrepareForUse();

            Lighting.ResetAnimation();
            Lighting.SetColourFrom(this, Result);

            // The offset belongs to the judgement being displayed now, rather than to the one this was last used for.
            if (JudgementBody?.Drawable is HitErrorJudgementPiece hitError && Result != null)
                hitError.SetTimeOffset(Result.TimeOffset);

            if (screenSpacePosition != null)
                Position = Parent!.ToLocalSpace(screenSpacePosition.Value);
        }

        protected override void ApplyHitAnimations()
        {
            bool hitLightingEnabled = config.Get<bool>(OsuSetting.HitLighting);

            Lighting.Alpha = 0;

            if (hitLightingEnabled)
            {
                // todo: this animation changes slightly based on new/old legacy skin versions.
                Lighting.ScaleTo(0.8f).ScaleTo(1.2f, 600, Easing.Out);
                Lighting.FadeIn(200).Then().Delay(200).FadeOut(1000);

                // extend the lifetime to cover lighting fade
                LifetimeEnd = Lighting.LatestTransformEndTime;
            }

            base.ApplyHitAnimations();
        }

        protected override SkinnableDrawable CreateJudgementBody(HitResult type)
        {
            if (!hitErrorJudgements.Value || (type != HitResult.Ok && type != HitResult.Meh))
                return base.CreateJudgementBody(type);

            // Looked up with a component no skin provides, so that the numbers are never replaced by a skinned judgement.
            return new SkinnableDrawable(new HitErrorLookup(), _ => new HitErrorJudgementPiece(type), confineMode: ConfineMode.NoScaling);
        }

        protected override Drawable CreateDefaultJudgement(HitResult result) =>
            // Tick hits don't show a judgement by default
            result.IsHit() && result.IsTick() ? Empty() : new OsuJudgementPiece(result);

        private partial class OsuJudgementPiece : DefaultJudgementPiece
        {
            public OsuJudgementPiece(HitResult result)
                : base(result)
            {
            }

            public override void PlayAnimation()
            {
                if (Result != HitResult.Miss)
                {
                    JudgementText
                        .ScaleTo(new Vector2(0.8f, 1))
                        .ScaleTo(new Vector2(1.2f, 1), 1800, Easing.OutQuint);
                }

                base.PlayAnimation();
            }
        }

        /// <summary>
        /// Shows how far off the perfect time a hit was, in milliseconds, in place of the judgement.
        /// </summary>
        private partial class HitErrorJudgementPiece : OsuJudgementPiece
        {
            /// <summary>
            /// How much darker a 50 is displayed than a 100.
            /// </summary>
            private const float meh_darkening = 0.4f;

            private Color4 lateColour;
            private Color4 earlyColour;

            public HitErrorJudgementPiece(HitResult result)
                : base(result)
            {
            }

            /// <remarks>
            /// The judgement font is wide enough that three digits and a sign cover a good part of the playfield, so a
            /// narrower one is used, without the horizontal squash that font is given.
            /// </remarks>
            protected override SpriteText CreateJudgementText() => new OsuSpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Font = OsuFont.Torus.With(size: 20, weight: FontWeight.Bold),
            };

            [BackgroundDependencyLoader]
            private void load(OsuColour colours)
            {
                earlyColour = colours.Green1;
                lateColour = colours.Red1;

                if (Result == HitResult.Meh)
                {
                    lateColour = lateColour.Darken(meh_darkening);
                    earlyColour = earlyColour.Darken(meh_darkening);
                }

                SetTimeOffset(0);
            }

            /// <summary>
            /// Displays the given offset from the perfect hit time, where a positive value is a late hit.
            /// </summary>
            public void SetTimeOffset(double timeOffset)
            {
                int milliseconds = (int)Math.Round(timeOffset);

                JudgementText.Text = milliseconds.ToString(@"+0;-0;0");
                JudgementText.Colour = milliseconds < 0 ? earlyColour : lateColour;
            }
        }

        /// <summary>
        /// A component no skin provides a drawable for.
        /// </summary>
        private class HitErrorLookup : ISkinComponentLookup
        {
        }
    }
}
