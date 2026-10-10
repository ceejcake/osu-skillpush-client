// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using osu.Framework.Bindables;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Graphics.UserInterface;

namespace osu.Game.Collections
{
    /// <summary>
    /// Stores a user-defined grouping of <see cref="BeatmapCollection"/>s into folders.
    /// A collection may be inside any number of folders.
    /// </summary>
    /// <remarks>
    /// This is stored in a separate file rather than in realm to avoid changing the database schema.
    /// Collections are referenced by <see cref="BeatmapCollection.ID"/>, so renaming a collection keeps its folders.
    /// </remarks>
    public class CollectionFolderManager
    {
        private const string filename = "collection-folders.json";

        /// <summary>
        /// Invoked whenever folders are created, renamed or deleted, or a collection is added to or removed from a folder.
        /// </summary>
        public event Action? FoldersChanged;

        private readonly Storage storage;

        private readonly List<string> folders = new List<string>();
        private readonly Dictionary<Guid, List<string>> memberships = new Dictionary<Guid, List<string>>();
        private readonly Dictionary<string, BindableBool> expanded = new Dictionary<string, BindableBool>(StringComparer.OrdinalIgnoreCase);

        public CollectionFolderManager(Storage storage)
        {
            this.storage = storage;
            load();
        }

        /// <summary>
        /// All folders, sorted by name.
        /// </summary>
        public IReadOnlyList<string> Folders => sorted(folders);

        /// <summary>
        /// Returns the folders the given collection is in (sorted by name), or an empty list if it isn't in any folder.
        /// </summary>
        public IReadOnlyList<string> GetFolders(Guid collectionId) =>
            memberships.TryGetValue(collectionId, out var list) ? sorted(list) : Array.Empty<string>();

        public bool IsInFolder(Guid collectionId, string folder) =>
            memberships.TryGetValue(collectionId, out var list) && list.Contains(folder, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Adds a collection to a folder, creating the folder if it doesn't exist.
        /// </summary>
        public void AddToFolder(Guid collectionId, string folder)
        {
            folder = folder.Trim();

            if (string.IsNullOrEmpty(folder))
                return;

            folder = addFolder(folder);

            if (!memberships.TryGetValue(collectionId, out var list))
                memberships[collectionId] = list = new List<string>();

            if (!list.Contains(folder))
                list.Add(folder);

            save();
        }

        public void RemoveFromFolder(Guid collectionId, string folder)
        {
            if (!memberships.TryGetValue(collectionId, out var list))
                return;

            list.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));

            if (list.Count == 0)
                memberships.Remove(collectionId);

            save();
        }

        /// <summary>
        /// Removes a collection from all folders.
        /// </summary>
        public void RemoveFromAllFolders(Guid collectionId)
        {
            memberships.Remove(collectionId);
            save();
        }

        /// <summary>
        /// Creates a new empty folder. Does nothing if a folder with the same name already exists.
        /// </summary>
        /// <returns>The name of the created (or already existing) folder.</returns>
        public string CreateFolder(string name)
        {
            string folder = addFolder(name.Trim());
            save();
            return folder;
        }

        /// <summary>
        /// Deletes a folder. Collections inside it are kept.
        /// </summary>
        public void DeleteFolder(string folder)
        {
            folders.Remove(folder);
            expanded.Remove(folder);

            foreach (var (id, list) in memberships.ToList())
            {
                list.Remove(folder);

                if (list.Count == 0)
                    memberships.Remove(id);
            }

            save();
        }

        /// <summary>
        /// Whether the given folder is expanded in collection lists. Changes to the returned bindable are persisted.
        /// </summary>
        public BindableBool GetExpanded(string folder)
        {
            if (!expanded.TryGetValue(folder, out var bindable))
            {
                expanded[folder] = bindable = new BindableBool();
                bindable.BindValueChanged(_ => save(false));
            }

            return bindable;
        }

        /// <summary>
        /// Groups collection menu items into one submenu per folder, followed by collections which aren't in any folder.
        /// A collection inside multiple folders appears in each of them.
        /// </summary>
        /// <param name="manager">The folder manager, or null if unavailable (in which case items are returned ungrouped).</param>
        /// <param name="items">The collections, alongside a function creating a menu item for each.</param>
        public static List<MenuItem> GroupMenuItems(CollectionFolderManager? manager, IEnumerable<(Guid collectionId, Func<MenuItem> createItem)> items)
        {
            if (manager == null)
                return items.Select(i => i.createItem()).ToList();

            return manager.groupMenuItems(items);
        }

        private List<MenuItem> groupMenuItems(IEnumerable<(Guid collectionId, Func<MenuItem> createItem)> items)
        {
            var grouped = new List<MenuItem>();
            var unsorted = new List<MenuItem>();
            var byFolder = new Dictionary<string, List<MenuItem>>();

            foreach (var (collectionId, createItem) in items)
            {
                var collectionFolders = GetFolders(collectionId);

                if (collectionFolders.Count == 0)
                {
                    unsorted.Add(createItem());
                    continue;
                }

                foreach (string folder in collectionFolders)
                {
                    if (!byFolder.TryGetValue(folder, out var list))
                        byFolder[folder] = list = new List<MenuItem>();

                    list.Add(createItem());
                }
            }

            foreach (string folder in Folders)
            {
                if (byFolder.TryGetValue(folder, out var list))
                    grouped.Add(new OsuMenuItem(folder) { Items = list });
            }

            grouped.AddRange(unsorted);
            return grouped;
        }

        private static List<string> sorted(IEnumerable<string> list) => list.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

        private string addFolder(string name)
        {
            string? existing = folders.FirstOrDefault(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
                return existing;

            folders.Add(name);
            return name;
        }

        private void load()
        {
            try
            {
                if (!storage.Exists(filename))
                    return;

                using (var stream = storage.GetStream(filename))
                using (var reader = new StreamReader(stream))
                {
                    var data = JsonConvert.DeserializeObject<StoredData>(reader.ReadToEnd());

                    if (data == null)
                        return;

                    foreach (string folder in data.Folders)
                        addFolder(folder);

                    foreach (var (id, list) in data.Memberships)
                        memberships[id] = list.Select(addFolder).Distinct().ToList();

                    // Older format, where a collection could only be inside a single folder.
                    foreach (var (id, folder) in data.Assignments ?? new Dictionary<Guid, string>())
                    {
                        if (!memberships.ContainsKey(id))
                            memberships[id] = new List<string> { addFolder(folder) };
                    }

                    foreach (string folder in data.Expanded)
                        GetExpanded(folder).Value = true;
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to load collection folders");
            }
        }

        private void save(bool notify = true)
        {
            try
            {
                var data = new StoredData
                {
                    Folders = folders.ToList(),
                    Memberships = memberships.ToDictionary(m => m.Key, m => m.Value.ToList()),
                    Expanded = expanded.Where(e => e.Value.Value && folders.Contains(e.Key)).Select(e => e.Key).ToList(),
                };

                using (var stream = storage.CreateFileSafely(filename))
                using (var writer = new StreamWriter(stream))
                    writer.Write(JsonConvert.SerializeObject(data, Formatting.Indented));
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to save collection folders");
            }

            if (notify)
                FoldersChanged?.Invoke();
        }

        private class StoredData
        {
            public List<string> Folders { get; set; } = new List<string>();

            public Dictionary<Guid, List<string>> Memberships { get; set; } = new Dictionary<Guid, List<string>>();

            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public Dictionary<Guid, string>? Assignments { get; set; }

            public List<string> Expanded { get; set; } = new List<string>();
        }
    }
}
