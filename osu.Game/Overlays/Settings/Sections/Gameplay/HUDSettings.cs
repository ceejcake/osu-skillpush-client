// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;

namespace osu.Game.Overlays.Settings.Sections.Gameplay
{
    public partial class HUDSettings : SettingsSubsection
    {
        protected override LocalisableString Header => GameplaySettingsStrings.HUDHeader;

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            Children = new Drawable[]
            {
                new SettingsItemV2(new FormEnumDropdown<HUDVisibilityMode>
                {
                    Caption = GameplaySettingsStrings.HUDVisibilityMode,
                    Current = config.GetBindable<HUDVisibilityMode>(OsuSetting.HUDVisibilityMode)
                }),
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = GameplaySettingsStrings.ShowReplaySettingsOverlay,
                    Current = config.GetBindable<bool>(OsuSetting.ReplaySettingsOverlay),
                })
                {
                    Keywords = new[] { "hide" },
                },
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = GameplaySettingsStrings.AlwaysShowKeyOverlay,
                    Current = config.GetBindable<bool>(OsuSetting.KeyOverlay),
                })
                {
                    Keywords = new[] { "counter" },
                },
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = GameplaySettingsStrings.AlwaysShowGameplayLeaderboard,
                    Current = config.GetBindable<bool>(OsuSetting.GameplayLeaderboard),
                }),
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = GameplaySettingsStrings.AlwaysRequireHoldForMenu,
                    Current = config.GetBindable<bool>(OsuSetting.AlwaysRequireHoldingForPause),
                }),
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = GameplaySettingsStrings.AlwaysShowHoldForMenuButton,
                    Current = config.GetBindable<bool>(OsuSetting.AlwaysShowHoldForMenuButton),
                }),
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = GameplaySettingsStrings.ShowHealthDisplayWhenCantFail,
                    Current = config.GetBindable<bool>(OsuSetting.ShowHealthDisplayWhenCantFail),
                })
                {
                    Keywords = new[] { "hp", "bar" },
                    ApplyClassicDefault = c => ((IHasCurrentValue<bool>)c).Current.Value = false,
                },
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Override hit error bar size",
                    HintText = "Use the size below for bar hit error meters on every skin, ignoring the skin's own scale.",
                    Current = config.GetBindable<bool>(OsuSetting.HitErrorBarSizeOverride),
                })
                {
                    Keywords = new[] { "hit error", "meter", "scale", "unstable rate" },
                },
                new SettingsItemV2(new FormSliderBar<float>
                {
                    Caption = "Hit error bar size",
                    Current = config.GetBindable<float>(OsuSetting.HitErrorBarSize),
                    KeyboardStep = 0.05f,
                    DisplayAsPercentage = true,
                })
                {
                    Keywords = new[] { "hit error", "meter", "scale", "unstable rate" },
                },
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Show missed presses on hit error bar",
                    HintText = "Show presses which didn't hit their note (because the cursor was off it, or the press was too early) on bar hit error meters as red lines, where they would have landed.",
                    Current = config.GetBindable<bool>(OsuSetting.HitErrorShowPressedMisses),
                })
                {
                    Keywords = new[] { "hit error", "meter", "miss", "unstable rate", "mcosu" },
                },
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Glow when aim causes misses",
                    HintText = "Make the sides of the screen glow when you start missing because of aim: red when your cursor gets to circles too late, blue when it has already left them by the time you click. Overshooting a circle at the right time doesn't count. The more misses in a row, the stronger the glow.",
                    Current = config.GetBindable<bool>(OsuSetting.AimFeedbackGlow),
                })
                {
                    Keywords = new[] { "aim", "lag", "overaim", "underaim", "glow", "feedback", "miss" },
                },
            };
        }
    }
}
