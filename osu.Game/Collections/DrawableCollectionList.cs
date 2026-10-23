// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Database;
using osu.Game.Graphics.Containers;
using osuTK;
using Realms;

namespace osu.Game.Collections
{
    /// <summary>
    /// Visualises a list of <see cref="BeatmapCollection"/>s.
    /// </summary>
    public partial class DrawableCollectionList : OsuRearrangeableListContainer<Live<BeatmapCollection>>
    {
        public new MarginPadding Padding
        {
            get => base.Padding;
            set => base.Padding = value;
        }

        protected override ScrollContainer<Drawable> CreateScrollContainer() => scroll = new Scroll();

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private CollectionFolderManager? collectionFolders { get; set; }

        /// <summary>
        /// When present, only collections inside the folder being viewed are shown, below a grid of the folders inside it.
        /// </summary>
        [Resolved]
        private CollectionFolderBrowser? browser { get; set; }

        private CollectionFolderGrid? folderGrid;

        private Scroll scroll = null!;

        private IDisposable? realmSubscription;

        private Flow flow = null!;

        public IEnumerable<Drawable> OrderedItems => flow.FlowingChildren;

        public string SearchTerm
        {
            get => flow.SearchTerm;
            set => flow.SearchTerm = value;
        }

        protected override FillFlowContainer<RearrangeableListItem<Live<BeatmapCollection>>> CreateListFillFlowContainer() => flow = new Flow
        {
            DragActive = { BindTarget = DragActive }
        };

        [BackgroundDependencyLoader]
        private void load()
        {
            if (browser != null && collectionFolders != null)
                scroll.AddHeader(folderGrid = new CollectionFolderGrid());
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            realmSubscription = realm.RegisterForNotifications(r => r.All<BeatmapCollection>().OrderBy(c => c.Name), collectionsChanged);

            if (collectionFolders != null)
                collectionFolders.FoldersChanged += onFoldersChanged;

            browser?.CurrentFolder.BindValueChanged(_ => updateVisibility());
        }

        /// <summary>
        /// When non-null, signifies that a new collection was created and should be presented to the user.
        /// </summary>
        private Guid? lastCreated;

        protected override void OnItemsChanged()
        {
            base.OnItemsChanged();

            if (lastCreated != null)
            {
                var createdItem = flow.Children.SingleOrDefault(item => item.Model.Value.ID == lastCreated);

                if (createdItem != null)
                {
                    ScheduleAfterChildren(() => scroll.ScrollIntoView(createdItem));
                }

                lastCreated = null;
            }
        }

        private IRealmCollection<BeatmapCollection>? currentCollections;

        private void collectionsChanged(IRealmCollection<BeatmapCollection> collections, ChangeSet? changes)
        {
            currentCollections = collections;

            collectionFolders?.RemoveMissingCollections(collections.AsEnumerable().Select(c => c.ID));

            if (changes?.InsertedIndices.Length == 1)
                lastCreated = collections[changes.InsertedIndices[0]].ID;

            var modified = changes?.NewModifiedIndices.Select(i => collections[i].ID).ToHashSet();

            updateItems(modified);
        }

        private void onFoldersChanged() => Scheduler.AddOnce(updateVisibility);

        /// <summary>
        /// Whether a collection is inside the folder being viewed.
        /// </summary>
        private bool isInView(Guid collectionId) => browser == null || collectionFolders?.IsDirectlyInside(collectionId, browser.CurrentFolder.Value) != false;

        /// <summary>
        /// Shows only the collections inside the folder being viewed.
        /// Items are kept rather than recreated, so that any open folder popover stays open.
        /// </summary>
        private void updateVisibility()
        {
            int visible = 0;

            foreach (var item in flow.Children.OfType<DrawableCollectionListItem>())
            {
                item.InCurrentView = isInView(item.Model.ID);

                if (item.InCurrentView)
                    visible++;
            }

            if (folderGrid != null)
                folderGrid.VisibleCollections = visible;
        }

        /// <summary>
        /// Recounts how many beatmaps of each collection are available locally (which changes as beatmaps are downloaded or deleted).
        /// </summary>
        public void RefreshCounts()
        {
            foreach (var item in flow.Children.OfType<DrawableCollectionListItem>())
                item.RefreshCount();
        }

        private List<BeatmapCollection>? getOrderedCollections() => currentCollections?.AsEnumerable().ToList();

        /// <summary>
        /// Updates the displayed items.
        /// </summary>
        /// <param name="modified">If non-null, only these collections need refreshing unless the ordering has changed.</param>
        private void updateItems(HashSet<Guid>? modified)
        {
            var ordered = getOrderedCollections();

            if (ordered == null)
                return;

            if (modified != null && ordered.Select(c => c.ID).SequenceEqual(Items.Select(i => i.ID)))
            {
                for (int i = 0; i < ordered.Count; i++)
                {
                    if (!modified.Contains(ordered[i].ID))
                        continue;

                    Items.RemoveAt(i);
                    Items.Insert(i, ordered[i].ToLive(realm));
                }

                Scheduler.AddOnce(updateVisibility);
                return;
            }

            Items.Clear();
            Items.AddRange(ordered.Select(c => c.ToLive(realm)));
            Scheduler.AddOnce(updateVisibility);
        }

        protected override OsuRearrangeableListItem<Live<BeatmapCollection>> CreateOsuDrawable(Live<BeatmapCollection> item) =>
            new DrawableCollectionListItem(item, true) { InCurrentView = isInView(item.ID) };

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            realmSubscription?.Dispose();

            if (collectionFolders != null)
                collectionFolders.FoldersChanged -= onFoldersChanged;
        }

        /// <summary>
        /// The scroll container for this <see cref="DrawableCollectionList"/>.
        /// Contains the main flow of <see cref="DrawableCollectionListItem"/> and attaches a placeholder item to the end of the list.
        /// </summary>
        private partial class Scroll : OsuScrollContainer
        {
            protected override Container<Drawable> Content => content;
            private readonly FillFlowContainer content;

            /// <summary>
            /// Adds a drawable above the list.
            /// </summary>
            public void AddHeader(Drawable header)
            {
                content.Add(header);
                content.SetLayoutPosition(header, -1);
            }

            public Scroll()
            {
                ScrollbarOverlapsContent = false;

                base.Content.Add(content = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    LayoutDuration = 200,
                    LayoutEasing = Easing.OutQuint,
                });
            }
        }

        /// <summary>
        /// The flow of <see cref="DrawableCollectionListItem"/>. Disables layout easing unless a drag is in progress.
        /// </summary>
        private partial class Flow : SearchContainer<RearrangeableListItem<Live<BeatmapCollection>>>
        {
            public readonly IBindable<bool> DragActive = new Bindable<bool>();

            public Flow()
            {
                Spacing = new Vector2(0, 5);
                LayoutEasing = Easing.OutQuint;

                Padding = new MarginPadding { Right = 5 };
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();
                DragActive.BindValueChanged(active => LayoutDuration = active.NewValue ? 200 : 0);
            }
        }
    }
}
