// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input.Events;
using osu.Framework.Logging;
using osu.Framework.Utils;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.Play
{
    /// <summary>
    /// An overlay displaying how difficult the beatmap is over time, which can be clicked to seek gameplay to a chosen point.
    /// </summary>
    public partial class StrainGraphOverlay : OverlayContainer
    {
        /// <summary>
        /// Invoked when the user picks a time to seek to, in beatmap time.
        /// </summary>
        public Action<double> RequestSeek;

        protected override bool BlockNonPositionalInput => false;

        [Resolved]
        private IBindable<WorkingBeatmap> beatmap { get; set; }

        [Resolved]
        private GameplayState gameplayState { get; set; }

        [Resolved]
        private OsuColour colours { get; set; }

        [Resolved]
        private SessionStatics statics { get; set; }

        private OsuSpriteText missesText;

        private StrainGraph graph;
        private LoadingSpinner loading;
        private OsuSpriteText emptyText;

        private CancellationTokenSource calculationCancellation;
        private bool calculationStarted;

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.Black,
                    Alpha = 0.75f,
                },
                new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.X,
                    Width = 0.9f,
                    AutoSizeAxes = Axes.Y,
                    Masking = true,
                    CornerRadius = 10,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = new Color4(20, 20, 25, 255),
                        },
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Padding = new MarginPadding(20),
                            Spacing = new Vector2(0, 6),
                            Children = new Drawable[]
                            {
                                new OsuSpriteText
                                {
                                    Text = "Practice",
                                    Font = OsuFont.Torus.With(size: 26, weight: FontWeight.SemiBold),
                                },
                                new OsuSpriteText
                                {
                                    Text = "Click the graph to jump to that point of the map.",
                                    Font = OsuFont.Torus.With(size: 16),
                                    Alpha = 0.8f,
                                },
                                new OsuSpriteText
                                {
                                    Text = "Jumping means this play will not be saved or submitted.",
                                    Font = OsuFont.Torus.With(size: 14),
                                    Colour = new Color4(255, 190, 100, 255),
                                },
                                missesText = new OsuSpriteText
                                {
                                    Font = OsuFont.Torus.With(size: 14, weight: FontWeight.SemiBold),
                                    Colour = StrainGraph.MISS_COLOUR,
                                    Alpha = 0,
                                },
                                new Container
                                {
                                    RelativeSizeAxes = Axes.X,
                                    Height = 220,
                                    Margin = new MarginPadding { Top = 10 },
                                    Children = new Drawable[]
                                    {
                                        graph = new StrainGraph
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            Alpha = 0,
                                            OnSeekRequested = time => RequestSeek?.Invoke(time),
                                        },
                                        loading = new LoadingSpinner
                                        {
                                            Anchor = Anchor.Centre,
                                            Origin = Anchor.Centre,
                                        },
                                        emptyText = new OsuSpriteText
                                        {
                                            Anchor = Anchor.Centre,
                                            Origin = Anchor.Centre,
                                            Text = "No difficulty data available for this beatmap.",
                                            Alpha = 0,
                                        },
                                    }
                                },
                            }
                        },
                    }
                },
            };
        }

        protected override void PopIn()
        {
            beginCalculation();
            updateMisses();

            this.FadeIn(200, Easing.OutQuint);
        }

        protected override void PopOut() => this.FadeOut(200, Easing.OutQuint);

        /// <summary>
        /// Shows where the misses were in the last full playthrough of this beatmap.
        /// </summary>
        private void updateMisses()
        {
            var last = statics.Get<LastPlaythroughMisses>(Static.LastPlaythroughMisses);

            if (last == null || last.BeatmapID != beatmap.Value.BeatmapInfo.ID)
            {
                graph.MissTimes = Array.Empty<double>();
                missesText.Alpha = 0;
                return;
            }

            graph.MissTimes = last.MissTimes;

            missesText.Alpha = 1;
            missesText.Text = last.MissTimes.Count switch
            {
                0 => "No misses in your last full playthrough.",
                1 => "Red line: the miss in your last full playthrough.",
                _ => $"Red lines: the {last.MissTimes.Count} misses in your last full playthrough.",
            };
        }

        protected override bool OnClick(ClickEvent e)
        {
            // Clicking outside of the graph dismisses the overlay.
            Hide();
            return true;
        }

        private void beginCalculation()
        {
            if (calculationStarted)
                return;

            calculationStarted = true;
            loading.Show();

            var ruleset = gameplayState.Ruleset;
            var mods = gameplayState.Mods.ToArray();

            // The beatmap gameplay is using has already been converted and had mods applied. Handing it to the
            // calculator directly keeps it from converting the beatmap again on this thread, which would apply
            // defaults to the very hit objects being played.
            var workingBeatmap = new PreparedWorkingBeatmap(gameplayState.Beatmap);

            calculationCancellation = new CancellationTokenSource();
            var token = calculationCancellation.Token;

            Task.Run(() =>
            {
                var calculator = ruleset.CreateDifficultyCalculator(workingBeatmap);
                return calculator.CalculateStrainSections(mods, token);
            }, token).ContinueWith(task =>
            {
                if (task.IsFaulted)
                    Logger.Error(task.Exception, "Strain calculation failed!");

                List<StrainSection> sections = task.IsCompletedSuccessfully ? task.GetResultSafely() : new List<StrainSection>();

                Schedule(() =>
                {
                    loading.Hide();

                    if (sections.Count < 2)
                    {
                        emptyText.FadeIn(200, Easing.OutQuint);
                        return;
                    }

                    graph.Sections = sections;
                    graph.HighlightColour = colours.Yellow;
                    graph.FadeIn(200, Easing.OutQuint);
                });
            }, token);
        }

        protected override void Dispose(bool isDisposing)
        {
            calculationCancellation?.Cancel();
            calculationCancellation?.Dispose();

            base.Dispose(isDisposing);
        }

        /// <summary>
        /// A working beatmap which hands out a beatmap that is already converted and has mods applied.
        /// </summary>
        private class PreparedWorkingBeatmap : WorkingBeatmap
        {
            private readonly IBeatmap playableBeatmap;

            public PreparedWorkingBeatmap(IBeatmap playableBeatmap)
                : base(playableBeatmap.BeatmapInfo, null)
            {
                this.playableBeatmap = playableBeatmap;
            }

            public override IBeatmap GetPlayableBeatmap(IRulesetInfo ruleset, IReadOnlyList<Mod> mods, CancellationToken token) => playableBeatmap;

            protected override IBeatmap GetBeatmap() => playableBeatmap;

            public override Texture GetBackground() => null;

            protected override Track GetBeatmapTrack() => null;

            protected internal override ISkin GetSkin() => null;

            public override Stream GetStream(string storagePath) => null;
        }

        /// <summary>
        /// Displays a list of <see cref="StrainSection"/>s as a bar graph, and reports the time the user clicks on.
        /// </summary>
        public partial class StrainGraph : CompositeDrawable
        {
            /// <summary>
            /// The maximum number of bars to display. Longer beatmaps have their sections combined to fit.
            /// </summary>
            private const int maximum_columns = 400;

            public Action<double> OnSeekRequested;

            public Color4 HighlightColour { get; set; } = Color4.Yellow;

            public static readonly Color4 MISS_COLOUR = new Color4(255, 80, 80, 255);

            private IReadOnlyList<double> missTimes = Array.Empty<double>();

            /// <summary>
            /// Times (in beatmap time) to mark as misses.
            /// </summary>
            public IReadOnlyList<double> MissTimes
            {
                get => missTimes;
                set
                {
                    missTimes = value;
                    updateMissMarkers();
                }
            }

            private readonly Container<Box> missMarkers;

            [Resolved]
            private IGameplayClock gameplayClock { get; set; }

            private readonly Container<Box> bars;
            private readonly Box currentTimeMarker;
            private readonly Box hoverMarker;
            private readonly OsuSpriteText hoverTime;
            private readonly Container<OsuSpriteText> axis;

            private List<StrainSection> sections = new List<StrainSection>();

            public List<StrainSection> Sections
            {
                get => sections;
                set
                {
                    sections = value;
                    updateBars();
                }
            }

            private double firstTime;
            private double lastTime;

            public StrainGraph()
            {
                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(10, 10, 12, 255),
                    },
                    bars = new Container<Box> { RelativeSizeAxes = Axes.Both },
                    missMarkers = new Container<Box> { RelativeSizeAxes = Axes.Both },
                    currentTimeMarker = new Box
                    {
                        RelativeSizeAxes = Axes.Y,
                        RelativePositionAxes = Axes.X,
                        Width = 2,
                        Alpha = 0,
                        Colour = Color4.White,
                    },
                    hoverMarker = new Box
                    {
                        RelativeSizeAxes = Axes.Y,
                        RelativePositionAxes = Axes.X,
                        Width = 1,
                        Alpha = 0,
                        Colour = Color4.White.Opacity(0.5f),
                    },
                    hoverTime = new OsuSpriteText
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        RelativePositionAxes = Axes.X,
                        Margin = new MarginPadding { Top = 4, Left = 4 },
                        Alpha = 0,
                        Font = OsuFont.Torus.With(size: 14, weight: FontWeight.SemiBold),
                    },
                    axis = new Container<OsuSpriteText>
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Alpha = 0.6f,
                    },
                };
            }

            private void updateMissMarkers()
            {
                missMarkers.Clear();

                if (sections.Count < 2)
                    return;

                foreach (double time in missTimes)
                {
                    float progress = (float)((time - firstTime) / (lastTime - firstTime));

                    missMarkers.Add(new Box
                    {
                        RelativeSizeAxes = Axes.Y,
                        RelativePositionAxes = Axes.X,
                        Origin = Anchor.TopCentre,
                        X = Math.Clamp(progress, 0, 1),
                        Width = 2,
                        Colour = MISS_COLOUR,
                        Alpha = 0.9f,
                    });
                }
            }

            private void updateBars()
            {
                bars.Clear();
                axis.Clear();

                if (sections.Count < 2)
                {
                    updateMissMarkers();
                    return;
                }

                firstTime = sections[0].Time;
                lastTime = sections[^1].Time;

                updateMissMarkers();

                int columns = Math.Min(maximum_columns, sections.Count);

                for (int i = 0; i < columns; i++)
                {
                    int start = (int)((long)i * sections.Count / columns);
                    int end = (int)((long)(i + 1) * sections.Count / columns);

                    double strain = 0;

                    for (int j = start; j < Math.Max(end, start + 1); j++)
                        strain = Math.Max(strain, sections[j].Strain);

                    bars.Add(new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        RelativePositionAxes = Axes.X,
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        X = i / (float)columns,
                        Width = 1f / columns,
                        Height = Math.Max(0.02f, (float)strain),
                        Colour = colourFor(strain),
                    });
                }

                const int axis_labels = 5;

                for (int i = 0; i < axis_labels; i++)
                {
                    axis.Add(new OsuSpriteText
                    {
                        RelativePositionAxes = Axes.X,
                        X = i / (float)axis_labels,
                        Margin = new MarginPadding { Left = 4 },
                        Text = formatTime(timeAt(i / (float)axis_labels)),
                        Font = OsuFont.Torus.With(size: 12),
                    });
                }
            }

            private Color4 colourFor(double strain)
                => Interpolation.ValueAt(Math.Clamp(strain, 0, 1), new Color4(60, 130, 200, 255), HighlightColour, 0, 1);

            private static string formatTime(double time) => TimeSpan.FromMilliseconds(Math.Max(0, time)).ToString(@"m\:ss");

            protected override void Update()
            {
                base.Update();

                if (sections.Count < 2)
                    return;

                float progress = (float)((gameplayClock.CurrentTime - firstTime) / (lastTime - firstTime));

                currentTimeMarker.Alpha = progress >= 0 && progress <= 1 ? 1 : 0;
                currentTimeMarker.X = Math.Clamp(progress, 0, 1);
            }

            protected override bool OnHover(HoverEvent e) => true;

            protected override bool OnMouseMove(MouseMoveEvent e)
            {
                if (sections.Count < 2)
                    return false;

                float progress = Math.Clamp(ToLocalSpace(e.ScreenSpaceMousePosition).X / DrawWidth, 0, 1);

                hoverMarker.Alpha = 1;
                hoverMarker.X = progress;

                hoverTime.Alpha = 1;
                hoverTime.X = progress;
                hoverTime.Text = formatTime(timeAt(progress));

                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                hoverMarker.Alpha = 0;
                hoverTime.Alpha = 0;
            }

            protected override bool OnClick(ClickEvent e)
            {
                if (sections.Count < 2)
                    return true;

                float progress = Math.Clamp(ToLocalSpace(e.ScreenSpaceMousePosition).X / DrawWidth, 0, 1);

                OnSeekRequested?.Invoke(timeAt(progress));
                return true;
            }

            private double timeAt(float progress) => firstTime + (lastTime - firstTime) * progress;
        }
    }
}
