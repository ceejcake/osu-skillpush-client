// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
    /// A user-created folder of <see cref="BeatmapCollection"/>s. Folders may be nested inside other folders.
    /// </summary>
    public class CollectionFolder
    {
        public Guid ID { get; }

        public string Name { get; internal set; }

        /// <summary>
        /// The folder this folder is inside, or null if it is at the top level.
        /// </summary>
        public Guid? ParentID { get; internal set; }

        public CollectionFolder(Guid id, string name, Guid? parentId)
        {
            ID = id;
            Name = name;
            ParentID = parentId;
        }
    }

    /// <summary>
    /// Stores a user-defined tree of folders containing <see cref="BeatmapCollection"/>s.
    /// A collection may be inside any number of folders. Collections which aren't in any folder are at the top level.
    /// </summary>
    /// <remarks>
    /// This is stored in a separate file rather than in realm to avoid changing the database schema.
    /// Collections are referenced by <see cref="BeatmapCollection.ID"/>, so renaming a collection keeps its folders.
    /// </remarks>
    public class CollectionFolderManager
    {
        private const string filename = "collection-folders.json";

        /// <summary>
        /// The prefix of <see cref="Configuration.OsuSetting.SongSelectCollectionFilter"/> values which select a folder rather than a collection.
        /// </summary>
        private const string folder_filter_prefix = "folder:";

        /// <summary>
        /// Invoked whenever folders are created, renamed, moved or deleted, or a collection is added to or removed from a folder.
        /// </summary>
        public event Action? FoldersChanged;

        private readonly Storage storage;

        private readonly Dictionary<Guid, CollectionFolder> folders = new Dictionary<Guid, CollectionFolder>();
        private readonly Dictionary<Guid, List<Guid>> memberships = new Dictionary<Guid, List<Guid>>();
        private readonly Dictionary<Guid, BindableBool> expanded = new Dictionary<Guid, BindableBool>();

        public CollectionFolderManager(Storage storage)
        {
            this.storage = storage;
            load();
        }

        /// <summary>
        /// All folders (at any depth), sorted by name.
        /// </summary>
        public IReadOnlyList<CollectionFolder> Folders => sorted(folders.Values);

        public CollectionFolder? GetFolder(Guid folderId) => folders.GetValueOrDefault(folderId);

        /// <summary>
        /// Returns the folders directly inside the given folder (or at the top level if null), sorted by name.
        /// </summary>
        public IReadOnlyList<CollectionFolder> GetChildFolders(Guid? parentId) => sorted(folders.Values.Where(f => f.ParentID == parentId));

        /// <summary>
        /// Returns the given folder and all of its parents, starting from the top level.
        /// </summary>
        public IReadOnlyList<CollectionFolder> GetPath(Guid folderId)
        {
            var path = new List<CollectionFolder>();

            for (var folder = GetFolder(folderId); folder != null; folder = folder.ParentID is Guid parent ? GetFolder(parent) : null)
                path.Insert(0, folder);

            return path;
        }

        /// <summary>
        /// Returns the full name of a folder, including its parents (e.g. "Tournament / Pool A").
        /// </summary>
        public string GetPathName(Guid folderId) => string.Join(" / ", GetPath(folderId).Select(f => f.Name));

        /// <summary>
        /// Whether <paramref name="folderId"/> is <paramref name="ancestorId"/> or anywhere inside it.
        /// </summary>
        public bool IsWithin(Guid folderId, Guid ancestorId) => GetPath(folderId).Any(f => f.ID == ancestorId);

        /// <summary>
        /// Returns the folders the given collection is directly inside (sorted by name), or an empty list if it isn't in any folder.
        /// </summary>
        public IReadOnlyList<CollectionFolder> GetFolders(Guid collectionId) =>
            memberships.TryGetValue(collectionId, out var list) ? sorted(list.Select(GetFolder).OfType<CollectionFolder>()) : Array.Empty<CollectionFolder>();

        public bool IsInFolder(Guid collectionId, Guid folderId) =>
            memberships.TryGetValue(collectionId, out var list) && list.Contains(folderId);

        public bool IsInAnyFolder(Guid collectionId) => memberships.ContainsKey(collectionId);

        /// <summary>
        /// Whether a collection should be shown when viewing the given folder (or the top level if null).
        /// </summary>
        public bool IsDirectlyInside(Guid collectionId, Guid? folderId) =>
            folderId is Guid id ? IsInFolder(collectionId, id) : !IsInAnyFolder(collectionId);

        /// <summary>
        /// Returns the collections inside a folder.
        /// </summary>
        /// <param name="folderId">The folder.</param>
        /// <param name="recursive">Whether to also include collections inside the folder's subfolders.</param>
        public IReadOnlyList<Guid> GetCollections(Guid folderId, bool recursive = false) =>
            memberships.Where(m => m.Value.Any(f => recursive ? IsWithin(f, folderId) : f == folderId))
                       .Select(m => m.Key)
                       .ToList();

        /// <summary>
        /// Adds a collection to a folder.
        /// </summary>
        public void AddToFolder(Guid collectionId, Guid folderId)
        {
            if (!folders.ContainsKey(folderId))
                return;

            if (!memberships.TryGetValue(collectionId, out var list))
                memberships[collectionId] = list = new List<Guid>();

            if (!list.Contains(folderId))
                list.Add(folderId);

            save();
        }

        public void RemoveFromFolder(Guid collectionId, Guid folderId)
        {
            if (!memberships.TryGetValue(collectionId, out var list))
                return;

            list.Remove(folderId);

            if (list.Count == 0)
                memberships.Remove(collectionId);

            save();
        }

        /// <summary>
        /// Moves a collection from one folder to another, like moving a file.
        /// Any other folders the collection is inside are kept.
        /// </summary>
        /// <param name="collectionId">The collection.</param>
        /// <param name="from">The folder to move the collection out of, or null if it is being moved from the top level.</param>
        /// <param name="to">The folder to move the collection into, or null to move it out to the top level.</param>
        public void MoveCollection(Guid collectionId, Guid? from, Guid? to)
        {
            if (from == to)
                return;

            if (from is Guid source && memberships.TryGetValue(collectionId, out var list))
            {
                list.Remove(source);

                if (list.Count == 0)
                    memberships.Remove(collectionId);
            }

            if (to is Guid target)
                AddToFolder(collectionId, target);
            else
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
        /// Forgets about collections which no longer exist.
        /// </summary>
        /// <param name="existingCollections">All collections which currently exist.</param>
        public void RemoveMissingCollections(IEnumerable<Guid> existingCollections)
        {
            var existing = existingCollections.ToHashSet();
            var missing = memberships.Keys.Where(id => !existing.Contains(id)).ToList();

            if (missing.Count == 0)
                return;

            foreach (var id in missing)
                memberships.Remove(id);

            save();
        }

        /// <summary>
        /// Creates a new empty folder.
        /// If a folder with the same name already exists in the same place, that folder is returned instead.
        /// </summary>
        /// <param name="name">The name of the folder.</param>
        /// <param name="parentId">The folder to create the new folder inside, or null to create it at the top level.</param>
        public CollectionFolder CreateFolder(string name, Guid? parentId = null)
        {
            var folder = addFolder(name.Trim(), parentId);
            save();
            return folder;
        }

        public void RenameFolder(Guid folderId, string name)
        {
            name = name.Trim();

            if (string.IsNullOrEmpty(name) || !folders.TryGetValue(folderId, out var folder) || folder.Name == name)
                return;

            folder.Name = name;
            save();
        }

        /// <summary>
        /// Whether a folder can be moved inside another folder (or to the top level if null).
        /// A folder can't be moved inside itself or one of its own subfolders.
        /// </summary>
        public bool CanMoveFolder(Guid folderId, Guid? newParentId)
        {
            if (!folders.TryGetValue(folderId, out var folder) || folder.ParentID == newParentId)
                return false;

            return newParentId is not Guid parent || (folders.ContainsKey(parent) && !IsWithin(parent, folderId));
        }

        /// <summary>
        /// Moves a folder (along with everything inside it) inside another folder, or to the top level if null.
        /// </summary>
        /// <returns>Whether the folder was moved.</returns>
        public bool MoveFolder(Guid folderId, Guid? newParentId)
        {
            if (!CanMoveFolder(folderId, newParentId))
                return false;

            folders[folderId].ParentID = newParentId;
            save();
            return true;
        }

        /// <summary>
        /// Deletes a folder. Collections and folders inside it are kept, and move up to where the folder was.
        /// </summary>
        public void DeleteFolder(Guid folderId)
        {
            if (!folders.Remove(folderId, out var deleted))
                return;

            expanded.Remove(folderId);

            foreach (var child in folders.Values.Where(f => f.ParentID == folderId))
                child.ParentID = deleted.ParentID;

            foreach (var (collectionId, list) in memberships.ToList())
            {
                if (!list.Remove(folderId))
                    continue;

                if (deleted.ParentID is Guid parent && !list.Contains(parent))
                    list.Add(parent);

                if (list.Count == 0)
                    memberships.Remove(collectionId);
            }

            save();
        }

        /// <summary>
        /// Whether the given folder is expanded in collection lists. Changes to the returned bindable are persisted.
        /// </summary>
        public BindableBool GetExpanded(Guid folderId)
        {
            if (!expanded.TryGetValue(folderId, out var bindable))
            {
                expanded[folderId] = bindable = new BindableBool();
                bindable.BindValueChanged(_ => save(false));
            }

            return bindable;
        }

        /// <summary>
        /// Returns the value of <see cref="Configuration.OsuSetting.SongSelectCollectionFilter"/> which selects all beatmaps inside a folder.
        /// </summary>
        public static string GetFolderFilterValue(Guid folderId) => folder_filter_prefix + folderId;

        /// <summary>
        /// Checks whether a value of <see cref="Configuration.OsuSetting.SongSelectCollectionFilter"/> selects a folder.
        /// </summary>
        public static bool TryParseFolderFilterValue(string? value, [NotNullWhen(true)] out Guid? folderId)
        {
            folderId = null;

            if (value == null || !value.StartsWith(folder_filter_prefix, StringComparison.Ordinal) || !Guid.TryParse(value.AsSpan(folder_filter_prefix.Length), out var id))
                return false;

            folderId = id;
            return true;
        }

        /// <summary>
        /// Groups collection menu items into nested submenus matching the folder tree, followed by collections which aren't in any folder.
        /// A collection inside multiple folders appears in each of them.
        /// </summary>
        /// <param name="manager">The folder manager, or null if unavailable (in which case items are returned ungrouped).</param>
        /// <param name="items">The collections, alongside a function creating a menu item for each.</param>
        public static List<MenuItem> GroupMenuItems(CollectionFolderManager? manager, IEnumerable<(Guid collectionId, Func<MenuItem> createItem)> items)
        {
            if (manager == null)
                return items.Select(i => i.createItem()).ToList();

            return manager.groupMenuItems(null, items.ToList());
        }

        private List<MenuItem> groupMenuItems(Guid? parentId, List<(Guid collectionId, Func<MenuItem> createItem)> items)
        {
            var grouped = new List<MenuItem>();

            foreach (var folder in GetChildFolders(parentId))
            {
                var inside = groupMenuItems(folder.ID, items);

                if (inside.Count > 0)
                    grouped.Add(new OsuMenuItem(folder.Name) { Items = inside });
            }

            grouped.AddRange(items.Where(i => IsDirectlyInside(i.collectionId, parentId)).Select(i => i.createItem()));
            return grouped;
        }

        private static List<CollectionFolder> sorted(IEnumerable<CollectionFolder> list) => list.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();

        private CollectionFolder addFolder(string name, Guid? parentId, Guid? id = null)
        {
            var existing = folders.Values.FirstOrDefault(f => f.ParentID == parentId && string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
                return existing;

            var folder = new CollectionFolder(id ?? Guid.NewGuid(), name, parentId);
            folders[folder.ID] = folder;
            return folder;
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

                    if (data.FolderTree != null)
                        loadTree(data);
                    else
                        loadLegacy(data);
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to load collection folders");
            }
        }

        private void loadTree(StoredData data)
        {
            foreach (var folder in data.FolderTree!)
                folders[folder.ID] = new CollectionFolder(folder.ID, folder.Name, folder.ParentID);

            // Guard against a corrupted file: any folder whose parent is missing or which is inside itself is moved to the top level.
            foreach (var folder in folders.Values)
            {
                var visited = new HashSet<Guid> { folder.ID };

                for (Guid? parent = folder.ParentID; parent is Guid p; parent = folders.GetValueOrDefault(p)?.ParentID)
                {
                    if (!folders.ContainsKey(p) || !visited.Add(p))
                    {
                        folder.ParentID = null;
                        break;
                    }
                }
            }

            foreach (var (collectionId, list) in data.FolderMemberships ?? new Dictionary<Guid, List<Guid>>())
            {
                var valid = list.Where(folders.ContainsKey).Distinct().ToList();

                if (valid.Count > 0)
                    memberships[collectionId] = valid;
            }

            foreach (var folderId in data.ExpandedFolders ?? new List<Guid>())
            {
                if (folders.ContainsKey(folderId))
                    GetExpanded(folderId).Value = true;
            }
        }

        /// <summary>
        /// Loads the older format, where folders were identified by name and couldn't be nested.
        /// </summary>
        private void loadLegacy(StoredData data)
        {
            CollectionFolder folderNamed(string name) => addFolder(name, null);

            foreach (string folder in data.Folders ?? new List<string>())
                folderNamed(folder);

            foreach (var (collectionId, list) in data.Memberships ?? new Dictionary<Guid, List<string>>())
                memberships[collectionId] = list.Select(name => folderNamed(name).ID).Distinct().ToList();

            // Oldest format, where a collection could only be inside a single folder.
            foreach (var (collectionId, folder) in data.Assignments ?? new Dictionary<Guid, string>())
            {
                if (!memberships.ContainsKey(collectionId))
                    memberships[collectionId] = new List<Guid> { folderNamed(folder).ID };
            }

            foreach (string folder in data.Expanded ?? new List<string>())
                GetExpanded(folderNamed(folder).ID).Value = true;
        }

        private void save(bool notify = true)
        {
            try
            {
                var data = new StoredData
                {
                    FolderTree = folders.Values.Select(f => new StoredFolder { ID = f.ID, Name = f.Name, ParentID = f.ParentID }).ToList(),
                    FolderMemberships = memberships.ToDictionary(m => m.Key, m => m.Value.ToList()),
                    ExpandedFolders = expanded.Where(e => e.Value.Value && folders.ContainsKey(e.Key)).Select(e => e.Key).ToList(),
                };

                using (var stream = storage.CreateFileSafely(filename))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(JsonConvert.SerializeObject(data, Formatting.Indented, new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore
                    }));
                }
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
            public List<StoredFolder>? FolderTree { get; set; }

            public Dictionary<Guid, List<Guid>>? FolderMemberships { get; set; }

            public List<Guid>? ExpandedFolders { get; set; }

            // Older format, where folders were identified by name (only read, never written).

            public List<string>? Folders { get; set; }

            public Dictionary<Guid, List<string>>? Memberships { get; set; }

            public Dictionary<Guid, string>? Assignments { get; set; }

            public List<string>? Expanded { get; set; }
        }

        private class StoredFolder
        {
            public Guid ID { get; set; }

            public string Name { get; set; } = string.Empty;

            public Guid? ParentID { get; set; }
        }
    }
}
