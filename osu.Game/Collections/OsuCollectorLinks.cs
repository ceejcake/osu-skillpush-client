// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using osu.Framework.Logging;
using osu.Framework.Platform;

namespace osu.Game.Collections
{
    /// <summary>
    /// Remembers which osu!collector collection each local <see cref="BeatmapCollection"/> was imported from, so that it can be updated later.
    /// </summary>
    /// <remarks>
    /// This is stored in a separate file rather than in realm to avoid changing the database schema.
    /// </remarks>
    public class OsuCollectorLinks
    {
        private const string filename = "osucollector-links.json";

        private readonly Storage storage;

        private readonly Dictionary<Guid, int> links = new Dictionary<Guid, int>();

        public OsuCollectorLinks(Storage storage)
        {
            this.storage = storage;
            load();
        }

        /// <summary>
        /// Returns the osu!collector collection the given collection was imported from, if it is known.
        /// </summary>
        public int? Get(Guid collectionId)
        {
            lock (links)
                return links.TryGetValue(collectionId, out int id) ? id : null;
        }

        public void Set(Guid collectionId, int collectorId)
        {
            lock (links)
            {
                if (links.TryGetValue(collectionId, out int existing) && existing == collectorId)
                    return;

                links[collectionId] = collectorId;
                save();
            }
        }

        /// <summary>
        /// Forgets about collections which no longer exist.
        /// </summary>
        public void RemoveMissingCollections(IEnumerable<Guid> existingCollections)
        {
            var existing = existingCollections.ToHashSet();

            lock (links)
            {
                var missing = links.Keys.Where(id => !existing.Contains(id)).ToList();

                if (missing.Count == 0)
                    return;

                foreach (var id in missing)
                    links.Remove(id);

                save();
            }
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
                    var data = JsonConvert.DeserializeObject<Dictionary<Guid, int>>(reader.ReadToEnd());

                    foreach (var (collectionId, collectorId) in data ?? new Dictionary<Guid, int>())
                        links[collectionId] = collectorId;
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to load osu!collector links");
            }
        }

        private void save()
        {
            try
            {
                using (var stream = storage.CreateFileSafely(filename))
                using (var writer = new StreamWriter(stream))
                    writer.Write(JsonConvert.SerializeObject(links, Formatting.Indented));
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to save osu!collector links");
            }
        }
    }
}
