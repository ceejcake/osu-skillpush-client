// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Input.Bindings;
using osu.Game.Localisation;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Select.Filter;
using osuTK;
using osuTK.Input;

namespace osu.Game.Screens.Select
{
    public sealed partial class FilterControl : OverlayContainer
    {
        // taken from draw visualiser. used for carousel alignment purposes.
        public const float HEIGHT_FROM_SCREEN_TOP = 141 - corner_radius;

        private const float corner_radius = 10;

        public IBindable<BeatmapSetInfo?> ScopedBeatmapSet { get; } = new Bindable<BeatmapSetInfo?>();

        private SongSelectSearchTextBox searchTextBox = null!;
        private ShearedToggleButton showConvertedBeatmapsButton = null!;
        private DifficultyRangeSlider difficultyRangeSlider = null!;
        private ShearedDropdown<SortMode> sortDropdown = null!;
        private ShearedDropdown<GroupModeDropdownItem> groupDropdown = null!;
        private readonly Bindable<string> configCollectionFilter = new Bindable<string>();

        /// <summary>
        /// An optional method which can force certain criteria adjustments.
        /// </summary>
        public Action<FilterCriteria>? ApplyRequiredCriteria { get; set; }

        [Resolved]
        private ISongSelect? songSelect { get; set; }

        [Resolved]
        private IBindable<RulesetInfo> ruleset { get; set; } = null!;

        [Resolved]
        private IBindable<IReadOnlyList<Mod>> mods { get; set; } = null!;

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private ModdedStarRatingCache? starRatingCache { get; set; }

        [Resolved]
        private CollectionFolderManager? collectionFolders { get; set; }

        private ModdedStarRatingCache.Tracking? starRatingTracking;

        private readonly Bindable<bool> moddedStarRatingSorting = new Bindable<bool>();

        private IBindable<APIUser> localUser = null!;
        private readonly IBindableList<int> localUserFavouriteBeatmapSets = new BindableList<int>();

        public LocalisableString StatusText
        {
            get => searchTextBox.StatusText;
            set => searchTextBox.StatusText = value;
        }

        public event Action<FilterCriteria>? CriteriaChanged;

        private FilterCriteria currentCriteria = null!;

        private IDisposable? collectionsSubscription;

        [BackgroundDependencyLoader]
        private void load(IAPIProvider api)
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;

            Shear = OsuGame.SHEAR;
            Margin = new MarginPadding { Top = -corner_radius, Right = -40 };

            InternalChildren = new Drawable[]
            {
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    CornerRadius = corner_radius,
                    Masking = true,
                    Child = new WedgeBackground
                    {
                        Anchor = Anchor.TopRight,
                        Scale = new Vector2(-1, 1),
                    }
                },
                new ReverseChildIDFillFlowContainer<Drawable>
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Padding = new MarginPadding { Top = corner_radius + 5, Bottom = 2, Right = 40f, Left = 2f },
                    Children = new Drawable[]
                    {
                        new ReverseChildIDFillFlowContainer<Drawable>
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0f, 5f),
                            Children = new Drawable[]
                            {
                                new Container
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Shear = -OsuGame.SHEAR,
                                    Child = searchTextBox = new SongSelectSearchTextBox
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        HoldFocus = true,
                                        ScopedBeatmapSet = { BindTarget = ScopedBeatmapSet },
                                    },
                                },
                                new GridContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Shear = -OsuGame.SHEAR,
                                    RowDimensions = new[] { new Dimension(GridSizeMode.AutoSize) },
                                    ColumnDimensions = new[]
                                    {
                                        new Dimension(),
                                        new Dimension(GridSizeMode.Absolute), // can probably be removed?
                                        new Dimension(GridSizeMode.AutoSize),
                                    },
                                    Content = new[]
                                    {
                                        new[]
                                        {
                                            difficultyRangeSlider = new DifficultyRangeSlider
                                            {
                                                RelativeSizeAxes = Axes.X,
                                                MinRange = 0.1f,
                                            },
                                            Empty(),
                                            showConvertedBeatmapsButton = new ShearedToggleButton
                                            {
                                                Anchor = Anchor.Centre,
                                                Origin = Anchor.Centre,
                                                AutoSizeAxes = Axes.X,
                                                Text = UserInterfaceStrings.ShowConverts,
                                                Height = 30f,
                                            },
                                        },
                                    }
                                },
                                new GridContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    Height = 30,
                                    Shear = -OsuGame.SHEAR,
                                    RowDimensions = new[] { new Dimension(GridSizeMode.AutoSize) },
                                    ColumnDimensions = new[]
                                    {
                                        new Dimension(maxSize: 180),
                                        new Dimension(GridSizeMode.Absolute, 5),
                                        new Dimension(maxSize: 180),
                                        new Dimension(GridSizeMode.Absolute, 5),
                                        new Dimension(),
                                        new Dimension(GridSizeMode.AutoSize),
                                    },
                                    Content = new[]
                                    {
                                        new[]
                                        {
                                            sortDropdown = new ShearedDropdown<SortMode>(SongSelectStrings.Sort)
                                            {
                                                RelativeSizeAxes = Axes.X,
                                                Items = Enum.GetValues<SortMode>(),
                                            },
                                            Empty(),
                                            groupDropdown = new GroupModeDropdown(SongSelectStrings.Group)
                                            {
                                                RelativeSizeAxes = Axes.X,
                                            },
                                            Empty(),
                                            new CollectionDropdown
                                            {
                                                RelativeSizeAxes = Axes.X,
                                            },
                                        }
                                    }
                                },
                            },
                        },
                        new ScopedBeatmapSetDisplay
                        {
                            ScopedBeatmapSet = { BindTarget = ScopedBeatmapSet },
                        }
                    },
                },
            };

            localUser = api.LocalUser.GetBoundCopy();
            localUserFavouriteBeatmapSets.BindTo(api.LocalUserState.FavouriteBeatmapSets);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            difficultyRangeSlider.LowerBound = config.GetBindable<double>(OsuSetting.DisplayStarsMinimum);
            difficultyRangeSlider.UpperBound = config.GetBindable<double>(OsuSetting.DisplayStarsMaximum);
            config.BindWith(OsuSetting.ShowConvertedBeatmaps, showConvertedBeatmapsButton.Active);
            config.BindWith(OsuSetting.SongSelectSortingMode, sortDropdown.Current);
            config.BindWith(OsuSetting.SongSelectCollectionFilter, configCollectionFilter);

            ruleset.BindValueChanged(_ => updateCriteria());
            mods.BindValueChanged(m =>
            {
                // The following is a note carried from old song select and may not be a valid reason anymore:
                // // Mods are updated once by the mod select overlay when song select is entered,
                // // regardless of if there are any mods or any changes have taken place.
                // // Updating the criteria here so early triggers a re-ordering of panels on song select, via... some mechanism.
                // // Todo: Investigate/fix and potentially remove this.
                // TODO: this might be simply removable with the new song select & carousel code.
                if (m.NewValue.SequenceEqual(m.OldValue))
                    return;

                var rulesetCriteria = currentCriteria.RulesetCriteria;
                if (rulesetCriteria?.FilterMayChangeFromMods(currentCriteria, m) == true)
                    updateCriteria();
            });

            searchTextBox.Current.BindValueChanged(_ => updateCriteria());

            ScheduledDelegate? sliderDebounce = null;

            // For slider dragging (where input events can arrive very often), even creating criteria can have
            // overhead, especially when a collection is selected (see ToImmutableHashSet() call).
            void debouncedUpdateCriteria()
            {
                sliderDebounce?.Cancel();
                sliderDebounce = Scheduler.AddDelayed(() => updateCriteria(), 50);
            }

            difficultyRangeSlider.LowerBound.BindValueChanged(_ => debouncedUpdateCriteria());
            difficultyRangeSlider.UpperBound.BindValueChanged(_ => debouncedUpdateCriteria());

            showConvertedBeatmapsButton.Active.BindValueChanged(_ => updateCriteria());
            sortDropdown.Current.BindValueChanged(_ => updateCriteria());
            groupDropdown.Current.BindValueChanged(_ => updateCriteria());
            configCollectionFilter.BindValueChanged(_ => updateCriteria());
            if (collectionFolders != null)
                collectionFolders.FoldersChanged += onFoldersChanged;

            collectionsSubscription = realm.RegisterForNotifications(r => r.All<BeatmapCollection>(), (_, changeSet) =>
            {
                if (changeSet != null && groupDropdown.Current.Value.Value == GroupMode.Collections)
                    updateCriteria();
            });

            localUser.BindValueChanged(_ => updateCriteria());
            localUserFavouriteBeatmapSets.BindCollectionChanged((_, _) => updateCriteria());
            ScopedBeatmapSet.BindValueChanged(_ => updateCriteria(clearScopedSet: false));

            if (starRatingCache != null)
            {
                starRatingTracking = starRatingCache.BeginTracking();
                starRatingCache.RatingsChanged += onStarRatingsChanged;
            }

            config.BindWith(OsuSetting.ModdedStarRatingSorting, moddedStarRatingSorting);
            moddedStarRatingSorting.BindValueChanged(_ => updateCriteria(clearScopedSet: false));

            updateCriteria();
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            collectionsSubscription?.Dispose();

            if (collectionFolders != null)
                collectionFolders.FoldersChanged -= onFoldersChanged;

            if (starRatingCache != null)
                starRatingCache.RatingsChanged -= onStarRatingsChanged;

            starRatingTracking?.Dispose();
        }

        /// <summary>
        /// Whether to filter and sort by star ratings under the current mods, which is turned on from the mod select overlay.
        /// </summary>
        private bool useModdedStarRatings(IEnumerable<string>? collectionBeatmapMD5Hashes) => moddedStarRatingSorting.Value && collectionBeatmapMD5Hashes != null;

        /// <summary>
        /// Star ratings under the current mods arrive gradually, and each batch can change which beatmaps are shown and in
        /// what order.
        /// </summary>
        private void onStarRatingsChanged()
        {
            if (currentCriteria?.StarRatings != null && currentCriteria.DependsOnStarRating)
                updateCriteria(clearScopedSet: false);
        }

        /// <summary>
        /// Returns all beatmaps inside the collections of a folder (including those in subfolders),
        /// or null if the folder doesn't exist (in which case beatmaps aren't filtered by collection at all).
        /// </summary>
        private IEnumerable<string>? getFolderBeatmapMD5Hashes(Guid folderId)
        {
            if (collectionFolders?.GetFolder(folderId) == null)
                return null;

            var collectionIds = collectionFolders.GetCollections(folderId, recursive: true);

            return realm.Run(r => collectionIds.Select(id => r.Find<BeatmapCollection>(id))
                                               .OfType<BeatmapCollection>()
                                               .SelectMany(c => c.BeatmapMD5Hashes)
                                               .ToImmutableHashSet());
        }

        private void onFoldersChanged()
        {
            // The contents of the selected folder may have changed.
            if (CollectionFolderManager.TryParseFolderFilterValue(configCollectionFilter.Value, out _))
                Scheduler.AddOnce(updateCriteriaForFolders);
        }

        private void updateCriteriaForFolders() => updateCriteria(clearScopedSet: false);

        /// <summary>
        /// Creates a <see cref="FilterCriteria"/> based on the current state of the controls.
        /// </summary>
        public FilterCriteria CreateCriteria()
        {
            string query = searchTextBox.Current.Value;
            bool isValidUser = localUser.Value.Id > 1;

            IEnumerable<string>? collectionBeatmapMD5Hashes = null;

            if (Guid.TryParse(configCollectionFilter.Value, out var collectionId))
                collectionBeatmapMD5Hashes = realm.Run(r => r.Find<BeatmapCollection>(collectionId)?.BeatmapMD5Hashes.ToImmutableHashSet());
            else if (CollectionFolderManager.TryParseFolderFilterValue(configCollectionFilter.Value, out var folderId))
                collectionBeatmapMD5Hashes = getFolderBeatmapMD5Hashes(folderId.Value);

            var criteria = new FilterCriteria
            {
                SelectedBeatmapSet = ScopedBeatmapSet.Value,
                Sort = sortDropdown.Current.Value,
                Group = groupDropdown.Current.Value?.Value ?? GroupMode.None,
                AllowConvertedBeatmaps = showConvertedBeatmapsButton.Active.Value,
                Ruleset = ruleset.Value,
                Mods = mods.Value,
                // Star ratings under the current mods are only worked out for a collection, as doing so for the whole
                // library takes too long. Outside of one, they are left out altogether rather than mixing in the few
                // beatmaps which happen to be known already.
                StarRatings = useModdedStarRatings(collectionBeatmapMD5Hashes) ? starRatingCache?.Current : null,
                CollectionBeatmapMD5Hashes = collectionBeatmapMD5Hashes,
                LocalUserId = isValidUser ? localUser.Value.Id : null,
                LocalUserUsername = isValidUser ? localUser.Value.Username : null,
            };

            if (!difficultyRangeSlider.LowerBound.IsDefault)
                criteria.UserStarDifficulty.Min = difficultyRangeSlider.LowerBound.Value;

            if (!difficultyRangeSlider.UpperBound.IsDefault)
                criteria.UserStarDifficulty.Max = difficultyRangeSlider.UpperBound.Value;

            criteria.RulesetCriteria = ruleset.Value.CreateInstance().CreateRulesetFilterCriteria();

            FilterQueryParser.ApplyQueries(criteria, query);

            ApplyRequiredCriteria?.Invoke(criteria);

            return criteria;
        }

        private void updateCriteria(bool clearScopedSet = true)
        {
            if (clearScopedSet && ScopedBeatmapSet.Value != null)
            {
                songSelect?.UnscopeBeatmapSet();
                // because `ScopedBeatmapSet` has a value change callback bound to it that calls `updateCriteria()` again,
                // we can just do nothing other than clear it to avoid extra work and duplicated `CriteriaChanged` invocations
                return;
            }

            currentCriteria = CreateCriteria();

            starRatingTracking?.SetBeatmaps(useModdedStarRatings(currentCriteria.CollectionBeatmapMD5Hashes) ? currentCriteria.CollectionBeatmapMD5Hashes : null);

            CriteriaChanged?.Invoke(currentCriteria);
        }

        /// <summary>
        /// Set the query to the search text box.
        /// </summary>
        /// <param name="query">The string to search.</param>
        public void Search(string query)
        {
            searchTextBox.Current.Value = query;
        }

        /// <summary>
        /// Set the query to the search text box.
        /// </summary>
        /// <param name="query">The string to search.</param>
        public void AddToSearch(string query)
        {
            string existingQuery = searchTextBox.Current.Value;

            if (existingQuery.Contains(query))
                return;

            searchTextBox.Current.Value = string.Join(' ', existingQuery.Trim(), query);
        }

        protected override void PopIn()
        {
            this.MoveToX(0, SongSelect.ENTER_DURATION, Easing.OutQuint)
                .FadeIn(SongSelect.ENTER_DURATION / 3, Easing.In);
        }

        protected override void PopOut()
        {
            this.MoveToX(150, SongSelect.ENTER_DURATION, Easing.OutQuint)
                .FadeOut(SongSelect.ENTER_DURATION / 3, Easing.In);
        }

        internal partial class SongSelectSearchTextBox : ShearedFilterTextBox
        {
            public IBindable<BeatmapSetInfo?> ScopedBeatmapSet { get; } = new Bindable<BeatmapSetInfo?>();

            protected override InnerSearchTextBox CreateInnerTextBox() => new InnerTextBox
            {
                ScopedBeatmapSet = { BindTarget = ScopedBeatmapSet },
            };

            private partial class InnerTextBox : InnerFilterTextBox
            {
                public IBindable<BeatmapSetInfo?> ScopedBeatmapSet { get; } = new Bindable<BeatmapSetInfo?>();

                public override bool HandleLeftRightArrows => false;

                public override bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
                {
                    if (e.Action == GlobalAction.Back && ScopedBeatmapSet.Value != null)
                        return false;

                    return base.OnPressed(e);
                }

                public override bool OnPressed(KeyBindingPressEvent<PlatformAction> e)
                {
                    // Conflicts with default group navigation keys (shift-left shift-right).
                    if (e.Action == PlatformAction.SelectBackwardChar || e.Action == PlatformAction.SelectForwardChar)
                        return false;

                    // the "cut" platform key binding (shift-delete) conflicts with the beatmap deletion action.
                    if (e.Action == PlatformAction.Cut && e.ShiftPressed && e.CurrentState.Keyboard.Keys.IsPressed(Key.Delete))
                        return false;

                    return base.OnPressed(e);
                }
            }
        }

        private partial class GroupModeDropdown : ShearedDropdown<GroupModeDropdownItem>
        {
            [Resolved]
            private IBindable<RulesetInfo> ruleset { get; set; } = null!;

            private Bindable<GroupMode> configGroupMode { get; } = new Bindable<GroupMode>();

            public GroupModeDropdown(LocalisableString label)
                : base(label)
            {
            }

            [BackgroundDependencyLoader]
            private void load(OsuConfigManager config)
            {
                config.BindWith(OsuSetting.SongSelectGroupMode, configGroupMode);
            }

            protected override void LoadAsyncComplete()
            {
                // Bindings set up intentionally in `LoadAsyncComplete()` rather than `LoadComplete()` as normal
                // to avoid double-filter on entering song select with the "variant" group mode engaged.
                ruleset.BindValueChanged(_ =>
                {
                    updateAvailableItems();
                    updateCurrentFromConfig();
                }, true);
                configGroupMode.BindValueChanged(_ => updateCurrentFromConfig());
                Current.BindValueChanged(currentChanged);

                // Ordering important - base call must run *after* the above bindings
                // because the bindings set up `Items`, and the base call is responsible for calling `GenerateItemText()`.
                base.LoadAsyncComplete();
            }

            private void updateAvailableItems()
            {
                var rulesetInstance = ruleset.Value.CreateInstance();
                var items = new List<GroupModeDropdownItem>();

                foreach (var item in Enum.GetValues<GroupMode>())
                {
                    switch (item)
                    {
                        default:
                            items.Add(new GroupModeDropdownItem(item, item.GetLocalisableDescription()));
                            break;

                        case GroupMode.Variant:
                            if (rulesetInstance.GameplayVariants.Count() <= 1)
                                break;

                            items.Add(new GroupModeDropdownItem(GroupMode.Variant, rulesetInstance.VariantDescription));
                            break;
                    }
                }

                Items = items.ToArray();
            }

            private bool synchronisingBindables;

            private void updateCurrentFromConfig()
            {
                if (synchronisingBindables)
                    return;

                synchronisingBindables = true;
                // Only rulesets that actually have variants expose and support the "variant" grouping mode.
                // If it's missing, default to no grouping.
                Current.Value = Items.SingleOrDefault(i => i.Value == configGroupMode.Value) ?? Items.Single(i => i.Value == GroupMode.None);
                synchronisingBindables = false;
            }

            private void currentChanged(ValueChangedEvent<GroupModeDropdownItem> current)
            {
                if (synchronisingBindables)
                    return;

                // Only rulesets that actually have variants expose and support the "variant" grouping mode.
                // If it's missing, code above will revert to no grouping, which also incurs a `Current` change.
                // However, for better user experience, don't write the new value out to config in this scenario
                // so that the variant grouping is re-engaged on switching to a ruleset that has variant support
                // (this also persists across game restarts).
                if (current.OldValue.Value == GroupMode.Variant && Items.All(i => i.Value != GroupMode.Variant))
                    return;

                synchronisingBindables = true;
                configGroupMode.Value = current.NewValue.Value;
                synchronisingBindables = false;
            }

            protected override LocalisableString GenerateItemText(GroupModeDropdownItem item) => item.Text;
        }

        private record GroupModeDropdownItem(GroupMode Value, LocalisableString Text);
    }
}
