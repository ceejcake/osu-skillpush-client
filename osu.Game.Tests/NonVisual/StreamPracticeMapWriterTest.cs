// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Beatmaps.Formats;
using osu.Game.Beatmaps.StreamPractice;
using osu.Game.Beatmaps.Timing;
using osu.Game.IO;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osuTK;

namespace osu.Game.Tests.NonVisual
{
    [TestFixture]
    public class StreamPracticeMapWriterTest
    {
        private static readonly StreamPracticeOptions continuous = new StreamPracticeOptions(StreamPracticePattern.Continuous);

        private static StreamPracticeOptions bursts(int length) => new StreamPracticeOptions(StreamPracticePattern.Bursts, length);

        #region Continuous

        /// <summary>
        /// The first lap of the "Extremely Long Stream Practice 2 [warmup]" beatmap this follows, a quarter of the circle.
        /// </summary>
        private static readonly (int x, int y)[] reference_positions =
        {
            (266, 86), (276, 88), (287, 90), (296, 94), (306, 98), (315, 104), (323, 110), (331, 117),
            (338, 125), (344, 133), (350, 142), (354, 152), (358, 161), (360, 172), (362, 182), (362, 192),
        };

        [Test]
        public void TestContinuousMatchesReferenceBeatmap()
        {
            // The reference beatmap's timing: 200 BPM from 28ms, with a stream from 91228ms to 163153ms.
            var notes = StreamPracticeMapWriter.CreateNotes(createSource(new[] { (28.0, 300.0) }, 91228, 163153), continuous);

            Assert.That(notes, Has.Count.EqualTo(960));
            Assert.That(notes.Any(n => n.IsSlider), Is.False);

            for (int i = 0; i < notes.Count; i++)
            {
                Assert.That(notes[i].Time, Is.EqualTo(91228 + i * 75), $"note {i} timing");
                Assert.That(notes[i].NewCombo, Is.EqualTo(i % 16 == 0), $"note {i} combo");
                Assert.That(notes[i].OnBeat, Is.EqualTo(i % 4 == 0), $"note {i} beat");
            }

            for (int i = 0; i < reference_positions.Length; i++)
            {
                Assert.That(notes[i].X, Is.EqualTo(reference_positions[i].x).Within(1), $"note {i} x");
                Assert.That(notes[i].Y, Is.EqualTo(reference_positions[i].y).Within(1), $"note {i} y");
            }

            // Every lap returns to the top of the circle, so the stream can carry on round indefinitely.
            Assert.That((notes[63].X, notes[63].Y), Is.EqualTo((255, 86)));
            Assert.That((notes[64].X, notes[64].Y), Is.EqualTo((notes[0].X, notes[0].Y)));
        }

        [Test]
        public void TestStartsOnTheBarAfterTheFirstObject()
        {
            // 120 BPM from 0ms, so a bar every 2000ms. The first object is a quarter of the way into a beat.
            var notes = StreamPracticeMapWriter.CreateNotes(createSource(new[] { (0.0, 500.0) }, 125, 6000), continuous);

            Assert.That(notes[0].Time, Is.EqualTo(2000));
            Assert.That(notes[0].OnBeat && notes[0].NewCombo);
        }

        [Test]
        public void TestEndsWithTheLastObject()
        {
            var notes = StreamPracticeMapWriter.CreateNotes(createSource(new[] { (0.0, 500.0) }, 0, 2060), continuous);

            Assert.That(notes.Last().Time, Is.EqualTo(2000));
        }

        [Test]
        public void TestFollowsBpmChanges()
        {
            // 120 BPM, then 200 BPM from 2010ms, part way through a beat.
            var notes = StreamPracticeMapWriter.CreateNotes(createSource(new[] { (0.0, 500.0), (2010.0, 300.0) }, 0, 4000), continuous);

            var before = notes.Where(n => n.Time < 2010).ToList();
            var after = notes.Where(n => n.Time >= 2010).ToList();

            Assert.That(before.Zip(before.Skip(1), (a, b) => b.Time - a.Time), Is.All.EqualTo(125));
            Assert.That(after.Zip(after.Skip(1), (a, b) => b.Time - a.Time), Is.All.InRange(74, 76));

            // The new timing point starts a bar of its own.
            Assert.That(after[0].Time, Is.EqualTo(2010));
            Assert.That(after[0].NewCombo);
            Assert.That(after[16].NewCombo);

            // The circle carries on from where it was, rather than starting again at the top.
            Assert.That((after[0].X, after[0].Y), Is.Not.EqualTo((notes[0].X, notes[0].Y)));
        }

        [Test]
        public void TestWaltzBarsAreThreeBeats()
        {
            var notes = StreamPracticeMapWriter.CreateNotes(createSource(new[] { (0.0, 500.0) }, 0, 6000, TimeSignature.SimpleTriple), continuous);

            Assert.That(notes.Select((n, i) => (n, i)).Where(p => p.n.NewCombo).Select(p => p.i).Take(3), Is.EqualTo(new[] { 0, 12, 24 }));
        }

        #endregion

        #region Bursts

        /// <summary>
        /// The opening of the "Burst Stream Practice Maps 2 [270 BPM 8 notes]" beatmap: position, time, new combo, beat, slider end.
        /// </summary>
        private static readonly (int x, int y, int time, bool newCombo, bool onBeat, (int, int)? sliderEnd)[] reference_8 =
        {
            (128, 64, 16882, true, true, null), (144, 64, 16937, false, false, null), (160, 64, 16993, false, false, null), (176, 64, 17048, false, false, null),
            (192, 64, 17104, false, true, null), (208, 64, 17159, false, false, null), (224, 64, 17215, false, false, null), (240, 64, 17271, false, false, null),
            (256, 64, 17326, false, true, (304, 64)), (320, 64, 17548, false, true, (368, 64)),
            (384, 64, 17771, true, true, null), (384, 80, 17826, false, false, null), (384, 96, 17882, false, false, null), (384, 112, 17937, false, false, null),
            (384, 128, 17993, false, true, null), (384, 144, 18048, false, false, null), (384, 160, 18104, false, false, null), (384, 176, 18160, false, false, null),
            (384, 192, 18215, false, true, (384, 240)), (384, 256, 18437, false, true, (384, 304)),
            (384, 320, 18659, true, true, null),
        };

        /// <summary>
        /// The opening of the "[270 BPM 16 notes]" beatmap, after its first side of circles.
        /// </summary>
        private static readonly (int x, int y, int time, bool newCombo, bool onBeat, (int, int)? sliderEnd)[] reference_16_after_first_burst =
        {
            (384, 64, 17771, true, true, (384, 112)), (384, 128, 17994, false, true, (384, 176)), (384, 192, 18216, false, true, (384, 240)), (384, 256, 18438, false, true, (384, 304)),
            (384, 320, 18659, true, true, null), (368, 320, 18714, false, false, null),
        };

        /// <summary>
        /// The opening of the "[270 BPM 32 notes]" beatmap, after its first two sides of circles.
        /// </summary>
        private static readonly (int x, int y, int time, bool newCombo, bool onBeat, (int, int)? sliderEnd)[] reference_32_after_first_burst =
        {
            (384, 320, 18659, true, true, (336, 320)), (320, 320, 18882, false, true, (272, 320)), (256, 320, 19104, false, true, (208, 320)), (192, 320, 19326, false, true, (144, 320)),
            (128, 320, 19548, true, true, (128, 272)), (128, 256, 19771, false, true, (128, 208)), (128, 192, 19993, false, true, (128, 144)), (128, 128, 20215, false, true, (128, 80)),
            (128, 64, 20437, true, true, null),
        };

        /// <summary>
        /// The reference beatmaps' timing: 270 BPM from 2660ms, with bursts from 16882ms.
        /// </summary>
        private static IBeatmap referenceBurstSource() => createSource(new[] { (2660.0, 222.222222222222) }, 16882, 60000);

        [Test]
        public void TestEightNoteBurstsMatchReferenceBeatmap()
        {
            assertMatches(StreamPracticeMapWriter.CreateNotes(referenceBurstSource(), bursts(8)), reference_8);
        }

        [Test]
        public void TestSixteenNoteBurstsMatchReferenceBeatmap()
        {
            var notes = StreamPracticeMapWriter.CreateNotes(referenceBurstSource(), bursts(16));

            Assert.That(notes.Take(16).Select(n => n.IsSlider), Is.All.False, "first burst");
            assertMatches(notes.Skip(16).ToList(), reference_16_after_first_burst);
        }

        [Test]
        public void TestThirtyTwoNoteBurstsMatchReferenceBeatmap()
        {
            var notes = StreamPracticeMapWriter.CreateNotes(referenceBurstSource(), bursts(32));

            Assert.That(notes.Take(32).Select(n => n.IsSlider), Is.All.False, "first burst");
            assertMatches(notes.Skip(32).ToList(), reference_32_after_first_burst);
        }

        [Test]
        public void TestRestIsAsLongAsTheBurst([Values(8, 16, 32, 64, 128, 256)] int length)
        {
            var notes = StreamPracticeMapWriter.CreateNotes(createSource(new[] { (0.0, 400.0) }, 0, 200000), bursts(length));

            // A burst and its rest take twice the burst's length in 1/4 notes, the rest being filled with one slider a beat.
            var cycle = notes.Take(length + length / 4).ToList();

            Assert.That(cycle.Take(length).Select(n => n.IsSlider), Is.All.False);
            Assert.That(cycle.Skip(length).Select(n => n.IsSlider), Is.All.True);
            Assert.That(notes[length + length / 4].Time, Is.EqualTo(2 * length * 100), "next burst");
            Assert.That(notes[length + length / 4].IsSlider, Is.False);
        }

        [Test]
        public void TestEachSectionStartsWithABurst()
        {
            // 8 note bursts, with the timing changing from 120 to 150 BPM part way through a rest.
            var notes = StreamPracticeMapWriter.CreateNotes(createSource(new[] { (0.0, 500.0), (3000.0, 400.0) }, 0, 8000), bursts(8));

            var first = notes.First(n => n.Time >= 3000);

            Assert.That(first.Time, Is.EqualTo(3000));
            Assert.That(first.IsSlider, Is.False);
        }

        [Test]
        public void TestRestSliderCutOffByTimingChangeIsLeftOut()
        {
            // 8 note bursts at 120 BPM, so the first rest is from 1000ms to 2000ms, with sliders at 1000ms and 1500ms. The
            // timing changes 100ms after the second starts, before it finishes.
            var notes = StreamPracticeMapWriter.CreateNotes(createSource(new[] { (0.0, 500.0), (1600.0, 400.0) }, 0, 8000), bursts(8));

            Assert.That(notes.Where(n => n.IsSlider && n.Time < 1600).Select(n => n.Time), Is.EqualTo(new[] { 1000 }));
        }

        [Test]
        public void TestBurstsOfPartialBeatsAreRefused()
        {
            Assert.Throws<ArgumentException>(() => StreamPracticeMapWriter.CreateNotes(createSource(new[] { (0.0, 500.0) }, 0, 8000), bursts(6)));
        }

        #endregion

        [Test]
        public void TestDifficultyNames()
        {
            var source = createSource(new[] { (28.0, 300.0) }, 0, 1000);
            source.BeatmapInfo.Metadata.Title = "Fury of the Storm";

            Assert.That(StreamPracticeMapWriter.GetDifficultyName(source, continuous), Is.EqualTo("Fury of the Storm - [200 BPM]"));
            Assert.That(StreamPracticeMapWriter.GetDifficultyName(source, bursts(16)), Is.EqualTo("Fury of the Storm - [200 BPM] - 16 notes"));
        }

        [Test]
        public void TestContinuousBeatmapDecodes()
        {
            var source = createSource(new[] { (28.0, 300.0) }, 91228, 95000);
            source.BeatmapInfo.Metadata.Title = "Fury of the Storm";
            source.Difficulty.ApproachRate = 9.3f;

            var decoded = decode(StreamPracticeMapWriter.Write(source, continuous, "audio 1234abcd.mp3", "bg.jpg", "practice enjoyer"));
            var notes = StreamPracticeMapWriter.CreateNotes(source, continuous);

            Assert.That(decoded.Metadata.Title, Is.EqualTo(StreamPracticeMapWriter.TITLE));
            Assert.That(decoded.Metadata.Artist, Is.EqualTo(StreamPracticeMapWriter.ARTIST));
            Assert.That(decoded.Metadata.Author.Username, Is.EqualTo("practice enjoyer"));
            Assert.That(decoded.Metadata.AudioFile, Is.EqualTo("audio 1234abcd.mp3"));
            Assert.That(decoded.Metadata.BackgroundFile, Is.EqualTo("bg.jpg"));
            Assert.That(decoded.BeatmapInfo.DifficultyName, Is.EqualTo("Fury of the Storm - [200 BPM]"));
            Assert.That(decoded.Difficulty.ApproachRate, Is.EqualTo(9.3f));
            Assert.That(decoded.ControlPointInfo.TimingPointAt(91228).BeatLength, Is.EqualTo(300));

            Assert.That(decoded.HitObjects, Has.Count.EqualTo(notes.Count));

            for (int i = 0; i < notes.Count; i++)
            {
                var hitObject = decoded.HitObjects[i];

                assertPositionAndTiming(hitObject, notes[i]);
                Assert.That(hitObject.Samples.Any(s => s.Name == HitSampleInfo.HIT_CLAP), Is.EqualTo(notes[i].OnBeat));
                Assert.That(hitObject.Samples.Any(s => s.Name == HitSampleInfo.HIT_FINISH), Is.EqualTo(notes[i].NewCombo));
                Assert.That(hitObject.Samples.All(s => s.Bank == HitSampleInfo.BANK_SOFT));
            }
        }

        [Test]
        public void TestBurstBeatmapDecodes()
        {
            var source = referenceBurstSource();
            var decoded = decode(StreamPracticeMapWriter.Write(source, bursts(8), "audio.mp3", null, "practice enjoyer"));
            var notes = StreamPracticeMapWriter.CreateNotes(source, bursts(8));

            Assert.That(decoded.HitObjects, Has.Count.EqualTo(notes.Count));

            for (int i = 0; i < notes.Count; i++)
            {
                var hitObject = decoded.HitObjects[i];

                assertPositionAndTiming(hitObject, notes[i]);
                Assert.That(hitObject.Samples.Any(s => s.Name == HitSampleInfo.HIT_WHISTLE), Is.EqualTo(notes[i].OnBeat && !notes[i].IsSlider));

                if (notes[i].SliderEnd is not (int endX, int endY))
                {
                    Assert.That(hitObject, Is.Not.InstanceOf<IHasPathWithRepeats>());
                    continue;
                }

                var slider = (IHasPathWithRepeats)hitObject;

                // Head, two reverses and a tail, a 1/4 apart, finishing three quarters of the way through the beat.
                Assert.That(slider.RepeatCount, Is.EqualTo(2));
                Assert.That(slider.Duration, Is.EqualTo(222.222222222222 * 3 / 4).Within(1));
                Assert.That(slider.Path.ControlPoints.Last().Position, Is.EqualTo(new Vector2(endX - notes[i].X, endY - notes[i].Y)));

                // The whistle is on the head alone.
                Assert.That(slider.NodeSamples[0].Any(s => s.Name == HitSampleInfo.HIT_WHISTLE));
                Assert.That(slider.NodeSamples.Skip(1).SelectMany(s => s).Any(s => s.Name == HitSampleInfo.HIT_WHISTLE), Is.False);
            }
        }

        [Test]
        public void TestBeatmapWithoutObjectsIsRefused()
        {
            var source = createSource(new[] { (0.0, 500.0) }, 0, 1000);
            source.HitObjects.Clear();

            Assert.Throws<InvalidOperationException>(() => StreamPracticeMapWriter.CreateNotes(source, continuous));
        }

        [Test]
        public void TestBeatmapWithoutTimingIsRefused()
        {
            var source = createSource(Array.Empty<(double, double)>(), 0, 1000);

            Assert.Throws<InvalidOperationException>(() => StreamPracticeMapWriter.CreateNotes(source, continuous));
        }

        private static void assertMatches(IReadOnlyList<StreamPracticeMapWriter.Note> notes, (int x, int y, int time, bool newCombo, bool onBeat, (int, int)? sliderEnd)[] expected)
        {
            for (int i = 0; i < expected.Length; i++)
            {
                var (x, y, time, newCombo, onBeat, sliderEnd) = expected[i];

                Assert.That((notes[i].X, notes[i].Y), Is.EqualTo((x, y)), $"object {i} position");

                // The reference beatmaps' times wander from the exact 1/4 grid by up to a couple of milliseconds, from being
                // placed by hand in the editor. 18714ms in the 16 note beatmap is 1.6ms early, for instance.
                Assert.That(notes[i].Time, Is.EqualTo(time).Within(2), $"object {i} timing");
                Assert.That(notes[i].NewCombo, Is.EqualTo(newCombo), $"object {i} combo");
                Assert.That(notes[i].OnBeat, Is.EqualTo(onBeat), $"object {i} beat");
                Assert.That(notes[i].SliderEnd, Is.EqualTo(sliderEnd), $"object {i} slider");
            }
        }

        private static void assertPositionAndTiming(HitObject hitObject, StreamPracticeMapWriter.Note note)
        {
            Assert.That(hitObject.StartTime, Is.EqualTo(note.Time));
            Assert.That(((IHasPosition)hitObject).Position, Is.EqualTo(new Vector2(note.X, note.Y)));
            Assert.That(((IHasCombo)hitObject).NewCombo, Is.EqualTo(note.NewCombo));
        }

        private static Beatmap decode(string text)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(text)))
            using (var reader = new LineBufferedReader(stream))
                return new LegacyBeatmapDecoder().Decode(reader);
        }

        private static Beatmap createSource((double time, double beatLength)[] timingPoints, double firstObject, double lastObject, TimeSignature? timeSignature = null)
        {
            var beatmap = new Beatmap();

            foreach (var (time, beatLength) in timingPoints)
                beatmap.ControlPointInfo.Add(time, new TimingControlPoint { BeatLength = beatLength, TimeSignature = timeSignature ?? TimeSignature.SimpleQuadruple });

            beatmap.HitObjects.Add(new HitObject { StartTime = firstObject });
            beatmap.HitObjects.Add(new HitObject { StartTime = lastObject });

            return beatmap;
        }
    }
}
