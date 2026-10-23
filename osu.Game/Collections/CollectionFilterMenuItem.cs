// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Localisation;
using osu.Game.Database;
using osu.Game.Localisation;

namespace osu.Game.Collections
{
    /// <summary>
    /// A <see cref="BeatmapCollection"/> filter.
    /// </summary>
    public class CollectionFilterMenuItem : IEquatable<CollectionFilterMenuItem>
    {
        /// <summary>
        /// The collection to filter beatmaps from.
        /// May be null to not filter by collection (include all beatmaps).
        /// </summary>
        public readonly Live<BeatmapCollection>? Collection;

        /// <summary>
        /// The name of the collection.
        /// </summary>
        public LocalisableString CollectionName { get; }

        /// <summary>
        /// The folder this collection is displayed inside, if any.
        /// </summary>
        public Guid? ParentFolderID { get; }

        /// <summary>
        /// The folders this item is nested inside, starting from the top level. The item is only shown while all of them are expanded.
        /// </summary>
        public IReadOnlyList<Guid> AncestorFolderIDs { get; protected init; } = Array.Empty<Guid>();

        /// <summary>
        /// Whether this is the first entry for this collection. Collections inside multiple folders have one entry per folder,
        /// and only the first is shown while searching so that results aren't duplicated.
        /// </summary>
        public bool IsPrimary { get; } = true;

        /// <summary>
        /// Creates a new <see cref="CollectionFilterMenuItem"/>.
        /// </summary>
        /// <param name="collection">The collection to filter beatmaps from.</param>
        /// <param name="ancestorFolderIds">The folders the collection is displayed inside, starting from the top level.</param>
        /// <param name="isPrimary">Whether this is the first entry for this collection.</param>
        public CollectionFilterMenuItem(Live<BeatmapCollection> collection, IReadOnlyList<Guid>? ancestorFolderIds = null, bool isPrimary = true)
            : this(collection.PerformRead(c => c.Name))
        {
            Collection = collection;
            AncestorFolderIDs = ancestorFolderIds ?? Array.Empty<Guid>();
            ParentFolderID = AncestorFolderIDs.Count > 0 ? AncestorFolderIDs[^1] : null;
            IsPrimary = isPrimary;
        }

        protected CollectionFilterMenuItem(LocalisableString name)
        {
            CollectionName = name;
        }

        public virtual bool Equals(CollectionFilterMenuItem? other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;

            if (Collection == null) return false;

            return Collection.ID == other.Collection?.ID && ParentFolderID == other.ParentFolderID;
        }

        public override int GetHashCode() => HashCode.Combine(Collection?.ID, ParentFolderID);
    }

    public class AllBeatmapsCollectionFilterMenuItem : CollectionFilterMenuItem
    {
        public AllBeatmapsCollectionFilterMenuItem()
            : base(CollectionsStrings.AllBeatmaps)
        {
        }

        public override bool Equals(CollectionFilterMenuItem? other) => other is AllBeatmapsCollectionFilterMenuItem;

        public override int GetHashCode() => 1;
    }

    public class ManageCollectionsFilterMenuItem : CollectionFilterMenuItem
    {
        public ManageCollectionsFilterMenuItem()
            : base(CollectionsStrings.ManageCollections)
        {
        }

        public override bool Equals(CollectionFilterMenuItem? other) => other is ManageCollectionsFilterMenuItem;

        public override int GetHashCode() => 2;
    }

    /// <summary>
    /// A folder of collections. Selecting it filters to all beatmaps inside the folder's collections (including those in subfolders).
    /// </summary>
    public class CollectionFolderFilterMenuItem : CollectionFilterMenuItem
    {
        public readonly Guid FolderID;

        public readonly string FolderName;

        /// <param name="folder">The folder.</param>
        /// <param name="collectionCount">The number of collections inside the folder, including those in subfolders.</param>
        /// <param name="ancestorFolderIds">The folders this folder is inside, starting from the top level.</param>
        public CollectionFolderFilterMenuItem(CollectionFolder folder, int collectionCount, IReadOnlyList<Guid>? ancestorFolderIds = null)
            : base($"{folder.Name} ({collectionCount})")
        {
            FolderID = folder.ID;
            FolderName = folder.Name;
            AncestorFolderIDs = ancestorFolderIds ?? Array.Empty<Guid>();
        }

        public override bool Equals(CollectionFilterMenuItem? other) => other is CollectionFolderFilterMenuItem folder && folder.FolderID == FolderID;

        public override int GetHashCode() => HashCode.Combine(3, FolderID);
    }
}
