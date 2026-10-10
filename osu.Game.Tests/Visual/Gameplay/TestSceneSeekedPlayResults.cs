// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking;
using osuTK;
using osuTK.Input;

namespace osu.Game.Tests.Visual.Gameplay
{
    public partial class TestSceneSeekedPlayResults : PlayerTestScene
    {
        private ImportTrackingPlayer trackingPlayer => (ImportTrackingPlayer)Player;

        protected override Ruleset CreatePlayerRuleset() => new OsuRuleset();

        protected override TestPlayer CreatePlayer(Ruleset ruleset) => new ImportTrackingPlayer();

        protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
        {
            var beatmap = new Beatmap { BeatmapInfo = { Ruleset = ruleset } };

            for (int i = 0; i < 10; i++)
                beatmap.HitObjects.Add(new HitCircle { StartTime = 1000 + i * 500, Position = new Vector2(256, 192) });

            return beatmap;
        }

        [Test]
        public void TestSeekedPlayShowsResultsWithoutSaving()
        {
            AddUntilStep("gameplay running", () => !Player.GameplayClockContainer.IsPaused.Value && Player.GameplayClockContainer.CurrentTime > 0);

            AddStep("seek forward", () => InputManager.Key(Key.Right));
            AddUntilStep("play invalidated", () => Player.GameplayInvalidatedBySeeking);

            AddUntilStep("results shown", () => Stack.CurrentScreen is ResultsScreen);
            AddAssert("score was not saved locally", () => !trackingPlayer.ScoreImported);
            AddAssert("score was not submitted", () => trackingPlayer.SubmittedScore == null);
        }

        [Test]
        public void TestNormalPlayStillSavesScore()
        {
            AddUntilStep("gameplay running", () => !Player.GameplayClockContainer.IsPaused.Value && Player.GameplayClockContainer.CurrentTime > 0);

            AddUntilStep("results shown", () => Stack.CurrentScreen is ResultsScreen);
            AddAssert("play not invalidated", () => !Player.GameplayInvalidatedBySeeking);
            AddAssert("score was saved locally", () => trackingPlayer.ScoreImported);
        }

        private partial class ImportTrackingPlayer : TestPlayer
        {
            public bool ScoreImported { get; private set; }

            public ImportTrackingPlayer()
                : base(false, true)
            {
            }

            protected override Task ImportScore(Score score)
            {
                ScoreImported = true;
                return base.ImportScore(score);
            }
        }
    }
}
