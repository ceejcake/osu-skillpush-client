// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Game.Audio;
using osu.Game.Rulesets.Osu.Mods;

namespace osu.Game.Tests.NonVisual
{
    [TestFixture]
    public class MusicPitchShiftTest
    {
        [TestCase(1.5)]
        [TestCase(0.75)]
        [TestCase(2)]
        [TestCase(0.5)]
        public void TestPitchChangesWithoutSpeed(double pitch)
        {
            var adjustments = new AudioAdjustments();

            using (new MusicPitchShift(adjustments, new BindableDouble(pitch)))
            {
                // Frequency carries pitch and speed together; the speed is the product of frequency and tempo.
                Assert.That(adjustments.AggregateFrequency.Value, Is.EqualTo(pitch).Within(1e-9), "pitch");
                Assert.That(speedOf(adjustments), Is.EqualTo(1).Within(1e-9), "speed");
            }
        }

        [Test]
        public void TestFollowsChanges()
        {
            var adjustments = new AudioAdjustments();
            var pitch = new BindableDouble(1);

            using (new MusicPitchShift(adjustments, pitch))
            {
                pitch.Value = 1.25;

                Assert.That(adjustments.AggregateFrequency.Value, Is.EqualTo(1.25).Within(1e-9));
                Assert.That(speedOf(adjustments), Is.EqualTo(1).Within(1e-9));
            }
        }

        [Test]
        public void TestRemovedWhenDisposed()
        {
            var adjustments = new AudioAdjustments();
            var pitch = new BindableDouble(1.5);

            new MusicPitchShift(adjustments, pitch).Dispose();

            Assert.That(adjustments.AggregateFrequency.Value, Is.EqualTo(1));
            Assert.That(adjustments.AggregateTempo.Value, Is.EqualTo(1));

            // No longer following the setting either.
            pitch.Value = 2;
            Assert.That(adjustments.AggregateFrequency.Value, Is.EqualTo(1));
        }

        [Test]
        public void TestCombinesWithRateChange()
        {
            // Double time speeds the music up without changing its pitch. With the pitch raised as well, both apply.
            var adjustments = new AudioAdjustments();
            var doubleTime = new OsuModDoubleTime { SpeedChange = { Value = 1.5 } };

            doubleTime.ApplyToTrack(adjustments);

            using (new MusicPitchShift(adjustments, new BindableDouble(1.2)))
            {
                Assert.That(speedOf(adjustments), Is.EqualTo(1.5).Within(1e-9), "speed");
                Assert.That(adjustments.AggregateFrequency.Value, Is.EqualTo(1.2).Within(1e-9), "pitch");
            }
        }

        private static double speedOf(AudioAdjustments adjustments) => adjustments.AggregateFrequency.Value * adjustments.AggregateTempo.Value;
    }
}
