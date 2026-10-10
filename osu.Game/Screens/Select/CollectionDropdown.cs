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
            var unsorted = new List<CollectionFilterMenuItem>();
            var byFolder = new Dictionary<string, List<CollectionFilterMenuItem>>();

            foreach (var collection in currentCollections)
            {
                var live = collection.ToLive(realm);
                var collectionFolderNames = collectionFolders?.GetFolders(collection.ID) ?? Array.Empty<string>();

                if (collectionFolderNames.Count == 0)
                {
                    unsorted.Add(new CollectionFilterMenuItem(live));
                    continue;
                }

                // A collection inside multiple folders gets an entry in each.
                for (int i = 0; i < collectionFolderNames.Count; i++)
                {
                    string folder = collectionFolderNames[i];

                    if (!byFolder.TryGetValue(folder, out var list))
                        byFolder[folder] = list = new List<CollectionFilterMenuItem>();

                    list.Add(new CollectionFilterMenuItem(live, folder, isPrimary: i == 0));
                }
            }

            foreach (string folder in collectionFolders?.Folders ?? Array.Empty<string>())
            {
                if (!byFolder.TryGetValue(folder, out var list))
                    continue;

                items.Add(new CollectionFolderFilterMenuItem(folder, list.Count));
                items.AddRange(list);
            }

            items.AddRange(unsorted);

            if (ShowManageCollectionsItem)
                items.Add(new ManageCollectionsFilterMenuItem());

            if (filters.Count == 0)
            {
                filters.AddRange(items);
                Current.Value = filters.FirstOrDefault(item => item.Collection?.PerformRead(c => c.ID.ToString()) == configCollectionFilter.Value) ?? allBeatmapsItem;
                return;
            }

            var selectedItem = Current.Value;

            // Items are matched by collection ID and folder, so the current selection is retained across the replacement
            // as long as the selected collection is still in the same folder.
            filters.ReplaceRange(0, filters.Count, items);

            var updatedItem = selectedItem?.Collection == null
                ? null
                : filters.FirstOrDefault(f => f.Equals(selectedItem))
                  ?? filters.FirstOrDefault(f => f is not CollectionFolderFilterMenuItem && f.Collection?.ID == selectedItem.Collection.ID);

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
                    // Folders can't be selected (this is reached via keyboard selection) - toggle them instead.
                    Current.Value = filter.OldValue;
                    collectionFolders?.GetExpanded(folderItem.FolderName).Toggle();
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

            private readonly string? folder;

            private readonly bool isPrimary;

            [Resolved]
            private IBindable<WorkingBeatmap> beatmap { get; set; } = null!;

            [Resolved]
            private CollectionFolderManager? collectionFolders { get; set; }

            private readonly BindableBool folderExpanded = new BindableBool(true);

            private bool matchingFilter = true;
            private bool filteringActive;

            public DrawableCollectionMenuItem(MenuItem item)
                : base(item)
            {
                var value = ((DropdownMenuItem<CollectionFilterMenuItem>)item).Value;

                collection = value.Collection;
                folder = value.Folder;
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
                bool inFolder = folder != null && !filteringActive;

                bool visible = filteringActive
                    ? matchingFilter && isPrimary
                    : !inFolder || folderExpanded.Value;

                this.FadeTo(visible ? 1 : 0);

                if (Foreground.Children.FirstOrDefault() is Content content)
                    content.Label.Padding = new MarginPadding { Left = inFolder ? 35 : 15 };
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

                if (folder != null && collectionFolders != null)
                    folderExpanded.BindTo(collectionFolders.GetExpanded(folder));

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
        /// A header for a folder of collections. Clicking it expands or collapses the folder. Hidden while searching.
        /// </summary>
        protected partial class DrawableFolderMenuItem : ShearedDropdownMenu.ShearedMenuItem
        {
            private readonly string folderName;

            private readonly BindableBool expanded = new BindableBool();

            [Resolved]
            private CollectionFolderManager? collectionFolders { get; set; }

            private SpriteIcon icon = null!;

            public DrawableFolderMenuItem(MenuItem item)
                : base(item)
            {
                folderName = ((CollectionFolderFilterMenuItem)((DropdownMenuItem<CollectionFilterMenuItem>)item).Value).FolderName;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                var content = (FolderContent)Foreground.Children.First();

                content.Label.Padding = new MarginPadding { Left = 35 };
                content.Label.Font = OsuFont.GetFont(weight: FontWeight.Bold);

                content.Add(icon = new SpriteIcon
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    X = 15,
                    Size = new Vector2(13),
                });
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                if (collectionFolders != null)
                    expanded.BindTo(collectionFolders.GetExpanded(folderName));

                expanded.BindValueChanged(e => icon.Icon = e.NewValue ? FontAwesome.Solid.FolderOpen : FontAwesome.Solid.Folder, true);
            }

            public override bool FilteringActive
            {
                set => this.FadeTo(value ? 0 : 1);
            }

            protected override void UpdateFilteringState(bool filtered)
            {
                // Visibility is handled by FilteringActive, as folder names should never match searches.
            }

            // Keep focus on the dropdown's search bar, so that the menu stays open when toggling folders.
            public override bool ChangeFocusOnClick => false;

            protected override bool OnClick(ClickEvent e)
            {
                expanded.Toggle();
                return true;
            }

            protected override Drawable CreateContent() => new FolderContent();

            private partial class FolderContent : Content
            {
                public void Add(Drawable drawable) => AddInternal(drawable);
            }
        }
    }
}
