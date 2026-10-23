// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Localisation;
using osu.Game.Overlays;
using osu.Game.Resources.Localisation.Web;
using osuTK;

namespace osu.Game.Collections
{
    public partial class ManageCollectionsDialog : OsuFocusedOverlayContainer
    {
        private const double enter_duration = 500;
        private const double exit_duration = 200;

        protected override string PopInSampleName => @"UI/overlay-big-pop-in";
        protected override string PopOutSampleName => @"UI/overlay-big-pop-out";

        private IDisposable? duckOperation;

        private BasicSearchTextBox searchTextBox = null!;
        private DrawableCollectionList list = null!;

        [Resolved]
        private MusicController? musicController { get; set; }

        /// <summary>
        /// The folder view. Only present if folders are available.
        /// </summary>
        private CollectionFolderBrowser? browser;

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));

            if (parent.Get<CollectionFolderManager>() != null)
                dependencies.Cache(browser = new CollectionFolderBrowser());

            return dependencies;
        }

        public ManageCollectionsDialog()
        {
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;

            RelativeSizeAxes = Axes.Both;
            Size = new Vector2(0.5f, 0.8f);

            Masking = true;
            CornerRadius = 10;
        }

        [BackgroundDependencyLoader]
        private void load(OsuColour colours)
        {
            Children = new Drawable[]
            {
                new Box
                {
                    Colour = colours.GreySeaFoamDark,
                    RelativeSizeAxes = Axes.Both,
                },
                new PopoverContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = new GridContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        RowDimensions = new[]
                        {
                            new Dimension(GridSizeMode.AutoSize),
                        },
                        Content = new[]
                        {
                            new Drawable[]
                            {
                                new Container
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Children = new Drawable[]
                                    {
                                        new OsuSpriteText
                                        {
                                            Anchor = Anchor.Centre,
                                            Origin = Anchor.Centre,
                                            Text = CollectionsStrings.ManageCollectionsTitle,
                                            Font = OsuFont.GetFont(size: 30),
                                            Padding = new MarginPadding { Vertical = 10 },
                                        },
                                        new IconButton
                                        {
                                            Anchor = Anchor.CentreRight,
                                            Origin = Anchor.CentreRight,
                                            Icon = FontAwesome.Solid.Times,
                                            Colour = colours.GreySeaFoamDarker,
                                            Scale = new Vector2(0.8f),
                                            X = -10,
                                            Action = () => State.Value = Visibility.Hidden
                                        }
                                    }
                                }
                            },
                            new Drawable[]
                            {
                                new Container
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Masking = true,
                                    Children = new Drawable[]
                                    {
                                        new Box
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            Colour = colours.GreySeaFoamDarker,
                                        },
                                        new Container
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            Padding = new MarginPadding(10),
                                            Children = new Drawable[]
                                            {
                                                list = new DrawableCollectionList
                                                {
                                                    Padding = new MarginPadding { Top = browser != null ? 90 : 50, Bottom = 50 },
                                                    RelativeSizeAxes = Axes.Both,
                                                },
                                                searchTextBox = new BasicSearchTextBox
                                                {
                                                    RelativeSizeAxes = Axes.X,
                                                    Height = 40,
                                                    ReleaseFocusOnCommit = false,
                                                    HoldFocus = true,
                                                    PlaceholderText = HomeStrings.SearchPlaceholder,
                                                },
                                                browser != null
                                                    ? new CollectionFolderBreadcrumbs
                                                    {
                                                        Y = 48,
                                                    }
                                                    : Empty(),
                                                new Container
                                                {
                                                    RelativeSizeAxes = Axes.X,
                                                    AutoSizeAxes = Axes.Y,
                                                    Anchor = Anchor.BottomLeft,
                                                    Origin = Anchor.BottomLeft,
                                                    Children = new Drawable[]
                                                    {
                                                        new NewCollectionEntryItem()
                                                    }
                                                },
                                            }
                                        },
                                        browser ?? Empty(),
                                    }
                                }
                            },
                        }
                    }
                }
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            searchTextBox.Current.BindValueChanged(_ =>
            {
                list.SearchTerm = searchTextBox.Current.Value;

                if (browser != null)
                    browser.SearchTerm.Value = searchTextBox.Current.Value;
            });

            // Opening a folder (e.g. from search results) shows its contents.
            browser?.CurrentFolder.BindValueChanged(_ => searchTextBox.Current.Value = string.Empty);
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            duckOperation?.Dispose();
        }

        protected override void PopIn()
        {
            list.RefreshCounts();

            duckOperation = musicController?.Duck(new DuckParameters
            {
                DuckVolumeTo = 1,
                DuckDuration = 100,
                RestoreDuration = 100,
            });

            this.FadeIn(enter_duration, Easing.OutQuint);
            this.ScaleTo(0.9f).Then().ScaleTo(1f, enter_duration, Easing.OutQuint);
        }

        protected override void PopOut()
        {
            base.PopOut();

            duckOperation?.Dispose();

            this.FadeOut(exit_duration, Easing.OutQuint);
            this.ScaleTo(0.9f, exit_duration);

            // Ensure that textboxes commit
            GetContainingFocusManager()?.TriggerFocusContention(this);
        }

        private partial class NewCollectionEntryItem : DrawableCollectionListItem
        {
            [Resolved]
            private RealmAccess realm { get; set; } = null!;

            [Resolved]
            private CollectionFolderBrowser? browser { get; set; }

            [Resolved]
            private CollectionFolderManager? collectionFolders { get; set; }

            public NewCollectionEntryItem()
                : base(new BeatmapCollection().ToLiveUnmanaged(), false)
            {
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                TextBox.OnCommit += (_, _) =>
                {
                    if (string.IsNullOrEmpty(TextBox.Text))
                        return;

                    var collection = new BeatmapCollection(TextBox.Text);

                    realm.Write(r => r.Add(collection));
                    TextBox.Text = string.Empty;

                    // New collections are created inside the folder being viewed.
                    if (browser?.CurrentFolder.Value is System.Guid folder)
                        collectionFolders?.AddToFolder(collection.ID, folder);
                };
            }
        }
    }
}
