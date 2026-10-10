// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osuTK.Graphics;

namespace osu.Game.Tests.Visual.Gameplay
{
    /// <summary>
    /// Covers the setting which shows how early or late a 100 or 50 was, in place of the judgement.
    /// </summary>
    public partial class TestSceneHitErrorJudgements : OsuTestScene
    {
        /// <summary>
        /// Matches the darkening applied to a 50.
        /// </summary>
        private const float meh_darkening = 0.4f;

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        private readonly OsuColour colours = new OsuColour();

        private JudgementPooler<DrawableOsuJudgement> pooler = null!;
        private Container container = null!;

        private HitCircle hitCircle = null!;

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("turn the setting on", () => config.SetValue(OsuSetting.HitErrorJudgements, true));

            AddStep("create pool", () =>
            {
                // The same pooling gameplay uses, which prepares a drawable for each result type up front.
                pooler = new JudgementPooler<DrawableOsuJudgement>(new[] { HitResult.Great, HitResult.Ok, HitResult.Meh });

                Child = container = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = pooler,
                };
            });
        }

        [Test]
        public void TestEarlyHitIsGreenAndLateHitIsRed()
        {
            showJudgement(HitResult.Ok, -37);
            AddAssert("early offset shown", () => displayedText(), () => Is.EqualTo("-37"));
            AddAssert("shown in green", () => displayedColour(), () => Is.EqualTo(colours.Green1));

            showJudgement(HitResult.Ok, 23);
            AddAssert("late offset shown", () => displayedText(), () => Is.EqualTo("+23"));
            AddAssert("shown in red", () => displayedColour(), () => Is.EqualTo(colours.Red1));
        }

        [Test]
        public void TestNumbersUseANarrowFont()
        {
            showJudgement(HitResult.Ok, -137);

            // The judgement font is too wide for three digits and a sign.
            AddAssert("narrow font used", () => judgement.ChildrenOfType<SpriteText>().Single().Font.Family, () => Is.EqualTo("Torus"));
        }

        [Test]
        public void TestFiftiesAreDarker()
        {
            showJudgement(HitResult.Meh, -68);
            AddAssert("offset shown", () => displayedText(), () => Is.EqualTo("-68"));
            AddAssert("shown in darker green", () => displayedColour(), () => Is.EqualTo(colours.Green1.Darken(meh_darkening)));

            showJudgement(HitResult.Meh, 68);
            AddAssert("shown in darker red", () => displayedColour(), () => Is.EqualTo(colours.Red1.Darken(meh_darkening)));
        }

        [Test]
        public void TestReusedJudgementShowsTheNewOffset()
        {
            // Judgements are pooled, so the second one displayed is usually the same drawable as the first.
            showJudgement(HitResult.Ok, 23);
            AddAssert("first offset shown", () => displayedText(), () => Is.EqualTo("+23"));

            showJudgement(HitResult.Ok, -5);
            AddAssert("second offset shown", () => displayedText(), () => Is.EqualTo("-5"));
        }

        [Test]
        public void TestThreeHundredsAreLeftAlone()
        {
            showJudgement(HitResult.Great, 12);
            AddAssert("judgement shown as usual", () => displayedText(), () => Is.EqualTo("GREAT"));
        }

        [Test]
        public void TestSettingTurnedOffShowsJudgement()
        {
            AddStep("turn the setting off", () => config.SetValue(OsuSetting.HitErrorJudgements, false));

            showJudgement(HitResult.Ok, 23);
            AddAssert("judgement shown as usual", () => displayedText(), () => Is.EqualTo("OK"));
        }

        [Test]
        public void TestTogglingReachesJudgementsAlreadyCreated()
        {
            AddStep("turn the setting off", () => config.SetValue(OsuSetting.HitErrorJudgements, false));

            showJudgement(HitResult.Ok, 23);
            AddAssert("judgement shown as usual", () => displayedText(), () => Is.EqualTo("OK"));

            AddStep("turn the setting on", () => config.SetValue(OsuSetting.HitErrorJudgements, true));

            showJudgement(HitResult.Ok, 23);
            AddAssert("offset shown", () => displayedText(), () => Is.EqualTo("+23"));
        }

        [Test]
        public void TestFadesLikeTheJudgementItReplaces()
        {
            double withOffset = 0;
            double withJudgement = 0;

            showJudgement(HitResult.Ok, 23);
            AddStep("store fade", () => withOffset = fadeEndTime());

            AddStep("turn the setting off", () => config.SetValue(OsuSetting.HitErrorJudgements, false));

            showJudgement(HitResult.Ok, 23);
            AddStep("store fade", () => withJudgement = fadeEndTime());

            AddAssert("fades the same", () => withOffset, () => Is.EqualTo(withJudgement).Within(1));
        }

        private void showJudgement(HitResult type, double timeOffset)
        {
            AddStep($"show {type} at {timeOffset}ms", () =>
            {
                // Judged now, so that the judgement is alive for the rest of the test.
                hitCircle = new HitCircle { StartTime = Time.Current };
                hitCircle.ApplyDefaults(new ControlPointInfo(), new BeatmapDifficulty());

                var result = new JudgementResult(hitCircle, new Judgement())
                {
                    Type = type,
                    TimeOffset = timeOffset,
                };

                container.Add(pooler.Get(type, judgement => judgement.Apply(result, null))!.With(judgement =>
                {
                    judgement.Anchor = Anchor.Centre;
                    judgement.Origin = Anchor.Centre;
                }));
            });

            AddUntilStep("judgement shown", () => this.ChildrenOfType<DrawableOsuJudgement>().Any(j => j.Result?.Type == type));
        }

        private DrawableOsuJudgement judgement => this.ChildrenOfType<DrawableOsuJudgement>().Last();

        private string displayedText() => judgement.ChildrenOfType<SpriteText>().Single().Text.ToString();

        private Color4 displayedColour() => judgement.ChildrenOfType<SpriteText>().Single().Colour.TopLeft.SRGB;

        /// <summary>
        /// How long the judgement takes to disappear.
        /// </summary>
        private double fadeEndTime()
        {
            var text = judgement.ChildrenOfType<SpriteText>().Single();

            return text.LatestTransformEndTime - judgement.Result!.TimeAbsolute;
        }
    }
}
