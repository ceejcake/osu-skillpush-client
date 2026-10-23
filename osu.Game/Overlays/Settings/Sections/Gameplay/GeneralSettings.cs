// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Overlays.Settings.Sections.Gameplay
{
    public partial class GeneralSettings : SettingsSubsection
    {
        protected override LocalisableString Header => CommonStrings.General;

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            Children = new Drawable[]
            {
                new SettingsItemV2(new FormEnumDropdown<ScoringMode>
                {
                    Caption = GameplaySettingsStrings.ScoreDisplayMode,
                    Current = config.GetBindable<ScoringMode>(OsuSetting.ScoreDisplayMode),
                })
                {
                    Keywords = new[] { "scoring" },
                    ApplyClassicDefault = c => ((IHasCurrentValue<ScoringMode>)c).Current.Value = ScoringMode.Classic,
                },
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Use Score V2 by default",
                    HintText = "Score every play the way osu!stable's Score V2 did, without selecting the mod. The mod menu then offers \"Lazer Scoring\" to score a play the usual way instead. Past scores are left as they are.",
                    Current = config.GetBindable<bool>(OsuSetting.ScoreV2ByDefault),
                })
                {
                    Keywords = new[] { "scoring", "score v2", "scorev2", "lazer" },
                },
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = GraphicsSettingsStrings.HitLighting,
                    Current = config.GetBindable<bool>(OsuSetting.HitLighting)
                }),
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = GameplaySettingsStrings.HitErrorJudgements,
                    HintText = GameplaySettingsStrings.HitErrorJudgementsDescription,
                    Current = config.GetBindable<bool>(OsuSetting.HitErrorJudgements)
                })
                {
                    Keywords = new[] { "unstable rate", "timing", "ms", "early", "late" },
                },
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Misaim indicator",
                    HintText = "When a press misses its note because the cursor was off it, show where it landed and whether it was over or under aimed. Toggle during gameplay with Ctrl+M (changeable in key bindings).",
                    Current = config.GetBindable<bool>(OsuSetting.MisaimIndicator)
                })
                {
                    Keywords = new[] { "aim", "misaim", "overaim", "underaim", "feedback", "mcosu" },
                },
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Misaim indicator details",
                    HintText = "Also show the missed note, a line to where you pressed, and how far over or under you aimed. Toggle during gameplay with Ctrl+Shift+M.",
                    Current = config.GetBindable<bool>(OsuSetting.MisaimIndicatorDetails)
                })
                {
                    Keywords = new[] { "aim", "misaim", "overaim", "underaim", "feedback", "analysis" },
                },
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = GameplaySettingsStrings.StarFountains,
                    Current = config.GetBindable<bool>(OsuSetting.StarFountains)
                }),
            };
        }
    }
}
