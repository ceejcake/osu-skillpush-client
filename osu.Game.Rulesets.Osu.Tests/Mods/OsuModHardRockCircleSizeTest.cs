// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.UI;
using osu.Game.Tests.Beatmaps;
using osu.Game.Utils;
using osuTK;

namespace osu.Game.Rulesets.Osu.Tests.Mods
{
    [TestFixture]
    public class OsuModHardRockCircleSizeTest
    {
        [Test]
        public void TestOnlyCircleSizeChanges()
        {
            var difficulty = new BeatmapDifficulty { CircleSize = 4, OverallDifficulty = 7, ApproachRate = 8, DrainRate = 5 };
            var hardRock = difficulty.Clone();

            new OsuModHardRockCircleSize().ApplyToDifficulty(difficulty);
            new OsuModHardRock().ApplyToDifficulty(hardRock);

            Assert.That(difficulty.CircleSize, Is.EqualTo(hardRock.CircleSize), "same circle size as Hard Rock");
            Assert.That(difficulty.OverallDifficulty, Is.EqualTo(7));
            Assert.That(difficulty.ApproachRate, Is.EqualTo(8));
            Assert.That(difficulty.DrainRate, Is.EqualTo(5));
        }

        [Test]
        public void TestCircleSizeCapped()
        {
            var difficulty = new BeatmapDifficulty { CircleSize = 9 };
            new OsuModHardRockCircleSize().ApplyToDifficulty(difficulty);

            Assert.That(difficulty.CircleSize, Is.EqualTo(10));
        }

        [Test]
        public void TestFlipsLikeHardRock()
        {
            var position = new Vector2(100, 50);

            var circle = new HitCircle { Position = position };
            var hardRockCircle = new HitCircle { Position = position };

            new OsuModHardRockCircleSize().ApplyToHitObject(circle);
            new OsuModHardRock().ApplyToHitObject(hardRockCircle);

            Assert.That(circle.Position, Is.EqualTo(hardRockCircle.Position));
            Assert.That(circle.Position, Is.EqualTo(new Vector2(100, OsuPlayfield.BASE_SIZE.Y - 50)));
        }

        [Test]
        public void TestIncompatibleWithHardRock()
        {
            Assert.That(ModUtils.CheckCompatibleSet(new Mod[] { new OsuModHardRock(), new OsuModHardRockCircleSize() }), Is.False);
            Assert.That(ModUtils.CheckCompatibleSet(new Mod[] { new OsuModMirror(), new OsuModHardRockCircleSize() }), Is.False, "both flip the map");
            Assert.That(ModUtils.CheckCompatibleSet(new Mod[] { new OsuModDoubleTime(), new OsuModHardRockCircleSize() }), Is.True);
        }

        [Test]
        public void TestLastInDifficultyIncrease()
        {
            var mods = new OsuRuleset().GetModsFor(ModType.DifficultyIncrease).ToArray();
            Assert.That(mods.Last(), Is.TypeOf<OsuModHardRockCircleSize>());
        }

        [Test]
        public void TestNotTreatedAsHardRock()
        {
            Assert.That(new OsuModHardRockCircleSize(), Is.Not.InstanceOf<ModHardRock>());
            Assert.That(new OsuModHardRockCircleSize(), Is.InstanceOf<IHasUpsideDownIcon>());
            Assert.That(new OsuModHardRockCircleSize().Icon, Is.EqualTo(new OsuModHardRock().Icon));
        }

        [Test]
        public void TestStarRatingIncreases()
        {
            var adjustmentMods = new OsuRuleset().CreateDifficultyCalculator(new TestWorkingBeatmap(new Beatmap())).CreateDifficultyAdjustmentModCombinations();
            Assert.That(adjustmentMods.SelectMany(m => m is MultiMod multi ? multi.Mods : new[] { m }).Any(m => m is OsuModHardRockCircleSize), Is.True);
        }
    }
}
