// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Timing;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Screens.Play;
using osuTK;
using osuTK.Input;

namespace osu.Game.Tests.Visual.Gameplay
{
    public partial class TestSceneBreakSkip : PlayerTestScene
    {
        private const double break_start = 4000;
        private const double break_end = 39000;

        protected override Ruleset CreatePlayerRuleset() => new OsuRuleset();

        protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
        {
            var beatmap = new Beatmap
            {
                BeatmapInfo = { Ruleset = ruleset },
            };

            for (int i = 0; i < 4; i++)
                beatmap.HitObjects.Add(new HitCircle { StartTime = 1000 + i * 500, Position = new Vector2(256, 192) });

            beatmap.HitObjects.Add(new HitCircle { StartTime = break_end + 1000, Position = new Vector2(256, 192) });
            beatmap.HitObjects.Add(new HitCircle { StartTime = break_end + 1500, Position = new Vector2(256, 192) });

            beatmap.Breaks.Add(new BreakPeriod(break_start, break_end));

            return beatmap;
        }

        [Test]
        public void TestSkipSkipsMostOfTheBreak()
        {
            AddUntilStep("wait for break", () => Player.GameplayClockContainer.CurrentTime > break_start + 500 && Player.IsBreakTime.Value);
            AddUntilStep("skip overlay available", () => Player.ChildrenOfType<SkipOverlay>().Any(o => o.IsPresent));

            AddStep("press skip", () => InputManager.Key(Key.Space));

            AddUntilStep("skipped to near the end of the break", () => Player.GameplayClockContainer.CurrentTime, () => Is.GreaterThan(break_end - 4000));
            AddAssert("did not skip into gameplay", () => Player.GameplayClockContainer.CurrentTime, () => Is.LessThan(break_end - 500));
            AddAssert("still in break", () => Player.IsBreakTime.Value);
            AddAssert("play not invalidated", () => !Player.GameplayInvalidatedBySeeking);

            AddUntilStep("gameplay resumes", () => !Player.IsBreakTime.Value);
        }

        [Test]
        public void TestSkipDoesNothingLateInBreak()
        {
            AddUntilStep("wait for break", () => Player.IsBreakTime.Value && Player.GameplayClockContainer.CurrentTime > break_start + 500);

            AddStep("press skip", () => InputManager.Key(Key.Space));
            AddUntilStep("skipped once", () => Player.GameplayClockContainer.CurrentTime > break_end - 4000);

            double timeAfterFirstSkip = 0;

            AddStep("press skip again", () =>
            {
                timeAfterFirstSkip = Player.GameplayClockContainer.CurrentTime;
                InputManager.Key(Key.Space);
            });

            AddAssert("time not moved forward again", () => Player.GameplayClockContainer.CurrentTime, () => Is.LessThan(timeAfterFirstSkip + 1000));
        }
    }
}
