// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.ComponentModel;

namespace osu.Game.Beatmaps.StreamPractice
{
    /// <summary>
    /// How a stream practice beatmap is laid out.
    /// </summary>
    /// <param name="Pattern">Whether the stream is continuous, or broken up into bursts.</param>
    /// <param name="BurstLength">The number of notes in each burst, when <paramref name="Pattern"/> is <see cref="StreamPracticePattern.Bursts"/>.</param>
    public record StreamPracticeOptions(StreamPracticePattern Pattern = StreamPracticePattern.Continuous, int BurstLength = 16)
    {
        /// <summary>
        /// The burst lengths on offer: powers of two from 8.
        /// </summary>
        public static readonly int[] BURST_LENGTHS = { 8, 16, 32, 64, 128, 256 };
    }

    public enum StreamPracticePattern
    {
        /// <summary>
        /// One unbroken stream, going round a circle.
        /// </summary>
        [Description("Continuous stream (circle)")]
        Continuous,

        /// <summary>
        /// Bursts of notes, each followed by a rest of the same length, going round a square.
        /// </summary>
        [Description("Bursts (square)")]
        Bursts,
    }
}
