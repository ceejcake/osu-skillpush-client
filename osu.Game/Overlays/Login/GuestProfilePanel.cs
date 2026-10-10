// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Leaderboards;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osu.Game.Users.Drawables;
using osu.Game.Utils;
using osuTK;

namespace osu.Game.Overlays.Login
{
    /// <summary>
    /// Shown in place of the login form while logged out: the local player's name, and the performance they would have
    /// online based on their local scores.
    /// </summary>
    public partial class GuestProfilePanel : FillFlowContainer
    {
        /// <summary>
        /// Called to request a hide of a parent displaying this container.
        /// </summary>
        public Action? RequestHide;

        /// <summary>
        /// Called when the player asks to sign in with an osu! account instead.
        /// </summary>
        public Action? RequestLogin;

        private const float top_plays_max_height = 300;

        private const float top_play_height = 44;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private LocalPerformanceTracker performanceTracker { get; set; } = null!;

        [Resolved]
        private IBindable<RulesetInfo> ruleset { get; set; } = null!;

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        private readonly IBindable<APIUser> localUser = new Bindable<APIUser>();
        private readonly IBindable<LocalPerformanceSummary> summary = new Bindable<LocalPerformanceSummary>();
        private readonly Bindable<bool> includeUnranked = new Bindable<bool>();

        private UpdateableAvatar avatar = null!;
        private OsuSpriteText usernameText = null!;
        private OsuSpriteText rulesetText = null!;

        private OsuSpriteText performanceText = null!;
        private OsuSpriteText accuracyText = null!;
        private OsuSpriteText countText = null!;
        private OsuSpriteText pendingText = null!;
        private OsuTextFlowContainer rulesText = null!;

        private OsuScrollContainer topPlaysScroll = null!;
        private FillFlowContainer topPlaysFlow = null!;
        private OsuTextFlowContainer emptyText = null!;

        public GuestProfilePanel()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            Direction = FillDirection.Vertical;
            Spacing = new Vector2(0, SettingsSection.ITEM_SPACING);
        }

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            config.BindWith(OsuSetting.GuestPerformanceIncludesUnranked, includeUnranked);

            Children = new Drawable[]
            {
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Horizontal,
                    Padding = new MarginPadding { Horizontal = SettingsPanel.CONTENT_MARGINS },
                    Spacing = new Vector2(12, 0),
                    Children = new Drawable[]
                    {
                        new Container
                        {
                            Size = new Vector2(56),
                            Masking = true,
                            CornerRadius = 8,
                            Child = avatar = new UpdateableAvatar(isInteractive: false)
                            {
                                RelativeSizeAxes = Axes.Both,
                            },
                        },
                        new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Direction = FillDirection.Vertical,
                            Children = new Drawable[]
                            {
                                usernameText = new TruncatingSpriteText
                                {
                                    Font = OsuFont.GetFont(size: 22, weight: FontWeight.Bold),
                                    MaxWidth = 230,
                                },
                                rulesetText = new OsuSpriteText
                                {
                                    Font = OsuFont.GetFont(size: 13, weight: FontWeight.SemiBold),
                                    Colour = colourProvider.Content2,
                                },
                            }
                        },
                    }
                },
                new GridContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Padding = new MarginPadding { Horizontal = SettingsPanel.CONTENT_MARGINS },
                    ColumnDimensions = new[]
                    {
                        new Dimension(),
                        new Dimension(),
                        new Dimension(),
                    },
                    RowDimensions = new[] { new Dimension(GridSizeMode.AutoSize) },
                    Content = new[]
                    {
                        new Drawable[]
                        {
                            createStatistic("Performance", out performanceText),
                            createStatistic("Accuracy", out accuracyText),
                            createStatistic("Ranked beatmaps", out countText),
                        }
                    }
                },
                pendingText = new OsuSpriteText
                {
                    Margin = new MarginPadding { Horizontal = SettingsPanel.CONTENT_MARGINS },
                    Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold),
                    Colour = colourProvider.Highlight1,
                    Alpha = 0,
                },
                new OsuSpriteText
                {
                    Margin = new MarginPadding { Horizontal = SettingsPanel.CONTENT_MARGINS },
                    Text = "BEST PERFORMANCE",
                    Font = OsuFont.GetFont(weight: FontWeight.Bold),
                },
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Padding = new MarginPadding { Horizontal = SettingsPanel.CONTENT_MARGINS },
                    Children = new Drawable[]
                    {
                        emptyText = new OsuTextFlowContainer(s => s.Font = OsuFont.GetFont(size: 14))
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Colour = colourProvider.Content2,
                            Text = "No plays count yet. Set a score on a ranked beatmap to earn pp.",
                        },
                        topPlaysScroll = new OsuScrollContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 0,
                            Child = topPlaysFlow = new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Vertical,
                                Spacing = new Vector2(0, 4),
                            },
                        },
                    }
                },
                new SettingsCheckbox
                {
                    LabelText = "Count unranked plays",
                    TooltipText = "Also count scores on unranked beatmaps, and with mods which don't give pp online.",
                    Current = includeUnranked,
                },
                rulesText = new OsuTextFlowContainer(s => s.Font = OsuFont.GetFont(size: 12))
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Padding = new MarginPadding { Horizontal = SettingsPanel.CONTENT_MARGINS },
                    Colour = colourProvider.Content2,
                },
                new LinkFlowContainer(s => s.Font = OsuFont.GetFont(size: 12))
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Padding = new MarginPadding { Horizontal = SettingsPanel.CONTENT_MARGINS },
                }.With(l => l.AddLink("Sign in with an osu! account", () => RequestLogin?.Invoke())),
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            localUser.BindTo(api.LocalUser);
            localUser.BindValueChanged(user =>
            {
                avatar.User = user.NewValue;
                usernameText.Text = user.NewValue.Username;
            }, true);

            ruleset.BindValueChanged(r => rulesetText.Text = $"Offline profile · {r.NewValue?.Name}", true);

            includeUnranked.BindValueChanged(unranked => rulesText.Text = unranked.NewValue
                ? "Counting every play, including unranked beatmaps and mods."
                : "Counted as osu! does online: only ranked beatmaps and ranked mods give pp.", true);

            summary.BindTo(performanceTracker.Summary);
            summary.BindValueChanged(s => updateSummary(s.NewValue), true);
        }

        private void updateSummary(LocalPerformanceSummary value)
        {
            performanceText.Text = $"{value.TotalPerformance:N0}pp";
            accuracyText.Text = value.CountedBeatmaps > 0 ? value.Accuracy.FormatAccuracy() : "-";
            countText.Text = value.CountedBeatmaps.ToString("N0");

            pendingText.Text = $"Calculating pp for {value.PendingScores:N0} scores...";
            pendingText.Alpha = value.PendingScores > 0 ? 1 : 0;

            topPlaysFlow.ChildrenEnumerable = value.TopPlays.Select((play, index) => new TopPlayRow(play, index + 1)
            {
                RequestHide = () => RequestHide?.Invoke(),
            });

            emptyText.Alpha = value.TopPlays.Count == 0 ? 1 : 0;

            float contentHeight = value.TopPlays.Count * (top_play_height + topPlaysFlow.Spacing.Y);
            topPlaysScroll.Height = Math.Min(contentHeight, top_plays_max_height);
        }

        private Drawable createStatistic(LocalisableString title, out OsuSpriteText valueText) => new FillFlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Vertical,
            Children = new Drawable[]
            {
                new OsuSpriteText
                {
                    Text = title,
                    Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold),
                    Colour = colourProvider.Content2,
                },
                valueText = new OsuSpriteText
                {
                    Font = OsuFont.GetFont(size: 20, weight: FontWeight.Bold),
                },
            }
        };

        public override bool AcceptsFocus => true;

        protected override bool OnClick(ClickEvent e) => true;

        private partial class TopPlayRow : OsuClickableContainer
        {
            public Action? RequestHide;

            private readonly LocalTopPlay play;
            private readonly int position;

            [Resolved]
            private OverlayColourProvider colourProvider { get; set; } = null!;

            [Resolved]
            private OsuGame? game { get; set; }

            private Box background = null!;

            public TopPlayRow(LocalTopPlay play, int position)
            {
                this.play = play;
                this.position = position;

                RelativeSizeAxes = Axes.X;
                Height = top_play_height;
                Masking = true;
                CornerRadius = 6;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                var score = play.Score;
                var beatmap = score.BeatmapInfo!;

                TooltipText = $"#{position} · {score.Date.LocalDateTime:g}";

                Action = () =>
                {
                    RequestHide?.Invoke();
                    game?.PresentScore(score);
                };

                Children = new Drawable[]
                {
                    background = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = colourProvider.Background3,
                    },
                    new GridContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Horizontal = 8 },
                        ColumnDimensions = new[]
                        {
                            new Dimension(GridSizeMode.Absolute, 36),
                            new Dimension(),
                            new Dimension(GridSizeMode.AutoSize),
                        },
                        Content = new[]
                        {
                            new Drawable[]
                            {
                                new DrawableRank(score.Rank)
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Size = new Vector2(30, 15),
                                },
                                new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Direction = FillDirection.Vertical,
                                    Padding = new MarginPadding { Right = 8 },
                                    Children = new Drawable[]
                                    {
                                        new TruncatingSpriteText
                                        {
                                            RelativeSizeAxes = Axes.X,
                                            Text = beatmap.Metadata.GetDisplayTitleRomanisable(includeCreator: false),
                                            Font = OsuFont.GetFont(size: 13, weight: FontWeight.SemiBold),
                                        },
                                        new FillFlowContainer
                                        {
                                            RelativeSizeAxes = Axes.X,
                                            AutoSizeAxes = Axes.Y,
                                            Direction = FillDirection.Horizontal,
                                            Spacing = new Vector2(4, 0),
                                            Children = new Drawable[]
                                            {
                                                new OsuSpriteText
                                                {
                                                    Anchor = Anchor.CentreLeft,
                                                    Origin = Anchor.CentreLeft,
                                                    Text = $"{score.Accuracy.FormatAccuracy()}",
                                                    Font = OsuFont.GetFont(size: 12, weight: FontWeight.Bold),
                                                    Colour = colourProvider.Highlight1,
                                                },
                                                new TruncatingSpriteText
                                                {
                                                    Anchor = Anchor.CentreLeft,
                                                    Origin = Anchor.CentreLeft,
                                                    Text = beatmap.DifficultyName,
                                                    Font = OsuFont.GetFont(size: 12),
                                                    Colour = colourProvider.Content2,
                                                    MaxWidth = 110,
                                                },
                                                new FillFlowContainer
                                                {
                                                    Anchor = Anchor.CentreLeft,
                                                    Origin = Anchor.CentreLeft,
                                                    AutoSizeAxes = Axes.Both,
                                                    Direction = FillDirection.Horizontal,
                                                    Spacing = new Vector2(-8, 0),
                                                    ChildrenEnumerable = score.Mods.Select(mod => new ModSwitchTiny(mod)
                                                    {
                                                        Active = { Value = true },
                                                        Scale = new Vector2(0.4f),
                                                    }),
                                                },
                                            }
                                        },
                                    }
                                },
                                new FillFlowContainer
                                {
                                    AutoSizeAxes = Axes.Both,
                                    Anchor = Anchor.CentreRight,
                                    Origin = Anchor.CentreRight,
                                    Direction = FillDirection.Vertical,
                                    Children = new Drawable[]
                                    {
                                        new OsuSpriteText
                                        {
                                            Anchor = Anchor.TopRight,
                                            Origin = Anchor.TopRight,
                                            Text = $"{play.Performance:N0}pp",
                                            Font = OsuFont.GetFont(size: 15, weight: FontWeight.Bold),
                                        },
                                        new OsuSpriteText
                                        {
                                            Anchor = Anchor.TopRight,
                                            Origin = Anchor.TopRight,
                                            Text = $"{play.Weight:P0} · {play.Performance * play.Weight:N0}pp",
                                            Font = OsuFont.GetFont(size: 11),
                                            Colour = colourProvider.Content2,
                                        },
                                    }
                                },
                            }
                        }
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                background.FadeColour(colourProvider.Background2, 150, Easing.OutQuint);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                background.FadeColour(colourProvider.Background3, 300, Easing.OutQuint);
                base.OnHoverLost(e);
            }
        }
    }
}
