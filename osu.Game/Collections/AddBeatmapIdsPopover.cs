// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input;
using osu.Framework.Input.Events;
using osu.Framework.Platform;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osuTK;

namespace osu.Game.Collections
{
    /// <summary>
    /// A popover for adding beatmaps to a collection from a pasted list of beatmap IDs or links.
    /// Duplicate IDs and beatmaps already in the collection are skipped, and missing beatmap sets are downloaded.
    /// </summary>
    public partial class AddBeatmapIdsPopover : OsuPopover
    {
        private readonly Live<BeatmapCollection> collection;

        [Resolved]
        private OsuCollectorImporter? importer { get; set; }

        private PastedListTextBox textBox = null!;

        public AddBeatmapIdsPopover(Live<BeatmapCollection> collection)
        {
            this.collection = collection;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            string name = collection.PerformRead(c => c.IsValid ? c.Name : string.Empty);

            Child = new FillFlowContainer
            {
                Width = 320,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(8),
                Children = new Drawable[]
                {
                    new OsuSpriteText
                    {
                        Text = $"Add to \"{name}\"",
                        Font = OsuFont.GetFont(size: 18, weight: FontWeight.Bold),
                    },
                    new OsuTextFlowContainer(s => s.Font = OsuFont.GetFont(size: 12))
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Text = "Paste beatmap IDs or links (any number of them). Duplicates and beatmaps already in the collection are skipped, and missing beatmaps are downloaded.",
                    },
                    textBox = new PastedListTextBox
                    {
                        RelativeSizeAxes = Axes.X,
                        PlaceholderText = "Beatmap IDs or links",
                        CommitOnFocusLost = false,
                    },
                    new RoundedButton
                    {
                        RelativeSizeAxes = Axes.X,
                        Text = "Add",
                        Enabled = { Value = importer != null },
                        Action = add,
                    },
                }
            };

            textBox.OnCommit += (_, _) => add();
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            ScheduleAfterChildren(() => GetContainingFocusManager()?.ChangeFocus(textBox));
        }

        private void add()
        {
            if (importer == null || string.IsNullOrWhiteSpace(textBox.Text))
                return;

            string name = collection.PerformRead(c => c.IsValid ? c.Name : string.Empty);

            if (string.IsNullOrEmpty(name))
                return;

            importer.ImportBeatmapIds(name, textBox.Text);
            this.HidePopover();
        }

        /// <summary>
        /// A text box which takes a pasted list of any length, with one entry per line.
        /// </summary>
        public partial class PastedListTextBox : OsuTextBox
        {
            [Resolved]
            private Clipboard clipboard { get; set; } = null!;

            public PastedListTextBox()
            {
                LengthLimit = 1000000;
            }

            public override bool OnPressed(KeyBindingPressEvent<PlatformAction> e)
            {
                // Text boxes are a single line and drop line breaks, which would run the entries of a pasted list together.
                if (e.Action == PlatformAction.Paste && clipboard.GetText() is string pasted && pasted.IndexOfAny(new[] { '\r', '\n', '\t' }) >= 0)
                {
                    string separated = OsuCollectorImporter.JoinPastedLines(pasted);

                    Text = string.IsNullOrEmpty(Text) ? separated : $"{Text} {separated}";
                    return true;
                }

                return base.OnPressed(e);
            }
        }
    }
}
