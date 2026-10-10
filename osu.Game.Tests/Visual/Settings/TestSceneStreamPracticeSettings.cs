// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Testing;
using osu.Game.Beatmaps.StreamPractice;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osu.Game.Overlays.Settings.Sections.UserInterface;

namespace osu.Game.Tests.Visual.Settings
{
    public partial class TestSceneStreamPracticeSettings : OsuTestScene
    {
        [Cached]
        private readonly OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Purple);

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("reset", () => config.SetValue(OsuSetting.StreamPracticePattern, StreamPracticePattern.Continuous));

            // Settings are hidden by the search container they're shown in, as in the settings panel.
            AddStep("create settings", () => Child = new PopoverContainer
            {
                RelativeSizeAxes = Axes.Both,
                Child = new SearchContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Width = 400,
                    AutoSizeAxes = Axes.Y,
                    Child = new StreamPracticeSettings(),
                },
            });
        }

        [Test]
        public void TestBurstLengthOnlyShownForBursts()
        {
            AddAssert("continuous by default", () => config.GetBindable<StreamPracticePattern>(OsuSetting.StreamPracticePattern).Default, () => Is.EqualTo(StreamPracticePattern.Continuous));
            AddUntilStep("burst length hidden", () => burstLength().IsPresent, () => Is.False);

            AddStep("choose bursts", () => config.SetValue(OsuSetting.StreamPracticePattern, StreamPracticePattern.Bursts));
            AddUntilStep("burst length shown", () => burstLength().IsPresent);
        }

        [Test]
        public void TestBurstLengthsOnOffer()
        {
            AddAssert("powers of two from 8", () => this.ChildrenOfType<FormDropdown<int>>().Single().Items, () => Is.EqualTo(new[] { 8, 16, 32, 64, 128, 256 }));
            AddAssert("16 by default", () => config.GetBindable<int>(OsuSetting.StreamPracticeBurstLength).Default, () => Is.EqualTo(16));
        }

        private SettingsItemV2 burstLength() => this.ChildrenOfType<SettingsItemV2>().Single(i => i.Control is FormDropdown<int>);
    }
}
