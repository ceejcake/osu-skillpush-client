// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Collections
{
    /// <summary>
    /// Holds the state of the folder view in <see cref="ManageCollectionsDialog"/>: the folder being viewed, and any folder or collection being dragged.
    /// Also displays whatever is being dragged, so this should be placed above everything else in the dialog.
    /// </summary>
    public partial class CollectionFolderBrowser : Container
    {
        /// <summary>
        /// The folder being viewed, or null for the top level.
        /// </summary>
        public readonly Bindable<Guid?> CurrentFolder = new Bindable<Guid?>();

        /// <summary>
        /// The current search term. While searching, matching folders and collections are shown regardless of which folder they're inside.
        /// </summary>
        public readonly Bindable<string> SearchTerm = new Bindable<string>(string.Empty);

        /// <summary>
        /// The item currently being dragged, if any.
        /// </summary>
        public CollectionDragItem? DraggedItem { get; private set; }

        [Resolved]
        private CollectionFolderManager? collectionFolders { get; set; }

        private readonly List<ICollectionDropTarget> dropTargets = new List<ICollectionDropTarget>();

        private ICollectionDropTarget? hoveredTarget;

        private DragProxy? proxy;

        public CollectionFolderBrowser()
        {
            RelativeSizeAxes = Axes.Both;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (collectionFolders != null)
                collectionFolders.FoldersChanged += onFoldersChanged;
        }

        // Only displays dragged items, and should never block input.
        public override bool PropagatePositionalInputSubTree => false;

        public void RegisterDropTarget(ICollectionDropTarget target) => dropTargets.Add(target);

        public void UnregisterDropTarget(ICollectionDropTarget target)
        {
            dropTargets.Remove(target);

            if (hoveredTarget == target)
                hoveredTarget = null;
        }

        public void BeginDrag(CollectionDragItem item, Vector2 screenSpacePosition)
        {
            EndDrag(null);

            DraggedItem = item;
            Add(proxy = new DragProxy(item));
            UpdateDrag(screenSpacePosition);
        }

        public void UpdateDrag(Vector2 screenSpacePosition)
        {
            if (DraggedItem == null || proxy == null)
                return;

            proxy.Position = ToLocalSpace(screenSpacePosition) + new Vector2(12);

            var target = dropTargets.LastOrDefault(t => t.IsPresent && t.AcceptsDropAt(screenSpacePosition) && t.CanAccept(DraggedItem));

            if (target == hoveredTarget)
                return;

            hoveredTarget?.SetDropHighlight(false);
            hoveredTarget = target;
            hoveredTarget?.SetDropHighlight(true);
        }

        /// <summary>
        /// Finishes the current drag, dropping the dragged item onto whatever is under the cursor.
        /// </summary>
        /// <param name="screenSpacePosition">The position to drop at, or null to cancel the drag.</param>
        public void EndDrag(Vector2? screenSpacePosition)
        {
            if (screenSpacePosition != null)
                UpdateDrag(screenSpacePosition.Value);

            var item = DraggedItem;
            var target = screenSpacePosition != null ? hoveredTarget : null;

            hoveredTarget?.SetDropHighlight(false);
            hoveredTarget = null;
            DraggedItem = null;

            proxy?.FadeOut(100).Expire();
            proxy = null;

            if (item != null)
                target?.Accept(item);
        }

        private void onFoldersChanged() => Schedule(() =>
        {
            // The folder being viewed was deleted.
            if (CurrentFolder.Value is Guid current && collectionFolders?.GetFolder(current) == null)
                CurrentFolder.Value = null;
        });

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (collectionFolders != null)
                collectionFolders.FoldersChanged -= onFoldersChanged;
        }

        private partial class DragProxy : CompositeDrawable
        {
            private readonly CollectionDragItem item;

            public DragProxy(CollectionDragItem item)
            {
                this.item = item;

                AutoSizeAxes = Axes.Both;
                Masking = true;
                CornerRadius = 6;
                Alpha = 0.9f;
            }

            [BackgroundDependencyLoader]
            private void load(OsuColour colours)
            {
                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = colours.GreySeaFoamLight,
                    },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(6, 0),
                        Padding = new MarginPadding { Horizontal = 10, Vertical = 6 },
                        Children = new Drawable[]
                        {
                            new SpriteIcon
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Size = new Vector2(14),
                                Icon = item.IsFolder ? FontAwesome.Solid.Folder : FontAwesome.Solid.List,
                                Colour = item.IsFolder ? colours.Yellow : Color4.White,
                            },
                            new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = item.Name,
                                Font = OsuFont.GetFont(size: 15, weight: FontWeight.SemiBold),
                            },
                        }
                    },
                };
            }
        }
    }

    /// <summary>
    /// A folder or collection being dragged in <see cref="ManageCollectionsDialog"/>.
    /// </summary>
    public class CollectionDragItem
    {
        /// <summary>
        /// The folder being dragged, if a folder is being dragged.
        /// </summary>
        public Guid? FolderID { get; private init; }

        /// <summary>
        /// The collection being dragged, if a collection is being dragged.
        /// </summary>
        public Guid? CollectionID { get; private init; }

        /// <summary>
        /// The folder the collection is being dragged out of, or null if it is being dragged from the top level.
        /// </summary>
        public Guid? SourceFolderID { get; private init; }

        public string Name { get; private init; } = string.Empty;

        public bool IsFolder => FolderID != null;

        public static CollectionDragItem ForFolder(CollectionFolder folder) => new CollectionDragItem { FolderID = folder.ID, Name = folder.Name };

        public static CollectionDragItem ForCollection(Guid collectionId, string name, Guid? sourceFolderId) =>
            new CollectionDragItem { CollectionID = collectionId, Name = name, SourceFolderID = sourceFolderId };
    }

    /// <summary>
    /// Something which folders or collections can be dropped onto.
    /// </summary>
    public interface ICollectionDropTarget
    {
        bool IsPresent { get; }

        /// <summary>
        /// Whether a drop at the given position would land on this target (taking into account any masking by parents).
        /// </summary>
        bool AcceptsDropAt(Vector2 screenSpacePosition);

        bool CanAccept(CollectionDragItem item);

        void Accept(CollectionDragItem item);

        void SetDropHighlight(bool highlighted);
    }

    /// <summary>
    /// Moves dragged folders or collections into a target folder (or the top level if the target is null).
    /// </summary>
    public static class CollectionDropHandling
    {
        public static bool CanAccept(CollectionFolderManager manager, CollectionDragItem item, Guid? targetFolderId)
        {
            if (item.FolderID is Guid folderId)
                return manager.CanMoveFolder(folderId, targetFolderId);

            if (item.CollectionID is Guid collectionId)
                return targetFolderId != item.SourceFolderID && !(targetFolderId is Guid target && manager.IsInFolder(collectionId, target));

            return false;
        }

        public static void Accept(CollectionFolderManager manager, CollectionDragItem item, Guid? targetFolderId)
        {
            if (item.FolderID is Guid folderId)
                manager.MoveFolder(folderId, targetFolderId);
            else if (item.CollectionID is Guid collectionId)
                manager.MoveCollection(collectionId, item.SourceFolderID, targetFolderId);
        }
    }

    /// <summary>
    /// The folders inside the folder being viewed, shown as a grid of icons.
    /// Clicking a folder opens it, and folders or collections can be dragged onto a folder to move them inside it.
    /// </summary>
    public partial class CollectionFolderGrid : CompositeDrawable
    {
        [Resolved]
        private CollectionFolderBrowser browser { get; set; } = null!;

        [Resolved]
        private CollectionFolderManager collectionFolders { get; set; } = null!;

        private FillFlowContainer<DrawableCollectionFolder> flow = null!;
        private OsuSpriteText collectionsLabel = null!;

        private readonly BindableInt visibleCollections = new BindableInt();

        /// <summary>
        /// The number of collections shown below the grid, used to show a message when there are none.
        /// </summary>
        public int VisibleCollections
        {
            set => visibleCollections.Value = value;
        }

        public CollectionFolderGrid()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
        }

        [BackgroundDependencyLoader]
        private void load(OsuColour colours)
        {
            InternalChild = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 10),
                Padding = new MarginPadding { Bottom = 10, Right = 5 },
                Children = new Drawable[]
                {
                    flow = new FillFlowContainer<DrawableCollectionFolder>
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Spacing = new Vector2(10),
                    },
                    collectionsLabel = new OsuSpriteText
                    {
                        Font = OsuFont.GetFont(size: 13, weight: FontWeight.SemiBold),
                        Colour = colours.GreySeaFoamLighter,
                    },
                }
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            collectionFolders.FoldersChanged += onFoldersChanged;

            browser.CurrentFolder.BindValueChanged(_ => populate());
            browser.SearchTerm.BindValueChanged(_ => populate());
            visibleCollections.BindValueChanged(_ => updateLabel());

            populate();
        }

        private void onFoldersChanged() => Scheduler.AddOnce(populate);

        private void populate()
        {
            string search = browser.SearchTerm.Value.Trim();
            bool searching = !string.IsNullOrEmpty(search);

            var shown = searching
                ? collectionFolders.Folders.Where(f => f.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                : collectionFolders.GetChildFolders(browser.CurrentFolder.Value);

            flow.ChildrenEnumerable = shown.Select(f => new DrawableCollectionFolder(f, showLocation: searching));
            flow.Alpha = flow.Count > 0 ? 1 : 0;

            updateLabel();
        }

        private void updateLabel()
        {
            if (!string.IsNullOrEmpty(browser.SearchTerm.Value.Trim()))
                collectionsLabel.Text = "Search results";
            else if (visibleCollections.Value == 0)
                collectionsLabel.Text = browser.CurrentFolder.Value == null ? "No collections outside of folders." : "No collections in this folder. New collections created below go here.";
            else
                collectionsLabel.Text = "Collections (drag by the handle onto a folder, or onto the path above, to move)";
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (collectionFolders.IsNotNull())
                collectionFolders.FoldersChanged -= onFoldersChanged;
        }
    }

    /// <summary>
    /// A folder icon in a <see cref="CollectionFolderGrid"/>.
    /// </summary>
    public partial class DrawableCollectionFolder : OsuClickableContainer, ICollectionDropTarget, IHasPopover
    {
        public readonly CollectionFolder Folder;

        private readonly bool showLocation;

        [Resolved]
        private CollectionFolderBrowser browser { get; set; } = null!;

        [Resolved]
        private CollectionFolderManager collectionFolders { get; set; } = null!;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        private Box background = null!;
        private SpriteIcon icon = null!;
        private FillFlowContainer buttons = null!;

        private bool dropHighlighted;

        /// <param name="folder">The folder.</param>
        /// <param name="showLocation">Whether to show where the folder is, rather than what is inside it (used while searching).</param>
        public DrawableCollectionFolder(CollectionFolder folder, bool showLocation = false)
        {
            Folder = folder;
            this.showLocation = showLocation;

            Size = new Vector2(120, 110);
            Masking = true;
            CornerRadius = 8;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            int folderCount = collectionFolders.GetChildFolders(Folder.ID).Count;
            int collectionCount = collectionFolders.GetCollections(Folder.ID).Count;
            int itemCount = folderCount + collectionCount;

            string subtitle = showLocation
                ? Folder.ParentID is Guid parent ? $"in {collectionFolders.GetPathName(parent)}" : "top level"
                : itemCount == 0
                    ? "empty"
                    : itemCount == 1
                        ? "1 item"
                        : $"{itemCount} items";

            TooltipText = $"{collectionFolders.GetPathName(Folder.ID)} ({folderCount} folders, {collectionCount} collections)";

            Children = new Drawable[]
            {
                background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = colours.GreySeaFoamDark,
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 3),
                    Children = new Drawable[]
                    {
                        icon = new SpriteIcon
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Size = new Vector2(44),
                            Icon = FontAwesome.Solid.Folder,
                            Colour = colours.Yellow,
                            Margin = new MarginPadding { Bottom = 4 },
                        },
                        new TruncatingSpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            RelativeSizeAxes = Axes.X,
                            Padding = new MarginPadding { Horizontal = 6 },
                            Text = Folder.Name,
                            Font = OsuFont.GetFont(size: 15, weight: FontWeight.Bold),
                        },
                        new TruncatingSpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            RelativeSizeAxes = Axes.X,
                            Padding = new MarginPadding { Horizontal = 6 },
                            Text = subtitle,
                            Font = OsuFont.GetFont(size: 12),
                            Colour = colours.GreySeaFoamLighter,
                        },
                    }
                },
                buttons = new FillFlowContainer
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Alpha = 0,
                    Children = new Drawable[]
                    {
                        new IconButton
                        {
                            Icon = FontAwesome.Solid.PencilAlt,
                            TooltipText = "Rename folder",
                            Scale = new Vector2(0.6f),
                            Action = this.ShowPopover,
                        },
                        new IconButton
                        {
                            Icon = FontAwesome.Solid.Trash,
                            TooltipText = "Delete folder (everything inside is kept, and moves up a level)",
                            Scale = new Vector2(0.6f),
                            Action = () => collectionFolders.DeleteFolder(Folder.ID),
                        },
                    }
                },
            };

            Action = () =>
            {
                browser.CurrentFolder.Value = Folder.ID;
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            browser.RegisterDropTarget(this);
        }

        public Popover GetPopover() => new FolderNamePopover("Rename folder", Folder.Name, name => collectionFolders.RenameFolder(Folder.ID, name));

        protected override bool OnHover(HoverEvent e)
        {
            updateState();
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            updateState();
            base.OnHoverLost(e);
        }

        protected override bool OnDragStart(DragStartEvent e)
        {
            browser.BeginDrag(CollectionDragItem.ForFolder(Folder), e.ScreenSpaceMousePosition);
            this.FadeTo(0.5f, 100);
            return true;
        }

        protected override void OnDrag(DragEvent e) => browser.UpdateDrag(e.ScreenSpaceMousePosition);

        protected override void OnDragEnd(DragEndEvent e)
        {
            this.FadeIn(100);
            browser.EndDrag(e.ScreenSpaceMousePosition);
        }

        public bool AcceptsDropAt(Vector2 screenSpacePosition) =>
            ReceivePositionalInputAt(screenSpacePosition) && this.FindClosestParent<ScrollContainer<Drawable>>()?.ReceivePositionalInputAt(screenSpacePosition) != false;

        public bool CanAccept(CollectionDragItem item) => CollectionDropHandling.CanAccept(collectionFolders, item, Folder.ID);

        public void Accept(CollectionDragItem item) => CollectionDropHandling.Accept(collectionFolders, item, Folder.ID);

        public void SetDropHighlight(bool highlighted)
        {
            dropHighlighted = highlighted;
            updateState();
        }

        private void updateState()
        {
            Color4 colour = dropHighlighted ? colours.Blue3 : colours.GreySeaFoamDark;

            background.FadeColour(IsHovered && !dropHighlighted ? colour.Lighten(0.3f) : colour, 100, Easing.OutQuint);
            icon.Icon = dropHighlighted ? FontAwesome.Solid.FolderOpen : FontAwesome.Solid.Folder;
            buttons.FadeTo(IsHovered && browser.DraggedItem == null ? 1 : 0, 100);
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (browser.IsNotNull())
                browser.UnregisterDropTarget(this);
        }
    }

    /// <summary>
    /// The path to the folder being viewed. Clicking a part of the path goes back to that folder,
    /// and folders or collections can be dragged onto a part of the path to move them there.
    /// Also has a button to create a new folder inside the folder being viewed.
    /// </summary>
    public partial class CollectionFolderBreadcrumbs : CompositeDrawable
    {
        [Resolved]
        private CollectionFolderBrowser browser { get; set; } = null!;

        [Resolved]
        private CollectionFolderManager collectionFolders { get; set; } = null!;

        private FillFlowContainer<Segment> path = null!;

        public CollectionFolderBreadcrumbs()
        {
            RelativeSizeAxes = Axes.X;
            Height = 32;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Right = 80 },
                    Child = path = new FillFlowContainer<Segment>
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(2, 0),
                    },
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Children = new Drawable[]
                    {
                        new IconButton
                        {
                            Icon = FontAwesome.Solid.LevelUpAlt,
                            TooltipText = "Up a level",
                            Action = () =>
                            {
                                if (browser.CurrentFolder.Value is Guid current)
                                    browser.CurrentFolder.Value = collectionFolders.GetFolder(current)?.ParentID;
                            },
                        },
                        new NewFolderButton(),
                    }
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            collectionFolders.FoldersChanged += onFoldersChanged;
            browser.CurrentFolder.BindValueChanged(_ => populate(), true);
        }

        private void onFoldersChanged() => Scheduler.AddOnce(populate);

        private void populate()
        {
            var segments = new List<Segment> { new Segment(null, "All collections", isCurrent: browser.CurrentFolder.Value == null) };

            if (browser.CurrentFolder.Value is Guid current)
            {
                var folders = collectionFolders.GetPath(current);

                for (int i = 0; i < folders.Count; i++)
                    segments.Add(new Segment(folders[i].ID, folders[i].Name, isCurrent: i == folders.Count - 1));
            }

            path.ChildrenEnumerable = segments;
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (collectionFolders.IsNotNull())
                collectionFolders.FoldersChanged -= onFoldersChanged;
        }

        public partial class Segment : OsuClickableContainer, ICollectionDropTarget
        {
            public readonly Guid? FolderID;

            private readonly string name;
            private readonly bool isCurrent;

            [Resolved]
            private CollectionFolderBrowser browser { get; set; } = null!;

            [Resolved]
            private CollectionFolderManager collectionFolders { get; set; } = null!;

            [Resolved]
            private OsuColour colours { get; set; } = null!;

            private Box background = null!;

            private bool dropHighlighted;

            public Segment(Guid? folderId, string name, bool isCurrent)
            {
                FolderID = folderId;
                this.name = name;
                this.isCurrent = isCurrent;

                AutoSizeAxes = Axes.Both;
                Masking = true;
                CornerRadius = 5;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Children = new Drawable[]
                {
                    background = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Alpha = 0,
                    },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(5, 0),
                        Padding = new MarginPadding { Horizontal = 6, Vertical = 5 },
                        Children = new Drawable[]
                        {
                            new SpriteIcon
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Size = new Vector2(10),
                                Icon = FolderID == null ? FontAwesome.Solid.Home : FontAwesome.Solid.ChevronRight,
                                Colour = colours.GreySeaFoamLighter,
                            },
                            new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = name,
                                Font = OsuFont.GetFont(size: 15, weight: isCurrent ? FontWeight.Bold : FontWeight.Regular),
                            },
                        }
                    },
                };

                Action = () => browser.CurrentFolder.Value = FolderID;
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();
                browser.RegisterDropTarget(this);
            }

            protected override bool OnHover(HoverEvent e)
            {
                updateState();
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                updateState();
                base.OnHoverLost(e);
            }

            public bool AcceptsDropAt(Vector2 screenSpacePosition) => ReceivePositionalInputAt(screenSpacePosition);

            public bool CanAccept(CollectionDragItem item) => !isCurrent && CollectionDropHandling.CanAccept(collectionFolders, item, FolderID);

            public void Accept(CollectionDragItem item) => CollectionDropHandling.Accept(collectionFolders, item, FolderID);

            public void SetDropHighlight(bool highlighted)
            {
                dropHighlighted = highlighted;
                updateState();
            }

            private void updateState()
            {
                background.Colour = dropHighlighted ? colours.Blue3 : colours.GreySeaFoamLight;
                background.FadeTo(dropHighlighted || (IsHovered && !isCurrent) ? 1 : 0, 100);
            }

            protected override void Dispose(bool isDisposing)
            {
                base.Dispose(isDisposing);

                if (browser.IsNotNull())
                    browser.UnregisterDropTarget(this);
            }
        }

        public partial class NewFolderButton : IconButton, IHasPopover
        {
            [Resolved]
            private CollectionFolderBrowser browser { get; set; } = null!;

            [Resolved]
            private CollectionFolderManager collectionFolders { get; set; } = null!;

            public NewFolderButton()
            {
                Icon = FontAwesome.Solid.FolderPlus;
                TooltipText = "New folder";
                Action = this.ShowPopover;
            }

            public Popover GetPopover() => new FolderNamePopover("New folder", string.Empty, name => collectionFolders.CreateFolder(name, browser.CurrentFolder.Value));
        }
    }

    /// <summary>
    /// The handle used to drag a collection into a folder.
    /// </summary>
    public partial class CollectionDragHandle : CompositeDrawable, IHasTooltip
    {
        private readonly Guid collectionId;
        private readonly Func<string> getName;

        [Resolved]
        private CollectionFolderBrowser? browser { get; set; }

        [Resolved]
        private CollectionFolderManager? collectionFolders { get; set; }

        private SpriteIcon icon = null!;

        private Color4 idleColour;

        public LocalisableString TooltipText => "Drag onto a folder to move this collection";

        public CollectionDragHandle(Guid collectionId, Func<string> getName)
        {
            this.collectionId = collectionId;
            this.getName = getName;

            RelativeSizeAxes = Axes.Y;
            Width = 20;
        }

        [BackgroundDependencyLoader]
        private void load(OsuColour colours)
        {
            InternalChild = icon = new SpriteIcon
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(12),
                Icon = FontAwesome.Solid.GripVertical,
                Colour = idleColour = colours.GreySeaFoamLighter,
            };

            Alpha = browser == null ? 0 : 1;
        }

        protected override bool OnHover(HoverEvent e)
        {
            icon.FadeColour(Color4.White, 100);
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            icon.FadeColour(idleColour, 100);
            base.OnHoverLost(e);
        }

        protected override bool OnDragStart(DragStartEvent e)
        {
            if (browser == null)
                return false;

            string search = browser.SearchTerm.Value.Trim();

            // While searching, collections from all folders are shown, so the collection might not be inside the folder being viewed.
            Guid? source = browser.CurrentFolder.Value;

            if (!string.IsNullOrEmpty(search) && source is Guid current && collectionFolders?.IsInFolder(collectionId, current) != true)
                source = null;

            browser.BeginDrag(CollectionDragItem.ForCollection(collectionId, getName(), source), e.ScreenSpaceMousePosition);
            return true;
        }

        protected override void OnDrag(DragEvent e) => browser?.UpdateDrag(e.ScreenSpaceMousePosition);

        protected override void OnDragEnd(DragEndEvent e) => browser?.EndDrag(e.ScreenSpaceMousePosition);
    }

    /// <summary>
    /// A popover asking for the name of a folder.
    /// </summary>
    public partial class FolderNamePopover : OsuPopover
    {
        private readonly string title;
        private readonly string initialName;
        private readonly Action<string> onCommit;

        private OsuTextBox textBox = null!;

        public FolderNamePopover(string title, string initialName, Action<string> onCommit)
        {
            this.title = title;
            this.initialName = initialName;
            this.onCommit = onCommit;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Child = new FillFlowContainer
            {
                Width = 240,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(8),
                Children = new Drawable[]
                {
                    new OsuSpriteText
                    {
                        Text = title,
                        Font = OsuFont.GetFont(size: 18, weight: FontWeight.Bold),
                    },
                    textBox = new OsuTextBox
                    {
                        RelativeSizeAxes = Axes.X,
                        Text = initialName,
                        PlaceholderText = "Folder name (enter to confirm)",
                        CommitOnFocusLost = false,
                    },
                }
            };

            textBox.OnCommit += (_, _) =>
            {
                string name = textBox.Text.Trim();

                if (string.IsNullOrEmpty(name))
                    return;

                onCommit(name);
                this.HidePopover();
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            ScheduleAfterChildren(() =>
            {
                GetContainingFocusManager()?.ChangeFocus(textBox);
                textBox.SelectAll();
            });
        }
    }
}
