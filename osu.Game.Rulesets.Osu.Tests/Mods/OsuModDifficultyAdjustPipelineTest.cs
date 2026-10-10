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
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Tests.Beatmaps;
using osu.Game.Utils;

namespace osu.Game.Rulesets.Osu.Tests.Mods
{
    /// <summary>
    /// Checks that an adjusted approach rate survives the whole beatmap pipeline, whatever else is applied to it.
    /// </summary>
    [TestFixture]
    public class OsuModDifficultyAdjustPipelineTest : DifficultyCalculatorTest
    {
        /// <summary>
        /// The time an object is visible for at AR 10.
        /// </summary>
        private const double approach_rate_10_preempt = 450;

        protected override string ResourceAssembly => "osu.Game.Rulesets.Osu.Tests";

        private static readonly object[][] mod_combinations =
        {
            new object[] { "on its own", new Func<Mod[]>(() => new Mod[] { approachRate10() }) },
            new object[] { "with double time", new Func<Mod[]>(() => new Mod[] { approachRate10(), new OsuModDoubleTime() }) },
            new object[] { "with half time", new Func<Mod[]>(() => new Mod[] { approachRate10(), new OsuModHalfTime() }) },
            new object[] { "with hard rock", new Func<Mod[]>(() => new Mod[] { approachRate10(), new OsuModHardRock() }) },
            new object[] { "with easy", new Func<Mod[]>(() => new Mod[] { approachRate10(), new OsuModEasy() }) },
            new object[]
            {
                "with a bpm locked double time",
                new Func<Mod[]>(() => new Mod[] { approachRate10(), new OsuModDoubleTime { LockBpm = { Value = true }, TargetBpm = { Value = 220 } } })
            },
            new object[]
            {
                "with a bpm locked nightcore and hard rock",
                new Func<Mod[]>(() => new Mod[] { approachRate10(), new OsuModHardRock(), new OsuModNightcore { LockBpm = { Value = true }, TargetBpm = { Value = 260 } } })
            },
            new object[]
            {
                "with a bpm locked half time",
                new Func<Mod[]>(() => new Mod[] { approachRate10(), new OsuModHalfTime { LockBpm = { Value = true }, TargetBpm = { Value = 90 } } })
            },
        };

        [TestCaseSource(nameof(mod_combinations))]
        public void TestApproachRateIsPlayedAsSet(string description, Func<Mod[]> createMods)
        {
            var working = GetBeatmap("diffcalc-test");

            var mods = createMods();
            var playable = working.GetPlayableBeatmap(new OsuRuleset().RulesetInfo, mods);

            // The rate has to be read after the beatmap has been prepared, as a bpm lock only settles at that point.
            double rate = ModUtils.CalculateRateWithMods(mods);
            double reactionTime = playable.HitObjects.OfType<HitCircle>().First().TimePreempt / rate;

            Assert.That(reactionTime, Is.EqualTo(approach_rate_10_preempt).Within(1), $"approach rate was not held {description}");

            // What the menus show has to agree with what is actually played.
            var displayed = new OsuRuleset().GetAdjustedDisplayDifficulty(working.BeatmapInfo, createMods());

            Assert.That(displayed.ApproachRate, Is.EqualTo(10).Within(0.05), $"displayed approach rate was wrong {description}");
        }

        private static OsuModDifficultyAdjust approachRate10() => new OsuModDifficultyAdjust { ApproachRate = { Value = 10 } };

        protected override DifficultyCalculator CreateDifficultyCalculator(IWorkingBeatmap beatmap) => new OsuDifficultyCalculator(new OsuRuleset().RulesetInfo, beatmap);

        protected override Ruleset CreateRuleset() => new OsuRuleset();
    }
}
