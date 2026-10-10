// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Audio;
using osu.Framework.Bindables;

namespace osu.Game.Audio
{
    /// <summary>
    /// Changes the pitch of the music without changing its speed.
    /// </summary>
    /// <remarks>
    /// Frequency changes speed and pitch together, and tempo changes speed alone. Raising one by the same factor the other is
    /// lowered by leaves the speed (and so the gameplay clock, which follows their product) exactly where it was, with only
    /// the pitch moved.
    /// </remarks>
    public sealed class MusicPitchShift : IDisposable
    {
        public const double MINIMUM_PITCH = 0.5;
        public const double MAXIMUM_PITCH = 2;

        private readonly IAdjustableAudioComponent component;
        private readonly IBindable<double> pitch;

        private readonly BindableDouble frequency = new BindableDouble(1);
        private readonly BindableDouble tempo = new BindableDouble(1);

        /// <param name="component">The audio to change the pitch of.</param>
        /// <param name="pitch">The pitch, as a multiple of the original. Followed as it changes.</param>
        public MusicPitchShift(IAdjustableAudioComponent component, IBindable<double> pitch)
        {
            this.component = component;
            this.pitch = pitch.GetBoundCopy();

            this.pitch.BindValueChanged(p =>
            {
                double value = Math.Clamp(p.NewValue, MINIMUM_PITCH, MAXIMUM_PITCH);

                frequency.Value = value;
                tempo.Value = 1 / value;
            }, true);

            component.AddAdjustment(AdjustableProperty.Frequency, frequency);
            component.AddAdjustment(AdjustableProperty.Tempo, tempo);
        }

        public void Dispose()
        {
            component.RemoveAdjustment(AdjustableProperty.Frequency, frequency);
            component.RemoveAdjustment(AdjustableProperty.Tempo, tempo);
            pitch.UnbindAll();
        }
    }
}
