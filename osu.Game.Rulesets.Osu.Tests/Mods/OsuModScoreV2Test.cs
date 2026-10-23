// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Osu.Tests.Mods
{
    /// <summary>
    /// Checks the mod against a separate implementation of osu!stable's Score V2 formula.
    /// </summary>
    [TestFixture]
    public class OsuModScoreV2Test
    {
        private const int object_count = 100;

        private static readonly object[][] plays =
        {
            new object[] { "a perfect play", Enumerable.Repeat(HitResult.Great, object_count).ToArray() },
            new object[] { "one miss at the start", missAt(0) },
            new object[] { "one miss at the end", missAt(object_count - 1) },
            new object[] { "a play of hundreds", Enumerable.Repeat(HitResult.Ok, object_count).ToArray() },
            new object[]
            {
                "a mixed play",
                Enumerable.Range(0, object_count).Select(i => i % 17 == 0 ? HitResult.Miss : i % 5 == 0 ? HitResult.Ok : i % 11 == 0 ? HitResult.Meh : HitResult.Great).ToArray()
            },
        };

        [TestCaseSource(nameof(plays))]
        public void TestMatchesStableFormula(string description, HitResult[] results)
        {
            Assert.That(scoreFor(results, scoreV2: true), Is.EqualTo(Math.Round(referenceScore(results))).Within(1), $"the score was wrong for {description}");
        }

        [Test]
        public void TestModIsOfferedForSelection()
        {
            var mod = new OsuRuleset().CreateAllMods().OfType<OsuModScoreV2>().SingleOrDefault();

            Assert.That(mod, Is.Not.Null, "the mod was not in the ruleset's mod list");
            Assert.That(mod!.Type, Is.EqualTo(ModType.Conversion));
            Assert.That(mod.UserPlayable, Is.True);

            // Mod select hides anything without an implementation, which means anything which is not an `IApplicableMod`.
            Assert.That(mod.HasImplementation, Is.True);
        }

        [Test]
        public void TestPerfectPlayScoresMaximum()
        {
            Assert.That(scoreFor(Enumerable.Repeat(HitResult.Great, object_count).ToArray(), scoreV2: true), Is.EqualTo(1000000));
        }

        [Test]
        public void TestScoringDiffersFromDefault()
        {
            var results = missAt(object_count / 2);

            Assert.That(scoreFor(results, scoreV2: true), Is.Not.EqualTo(scoreFor(results, scoreV2: false)));
        }

        [Test]
        public void TestLosingComboInTheMiddleCostsMost()
        {
            // Breaking a long run in half costs more of the combo portion than clipping either of its ends, and the
            // two ends cost the same as each other.
            long nearTheStart = scoreFor(missAt(1), scoreV2: true);
            long nearTheEnd = scoreFor(missAt(object_count - 2), scoreV2: true);

            Assert.That(scoreFor(missAt(object_count / 2), scoreV2: true), Is.LessThan(nearTheStart));
            Assert.That(nearTheEnd, Is.EqualTo(nearTheStart));
        }

        [Test]
        public void TestDefaultScoringIsUntouchedWithoutTheMod()
        {
            var results = missAt(object_count / 2);

            var processor = new ScoreProcessor(new OsuRuleset());
            processor.ApplyBeatmap(createBeatmap());

            double accuracy = (double)results.Count(r => r == HitResult.Great) / object_count;

            // The default formula, which the mod must not have disturbed.
            double comboPortion = 0;
            double maximumComboPortion = 0;
            int combo = 0;

            for (int i = 0; i < results.Length; i++)
            {
                maximumComboPortion += 300 * Math.Pow(i + 1, ScoreProcessor.COMBO_EXPONENT);

                if (results[i] == HitResult.Miss)
                    combo = 0;
                else
                    comboPortion += 300 * Math.Pow(++combo, ScoreProcessor.COMBO_EXPONENT);
            }

            double expected = 500000 * accuracy * (comboPortion / maximumComboPortion) + 500000 * Math.Pow(accuracy, 5);

            Assert.That(scoreFor(results, scoreV2: false), Is.EqualTo(Math.Round(expected)).Within(1));
        }

        private static HitResult[] missAt(int index)
        {
            var results = Enumerable.Repeat(HitResult.Great, object_count).ToArray();
            results[index] = HitResult.Miss;
            return results;
        }

        /// <summary>
        /// osu!stable's Score V2 formula, written out separately from the mod.
        /// </summary>
        private static double referenceScore(IReadOnlyList<HitResult> results)
        {
            double maximumComboPortion = 0;

            for (int i = 0; i < results.Count; i++)
                maximumComboPortion += 300 * (1 + (i + 1) / 10.0);

            double comboPortion = 0;
            double baseScore = 0;
            int combo = 0;

            foreach (var result in results)
            {
                if (result == HitResult.Miss)
                {
                    combo = 0;
                    continue;
                }

                int value = baseScoreFor(result);

                baseScore += value;
                comboPortion += value * (1 + ++combo / 10.0);
            }

            double accuracy = baseScore / (300.0 * results.Count);

            return 700000 * comboPortion / maximumComboPortion + 300000 * Math.Pow(accuracy, 10);
        }

        private static int baseScoreFor(HitResult result)
        {
            switch (result)
            {
                case HitResult.Great:
                    return 300;

                case HitResult.Ok:
                    return 100;

                case HitResult.Meh:
                    return 50;

                default:
                    return 0;
            }
        }

        [Test]
        public void TestScoreV2CanBeTheDefault()
        {
            var results = missAt(object_count / 2);

            long standard = scoreFor(results, scoreV2: false);
            long scoreV2 = scoreFor(results, scoreV2: true);

            try
            {
                ScoringFormulaDefaults.ScoreV2ByDefault.Value = true;

                Assert.That(scoreFor(results, Array.Empty<Mod>()), Is.EqualTo(scoreV2), "a play without mods should be scored as Score V2 while it is the default");
                Assert.That(scoreFor(results, new OsuModLazerScoring()), Is.EqualTo(standard), "Lazer Scoring should give the usual scoring back");

                Assert.That(new OsuModScoreV2().UserPlayable, Is.False, "Score V2 would do nothing, so it shouldn't be offered");
                Assert.That(new OsuModLazerScoring().UserPlayable, Is.True);
            }
            finally
            {
                ScoringFormulaDefaults.ScoreV2ByDefault.Value = false;
            }

            Assert.That(scoreFor(results, new OsuModLazerScoring()), Is.EqualTo(standard), "Lazer Scoring does nothing when it isn't needed");
            Assert.That(new OsuModScoreV2().UserPlayable, Is.True);
            Assert.That(new OsuModLazerScoring().UserPlayable, Is.False);
        }

        [Test]
        public void TestDefaultIsDecidedWhenThePlayIsSetUp()
        {
            var results = missAt(object_count / 2);
            var beatmap = createBeatmap();

            var processor = new ScoreProcessor(new OsuRuleset());

            try
            {
                // Turned on before the beatmap is applied, as happens when the setting is changed before a play.
                ScoringFormulaDefaults.ScoreV2ByDefault.Value = true;
                processor.ApplyBeatmap(beatmap);
            }
            finally
            {
                // Turned off again part way through, which a play in progress must not react to.
                ScoringFormulaDefaults.ScoreV2ByDefault.Value = false;
            }

            for (int i = 0; i < results.Length; i++)
                processor.ApplyResult(new JudgementResult(beatmap.HitObjects[i], beatmap.HitObjects[i].Judgement) { Type = results[i] });

            Assert.That(processor.TotalScore.Value, Is.EqualTo(scoreFor(results, scoreV2: true)));
        }

        [Test]
        public void TestLazerScoringIsOfferedNextToScoreV2()
        {
            var mods = new OsuRuleset().GetModsFor(ModType.Conversion).ToList();

            Assert.That(mods.FindIndex(m => m is OsuModLazerScoring), Is.EqualTo(mods.FindIndex(m => m is OsuModScoreV2) + 1));
            Assert.That(new OsuModLazerScoring().Icon, Is.EqualTo(new OsuModScoreV2().Icon));
            Assert.That(new OsuModLazerScoring(), Is.InstanceOf<IHasUpsideDownIcon>());
        }

        private static long scoreFor(HitResult[] results, bool scoreV2) => scoreFor(results, scoreV2 ? new Mod[] { new OsuModScoreV2() } : Array.Empty<Mod>());

        private static long scoreFor(HitResult[] results, params Mod[] mods)
        {
            var beatmap = createBeatmap();

            var processor = new ScoreProcessor(new OsuRuleset());

            // The mods have to be in place before the beatmap is applied, as the maximum score is worked out from it.
            processor.Mods.Value = mods;
            processor.ApplyBeatmap(beatmap);

            for (int i = 0; i < results.Length; i++)
                processor.ApplyResult(new JudgementResult(beatmap.HitObjects[i], beatmap.HitObjects[i].Judgement) { Type = results[i] });

            return processor.TotalScore.Value;
        }

        private static Beatmap<HitObject> createBeatmap() => new Beatmap<HitObject>
        {
            HitObjects = Enumerable.Range(0, object_count).Select(i => (HitObject)new HitCircle { StartTime = i * 1000 }).ToList(),
        };
    }
}
