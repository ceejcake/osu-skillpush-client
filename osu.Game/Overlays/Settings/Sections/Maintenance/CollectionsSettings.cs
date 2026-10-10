// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Input;
using osu.Framework.Input.Events;
using osu.Framework.Platform;
using osu.Framework.Localisation;
using osu.Game.Collections;
using osu.Game.Database;
using osu.Game.Localisation;
using osu.Game.Overlays.Notifications;

namespace osu.Game.Overlays.Settings.Sections.Maintenance
{
    public partial class CollectionsSettings : SettingsSubsection
    {
        protected override LocalisableString Header => CommonStrings.Collections;

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private INotificationOverlay? notificationOverlay { get; set; }

        [BackgroundDependencyLoader]
        private void load(IDialogOverlay? dialogOverlay)
        {
            var collectorInput = new Bindable<string>(string.Empty);
            var beatmapIdsName = new Bindable<string>(string.Empty);
            var beatmapIdsInput = new Bindable<string>(string.Empty);
            OsuCollectorImporter collectorImporter;

            AddRange(new Drawable[]
            {
                collectorImporter = new OsuCollectorImporter(),
                new SettingsTextBox
                {
                    LabelText = "osu!collector collection URL or ID",
                    Current = collectorInput,
                },
                new SettingsButtonV2
                {
                    Text = "Import from osu!collector",
                    Action = () => collectorImporter.Import(collectorInput.Value),
                },
                new SettingsTextBox
                {
                    LabelText = "New collection name",
                    Current = beatmapIdsName,
                },
                new BeatmapIdsTextBox
                {
                    LabelText = "Beatmap IDs or links (paste any number of them)",
                    Current = beatmapIdsInput,
                },
                new SettingsButtonV2
                {
                    Text = "Create collection from beatmap IDs",
                    TooltipText = "Missing beatmaps are downloaded. Using the name of an existing collection adds to it.",
                    Action = () => collectorImporter.ImportBeatmapIds(beatmapIdsName.Value, beatmapIdsInput.Value),
                },
            });

            Add(new DangerousSettingsButtonV2
            {
                Text = MaintenanceSettingsStrings.DeleteAllCollections,
                Action = () =>
                {
                    dialogOverlay?.Push(new MassDeleteConfirmationDialog(deleteAllCollections, DeleteConfirmationContentStrings.Collections));
                }
            });
        }

        /// <summary>
        /// A text box which takes a pasted list of any length, with one entry per line.
        /// </summary>
        private partial class BeatmapIdsTextBox : SettingsTextBox
        {
            protected override Drawable CreateControl() => new PastedListTextBox
            {
                RelativeSizeAxes = Axes.X,
                CommitOnFocusLost = true,
            };

            private partial class PastedListTextBox : OutlinedTextBox
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
                        string separated = string.Join(' ', pasted.Split(new[] { '\r', '\n', '\t' }, System.StringSplitOptions.RemoveEmptyEntries));

                        Text = string.IsNullOrEmpty(Text) ? separated : $"{Text} {separated}";
                        return true;
                    }

                    return base.OnPressed(e);
                }
            }
        }

        private void deleteAllCollections()
        {
            bool anyDeleted = realm.Write(r =>
            {
                if (r.All<BeatmapCollection>().Any())
                {
                    r.RemoveAll<BeatmapCollection>();
                    return true;
                }
                else
                {
                    return false;
                }
            });

            notificationOverlay?.Post(new ProgressCompletionNotification { Text = anyDeleted ? MaintenanceSettingsStrings.DeletedAllCollections : MaintenanceSettingsStrings.NoCollectionsFoundToDelete });
        }
    }
}
