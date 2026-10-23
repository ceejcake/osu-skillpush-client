// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Localisation;
using osu.Game.Overlays;
using osuTK;
using osuTK.Graphics;
using Realms;

namespace osu.Game.Collections
{
    /// <summary>
    /// Visualises a <see cref="BeatmapCollection"/> inside a <see cref="DrawableCollectionList"/>.
    /// </summary>
    public partial class DrawableCollectionListItem : OsuRearrangeableListItem<Live<BeatmapCollection>>, IFilterable
    {
        private const float item_height = 45;
        private const float button_width = item_height * 0.75f;
        private const float drag_handle_width = 20;

        protected TextBox TextBox => content.TextBox;

        private ItemContent content = null!;

        /// <summary>
        /// Creates a new <see cref="DrawableCollectionListItem"/>.
        /// </summary>
        /// <param name="item">The <see cref="BeatmapCollection"/>.</param>
        /// <param name="isCreated">Whether <paramref name="item"/> currently exists inside realm.</param>
        public DrawableCollectionListItem(Live<BeatmapCollection> item, bool isCreated)
            : base(item)
        {
            // For now we don't support rearranging and always use alphabetical sort.
            // Change this to:
            //
            // ShowDragHandle.Value = item.IsManaged;
            //
            // if we want to support user sorting (but changes will need to be made to realm to persist).
            ShowDragHandle.Value = false;

            Masking = true;

            // This doesn't match the latest design spec (should be 5) but is an in-between that feels right to the eye
            // until we move everything over to Form controls.
            CornerRadius = 10;
            CornerExponent = 2.5f;
        }

        protected override Drawable CreateContent() => content = new ItemContent(Model)
        {
            FolderPopoverStateChanged = open =>
            {
                folderPopoverOpen = open;
                updateVisibility();
            }
        };

        private bool inCurrentView = true;

        /// <summary>
        /// Whether the collection is inside the folder being viewed. Collections outside of it are only shown while searching.
        /// </summary>
        public bool InCurrentView
        {
            get => inCurrentView;
            set
            {
                if (inCurrentView == value)
                    return;

                inCurrentView = value;
                updateVisibility();
            }
        }

        /// <summary>
        /// While the folder popover is open, the collection is kept visible even if it was moved out of the folder being viewed,
        /// so that the popover doesn't disappear while in use.
        /// </summary>
        private bool folderPopoverOpen;

        /// <summary>
        /// The main content of the <see cref="DrawableCollectionListItem"/>.
        /// </summary>
        private partial class ItemContent : CompositeDrawable
        {
            private readonly Live<BeatmapCollection> collection;

            public ItemTextBox TextBox { get; private set; } = null!;

            public Action<bool>? FolderPopoverStateChanged;

            public ItemContent(Live<BeatmapCollection> collection)
            {
                this.collection = collection;

                RelativeSizeAxes = Axes.X;
                Height = item_height;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChildren = new[]
                {
                    collection.IsManaged
                        ? new DeleteButton(collection)
                        {
                            Anchor = Anchor.CentreRight,
                            Origin = Anchor.CentreRight,
                            IsTextBoxHovered = v => TextBox.ReceivePositionalInputAt(v)
                        }
                        : Empty(),
                    collection.IsManaged
                        ? new CollectionDragHandle(collection.ID, () => collection.PerformRead(c => c.IsValid ? c.Name : string.Empty))
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                        }
                        : Empty(),
                    collection.IsManaged
                        ? new FolderButton(collection)
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            X = drag_handle_width,
                            PopoverStateChanged = open => FolderPopoverStateChanged?.Invoke(open),
                        }
                        : Empty(),
                    collection.IsManaged
                        ? new AddBeatmapIdsButton(collection)
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            X = drag_handle_width + button_width,
                        }
                        : Empty(),
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding
                        {
                            Left = collection.IsManaged ? drag_handle_width + button_width * 2 : 0,
                            Right = collection.IsManaged ? button_width : 0
                        },
                        Children = new Drawable[]
                        {
                            TextBox = new ItemTextBox(collection)
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = item_height,
                                CommitOnFocusLost = true,
                            },
                        }
                    },
                };
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                // Bind late, as the collection name may change externally while still loading.
                TextBox.Current.Value = collection.PerformRead(c => c.IsValid ? c.Name : string.Empty);
                TextBox.OnCommit += onCommit;
            }

            private void onCommit(TextBox sender, bool newText)
            {
                if (collection.IsManaged && collection.Value.Name != TextBox.Current.Value)
                    collection.PerformWrite(c => c.Name = TextBox.Current.Value);
            }
        }

        private partial class ItemTextBox : OsuTextBox
        {
            protected override float LeftRightPadding => item_height / 2;

            private const float count_text_size = 12;

            private readonly Live<BeatmapCollection> collection;

            private OsuSpriteText countText = null!;
            private OsuSpriteText? folderText;

            [Resolved]
            private CollectionFolderManager? collectionFolders { get; set; }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                if (collectionFolders != null)
                    collectionFolders.FoldersChanged += updateFolderText;

                updateFolderText();
            }

            /// <summary>
            /// Shows how many beatmaps are in the collection, and how many of those aren't available locally
            /// (which song select can't show, e.g. because they haven't been downloaded).
            /// </summary>
            public void RefreshCount()
            {
                if (!collection.IsManaged)
                    return;

                var (total, missing) = collection.PerformRead(c =>
                {
                    if (!c.IsValid)
                        return (0, 0);

                    var beatmaps = c.Realm!.All<BeatmapInfo>();
                    return (c.BeatmapMD5Hashes.Count, c.BeatmapMD5Hashes.Count(h => !beatmaps.Filter("MD5Hash == $0 AND BeatmapSet.DeletePending == false", h).Any()));
                });

                countText.Text = missing > 0
                    ? LocalisableString.Interpolate($"{CommonStrings.ItemsCount(total)} ({missing} not downloaded)")
                    : CommonStrings.ItemsCount(total);
            }

            private void updateFolderText()
            {
                if (folderText == null)
                    return;

                var itemFolders = collection.PerformRead(c => c.IsValid ? collectionFolders?.GetFolders(c.ID) : null);
                folderText.Text = itemFolders?.Count > 0 ? $"in {string.Join(", ", itemFolders.Select(f => f.Name))}" : string.Empty;
            }

            protected override void Dispose(bool isDisposing)
            {
                base.Dispose(isDisposing);

                if (collectionFolders != null)
                    collectionFolders.FoldersChanged -= updateFolderText;
            }

            public ItemTextBox(Live<BeatmapCollection> collection)
            {
                this.collection = collection;

                CornerRadius = 10;
                CornerExponent = 2.5f;
            }

            [BackgroundDependencyLoader]
            private void load(OsuColour colours)
            {
                BackgroundUnfocused = colours.GreySeaFoamDarker.Darken(0.5f);
                BackgroundFocused = colours.GreySeaFoam;

                if (collection.IsManaged)
                {
                    TextContainer.Height *= (Height - count_text_size) / Height;
                    TextContainer.Margin = new MarginPadding { Bottom = count_text_size };

                    TextContainer.Add(new FillFlowContainer
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.TopLeft,
                        Depth = float.MinValue,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(8, 0),
                        Margin = new MarginPadding { Top = 2, Left = 2 },
                        Children = new Drawable[]
                        {
                            countText = new OsuSpriteText
                            {
                                Font = OsuFont.Default.With(size: count_text_size, weight: FontWeight.SemiBold),
                                Colour = colours.Yellow
                            },
                            folderText = new OsuSpriteText
                            {
                                Font = OsuFont.Default.With(size: count_text_size, weight: FontWeight.SemiBold),
                                Colour = colours.BlueLight,
                            },
                        }
                    });

                    // interestingly, it is not required to subscribe to change notifications on this collection at all for this to work correctly.
                    // the reasoning for this is that `DrawableCollectionList` already takes out a subscription on the set of all `BeatmapCollection`s -
                    // but that subscription does not only cover *changes to the set of collections* (i.e. addition/removal/rearrangement of collections),
                    // but also covers *changes to the properties of collections*, which `BeatmapMD5Hashes` is one.
                    // when a collection item changes due to `BeatmapMD5Hashes` changing, the list item is deleted and re-inserted, thus guaranteeing this to work correctly.
                    RefreshCount();
                }
                else
                {
                    PlaceholderText = CollectionsStrings.CreateNew;
                }
            }
        }

        /// <summary>
        /// Recounts how many of the collection's beatmaps are available locally.
        /// </summary>
        public void RefreshCount() => content.TextBox.RefreshCount();

        /// <summary>
        /// Shows a popover to move the collection into a folder.
        /// </summary>
        public partial class FolderButton : IconButton, IHasPopover
        {
            private readonly Live<BeatmapCollection> collection;

            /// <summary>
            /// Invoked with whether the popover is open whenever it opens or closes.
            /// </summary>
            public Action<bool>? PopoverStateChanged;

            public FolderButton(Live<BeatmapCollection> collection)
            {
                this.collection = collection;

                Icon = FontAwesome.Solid.FolderOpen;
                TooltipText = "Folders";
                Size = new Vector2(button_width, item_height);
                Action = this.ShowPopover;
            }

            public Popover GetPopover()
            {
                var popover = new CollectionFolderPopover(collection.ID);
                popover.State.BindValueChanged(state => PopoverStateChanged?.Invoke(state.NewValue == Visibility.Visible));
                return popover;
            }
        }

        /// <summary>
        /// Shows a popover to add beatmaps to the collection from a pasted list of beatmap IDs.
        /// </summary>
        public partial class AddBeatmapIdsButton : IconButton, IHasPopover
        {
            private readonly Live<BeatmapCollection> collection;

            public AddBeatmapIdsButton(Live<BeatmapCollection> collection)
            {
                this.collection = collection;

                Icon = FontAwesome.Solid.Paste;
                TooltipText = "Add beatmap IDs";
                Size = new Vector2(button_width, item_height);
                Action = this.ShowPopover;
            }

            public Popover GetPopover() => new AddBeatmapIdsPopover(collection);
        }

        public partial class DeleteButton : OsuClickableContainer
        {
            public Func<Vector2, bool> IsTextBoxHovered = null!;

            [Resolved]
            private IDialogOverlay? dialogOverlay { get; set; }

            private readonly Live<BeatmapCollection> collection;

            private Drawable fadeContainer = null!;
            private Drawable background = null!;

            public DeleteButton(Live<BeatmapCollection> collection)
            {
                this.collection = collection;
                RelativeSizeAxes = Axes.Y;

                Width = button_width + item_height / 2; // add corner radius to cover with fill
            }

            [BackgroundDependencyLoader]
            private void load(OsuColour colours)
            {
                Child = fadeContainer = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Alpha = 0.1f,
                    Children = new[]
                    {
                        background = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = colours.Red
                        },
                        new SpriteIcon
                        {
                            Anchor = Anchor.CentreRight,
                            Origin = Anchor.Centre,
                            X = -button_width * 0.6f,
                            Size = new Vector2(10),
                            Icon = FontAwesome.Solid.Trash
                        }
                    }
                };

                Action = () =>
                {
                    if (collection.PerformRead(c => c.BeatmapMD5Hashes.Count) == 0)
                        deleteCollection();
                    else
                        dialogOverlay?.Push(new DeleteCollectionDialog(collection, deleteCollection));
                };
            }

            public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => base.ReceivePositionalInputAt(screenSpacePos) && !IsTextBoxHovered(screenSpacePos);

            protected override bool OnHover(HoverEvent e)
            {
                fadeContainer.FadeTo(1f, 100, Easing.Out);
                return false;
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                fadeContainer.FadeTo(0.1f, 100);
            }

            protected override bool OnClick(ClickEvent e)
            {
                background.FlashColour(Color4.White, 150);

                return base.OnClick(e);
            }

            private void deleteCollection() => collection.PerformWrite(c => c.Realm!.Remove(c));
        }

        public IEnumerable<LocalisableString> FilterTerms => Model.PerformRead(m => m.IsValid ? new[] { (LocalisableString)m.Name } : []);

        private bool matchingFilter = true;

        public bool MatchingFilter
        {
            get => matchingFilter;
            set
            {
                matchingFilter = value;
                updateVisibility();
            }
        }

        private bool filteringActive;

        public bool FilteringActive
        {
            get => filteringActive;
            set
            {
                filteringActive = value;
                updateVisibility();
            }
        }

        private void updateVisibility()
        {
            bool visible = matchingFilter && (filteringActive || inCurrentView || folderPopoverOpen);

            if (visible)
                this.FadeIn(200);
            else
                Hide();
        }
    }
}
