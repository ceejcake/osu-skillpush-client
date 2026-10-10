// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.Play.HUD.HitErrorMeters
{
    public abstract partial class HitErrorMeter : CompositeDrawable, ISerialisableDrawable
    {
        protected HitWindows HitWindows { get; private set; }

        [Resolved]
        private ScoreProcessor processor { get; set; }

        [Resolved]
        private OsuColour colours { get; set; }

        [Resolved(canBeNull: true)]
        private GameplayClockContainer gameplayClockContainer { get; set; }

        public bool UsesFixedAnchor { get; set; }

        /// <summary>
        /// The content to scale when the global hit error bar size override is enabled (<see cref="OsuSetting.HitErrorBarSizeOverride"/>).
        /// If null, the override doesn't apply to this meter.
        /// </summary>
        protected Drawable SizeOverrideTarget { get; set; }

        private Bindable<bool> sizeOverride;
        private Bindable<float> overrideSize;

        [BackgroundDependencyLoader(true)]
        private void load(DrawableRuleset drawableRuleset, OsuConfigManager config)
        {
            HitWindows = drawableRuleset?.FirstAvailableHitWindows ?? HitWindows.Empty;

            sizeOverride = config?.GetBindable<bool>(OsuSetting.HitErrorBarSizeOverride);
            overrideSize = config?.GetBindable<float>(OsuSetting.HitErrorBarSize);

            // This is to allow the visual state to be correct after HUD comes visible after being hidden.
            AlwaysPresent = true;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (gameplayClockContainer != null)
                gameplayClockContainer.OnSeek += Clear;

            processor.NewJudgement += processorNewJudgement;
        }

        protected override void Update()
        {
            base.Update();
            updateSizeOverride();
        }

        /// <summary>
        /// Applies the global size override, by counter-scaling <see cref="SizeOverrideTarget"/> so that the meter's final size
        /// (relative to the gameplay screen) matches the configured size, regardless of any scale applied by the skin layout
        /// or by containers the skin / ruleset places the meter inside. The skin layout itself is left untouched.
        /// </summary>
        private void updateSizeOverride()
        {
            if (SizeOverrideTarget == null)
                return;

            if (sizeOverride?.Value != true || overrideSize == null)
            {
                SizeOverrideTarget.Scale = Vector2.One;
                return;
            }

            Vector2 currentScale = getScaleRelativeToScreen();

            // Can happen transiently (e.g. while a parent is animating from zero scale). Keep the last applied scale.
            if (currentScale.X < 0.001f || currentScale.Y < 0.001f)
                return;

            float size = overrideSize.Value;
            SizeOverrideTarget.Scale = new Vector2(size / currentScale.X, size / currentScale.Y);
        }

        /// <summary>
        /// The scale of this meter's local space relative to the containing gameplay screen, which includes the meter's own
        /// skin-applied scale and the scale of all containers between the meter and the screen.
        /// </summary>
        private Vector2 getScaleRelativeToScreen()
        {
            // Measure against the gameplay screen if there is one, else the top-most ancestor.
            Drawable reference = null;

            for (var d = Parent; d != null; d = d.Parent)
            {
                reference = d;

                if (d is Player)
                    break;
            }

            if (reference == null)
                return new Vector2(Math.Abs(Scale.X), Math.Abs(Scale.Y));

            Vector2 origin = ToSpaceOfOtherDrawable(Vector2.Zero, reference);

            return new Vector2(
                (ToSpaceOfOtherDrawable(new Vector2(1, 0), reference) - origin).Length,
                (ToSpaceOfOtherDrawable(new Vector2(0, 1), reference) - origin).Length);
        }

        // Scheduled as meter implementations are likely going to change/add drawables when reacting to this.
        private void processorNewJudgement(JudgementResult j) => Schedule(() => OnNewJudgement(j));

        /// <summary>
        /// Fired when a new judgement arrives.
        /// </summary>
        /// <param name="judgement">The new judgement.</param>
        protected abstract void OnNewJudgement(JudgementResult judgement);

        protected Color4 GetColourForHitResult(HitResult result)
        {
            return colours.ForHitResult(result);
        }

        /// <summary>
        /// Invoked by <see cref="GameplayClockContainer.OnSeek"/>.
        /// Any inheritors of <see cref="HitErrorMeter"/> should have this method clear their container that displays the hit error results.
        /// </summary>
        public abstract void Clear();

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (processor != null)
                processor.NewJudgement -= processorNewJudgement;

            if (gameplayClockContainer != null)
                gameplayClockContainer.OnSeek -= Clear;
        }
    }
}
