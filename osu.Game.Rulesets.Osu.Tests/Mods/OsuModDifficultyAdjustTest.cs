// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Scoring;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Osu.Tests.Mods
{
    [TestFixture]
    public class OsuModDifficultyAdjustTest
    {
        [Test]
        public void TestOnlyApproachRateIsAdjustedByDefault()
        {
            var mod = new OsuModDifficultyAdjust();

            Assert.That(mod.AdjustApproachRate.Value, Is.True);
            Assert.That(mod.AdjustCircleSize.Value, Is.False);
            Assert.That(mod.AdjustDrainRate.Value, Is.False);
            Assert.That(mod.AdjustOverallDifficulty.Value, Is.False);
            Assert.That(mod.ExtendedLimits.Value, Is.True);
        }

        [Test]
        public void TestNoIncompatibleMods()
        {
            Assert.That(new OsuModDifficultyAdjust().IncompatibleMods, Is.Empty);
            Assert.That(new OsuModHardRock().IncompatibleMods, Has.No.Member(typeof(ModDifficultyAdjust)));
            Assert.That(new OsuModEasy().IncompatibleMods, Has.No.Member(typeof(ModDifficultyAdjust)));
        }

        [TestCase(9f)]
        [TestCase(10f)]
        [TestCase(11f)]
        public void TestApproachRateIsPlayedAsSet(float approachRate)
        {
            var mod = new OsuModDifficultyAdjust { ApproachRate = { Value = approachRate } };

            double withoutRateChange = preemptWith(new Mod[] { mod }, 1);
            double withDoubleTime = preemptWith(new Mod[] { mod, new OsuModDoubleTime() }, 1.5);
            double withHalfTime = preemptWith(new Mod[] { mod, new OsuModHalfTime() }, 0.75);
            double withHardRock = preemptWith(new Mod[] { mod, new OsuModHardRock() }, 1);

            Assert.That(withDoubleTime, Is.EqualTo(withoutRateChange).Within(1));
            Assert.That(withHalfTime, Is.EqualTo(withoutRateChange).Within(1));
            Assert.That(withHardRock, Is.EqualTo(withoutRateChange).Within(1));
        }

        [Test]
        public void TestApproachRateWithoutAValueHoldsTheBeatmapsOwn()
        {
            // The adjustment is on by default, which pins whatever approach rate would otherwise be in effect.
            var mod = new OsuModDifficultyAdjust();

            double withoutRateChange = preemptWith(new Mod[] { mod }, 1);
            double withDoubleTime = preemptWith(new Mod[] { mod, new OsuModDoubleTime() }, 1.5);

            Assert.That(withoutRateChange, Is.EqualTo(preemptWith(new Mod[] { }, 1)).Within(1), "the beatmap's own approach rate should be untouched");
            Assert.That(withDoubleTime, Is.EqualTo(withoutRateChange).Within(1));
        }

        [TestCase(8f)]
        [TestCase(10f)]
        public void TestOverallDifficultyIsPlayedAsSet(float overallDifficulty)
        {
            var mod = new OsuModDifficultyAdjust
            {
                AdjustOverallDifficulty = { Value = true },
                OverallDifficulty = { Value = overallDifficulty },
            };

            double withoutRateChange = greatWindowWith(new Mod[] { mod }, 1);
            double withDoubleTime = greatWindowWith(new Mod[] { mod, new OsuModDoubleTime() }, 1.5);
            double withHalfTime = greatWindowWith(new Mod[] { mod, new OsuModHalfTime() }, 0.75);

            Assert.That(withDoubleTime, Is.EqualTo(withoutRateChange).Within(1));
            Assert.That(withHalfTime, Is.EqualTo(withoutRateChange).Within(1));
        }

        [Test]
        public void TestAdjustedSettingsAreNotAffectedByHardRock()
        {
            var mod = new OsuModDifficultyAdjust
            {
                AdjustCircleSize = { Value = true },
                CircleSize = { Value = 4 },
                AdjustDrainRate = { Value = true },
                DrainRate = { Value = 5 },
                AdjustOverallDifficulty = { Value = true },
                OverallDifficulty = { Value = 6 },
                ApproachRate = { Value = 7 },
            };

            var difficulty = applyMods(new Mod[] { new OsuModHardRock(), mod });

            Assert.That(difficulty.CircleSize, Is.EqualTo(4));
            Assert.That(difficulty.DrainRate, Is.EqualTo(5));
            Assert.That(difficulty.OverallDifficulty, Is.EqualTo(6));
            Assert.That(difficulty.ApproachRate, Is.EqualTo(7));
        }

        [Test]
        public void TestSettingsWhichAreOffLeaveOtherModsAlone()
        {
            var withoutDifficultyAdjust = applyMods(new Mod[] { new OsuModHardRock() });

            // Values are set, but their adjustments are off, so hard rock should be all that applies.
            var mod = new OsuModDifficultyAdjust
            {
                CircleSize = { Value = 2 },
                DrainRate = { Value = 2 },
                OverallDifficulty = { Value = 2 },
                AdjustApproachRate = { Value = false },
                ApproachRate = { Value = 2 },
            };

            var difficulty = applyMods(new Mod[] { new OsuModHardRock(), mod });

            Assert.That(difficulty.CircleSize, Is.EqualTo(withoutDifficultyAdjust.CircleSize));
            Assert.That(difficulty.DrainRate, Is.EqualTo(withoutDifficultyAdjust.DrainRate));
            Assert.That(difficulty.OverallDifficulty, Is.EqualTo(withoutDifficultyAdjust.OverallDifficulty));
            Assert.That(difficulty.ApproachRate, Is.EqualTo(withoutDifficultyAdjust.ApproachRate));
        }

        [Test]
        public void TestApproachRateAdjustmentDoesNotTouchOtherSettings()
        {
            var mod = new OsuModDifficultyAdjust { ApproachRate = { Value = 10 } };

            var difficulty = applyMods(new Mod[] { new OsuModHardRock(), mod });

            Assert.That(difficulty.ApproachRate, Is.EqualTo(10));
            Assert.That(difficulty.CircleSize, Is.GreaterThan(5), "circle size should still have been raised by hard rock");
            Assert.That(difficulty.DrainRate, Is.GreaterThan(5), "drain rate should still have been raised by hard rock");
        }

        /// <returns>The time the player has to react to an object, in real time.</returns>
        private static double preemptWith(Mod[] mods, double rate)
        {
            var hitCircle = new HitCircle();
            hitCircle.ApplyDefaults(new ControlPointInfo(), applyMods(mods));

            return hitCircle.TimePreempt / rate;
        }

        /// <returns>The window in which a hit counts as a great, in real time.</returns>
        private static double greatWindowWith(Mod[] mods, double rate)
        {
            var hitWindows = new OsuHitWindows();
            hitWindows.SetDifficulty(applyMods(mods).OverallDifficulty);

            return hitWindows.WindowFor(HitResult.Great) / rate;
        }

        private static BeatmapDifficulty applyMods(Mod[] mods)
        {
            var difficulty = new BeatmapDifficulty { CircleSize = 5, DrainRate = 5, OverallDifficulty = 5, ApproachRate = 5 };

            foreach (var mod in mods)
            {
                if (mod is IApplicableToDifficulty applicable)
                    applicable.ApplyToDifficulty(difficulty);
            }

            foreach (var mod in mods)
            {
                if (mod is IApplicableToDifficultyWithMods applicable)
                    applicable.ApplyToDifficulty(mods, difficulty);
            }

            return difficulty;
        }
    }
}
