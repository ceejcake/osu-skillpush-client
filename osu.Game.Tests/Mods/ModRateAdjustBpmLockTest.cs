// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Online.API;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Utils;

namespace osu.Game.Tests.Mods
{
    [TestFixture]
    public class ModRateAdjustBpmLockTest
    {
        private static readonly Func<ModRateAdjust>[] all_rate_mods =
        {
            () => new OsuModDoubleTime(),
            () => new OsuModNightcore(),
            () => new OsuModHalfTime(),
            () => new OsuModDaycore(),
        };

        /// <summary>
        /// Whichever mod the lock is on, it reaches the target from either side.
        /// </summary>
        [TestCase(120, 180, 1.5)] // faster
        [TestCase(220, 180, 0.8182)] // slower
        [TestCase(180, 180, 1.0)] // already at the target
        [TestCase(200, 240, 1.2)]
        [TestCase(150, 300, 2.0)] // as fast as a lock goes
        [TestCase(400, 120, 0.5)] // as slow as a lock goes
        [TestCase(500, 120, 0.5)] // too far: as close as possible
        public void TestEveryRateModReachesTargetBpm(double beatmapBpm, int targetBpm, double expectedRate)
        {
            foreach (var createMod in all_rate_mods)
            {
                var mod = createMod();
                mod.LockBpm.Value = true;
                mod.TargetBpm.Value = targetBpm;

                mod.ApplyToBeatmap(createBeatmap(beatmapBpm));

                Assert.That(mod.SpeedChange.Value, Is.EqualTo(expectedRate).Within(0.0001), $"{mod.Acronym} speed");
                Assert.That(ModUtils.CalculateRateWithMods(new[] { mod }), Is.EqualTo(expectedRate).Within(0.0001), $"{mod.Acronym} rate");
            }
        }

        [Test]
        public void TestLandsOnTheTarget()
        {
            // 0.818181...x, which the usual 0.01x steps of the speed setting would round to 0.82x and so 180.4 BPM.
            var mod = new OsuModDoubleTime { LockBpm = { Value = true }, TargetBpm = { Value = 180 } };

            mod.ApplyToBeatmap(createBeatmap(220));

            Assert.That(220 * mod.SpeedChange.Value, Is.EqualTo(180).Within(0.05));
        }

        [Test]
        public void TestRateIsUntouchedWhenLockDisabled()
        {
            var mod = new OsuModDoubleTime { SpeedChange = { Value = 1.4 } };

            mod.ApplyToBeatmap(createBeatmap(120));

            Assert.That(mod.SpeedChange.Value, Is.EqualTo(1.4));
            Assert.That(mod.SpeedChange.MinValue, Is.EqualTo(1.01), "range changed");
        }

        [Test]
        public void TestSameModAdaptsToEachBeatmap()
        {
            var mod = new OsuModNightcore
            {
                LockBpm = { Value = true },
                TargetBpm = { Value = 200 },
            };

            mod.ApplyToBeatmap(createBeatmap(160));
            Assert.That(mod.SpeedChange.Value, Is.EqualTo(1.25).Within(0.0001));

            mod.ApplyToBeatmap(createBeatmap(250));
            Assert.That(mod.SpeedChange.Value, Is.EqualTo(0.8).Within(0.0001));

            mod.ApplyToBeatmap(createBeatmap(125));
            Assert.That(mod.SpeedChange.Value, Is.EqualTo(1.6).Within(0.0001));
        }

        [Test]
        public void TestLockedRateSurvivesBeingStored()
        {
            // As a score's mods are stored, and recreated when it's looked at again (to work out its pp, for one). The speed
            // is restored before the lock, so mustn't be limited to the range the mod has without one.
            foreach (var (createMod, beatmapBpm) in new (Func<ModRateAdjust>, double)[]
                     {
                         (() => new OsuModDoubleTime(), 220),
                         (() => new OsuModHalfTime(), 120),
                     })
            {
                var played = createMod();
                played.LockBpm.Value = true;
                played.TargetBpm.Value = 180;
                played.ApplyToBeatmap(createBeatmap(beatmapBpm));

                var restored = new APIMod(played).ToMod(new OsuRuleset());

                Assert.That(ModUtils.CalculateRateWithMods(new[] { restored }), Is.EqualTo(180 / beatmapBpm).Within(0.0001), $"{played.Acronym} rate");
            }
        }

        [Test]
        public void TestUnlockedRateStaysInItsRangeWhenStored()
        {
            var restored = (ModRateAdjust)new APIMod(new OsuModDoubleTime { SpeedChange = { Value = 1.3 } }).ToMod(new OsuRuleset());

            Assert.That(restored.SpeedChange.Value, Is.EqualTo(1.3));
            Assert.That(restored.SpeedChange.MinValue, Is.EqualTo(1.01), "range changed");
        }

        [Test]
        public void TestSettlingForDisplayLeavesSelectedModAlone()
        {
            var selected = new OsuModDoubleTime { LockBpm = { Value = true }, TargetBpm = { Value = 180 } };
            var mods = new Mod[] { selected, new OsuModHidden() };

            var settled = ModUtils.WithBpmLockApplied(mods, 220);

            Assert.That(ModUtils.CalculateRateWithMods(settled), Is.EqualTo(180 / 220.0).Within(0.0001));
            Assert.That(settled.OfType<OsuModHidden>().Single(), Is.SameAs(mods[1]), "other mods copied needlessly");

            // The one in the mod select overlay keeps its slider's usual range and value.
            Assert.That(selected.SpeedChange.Value, Is.EqualTo(1.5));
            Assert.That(selected.SpeedChange.MinValue, Is.EqualTo(1.01));
        }

        [Test]
        public void TestSettlingWithoutLockChangesNothing()
        {
            var mods = new Mod[] { new OsuModDoubleTime() };

            Assert.That(ModUtils.WithBpmLockApplied(mods, 220), Is.SameAs(mods));
        }

        [Test]
        public void TestDisplayedDifficultyFollowsLockedRate()
        {
            // AR8 at 220 BPM, locked to 180, is played slower than written, so is shown lower than 8. Before settling the
            // lock, the double time's untouched 1.5x was used instead, showing it higher.
            var beatmapInfo = new BeatmapInfo { BPM = 220, Difficulty = new BeatmapDifficulty { ApproachRate = 8 } };
            var mods = new Mod[] { new OsuModDoubleTime { LockBpm = { Value = true }, TargetBpm = { Value = 180 } } };

            var displayed = new OsuRuleset().GetAdjustedDisplayDifficulty(beatmapInfo, mods);

            Assert.That(displayed.ApproachRate, Is.LessThan(8));
        }

        [Test]
        public void TestLockedModIsNotRanked()
        {
            var mod = new OsuModDoubleTime();
            Assert.That(mod.Ranked, Is.True);

            mod.LockBpm.Value = true;
            Assert.That(mod.Ranked, Is.False);
        }

        private static IBeatmap createBeatmap(double bpm)
        {
            var beatmap = new Beatmap
            {
                BeatmapInfo = { Ruleset = new OsuRuleset().RulesetInfo },
                HitObjects = new List<HitObject>
                {
                    new HitCircle { StartTime = 0 },
                    new HitCircle { StartTime = 10000 },
                }
            };

            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 60000 / bpm });

            return beatmap;
        }
    }
}
