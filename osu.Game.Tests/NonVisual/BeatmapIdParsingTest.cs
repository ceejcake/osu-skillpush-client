// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Game.Collections;

namespace osu.Game.Tests.NonVisual
{
    [TestFixture]
    public class BeatmapIdParsingTest
    {
        [TestCase("123", new[] { 123 })]
        [TestCase("123 456", new[] { 123, 456 })]
        [TestCase("123,456;789", new[] { 123, 456, 789 })]
        [TestCase("123\r\n456\n789\t12", new[] { 123, 456, 789, 12 })]
        [TestCase("  123 ,  456  ", new[] { 123, 456 })]
        [TestCase("123 123 456", new[] { 123, 456 })]
        [TestCase("https://osu.ppy.sh/beatmaps/4567", new[] { 4567 })]
        [TestCase("https://osu.ppy.sh/b/4567", new[] { 4567 })]
        [TestCase("https://osu.ppy.sh/beatmapsets/1#osu/75", new[] { 75 })]
        [TestCase("https://osu.ppy.sh/beatmapsets/1#mania/75 99", new[] { 75, 99 })]
        // A set on its own doesn't say which difficulty is wanted.
        [TestCase("https://osu.ppy.sh/beatmapsets/1", new int[0])]
        [TestCase("abc 12x -5 0", new int[0])]
        [TestCase("", new int[0])]
        public void TestParse(string input, int[] expected)
            => Assert.That(OsuCollectorImporter.ParseBeatmapIds(input), Is.EqualTo(expected));

        [TestCase("123 456", 0)]
        [TestCase("123 123 456", 1)]
        [TestCase("123 https://osu.ppy.sh/b/123 https://osu.ppy.sh/beatmapsets/1#osu/123 456 456", 3)]
        public void TestDuplicatesCounted(string input, int expectedDuplicates)
        {
            OsuCollectorImporter.ParseBeatmapIds(input, out int duplicates);
            Assert.That(duplicates, Is.EqualTo(expectedDuplicates));
        }

        [Test]
        public void TestPastedLinesJoined()
            => Assert.That(OsuCollectorImporter.JoinPastedLines("123\r\n456\n\n789\t12\r\n"), Is.EqualTo("123 456 789 12"));
    }
}
