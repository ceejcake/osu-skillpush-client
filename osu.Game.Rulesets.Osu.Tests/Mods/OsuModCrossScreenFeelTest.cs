// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.IO;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.UI;
using osu.Game.Tests.Beatmaps;
using osuTK;

namespace osu.Game.Rulesets.Osu.Tests.Mods
{
    /// <summary>
    /// Runs <see cref="OsuModCrossScreen"/> over real beatmaps and checks that the result still feels like a map a person made:
    /// the mapper's contrast between calm and intense sections is kept, nothing hugs the edge of the playfield, and no objects
    /// end up stacked which weren't stacked before.
    /// A per-section report is also written to the temp directory, for looking at by hand.
    /// </summary>
    [TestFixture]
    public class OsuModCrossScreenFeelTest
    {
        private static readonly string[] beatmaps = { "1124896", "1341554", "2593923", "801165" };

        private const double window_length = 20000;

        [TestCaseSource(nameof(beatmaps))]
        public void TestRealBeatmap(string name)
        {
            var original = getPlayable(name, Array.Empty<Mod>());
            var modded = getPlayable(name, new Mod[] { new OsuModCrossScreen() });

            var report = new StringBuilder();
            var stats = analyse(original, modded, report);

            string path = Path.Combine(Path.GetTempPath(), $"xs-report-{name}.txt");
            File.WriteAllText(path, report.ToString());
            TestContext.Out.WriteLine(report.ToString());

            Assert.That(stats.OutOfPlayfield, Is.Zero, "objects left the playfield");
            Assert.That(stats.AccidentalStacks, Is.Zero, "objects were stacked which weren't stacked before");
            Assert.That(stats.EdgeHuggers, Is.LessThanOrEqualTo(stats.OriginalEdgeHuggers + 2), "objects were pushed flush against the playfield edge");

            // The ratio of fast sections to slow sections should survive: within 25% of the original.
            if (stats.OriginalContrast > 1.2)
                Assert.That(stats.ModdedContrast / stats.OriginalContrast, Is.InRange(0.75, 1.25), "the contrast between calm and intense sections was lost");
        }

        private record Stats(int OutOfPlayfield, int AccidentalStacks, int EdgeHuggers, int OriginalEdgeHuggers, double OriginalContrast, double ModdedContrast);

        private static Stats analyse(IBeatmap original, IBeatmap modded, StringBuilder report)
        {
            var originalObjects = original.HitObjects.Cast<OsuHitObject>().ToList();
            var moddedObjects = modded.HitObjects.Cast<OsuHitObject>().ToList();

            Assert.That(moddedObjects.Count, Is.EqualTo(originalObjects.Count));

            var kiai = original.ControlPointInfo.EffectPoints.Where(e => e.KiaiMode).Select(e => e.Time).ToList();

            report.AppendLine($"{original.BeatmapInfo} ({originalObjects.Count} objects, {60000 / original.GetMostCommonBeatLength():N0} BPM)");
            report.AppendLine("window   kiai  velocity(px/ms) orig -> mod  (x)    | mean jump orig -> mod | edge orig/mod | stacks+");
            report.AppendLine(new string('-', 100));

            int outOfPlayfield = 0, accidentalStacks = 0, edgeHuggers = 0, originalEdgeHuggers = 0;
            var windowVelocities = new List<(bool kiai, double orig, double mod)>();

            double end = originalObjects[^1].StartTime;

            for (double windowStart = originalObjects[0].StartTime; windowStart <= end; windowStart += window_length)
            {
                double windowEnd = windowStart + window_length;

                var origV = new List<double>();
                var modV = new List<double>();
                var origLen = new List<double>();
                var modLen = new List<double>();
                int windowEdgeOrig = 0, windowEdgeMod = 0, windowStacks = 0;

                for (int i = 0; i < originalObjects.Count; i++)
                {
                    var o = originalObjects[i];
                    var m = moddedObjects[i];

                    if (o.StartTime < windowStart || o.StartTime >= windowEnd || o is Spinner)
                        continue;

                    if (isOutside(m.StackedPosition))
                        outOfPlayfield++;

                    if (isEdgeHugger(o.StackedPosition)) windowEdgeOrig++;
                    if (isEdgeHugger(m.StackedPosition)) windowEdgeMod++;

                    if (i == 0 || originalObjects[i - 1] is Spinner)
                        continue;

                    double dt = o.StartTime - originalObjects[i - 1].GetEndTime();
                    if (dt <= 0) continue;

                    float dOrig = (o.StackedPosition - originalObjects[i - 1].StackedEndPosition).Length;
                    float dMod = (m.StackedPosition - moddedObjects[i - 1].StackedEndPosition).Length;

                    if (dOrig > 3 && dMod <= 3)
                        windowStacks++;

                    origV.Add(dOrig / dt);
                    modV.Add(dMod / dt);
                    origLen.Add(dOrig);
                    modLen.Add(dMod);
                }

                if (origV.Count == 0)
                    continue;

                bool inKiai = kiai.Any(k => k >= windowStart - window_length && k < windowEnd) && isKiaiAt(original, windowStart + window_length / 2);

                double vo = origV.Average(), vm = modV.Average();
                windowVelocities.Add((inKiai, vo, vm));

                edgeHuggers += windowEdgeMod;
                originalEdgeHuggers += windowEdgeOrig;
                accidentalStacks += windowStacks;

                report.AppendLine($"{windowStart / 1000,5:N0}s  {(inKiai ? "kiai" : "    ")}  {vo,6:N2} -> {vm,6:N2}  ({vm / vo:N2}x)   | {origLen.Average(),6:N0} -> {modLen.Average(),6:N0}       | {windowEdgeOrig,4} /{windowEdgeMod,4}    | {windowStacks}");
            }

            double originalContrast = contrast(windowVelocities.Select(w => (w.kiai, w.orig)));
            double moddedContrast = contrast(windowVelocities.Select(w => (w.kiai, w.mod)));

            report.AppendLine();
            report.AppendLine($"kiai / calm velocity ratio: original {originalContrast:N2}, modded {moddedContrast:N2}");
            report.AppendLine($"out of playfield: {outOfPlayfield}, accidental stacks: {accidentalStacks}, edge huggers: {originalEdgeHuggers} -> {edgeHuggers}");
            report.AppendLine();

            return new Stats(outOfPlayfield, accidentalStacks, edgeHuggers, originalEdgeHuggers, originalContrast, moddedContrast);
        }

        /// <summary>
        /// How much faster the intense sections are than the calm ones. Falls back to the fastest third over the slowest third
        /// when a beatmap has no kiai.
        /// </summary>
        private static double contrast(IEnumerable<(bool kiai, double velocity)> windows)
        {
            var list = windows.ToList();

            if (list.Any(w => w.kiai) && list.Any(w => !w.kiai))
                return list.Where(w => w.kiai).Average(w => w.velocity) / list.Where(w => !w.kiai).Average(w => w.velocity);

            var sorted = list.Select(w => w.velocity).OrderBy(v => v).ToList();
            int third = Math.Max(1, sorted.Count / 3);

            return sorted.TakeLast(third).Average() / sorted.Take(third).Average();
        }

        private static bool isKiaiAt(IBeatmap beatmap, double time) => beatmap.ControlPointInfo.EffectPointAt(time).KiaiMode;

        private static bool isOutside(Vector2 position) =>
            position.X < -0.5f || position.Y < -0.5f || position.X > OsuPlayfield.BASE_SIZE.X + 0.5f || position.Y > OsuPlayfield.BASE_SIZE.Y + 0.5f;

        private static bool isEdgeHugger(Vector2 position) =>
            position.X <= 4 || position.Y <= 4 || position.X >= OsuPlayfield.BASE_SIZE.X - 4 || position.Y >= OsuPlayfield.BASE_SIZE.Y - 4;

        private static IBeatmap getPlayable(string name, Mod[] mods)
        {
            using var stream = typeof(OsuModCrossScreenFeelTest).Assembly.GetManifestResourceStream($"osu.Game.Rulesets.Osu.Tests.Resources.Testing.Beatmaps.{name}.osu");
            using var reader = new LineBufferedReader(stream!);

            var decoder = osu.Game.Beatmaps.Formats.Decoder.GetDecoder<Beatmap>(reader);
            ((osu.Game.Beatmaps.Formats.LegacyBeatmapDecoder)decoder).ApplyOffsets = false;

            var working = new TestWorkingBeatmap(decoder.Decode(reader));
            return working.GetPlayableBeatmap(new OsuRuleset().RulesetInfo, mods);
        }
    }
}
