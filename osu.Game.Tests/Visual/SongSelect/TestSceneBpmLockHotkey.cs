// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Screens.Select;
using osu.Game.Tests.Resources;
using osuTK.Input;

namespace osu.Game.Tests.Visual.SongSelect
{
    public partial class TestSceneBpmLockHotkey : SongSelectTestScene
    {
        private BpmLockPromptOverlay prompt => SongSelect.ChildrenOfType<BpmLockPromptOverlay>().Single();

        private OsuNumberBox numberBox => prompt.ChildrenOfType<OsuNumberBox>().Single();

        private ModRateAdjust selectedRateMod => SelectedMods.Value.OfType<ModRateAdjust>().Single();

        [SetUpSteps]
        public override void SetUpSteps()
        {
            AddStep("add beatmaps", () => Beatmaps.Import(TestResources.CreateTestBeatmapSetInfo()));
            LoadSongSelect();
            AddStep("clear mods", () => SelectedMods.Value = []);
        }

        [Test]
        public void TestSetBpmLockAddsDoubleTime()
        {
            AddStep("press F4", () => InputManager.Key(Key.F4));
            AddUntilStep("prompt shown", () => prompt.State.Value == Visibility.Visible);
            AddUntilStep("text box focused", () => numberBox.HasFocus);

            AddStep("enter 210", () =>
            {
                numberBox.Text = "210";
                InputManager.Key(Key.Enter);
            });

            AddUntilStep("prompt hidden", () => prompt.State.Value == Visibility.Hidden);
            AddUntilStep("double time selected", () => SelectedMods.Value.OfType<ModDoubleTime>().Any());
            AddAssert("bpm lock enabled", () => selectedRateMod.LockBpm.Value);
            AddAssert("target bpm applied", () => selectedRateMod.TargetBpm.Value, () => Is.EqualTo(210));
        }

        [Test]
        public void TestSetBpmLockOnExistingMod()
        {
            AddStep("select half time", () => SelectedMods.Value = [new OsuModHalfTime()]);

            AddStep("press F4", () => InputManager.Key(Key.F4));
            AddUntilStep("prompt shown", () => prompt.State.Value == Visibility.Visible);
            AddUntilStep("text box focused", () => numberBox.HasFocus);

            AddStep("enter 90", () =>
            {
                numberBox.Text = "90";
                InputManager.Key(Key.Enter);
            });

            AddUntilStep("prompt hidden", () => prompt.State.Value == Visibility.Hidden);
            AddAssert("half time still selected", () => SelectedMods.Value.OfType<ModHalfTime>().Any());
            AddAssert("bpm lock enabled", () => selectedRateMod.LockBpm.Value);
            AddAssert("target bpm applied", () => selectedRateMod.TargetBpm.Value, () => Is.EqualTo(90));
        }

        [Test]
        public void TestEmptyInputRemovesLock()
        {
            AddStep("select locked double time", () => SelectedMods.Value = [new OsuModDoubleTime { LockBpm = { Value = true }, TargetBpm = { Value = 200 } }]);

            AddStep("press F4", () => InputManager.Key(Key.F4));
            AddUntilStep("prompt shown", () => prompt.State.Value == Visibility.Visible);
            AddAssert("prompt populated with current bpm", () => numberBox.Text, () => Is.EqualTo("200"));

            AddStep("clear and commit", () =>
            {
                numberBox.Text = string.Empty;
                InputManager.Key(Key.Enter);
            });

            AddUntilStep("prompt hidden", () => prompt.State.Value == Visibility.Hidden);
            AddAssert("double time still selected", () => SelectedMods.Value.OfType<ModDoubleTime>().Any());
            AddAssert("bpm lock removed", () => !selectedRateMod.LockBpm.Value);
        }

        [Test]
        public void TestPromptDismissedWithoutChanges()
        {
            AddStep("select half time", () => SelectedMods.Value = [new OsuModHalfTime()]);

            AddStep("press F4", () => InputManager.Key(Key.F4));
            AddUntilStep("prompt shown", () => prompt.State.Value == Visibility.Visible);

            AddStep("press escape", () => InputManager.Key(Key.Escape));

            AddUntilStep("prompt hidden", () => prompt.State.Value == Visibility.Hidden);
            AddAssert("still at song select", () => Stack.CurrentScreen == SongSelect);
            AddAssert("bpm lock untouched", () => !selectedRateMod.LockBpm.Value);
        }
    }
}
