// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Tests.Beatmaps;

namespace osu.Game.Rulesets.Osu.Tests
{
    /// <summary>
    /// Song select only works star ratings out again when something they are sorted by changes.
    /// </summary>
    [TestFixture]
    public class StarRatingSortingModsTest : DifficultyCalculatorTest
    {
        protected override string ResourceAssembly => "osu.Game.Rulesets.Osu.Tests";

        /// <summary>
        /// Each is left out as having no effect on star rating, which the difficulty calculator has to agree with.
        /// </summary>
        private static readonly object[][] no_effect =
        {
            new object[] { "no fail", new Func<Mod[]>(() => new Mod[] { new OsuModNoFail() }) },
            new object[] { "sudden death", new Func<Mod[]>(() => new Mod[] { new OsuModSuddenDeath() }) },
            new object[] { "perfect", new Func<Mod[]>(() => new Mod[] { new OsuModPerfect() }) },
            new object[] { "accuracy challenge", new Func<Mod[]>(() => new Mod[] { new OsuModAccuracyChallenge() }) },
            new object[] { "score v2", new Func<Mod[]>(() => new Mod[] { new OsuModScoreV2() }) },
            new object[] { "muted", new Func<Mod[]>(() => new Mod[] { new OsuModMuted() }) },
            new object[] { "no scope", new Func<Mod[]>(() => new Mod[] { new OsuModNoScope() }) },
            new object[] { "classic", new Func<Mod[]>(() => new Mod[] { new OsuModClassic() }) },
            new object[] { "classic with every setting", new Func<Mod[]>(() => new Mod[] { new OsuModClassic { NoSliderHeadAccuracy = { Value = false }, ClassicNoteLock = { Value = false }, AlwaysPlayTailSample = { Value = false }, FadeHitCircleEarly = { Value = false }, ClassicHealth = { Value = false } } }) },
            new object[] { "difficulty adjust HP", new Func<Mod[]>(() => new Mod[] { new OsuModDifficultyAdjust { AdjustApproachRate = { Value = false }, AdjustDrainRate = { Value = true }, DrainRate = { Value = 2 } } }) },
            new object[] { "difficulty adjust stat turned off", new Func<Mod[]>(() => new Mod[] { new OsuModDifficultyAdjust { AdjustApproachRate = { Value = false }, CircleSize = { Value = 7 } } }) },
        };

        [TestCaseSource(nameof(no_effect))]
        public void TestLeftOutModHasNoEffect(string description, Func<Mod[]> createMods)
        {
            Assert.That(StarRatingSortingMods.Normalise(createMods()), Is.Empty, $"{description} was not left out");
            Assert.That(starRating(createMods()), Is.EqualTo(starRating()).Within(1e-9), $"{description} changes star rating");
        }

        /// <summary>
        /// Each pair differs only in a setting which is left out, which the difficulty calculator has to agree with.
        /// </summary>
        private static readonly object[][] no_effect_settings =
        {
            new object[]
            {
                "pitch adjustment",
                new Func<Mod[]>(() => new Mod[] { new OsuModDoubleTime() }),
                new Func<Mod[]>(() => new Mod[] { new OsuModDoubleTime { AdjustPitch = { Value = true } } }),
            },
            new object[]
            {
                "speed change under a bpm lock",
                new Func<Mod[]>(() => new Mod[] { new OsuModDoubleTime { LockBpm = { Value = true } } }),
                new Func<Mod[]>(() => new Mod[] { new OsuModDoubleTime { LockBpm = { Value = true }, SpeedChange = { Value = 1.8 } } }),
            },
            new object[]
            {
                "target bpm without a bpm lock",
                new Func<Mod[]>(() => new Mod[] { new OsuModDoubleTime() }),
                new Func<Mod[]>(() => new Mod[] { new OsuModDoubleTime { TargetBpm = { Value = 250 } } }),
            },
            new object[]
            {
                "extra lives",
                new Func<Mod[]>(() => new Mod[] { new OsuModEasy() }),
                new Func<Mod[]>(() => new Mod[] { new OsuModEasy { Retries = { Value = 5 } } }),
            },
            new object[]
            {
                "extended limits",
                new Func<Mod[]>(() => new Mod[] { new OsuModDifficultyAdjust { AdjustCircleSize = { Value = true }, CircleSize = { Value = 5 } } }),
                new Func<Mod[]>(() => new Mod[] { new OsuModDifficultyAdjust { AdjustCircleSize = { Value = true }, CircleSize = { Value = 5 }, ExtendedLimits = { Value = false } } }),
            },
        };

        [TestCaseSource(nameof(no_effect_settings))]
        public void TestLeftOutSettingHasNoEffect(string description, Func<Mod[]> createFirst, Func<Mod[]> createSecond)
        {
            Assert.That(keyOf(createSecond()), Is.EqualTo(keyOf(createFirst())), $"{description} was not left out");
            Assert.That(starRating(createSecond()), Is.EqualTo(starRating(createFirst())).Within(1e-9), $"{description} changes star rating");
        }

        [Test]
        public void TestHiddenIsLeftOut()
        {
            Assert.That(keyOf(new Mod[] { new OsuModHidden(), new OsuModDoubleTime() }), Is.EqualTo(keyOf(new Mod[] { new OsuModDoubleTime() })));
        }

        [Test]
        public void TestApproachRateIsLeftOut()
        {
            Assert.That(StarRatingSortingMods.Normalise(new Mod[] { new OsuModDifficultyAdjust { ApproachRate = { Value = 10 } } }), Is.Empty);

            // Only the approach rate is dropped from a difficulty adjust which also changes something else.
            Assert.That(keyOf(new Mod[] { new OsuModDifficultyAdjust { ApproachRate = { Value = 10 }, AdjustCircleSize = { Value = true }, CircleSize = { Value = 5 } } }),
                Is.EqualTo(keyOf(new Mod[] { new OsuModDifficultyAdjust { ApproachRate = { Value = 3 }, AdjustCircleSize = { Value = true }, CircleSize = { Value = 5 } } })));
        }

        [Test]
        public void TestSettingsThatMatterAreKept()
        {
            Assert.That(keyOf(new Mod[] { new OsuModDoubleTime { SpeedChange = { Value = 1.8 } } }), Is.Not.EqualTo(keyOf(new Mod[] { new OsuModDoubleTime() })));
            Assert.That(keyOf(new Mod[] { new OsuModDoubleTime { LockBpm = { Value = true }, TargetBpm = { Value = 250 } } }),
                Is.Not.EqualTo(keyOf(new Mod[] { new OsuModDoubleTime { LockBpm = { Value = true } } })));
            Assert.That(keyOf(new Mod[] { new OsuModCrossScreen { SpacingIntensity = { Value = 2 } } }), Is.Not.EqualTo(keyOf(new Mod[] { new OsuModCrossScreen() })));
            Assert.That(keyOf(new Mod[] { new OsuModHardRock() }), Is.Not.EqualTo(keyOf(Array.Empty<Mod>())));
            Assert.That(keyOf(new Mod[] { new OsuModFlashlight() }), Is.Not.EqualTo(keyOf(Array.Empty<Mod>())));
        }

        [Test]
        public void TestOverallDifficultyHeldUnderRateIsKept()
        {
            // Turned on without a value of its own, OD is held at the beatmap's value when the rate changes, which does
            // change star rating.
            var held = new Mod[] { new OsuModDifficultyAdjust { AdjustApproachRate = { Value = false }, AdjustOverallDifficulty = { Value = true } }, new OsuModDoubleTime() };

            Assert.That(keyOf(held), Is.Not.EqualTo(keyOf(new Mod[] { new OsuModDoubleTime() })));
        }

        [Test]
        public void TestOriginalModsAreUntouched()
        {
            var doubleTime = new OsuModDoubleTime { AdjustPitch = { Value = true } };

            StarRatingSortingMods.Normalise(new Mod[] { doubleTime });

            Assert.That(doubleTime.AdjustPitch.Value, Is.True);
        }

        private static string keyOf(Mod[] mods) => StarRatingSortingMods.GetKey(StarRatingSortingMods.Normalise(mods));

        private double starRating(params Mod[] mods) => CreateDifficultyCalculator(GetBeatmap("diffcalc-test")).Calculate(mods).StarRating;

        protected override DifficultyCalculator CreateDifficultyCalculator(IWorkingBeatmap beatmap) => new OsuDifficultyCalculator(new OsuRuleset().RulesetInfo, beatmap);

        protected override Ruleset CreateRuleset() => new OsuRuleset();
    }
}
