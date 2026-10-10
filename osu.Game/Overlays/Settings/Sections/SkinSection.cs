// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;
using osu.Game.Overlays.Dialog;
using osu.Game.Overlays.SkinEditor;
using osu.Game.Skinning;
using osuTK;
using Realms;
using WebCommonStrings = osu.Game.Resources.Localisation.Web.CommonStrings;

namespace osu.Game.Overlays.Settings.Sections
{
    public partial class SkinSection : SettingsSection
    {
        private SkinDropdown skinDropdown;

        public override LocalisableString Header => SkinSettingsStrings.SkinSectionHeader;

        public override Drawable CreateIcon() => new SpriteIcon
        {
            Icon = OsuIcon.SkinB
        };

        public override IEnumerable<LocalisableString> FilterTerms => base.FilterTerms.Concat(new LocalisableString[] { "skins" });

        private readonly List<Live<SkinInfo>> dropdownItems = new List<Live<SkinInfo>>();

        [Resolved]
        private SkinManager skins { get; set; }

        [Resolved]
        private RealmAccess realm { get; set; }

        private IDisposable realmSubscription;

        private Bindable<string> favouriteSkins;

        [BackgroundDependencyLoader(permitNulls: true)]
        private void load([CanBeNull] SkinEditorOverlay skinEditor, OsuConfigManager config)
        {
            favouriteSkins = config.GetBindable<string>(OsuSetting.FavouriteSkins);

            Children = new Drawable[]
            {
                new SettingsItemV2(skinDropdown = new SkinDropdown
                {
                    AlwaysShowSearchBar = true,
                    AllowNonContiguousMatching = true,
                    Caption = SkinSettingsStrings.CurrentSkin,
                    Current = skins.CurrentSkinInfo,
                }),
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Horizontal,
                    Padding = SettingsPanel.CONTENT_PADDING,
                    Children = new Drawable[]
                    {
                        // This is all super-temporary until we move skin settings to their own panel / overlay.
                        new RenameSkinButton { Padding = new MarginPadding { Right = 2.5f }, RelativeSizeAxes = Axes.X, Width = 1 / 3f },
                        new ExportSkinButton { Padding = new MarginPadding { Horizontal = 2.5f }, RelativeSizeAxes = Axes.X, Width = 1 / 3f },
                        new DeleteSkinButton { Padding = new MarginPadding { Left = 2.5f }, RelativeSizeAxes = Axes.X, Width = 1 / 3f },
                    }
                },
                new SettingsButtonV2
                {
                    Text = SkinSettingsStrings.SkinLayoutEditor,
                    Action = () => skinEditor?.ToggleVisibility(),
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            realmSubscription = realm.RegisterForNotifications(_ => realm.Realm.All<SkinInfo>()
                                                                         .Where(s => !s.DeletePending)
                                                                         .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase), skinsChanged);

            favouriteSkins.BindValueChanged(_ => Scheduler.AddOnce(repopulateSkins));

            skinDropdown.Current.BindValueChanged(skin =>
            {
                if (skin.NewValue.ID == SkinInfo.RANDOM_SKIN)
                {
                    // before selecting random, set the skin back to the previous selection.
                    // this is done because at this point it will be random_skin_info, and would
                    // cause SelectRandomSkin to be unable to skip the previous selection.
                    skins.CurrentSkinInfo.Value = skin.OldValue;
                    skins.SelectRandomSkin();
                }
            });
        }

        private void skinsChanged(IRealmCollection<SkinInfo> sender, ChangeSet changes)
        {
            // This can only mean that realm is recycling, else we would see the protected skins.
            // Because we are using `Live<>` in this class, we don't need to worry about this scenario too much.
            if (!sender.Any())
                return;
            // For simplicity repopulate the full list.
            Schedule(repopulateSkins);
        }

        /// <summary>
        /// Populates the skin list, with favourite skins at the top.
        /// </summary>
        private void repopulateSkins()
        {
            var favourites = ParseFavouriteSkins(favouriteSkins.Value);
            var allSkins = skins.GetAllUsableSkins();

            dropdownItems.Clear();
            dropdownItems.AddRange(allSkins.Where(s => favourites.Contains(s.ID))
                                           .OrderBy(s => s.ToString(), StringComparer.OrdinalIgnoreCase));
            dropdownItems.AddRange(allSkins.Where(s => !favourites.Contains(s.ID)));

            skinDropdown.Items = dropdownItems;
        }

        public static HashSet<Guid> ParseFavouriteSkins(string value) =>
            value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                 .Select(s => Guid.TryParse(s, out var id) ? id : Guid.Empty)
                 .Where(id => id != Guid.Empty)
                 .ToHashSet();

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            realmSubscription?.Dispose();
        }

        private partial class SkinDropdown : FormDropdown<Live<SkinInfo>>
        {
            protected override LocalisableString GenerateItemText(Live<SkinInfo> item) => item.ToString();

            protected override DropdownMenu CreateMenu() => new SkinDropdownMenu
            {
                MaxHeight = MaxHeight,
            };

            private partial class SkinDropdownMenu : FormDropdownMenu
            {
                protected override DrawableDropdownMenuItem CreateDrawableDropdownMenuItem(MenuItem item) => new DrawableSkinMenuItem(item)
                {
                    BackgroundColourHover = HoverColour,
                    BackgroundColourSelected = SelectionColour
                };
            }

            /// <summary>
            /// A skin in the dropdown, with a heart button to mark it as a favourite.
            /// </summary>
            private partial class DrawableSkinMenuItem : OsuDropdownMenu.DrawableOsuDropdownMenuItem
            {
                private readonly Live<SkinInfo> skin;

                private Bindable<string> favouriteSkins;

                private IconButton heartButton;

                private bool isFavourite;

                [Resolved]
                private OsuColour colours { get; set; }

                public DrawableSkinMenuItem(MenuItem item)
                    : base(item)
                {
                    skin = ((DropdownMenuItem<Live<SkinInfo>>)item).Value;
                }

                private bool canFavourite => skin.ID != SkinInfo.RANDOM_SKIN;

                [BackgroundDependencyLoader]
                private void load(OsuConfigManager config)
                {
                    favouriteSkins = config.GetBindable<string>(OsuSetting.FavouriteSkins);

                    if (!canFavourite)
                        return;

                    if (Foreground.Children.FirstOrDefault() is Content content)
                        content.Label.Padding = new MarginPadding { Left = 15, Right = 25 };

                    AddInternal(heartButton = new NoFocusChangeIconButton
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        X = -5,
                        Scale = new Vector2(0.65f),
                        Action = toggleFavourite,
                    });
                }

                protected override void LoadComplete()
                {
                    base.LoadComplete();

                    if (canFavourite)
                        favouriteSkins.BindValueChanged(_ => updateFavouriteState(), true);
                }

                private void toggleFavourite()
                {
                    var favourites = ParseFavouriteSkins(favouriteSkins.Value);

                    if (!favourites.Remove(skin.ID))
                        favourites.Add(skin.ID);

                    favouriteSkins.Value = string.Join(',', favourites);
                }

                private void updateFavouriteState()
                {
                    isFavourite = ParseFavouriteSkins(favouriteSkins.Value).Contains(skin.ID);

                    heartButton.Icon = isFavourite ? FontAwesome.Solid.Heart : FontAwesome.Regular.Heart;
                    heartButton.IconColour = isFavourite ? colours.Pink1 : Colour4.White;
                    heartButton.TooltipText = isFavourite ? "Remove from favourites" : "Add to favourites";

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
                    if (heartButton != null)
                        heartButton.Alpha = isFavourite || IsHovered || IsPreSelected ? 1 : 0;
                }

                private partial class NoFocusChangeIconButton : IconButton
                {
                    public override bool ChangeFocusOnClick => false;
                }
            }
        }

        public partial class RenameSkinButton : SettingsButtonV2, IHasPopover
        {
            [Resolved]
            private SkinManager skins { get; set; }

            private Bindable<Skin> currentSkin;

            [BackgroundDependencyLoader]
            private void load()
            {
                Text = CommonStrings.Rename;
                Action = this.ShowPopover;
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                currentSkin = skins.CurrentSkin.GetBoundCopy();
                currentSkin.BindValueChanged(_ => updateState());
                currentSkin.BindDisabledChanged(_ => updateState(), true);
            }

            private void updateState() => Enabled.Value = !currentSkin.Disabled && currentSkin.Value.SkinInfo.PerformRead(s => !s.Protected);

            public Popover GetPopover()
            {
                return new RenameSkinPopover();
            }
        }

        public partial class ExportSkinButton : SettingsButtonV2
        {
            [Resolved]
            private SkinManager skins { get; set; }

            private Bindable<Skin> currentSkin;

            [BackgroundDependencyLoader]
            private void load()
            {
                Text = CommonStrings.Export;
                Action = export;
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                currentSkin = skins.CurrentSkin.GetBoundCopy();
                currentSkin.BindValueChanged(_ => updateState());
                currentSkin.BindDisabledChanged(_ => updateState(), true);
            }

            private void updateState() => Enabled.Value = !currentSkin.Disabled && currentSkin.Value.SkinInfo.PerformRead(s => !s.Protected);

            private void export()
            {
                try
                {
                    skins.ExportCurrentSkin();
                }
                catch (Exception e)
                {
                    Logger.Log($"Could not export current skin: {e.Message}", level: LogLevel.Error);
                }
            }
        }

        public partial class DeleteSkinButton : DangerousSettingsButtonV2
        {
            [Resolved]
            private SkinManager skins { get; set; }

            [Resolved(CanBeNull = true)]
            private IDialogOverlay dialogOverlay { get; set; }

            private Bindable<Skin> currentSkin;

            [BackgroundDependencyLoader]
            private void load()
            {
                Text = WebCommonStrings.ButtonsDelete;
                Action = delete;
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                currentSkin = skins.CurrentSkin.GetBoundCopy();
                currentSkin.BindValueChanged(_ => updateState());
                currentSkin.BindDisabledChanged(_ => updateState(), true);
            }

            private void updateState() => Enabled.Value = !currentSkin.Disabled && currentSkin.Value.SkinInfo.PerformRead(s => !s.Protected);

            private void delete()
            {
                dialogOverlay?.Push(new SkinDeleteDialog(currentSkin.Value));
            }
        }

        public partial class SkinDeleteDialog : DeletionDialog
        {
            private readonly Skin skin;

            public SkinDeleteDialog(Skin skin)
            {
                this.skin = skin;
                BodyText = skin.SkinInfo.Value.Name;
            }

            [BackgroundDependencyLoader]
            private void load(SkinManager manager)
            {
                DangerousAction = () =>
                {
                    manager.Delete(skin.SkinInfo.Value);
                    manager.CurrentSkinInfo.SetDefault();
                };
            }
        }

        public partial class RenameSkinPopover : OsuPopover
        {
            [Resolved]
            private SkinManager skins { get; set; }

            private readonly FocusedTextBox textBox;

            public RenameSkinPopover()
            {
                AutoSizeAxes = Axes.Both;
                Origin = Anchor.TopCentre;

                RoundedButton renameButton;

                Child = new FillFlowContainer
                {
                    Direction = FillDirection.Vertical,
                    AutoSizeAxes = Axes.Y,
                    Width = 250,
                    Spacing = new Vector2(10f),
                    Children = new Drawable[]
                    {
                        textBox = new FocusedTextBox
                        {
                            PlaceholderText = SkinSettingsStrings.SkinName,
                            FontSize = OsuFont.DEFAULT_FONT_SIZE,
                            RelativeSizeAxes = Axes.X,
                            SelectAllOnFocus = true,
                        },
                        renameButton = new RoundedButton
                        {
                            Height = 40,
                            RelativeSizeAxes = Axes.X,
                            MatchingFilter = true,
                            Text = WebCommonStrings.ButtonsSave,
                        }
                    }
                };

                renameButton.Action += rename;
                textBox.OnCommit += (_, _) => rename();
            }

            protected override void PopIn()
            {
                textBox.Text = skins.CurrentSkinInfo.Value.Value.Name;
                textBox.TakeFocus();

                base.PopIn();
            }

            private void rename()
            {
                skins.Rename(skins.CurrentSkinInfo.Value, textBox.Text);
                PopOut();
            }
        }
    }
}
