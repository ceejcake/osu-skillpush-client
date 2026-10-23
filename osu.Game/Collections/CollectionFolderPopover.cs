// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osuTK;

namespace osu.Game.Collections
{
    /// <summary>
    /// A popover allowing a collection to be added to or removed from folders, and folders to be created or deleted.
    /// </summary>
    public partial class CollectionFolderPopover : OsuPopover
    {
        private const float width = 260;
        private const float row_height = 30;
        private const float row_spacing = 4;
        private const int max_visible_rows = 8;

        private readonly Guid collectionId;

        [Resolved]
        private CollectionFolderManager collectionFolders { get; set; } = null!;

        /// <summary>
        /// When present, new folders are created inside the folder being viewed.
        /// </summary>
        [Resolved]
        private CollectionFolderBrowser? browser { get; set; }

        private FillFlowContainer folderFlow = null!;
        private OsuScrollContainer folderScroll = null!;
        private OsuTextBox newFolderTextBox = null!;

        public CollectionFolderPopover(Guid collectionId)
        {
            this.collectionId = collectionId;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Child = new FillFlowContainer
            {
                Width = width,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(8),
                Children = new Drawable[]
                {
                    new OsuSpriteText
                    {
                        Text = "Folders",
                        Font = OsuFont.GetFont(size: 18, weight: FontWeight.Bold),
                    },
                    new OsuSpriteText
                    {
                        Text = "Click a folder to add or remove this collection.",
                        Font = OsuFont.GetFont(size: 12),
                    },
                    folderScroll = new OsuScrollContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        Child = folderFlow = new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(row_spacing),
                        }
                    },
                    newFolderTextBox = new OsuTextBox
                    {
                        RelativeSizeAxes = Axes.X,
                        PlaceholderText = "New folder name (enter to add)",
                        CommitOnFocusLost = false,
                    },
                }
            };

            newFolderTextBox.OnCommit += (_, _) =>
            {
                string name = newFolderTextBox.Text.Trim();

                if (string.IsNullOrEmpty(name))
                    return;

                var folder = collectionFolders.CreateFolder(name, browser?.CurrentFolder.Value);
                collectionFolders.AddToFolder(collectionId, folder.ID);
                newFolderTextBox.Text = string.Empty;
                populateFolders();
            };

            populateFolders();
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            ScheduleAfterChildren(() => GetContainingFocusManager()?.ChangeFocus(newFolderTextBox));
        }

        private void populateFolders()
        {
            var rows = new List<Drawable>();

            addRows(null, 0);

            // Adds rows for the folders inside a folder (or the top level if null), with each folder followed by its subfolders.
            void addRows(Guid? parentId, int depth)
            {
                foreach (var folder in collectionFolders.GetChildFolders(parentId))
                {
                    rows.Add(new FolderRow(folder.Name, depth, collectionFolders.IsInFolder(collectionId, folder.ID))
                    {
                        Toggled = inFolder =>
                        {
                            if (inFolder)
                                collectionFolders.AddToFolder(collectionId, folder.ID);
                            else
                                collectionFolders.RemoveFromFolder(collectionId, folder.ID);
                        },
                        Deleted = () =>
                        {
                            collectionFolders.DeleteFolder(folder.ID);
                            populateFolders();
                        },
                    });

                    addRows(folder.ID, depth + 1);
                }
            }

            if (rows.Count == 0)
            {
                rows.Add(new OsuTextFlowContainer(s => s.Font = OsuFont.GetFont(size: 14))
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Text = "No folders yet. Type a name below to create one.",
                });
            }

            folderFlow.ChildrenEnumerable = rows;

            int visibleRows = Math.Clamp(collectionFolders.Folders.Count, 1, max_visible_rows);
            folderScroll.Height = visibleRows * (row_height + row_spacing);
        }

        private partial class FolderRow : CompositeDrawable
        {
            public Action<bool>? Toggled;
            public Action? Deleted;

            private readonly string folder;
            private readonly int depth;
            private bool inFolder;

            private Box background = null!;
            private SpriteIcon checkIcon = null!;

            [Resolved]
            private OsuColour colours { get; set; } = null!;

            public FolderRow(string folder, int depth, bool inFolder)
            {
                this.folder = folder;
                this.depth = depth;
                this.inFolder = inFolder;

                RelativeSizeAxes = Axes.X;
                Height = row_height;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChildren = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Left = depth * 16, Right = row_height + 4 },
                        Child = new OsuClickableContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            Masking = true,
                            CornerRadius = 5,
                            Action = toggle,
                            Children = new Drawable[]
                            {
                                background = new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                },
                                checkIcon = new SpriteIcon
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    X = 10,
                                    Size = new Vector2(12),
                                },
                                new TruncatingSpriteText
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    RelativeSizeAxes = Axes.X,
                                    Padding = new MarginPadding { Left = 30, Right = 10 },
                                    Text = folder,
                                    Font = OsuFont.GetFont(size: 15, weight: FontWeight.SemiBold),
                                    Colour = Colour4.White,
                                },
                            }
                        },
                    },
                    new IconButton
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Icon = FontAwesome.Solid.Trash,
                        TooltipText = $"Delete folder \"{folder}\" (everything inside is kept, and moves up a level)",
                        Action = () => Deleted?.Invoke(),
                    },
                };

                updateState();
            }

            private void toggle()
            {
                inFolder = !inFolder;
                updateState();
                Toggled?.Invoke(inFolder);
            }

            private void updateState()
            {
                background.FadeColour(inFolder ? colours.Blue3 : colours.Gray4, 100);
                checkIcon.Icon = inFolder ? FontAwesome.Solid.CheckSquare : FontAwesome.Regular.Square;
            }

            protected override bool OnHover(HoverEvent e)
            {
                background.FadeColour((inFolder ? colours.Blue3 : colours.Gray4).Lighten(0.2f), 100);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                updateState();
                base.OnHoverLost(e);
            }
        }
    }
}
