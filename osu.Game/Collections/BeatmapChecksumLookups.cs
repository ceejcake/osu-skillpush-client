// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using osu.Framework.Logging;
using osu.Framework.Platform;

namespace osu.Game.Collections
{
    /// <summary>
    /// Remembers which beatmap each checksum found in a collection turned out to be online, so that repairing collections
    /// doesn't have to look the same checksums up again (which takes a long time, as lookups are rate limited).
    /// </summary>
    public class BeatmapChecksumLookups
    {
        private const string filename = "collection-checksum-lookups.json";

        private readonly Storage storage;

        private readonly Dictionary<string, Entry?> entries = new Dictionary<string, Entry?>();

        public BeatmapChecksumLookups(Storage storage)
        {
            this.storage = storage;
            load();
        }

        /// <summary>
        /// Gets what is known about a checksum.
        /// </summary>
        /// <param name="checksum">The checksum.</param>
        /// <param name="entry">The beatmap it is, or null if it is known not to exist online (e.g. an outdated version).</param>
        /// <returns>Whether the checksum has been looked up before.</returns>
        public bool TryGet(string checksum, out Entry? entry)
        {
            lock (entries)
                return entries.TryGetValue(checksum, out entry);
        }

        /// <param name="checksum">The checksum.</param>
        /// <param name="entry">The beatmap it is, or null if it doesn't exist online.</param>
        public void Set(string checksum, Entry? entry)
        {
            lock (entries)
                entries[checksum] = entry;
        }

        public void Save()
        {
            try
            {
                string json;

                lock (entries)
                    json = JsonConvert.SerializeObject(entries);

                using (var stream = storage.CreateFileSafely(filename))
                using (var writer = new StreamWriter(stream))
                    writer.Write(json);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to save collection checksum lookups");
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
                    var data = JsonConvert.DeserializeObject<Dictionary<string, Entry?>>(reader.ReadToEnd());

                    foreach (var (checksum, entry) in data ?? new Dictionary<string, Entry?>())
                        entries[checksum] = entry;
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to load collection checksum lookups");
            }
        }

        /// <param name="BeatmapID">The online ID of the beatmap.</param>
        /// <param name="BeatmapSetID">The online ID of its beatmap set.</param>
        public record Entry(int BeatmapID, int BeatmapSetID);
    }
}
