// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;
using osuTK;
using Realms;

namespace osu.Game.Screens.Select
{
    /// <summary>
    /// A dropdown to select the collection to be used to filter results.
    /// </summary>
    public partial class CollectionDropdown : ShearedDropdown<CollectionFilterMenuItem> // TODO: partial class under FilterControl?
    {
        /// <summary>
        /// Whether to show the "manage collections..." menu item in the dropdown.
        /// </summary>
        protected virtual bool ShowManageCollectionsItem => true;

        private readonly BindableList<CollectionFilterMenuItem> filters = new BindableList<CollectionFilterMenuItem>();
        private readonly Bindable<string> configCollectionFilter = new Bindable<string>();

        [Resolved]
        private ManageCollectionsDialog? manageCollectionsDialog { get; set; }

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private CollectionFolderManager? collectionFolders { get; set; }

        private IDisposable? realmSubscription;

        private readonly CollectionFilterMenuItem allBeatmapsItem = new AllBeatmapsCollectionFilterMenuItem();

        public CollectionDropdown()
            : base(CollectionsStrings.Collection)
        {
            ItemSource = filters;

            Current.Value = allBeatmapsItem;
            AlwaysShowSearchBar = true;
        }

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager configManager)
        {
            configManager.BindWith(OsuSetting.SongSelectCollectionFilter, configCollectionFilter);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            realmSubscription = realm.RegisterForNotifications(r => r.All<BeatmapCollection>().OrderBy(c => c.Name), collectionsChanged);

            if (collectionFolders != null)
                collectionFolders.FoldersChanged += onFoldersChanged;

            Current.BindValueChanged(selectionChanged);
        }

        private IRealmCollection<BeatmapCollection>? currentCollections;

        private void collectionsChanged(IRealmCollection<BeatmapCollection> collections, ChangeSet? changes)
        {
            currentCollections = collections;
            rebuildFilters();
        }

        private void onFoldersChanged() => Scheduler.AddOnce(rebuildFilters);

        private void rebuildFilters()
        {
            if (currentCollections == null)
                return;

            var items = new List<CollectionFilterMenuItem> { allBeatmapsItem };
            var collections = currentCollections.ToList();
            var listedCollections = new HashSet<Guid>();

            addFolderContents(null, Array.Empty<Guid>());

            // Adds the folders and collections inside a folder (or the top level if null), with each folder followed by its contents.
            void addFolderContents(Guid? folderId, IReadOnlyList<Guid> ancestors)
            {
                if (collectionFolders != null)
                {
                    foreach (var folder in collectionFolders.GetChildFolders(folderId))
                    {
                        var inside = collectionFolders.GetCollections(folder.ID, recursive: true).ToHashSet();
                        int count = collections.Count(c => inside.Contains(c.ID));

                        // Folders which don't contain any collections (even in subfolders) have nothing to filter by.
                        if (count == 0)
                            continue;

                        items.Add(new CollectionFolderFilterMenuItem(folder, count, ancestors));
                        addFolderContents(folder.ID, ancestors.Append(folder.ID).ToArray());
                    }
                }

                foreach (var collection in collections)
                {
                    if (collectionFolders?.IsDirectlyInside(collection.ID, folderId) == false)
                        continue;

                    // A collection inside multiple folders gets an entry in each.
                    items.Add(new CollectionFilterMenuItem(collection.ToLive(realm), ancestors, isPrimary: listedCollections.Add(collection.ID)));
                }
            }

            if (ShowManageCollectionsItem)
                items.Add(new ManageCollectionsFilterMenuItem());

            if (filters.Count == 0)
            {
                filters.AddRange(items);
                Current.Value = CollectionFolderManager.TryParseFolderFilterValue(configCollectionFilter.Value, out var folderId)
                    ? filters.OfType<CollectionFolderFilterMenuItem>().FirstOrDefault(f => f.FolderID == folderId) ?? allBeatmapsItem
                    : filters.FirstOrDefault(item => item.Collection?.PerformRead(c => c.ID.ToString()) == configCollectionFilter.Value) ?? allBeatmapsItem;
                return;
            }

            var selectedItem = Current.Value;

            // Items are matched by collection ID and folder, so the current selection is retained across the replacement
            // as long as the selected collection is still in the same folder.
            filters.ReplaceRange(0, filters.Count, items);

            CollectionFilterMenuItem? updatedItem;

            if (selectedItem is CollectionFolderFilterMenuItem selectedFolder)
            {
                updatedItem = filters.FirstOrDefault(f => f.Equals(selectedFolder));

                // The selected folder was deleted, or no longer contains any collections.
                if (updatedItem == null)
                {
                    Current.Value = allBeatmapsItem;
                    return;
                }
            }
            else
            {
                updatedItem = selectedItem?.Collection == null
                    ? null
                    : filters.FirstOrDefault(f => f.Equals(selectedItem))
                      ?? filters.FirstOrDefault(f => f is not CollectionFolderFilterMenuItem && f.Collection?.ID == selectedItem.Collection.ID);
            }

            bool selectionLost = updatedItem != null && !updatedItem.Equals(Current.Value);

            if (updatedItem != null && (selectionLost || !updatedItem.CollectionName.Equals(selectedItem!.CollectionName)))
            {
                // This current update and schedule is required to work around dropdown headers not updating text even when the selected item
                // changes. It's not great but honestly the whole dropdown menu structure isn't great. This needs to be fixed, but I'll issue
                // a warning that it's going to be a frustrating journey.
                Current.Value = allBeatmapsItem;
                Schedule(() =>
                {
                    // current may have changed before the scheduled call is run.
                    if (Current.Value != allBeatmapsItem)
                        return;

                    Current.Value = filters.FirstOrDefault(f => f.Equals(updatedItem)) ?? filters[0];
                });
            }
        }

        private void selectionChanged(ValueChangedEvent<CollectionFilterMenuItem> filter)
        {
            // May be null during .Clear().
            if (filter.NewValue.IsNull())
                return;

            switch (filter.NewValue)
            {
                case CollectionFolderFilterMenuItem folderItem:
                    configCollectionFilter.Value = CollectionFolderManager.GetFolderFilterValue(folderItem.FolderID);
                    break;

                case ManageCollectionsFilterMenuItem:
                    // Never select the manage collection filter - rollback to the previous filter.
                    // This is done after the above since it is important that bindable is unbound from OldValue, which is lost after forcing it back to the old value.
                    Current.Value = filter.OldValue;
                    manageCollectionsDialog?.Show();
                    break;

                case CollectionFilterMenuItem collectionMenuItem when collectionMenuItem.Collection != null:
                    configCollectionFilter.Value = collectionMenuItem.Collection.PerformRead(c => c.ID.ToString());
                    break;

                case AllBeatmapsCollectionFilterMenuItem:
                    configCollectionFilter.Value = string.Empty;
                    break;
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            realmSubscription?.Dispose();

            if (collectionFolders != null)
                collectionFolders.FoldersChanged -= onFoldersChanged;
        }

        protected override LocalisableString GenerateItemText(CollectionFilterMenuItem item) => item.CollectionName;

        protected sealed override DropdownMenu CreateMenu() => CreateCollectionMenu();

        protected virtual ShearedCollectionDropdownMenu CreateCollectionMenu() => new ShearedCollectionDropdownMenu();

        protected partial class ShearedCollectionDropdownMenu : ShearedDropdownMenu
        {
            public ShearedCollectionDropdownMenu()
            {
                MaxHeight = 200;
            }

            protected override DrawableDropdownMenuItem CreateDrawableDropdownMenuItem(MenuItem item)
            {
                if (((DropdownMenuItem<CollectionFilterMenuItem>)item).Value is CollectionFolderFilterMenuItem)
                {
                    return new DrawableFolderMenuItem(item)
                    {
                        BackgroundColourHover = HoverColour,
                        BackgroundColourSelected = SelectionColour
                    };
                }

                return new DrawableCollectionMenuItem(item)
                {
                    BackgroundColourHover = HoverColour,
                    BackgroundColourSelected = SelectionColour
                };
            }
        }

        protected partial class DrawableCollectionMenuItem : ShearedDropdownMenu.ShearedMenuItem
        {
            private IconButton addOrRemoveButton = null!;

            private bool beatmapInCollection;

            private readonly Live<BeatmapCollection>? collection;

            private readonly IReadOnlyList<Guid> ancestorFolders;

            private readonly bool isPrimary;

            [Resolved]
            private IBindable<WorkingBeatmap> beatmap { get; set; } = null!;

            [Resolved]
            private CollectionFolderManager? collectionFolders { get; set; }

            private readonly FolderExpansion folderExpanded = new FolderExpansion();

            private bool matchingFilter = true;
            private bool filteringActive;

            public DrawableCollectionMenuItem(MenuItem item)
                : base(item)
            {
                var value = ((DropdownMenuItem<CollectionFilterMenuItem>)item).Value;

                collection = value.Collection;
                ancestorFolders = value.AncestorFolderIDs;
                isPrimary = value.IsPrimary;
            }

            public override bool FilteringActive
            {
                set
                {
                    filteringActive = value;
                    updateFolderState();
                }
            }

            protected override void UpdateFilteringState(bool filtered)
            {
                matchingFilter = filtered;
                updateFolderState();
            }

            /// <summary>
            /// Collections inside a collapsed folder are hidden, except while searching (where folders are ignored entirely).
            /// While searching, collections inside multiple folders are only shown once.
            /// </summary>
            private void updateFolderState()
            {
                bool visible = filteringActive
                    ? matchingFilter && isPrimary
                    : folderExpanded.Value;

                this.FadeTo(visible ? 1 : 0);

                if (Foreground.Children.FirstOrDefault() is Content content)
                    content.Label.Padding = new MarginPadding { Left = filteringActive || ancestorFolders.Count == 0 ? 15 : FolderIndent(ancestorFolders.Count) + 20 };
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                AddInternal(addOrRemoveButton = new NoFocusChangeIconButton
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    Shear = -OsuGame.SHEAR,
                    X = -OsuScrollContainer.SCROLL_BAR_WIDTH,
                    Scale = new Vector2(0.65f),
                    Action = addOrRemove,
                });
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                folderExpanded.Bind(collectionFolders, ancestorFolders);
                folderExpanded.BindValueChanged(_ => updateFolderState(), true);

                if (collection != null)
                {
                    beatmap.BindValueChanged(_ =>
                    {
                        beatmapInCollection = collection.PerformRead(c => c.BeatmapMD5Hashes.Contains(beatmap.Value.BeatmapInfo.MD5Hash));

                        addOrRemoveButton.Enabled.Value = !beatmap.IsDefault;
                        addOrRemoveButton.Icon = beatmapInCollection ? FontAwesome.Solid.MinusSquare : FontAwesome.Solid.PlusSquare;
                        addOrRemoveButton.TooltipText = beatmapInCollection ? CollectionsStrings.RemoveSelectedBeatmap : CollectionsStrings.AddSelectedBeatmap;

                        updateButtonVisibility();
                    }, true);
                }

                updateButtonVisibility();
            }

            protected override bool OnHover(HoverEvent e)
            {
                updateButtonVisibility();
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                updateButtonVisibility();
                base.OnHoverLost(e);
            }

            protected override void OnSelectChange()
            {
                base.OnSelectChange();
                updateButtonVisibility();
            }

            private void updateButtonVisibility()
            {
                if (collection == null)
                    addOrRemoveButton.Alpha = 0;
                else
                    addOrRemoveButton.Alpha = IsHovered || IsPreSelected || beatmapInCollection ? 1 : 0;
            }

            private void addOrRemove()
            {
                Debug.Assert(collection != null);

                Task.Run(() => collection.PerformWrite(c =>
                {
                    if (!c.BeatmapMD5Hashes.Remove(beatmap.Value.BeatmapInfo.MD5Hash))
                        c.BeatmapMD5Hashes.Add(beatmap.Value.BeatmapInfo.MD5Hash);
                }));
            }

            protected override Drawable CreateContent() => (Content)base.CreateContent();

            private partial class NoFocusChangeIconButton : IconButton
            {
                public override bool ChangeFocusOnClick => false;
            }
        }

        /// <summary>
        /// The left padding of the icon of a folder nested inside the given number of folders (or the label of a collection nested inside one fewer).
        /// </summary>
        protected static float FolderIndent(int depth) => 15 + 20 * Math.Max(0, depth - 1);

        /// <summary>
        /// A folder of collections. Clicking the folder icon expands or collapses the folder, and clicking anywhere else selects it,
        /// showing all beatmaps inside the folder's collections (including those in subfolders).
        /// </summary>
        protected partial class DrawableFolderMenuItem : ShearedDropdownMenu.ShearedMenuItem
        {
            private readonly Guid folderId;

            private readonly IReadOnlyList<Guid> ancestorFolders;

            private readonly BindableBool expanded = new BindableBool();

            private readonly FolderExpansion ancestorsExpanded = new FolderExpansion();

            [Resolved]
            private CollectionFolderManager? collectionFolders { get; set; }

            private ExpandButton expandButton = null!;

            private bool matchingFilter = true;
            private bool filteringActive;

            public DrawableFolderMenuItem(MenuItem item)
                : base(item)
            {
                var folder = (CollectionFolderFilterMenuItem)((DropdownMenuItem<CollectionFilterMenuItem>)item).Value;

                folderId = folder.FolderID;
                ancestorFolders = folder.AncestorFolderIDs;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                var content = (FolderContent)Foreground.Children.First();

                content.Label.Font = OsuFont.GetFont(weight: FontWeight.Bold);
                content.Add(expandButton = new ExpandButton
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Action = () => expanded.Toggle(),
                });
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                if (collectionFolders != null)
                    expanded.BindTo(collectionFolders.GetExpanded(folderId));

                expanded.BindValueChanged(e => expandButton.Icon.Icon = e.NewValue ? FontAwesome.Solid.FolderOpen : FontAwesome.Solid.Folder, true);

                ancestorsExpanded.Bind(collectionFolders, ancestorFolders);
                ancestorsExpanded.BindValueChanged(_ => updateState(), true);
            }

            public override bool FilteringActive
            {
                set
                {
                    filteringActive = value;
                    updateState();
                }
            }

            protected override void UpdateFilteringState(bool filtered)
            {
                matchingFilter = filtered;
                updateState();
            }

            /// <summary>
            /// Folders inside a collapsed folder are hidden. While searching, folders are shown if their name matches.
            /// </summary>
            private void updateState()
            {
                this.FadeTo(filteringActive ? (matchingFilter ? 1 : 0) : (ancestorsExpanded.Value ? 1 : 0));

                if (Foreground.Children.FirstOrDefault() is not FolderContent content)
                    return;

                float indent = filteringActive ? 15 : FolderIndent(ancestorFolders.Count + 1);

                expandButton.X = indent;
                content.Label.Padding = new MarginPadding { Left = indent + 20 };
            }

            protected override Drawable CreateContent() => new FolderContent();

            private partial class FolderContent : Content
            {
                public void Add(Drawable drawable) => AddInternal(drawable);
            }

            /// <summary>
            /// The folder icon, which expands or collapses the folder when clicked (without selecting it).
            /// </summary>
            public partial class ExpandButton : CompositeDrawable
            {
                public Action? Action;

                public readonly SpriteIcon Icon;

                public ExpandButton()
                {
                    Size = new Vector2(20);

                    InternalChild = Icon = new SpriteIcon
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Size = new Vector2(13),
                    };
                }

                // Keep focus on the dropdown's search bar, so that the menu stays open when toggling folders.
                public override bool ChangeFocusOnClick => false;

                protected override bool OnHover(HoverEvent e)
                {
                    Icon.ScaleTo(1.2f, 100, Easing.OutQuint);
                    return true;
                }

                protected override void OnHoverLost(HoverLostEvent e) => Icon.ScaleTo(1, 100, Easing.OutQuint);

                protected override bool OnClick(ClickEvent e)
                {
                    Action?.Invoke();
                    return true;
                }
            }
        }

        /// <summary>
        /// Whether all of a set of folders are expanded (i.e. whether something nested inside all of them should be visible).
        /// </summary>
        protected class FolderExpansion : Bindable<bool>
        {
            private readonly List<IBindable<bool>> folders = new List<IBindable<bool>>();

            public FolderExpansion()
                : base(true)
            {
            }

            public void Bind(CollectionFolderManager? manager, IEnumerable<Guid> folderIds)
            {
                if (manager == null)
                    return;

                foreach (var id in folderIds)
                {
                    var expanded = manager.GetExpanded(id).GetBoundCopy();
                    expanded.BindValueChanged(_ => update());
                    folders.Add(expanded);
                }

                update();
            }

            private void update() => Value = folders.All(f => f.Value);
        }
    }
}
