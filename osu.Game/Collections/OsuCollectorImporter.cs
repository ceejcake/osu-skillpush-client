// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;

namespace osu.Game.Collections
{
    /// <summary>
    /// Imports a collection from osucollector.com, or from a pasted list of beatmap IDs, into the local collection database,
    /// and downloads any beatmap sets from it which are not available locally.
    /// </summary>
    public partial class OsuCollectorImporter : Component
    {
        /// <summary>
        /// Maximum number of beatmap set downloads running at once.
        /// </summary>
        private const int max_concurrent_downloads = 3;

        private static readonly HttpClient http_client = createHttpClient();

        private static HttpClient createHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

            // Some mirrors (e.g. catboy.best) reject requests without a user agent.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("osu-lazer-dev");
            return client;
        }

        /// <summary>
        /// Maximum number of beatmap lookups running at once.
        /// </summary>
        private const int max_concurrent_lookups = 4;

        /// <summary>
        /// Public mirrors of the beatmap listing, used to find out which set a beatmap belongs to. Tried in order.
        /// </summary>
        private static readonly string[] lookup_urls =
        {
            "https://osu.direct/api/v2/b/{0}",
            "https://catboy.best/api/v2/b/{0}",
        };

        /// <summary>
        /// Public beatmap mirrors used when not logged in, tried in order.
        /// </summary>
        private static readonly string[] mirror_urls =
        {
            "https://catboy.best/d/{0}",
            "https://api.nerinyan.moe/d/{0}",
            "https://osu.direct/api/d/{0}",
        };

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private GameHost host { get; set; } = null!;

        [Resolved]
        private BeatmapModelDownloader beatmapDownloader { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmapManager { get; set; } = null!;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        [Resolved]
        private INotificationOverlay? notifications { get; set; }

        private readonly Queue<int> pendingDownloads = new Queue<int>();
        private int activeDownloads;

        /// <summary>
        /// The beatmaps wanted from each beatmap set being downloaded, and the collection they are wanted for.
        /// </summary>
        private readonly Dictionary<int, List<(string collection, int beatmapId)>> wantedFromDownloads = new Dictionary<int, List<(string, int)>>();

        private ProgressNotification? downloadNotification;
        private int totalDownloads;
        private int finishedDownloads;
        private int failedDownloads;

        /// <summary>
        /// Parses an osu!collector collection ID from either a bare number or a collection URL.
        /// </summary>
        public static int? ParseCollectionId(string input)
        {
            input = input.Trim();

            var match = Regex.Match(input, @"collections/(\d+)");
            if (match.Success)
                return int.Parse(match.Groups[1].Value);

            if (int.TryParse(input, out int id) && id > 0)
                return id;

            return null;
        }

        /// <summary>
        /// Reads beatmap IDs out of pasted text. Accepts bare IDs and beatmap links, separated by anything.
        /// </summary>
        /// <remarks>
        /// These are the IDs of individual difficulties. A link to a beatmap set which doesn't name a difficulty is skipped.
        /// </remarks>
        public static IReadOnlyList<int> ParseBeatmapIds(string input)
        {
            var ids = new List<int>();
            var seen = new HashSet<int>();

            foreach (string token in input.Split(new[] { ' ', '\t', '\r', '\n', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string? number = null;

                var match = Regex.Match(token, @"beatmapsets/\d+#\w+/(\d+)");
                if (!match.Success) match = Regex.Match(token, @"(?:beatmaps|/b)/(\d+)");

                if (match.Success)
                    number = match.Groups[1].Value;
                else if (token.All(char.IsAsciiDigit))
                    number = token;

                if (number != null && int.TryParse(number, out int id) && id > 0 && seen.Add(id))
                    ids.Add(id);
            }

            return ids;
        }

        /// <summary>
        /// Creates a collection (or adds to the one with the same name) from a pasted list of beatmap IDs,
        /// downloading the beatmap sets which aren't available locally.
        /// </summary>
        public void ImportBeatmapIds(string name, string input)
        {
            name = name.Trim();

            if (name.Length == 0)
            {
                notifications?.Post(new SimpleErrorNotification { Text = "Give the collection a name first." });
                return;
            }

            var ids = ParseBeatmapIds(input);

            if (ids.Count == 0)
            {
                notifications?.Post(new SimpleErrorNotification { Text = "Couldn't find any beatmap IDs in that text." });
                return;
            }

            // Beatmaps which are already here don't need looking up.
            var local = getLocalHashes(ids);
            var unknown = ids.Where(id => !local.ContainsKey(id)).ToList();

            if (unknown.Count == 0)
            {
                onBeatmapsResolved(name, ids.Count, local.Values.ToList(), new List<BeatmapLookup>(), 0);
                return;
            }

            var lookupNotification = new ProgressNotification
            {
                Text = $"Looking up beatmaps for \"{name}\" (0 of {unknown.Count})",
                CompletionText = $"Looked up beatmaps for \"{name}\".",
                State = ProgressNotificationState.Active,
            };

            notifications?.Post(lookupNotification);

            Task.Run(async () =>
            {
                var found = new List<BeatmapLookup>();
                int done = 0;
                int notFound = 0;

                using var limit = new System.Threading.SemaphoreSlim(max_concurrent_lookups);

                await Task.WhenAll(unknown.Select(async id =>
                {
                    await limit.WaitAsync().ConfigureAwait(false);

                    BeatmapLookup? result = null;

                    try
                    {
                        if (!lookupNotification.CancellationToken.IsCancellationRequested)
                            result = await LookupBeatmap(id).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        Logger.Log($"Lookup of beatmap {id} failed: {e.Message}");
                    }
                    finally
                    {
                        limit.Release();
                    }

                    lock (found)
                    {
                        if (result != null)
                            found.Add(result);
                        else
                            notFound++;

                        done++;
                    }

                    int doneNow = done;

                    runOnUpdateThread(() =>
                    {
                        if (lookupNotification.State != ProgressNotificationState.Active)
                            return;

                        lookupNotification.Progress = (float)doneNow / unknown.Count;
                        lookupNotification.Text = $"Looking up beatmaps for \"{name}\" ({doneNow} of {unknown.Count})";
                    });
                })).ConfigureAwait(false);

                runOnUpdateThread(() =>
                {
                    if (lookupNotification.State == ProgressNotificationState.Active)
                        lookupNotification.State = ProgressNotificationState.Completed;

                    onBeatmapsResolved(name, ids.Count, local.Values.ToList(), found, notFound);
                });
            });
        }

        private Dictionary<int, string> getLocalHashes(IReadOnlyCollection<int> beatmapIds) => realm.Run(r =>
        {
            var hashes = new Dictionary<int, string>();

            // A beatmap set imported on another thread a moment ago may not have reached this thread yet.
            r.Refresh();

            // One query per ID, rather than a pass over every beatmap in the library.
            foreach (int id in beatmapIds)
            {
                foreach (var beatmap in r.All<BeatmapInfo>().Where(b => b.OnlineID == id))
                {
                    if (beatmap.BeatmapSet?.DeletePending != false || string.IsNullOrEmpty(beatmap.MD5Hash))
                        continue;

                    hashes[id] = beatmap.MD5Hash;
                }
            }

            return hashes;
        });

        private void onBeatmapsResolved(string name, int requested, List<string> localHashes, List<BeatmapLookup> found, int notFound)
        {
            var hashes = localHashes.Concat(found.Select(f => f.Checksum)).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
            int added = addToCollection(name, hashes);

            foreach (var lookup in found)
            {
                if (!wantedFromDownloads.TryGetValue(lookup.BeatmapSetID, out var wanted))
                    wantedFromDownloads[lookup.BeatmapSetID] = wanted = new List<(string, int)>();

                wanted.Add((name, lookup.ID));
            }

            var missingSetIds = getMissingSets(found.Select(f => f.BeatmapSetID));

            // Sets which are already here, but with a different version of the difficulty than the one listed online.
            foreach (int setId in found.Select(f => f.BeatmapSetID).Distinct().Except(missingSetIds).ToList())
                addDownloadedBeatmaps(setId);

            string summary = $"Created \"{name}\" from {requested} beatmap IDs ({added} added). {missingSetIds.Count} beatmap sets need downloading.";
            if (notFound > 0)
                summary += $" {notFound} IDs couldn't be found.";

            notifications?.Post(new ProgressCompletionNotification { Text = summary });

            queueDownloads(missingSetIds);
        }

        /// <summary>
        /// Finds the checksum and beatmap set of a beatmap which isn't available locally.
        /// </summary>
        protected virtual async Task<BeatmapLookup?> LookupBeatmap(int beatmapId)
        {
            foreach (string lookup in lookup_urls)
            {
                string url = string.Format(lookup, beatmapId);

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        using var response = await http_client.GetAsync(url).ConfigureAwait(false);

                        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(3 * (attempt + 1))).ConfigureAwait(false);
                            continue;
                        }

                        if (!response.IsSuccessStatusCode)
                            break;

                        var result = JsonConvert.DeserializeObject<BeatmapLookup>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

                        if (result != null && result.BeatmapSetID > 0)
                        {
                            result.ID = beatmapId;
                            return result;
                        }

                        break;
                    }
                    catch (Exception e)
                    {
                        Logger.Log($"Lookup of beatmap {beatmapId} at {url} failed: {e.Message}");
                        break;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Adds the local copies of the beatmaps wanted from a beatmap set to their collections.
        /// </summary>
        /// <remarks>
        /// The copy a mirror hands out is not always the version the listing describes, in which case its checksum differs
        /// from the one already added to the collection.
        /// </remarks>
        private void addDownloadedBeatmaps(int setId)
        {
            if (!wantedFromDownloads.Remove(setId, out var wanted))
                return;

            var hashes = getLocalHashes(wanted.Select(w => w.beatmapId).ToList());

            foreach (var group in wanted.GroupBy(w => w.collection))
            {
                var groupHashes = group.Where(w => hashes.ContainsKey(w.beatmapId)).Select(w => hashes[w.beatmapId]).ToList();

                if (groupHashes.Count > 0)
                    addToCollection(group.Key, groupHashes, false);
            }
        }

        /// <summary>
        /// Adds beatmaps to the collection with the given name, creating it if there isn't one.
        /// </summary>
        /// <returns>The number of beatmaps which weren't already in the collection.</returns>
        private int addToCollection(string name, List<string> hashes, bool createIfMissing = true) => realm.Write(r =>
        {
            var existing = r.All<BeatmapCollection>().FirstOrDefault(c => c.Name == name);

            if (existing == null)
            {
                if (!createIfMissing)
                    return 0;

                r.Add(new BeatmapCollection(name, hashes));
                return hashes.Count;
            }

            int count = 0;

            foreach (string hash in hashes)
            {
                if (existing.BeatmapMD5Hashes.Contains(hash))
                    continue;

                existing.BeatmapMD5Hashes.Add(hash);
                count++;
            }

            if (count > 0)
                existing.LastModified = DateTimeOffset.UtcNow;

            return count;
        });

        private List<int> getMissingSets(IEnumerable<int> setIds) => realm.Run(r =>
        {
            var localSetIds = r.All<BeatmapSetInfo>().Where(s => !s.DeletePending).AsEnumerable().Select(s => s.OnlineID).ToHashSet();
            return setIds.Where(id => id > 0 && !localSetIds.Contains(id)).Distinct().ToList();
        });

        public void Import(string input)
        {
            int? collectionId = ParseCollectionId(input);

            if (collectionId == null)
            {
                notifications?.Post(new SimpleErrorNotification { Text = "Couldn't read an osu!collector collection ID from that input." });
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    string json = await http_client.GetStringAsync($"https://osucollector.com/api/collections/{collectionId}").ConfigureAwait(false);
                    var collection = JsonConvert.DeserializeObject<CollectorCollection>(json);

                    if (collection?.BeatmapSets == null)
                        throw new InvalidOperationException("Collection response was empty.");

                    runOnUpdateThread(() => onCollectionFetched(collection));
                }
                catch (Exception e)
                {
                    Logger.Error(e, $"Failed to fetch osu!collector collection {collectionId}");
                    runOnUpdateThread(() => notifications?.Post(new SimpleErrorNotification { Text = $"Failed to fetch osu!collector collection {collectionId}: {e.Message}" }));
                }
            });
        }

        private void onCollectionFetched(CollectorCollection collection)
        {
            string name = string.IsNullOrWhiteSpace(collection.Name) ? $"osu!collector {collection.ID}" : collection.Name.Trim();

            var hashes = collection.BeatmapSets
                                   .SelectMany(s => s.Beatmaps)
                                   .Select(b => b.Checksum)
                                   .Where(c => !string.IsNullOrEmpty(c))
                                   .Distinct()
                                   .ToList();

            int added = addToCollection(name, hashes);

            var missingSetIds = getMissingSets(collection.BeatmapSets.Select(s => s.ID));

            notifications?.Post(new ProgressCompletionNotification
            {
                Text = $"Imported \"{name}\" ({added} new of {hashes.Count} beatmaps). {missingSetIds.Count} beatmap sets need downloading."
            });

            queueDownloads(missingSetIds);
        }

        private void queueDownloads(List<int> missingSetIds)
        {
            if (missingSetIds.Count == 0)
                return;

            foreach (int id in missingSetIds)
                pendingDownloads.Enqueue(id);

            if (downloadNotification != null)
                totalDownloads += missingSetIds.Count;
            else
            {
                totalDownloads = missingSetIds.Count;
                finishedDownloads = 0;
                failedDownloads = 0;

                notifications?.Post(downloadNotification = new ProgressNotification
                {
                    Text = $"Downloading collection beatmaps (0 of {totalDownloads})",
                    CompletionText = "Finished downloading collection beatmaps.",
                    State = ProgressNotificationState.Active,
                });
            }

            startNextDownloads();
        }

        private void startNextDownloads()
        {
            bool noVideo = config.Get<bool>(OsuSetting.PreferNoVideo);

            while (activeDownloads < max_concurrent_downloads && pendingDownloads.Count > 0)
            {
                int setId = pendingDownloads.Dequeue();

                // Development builds can't log in to the official servers, so fall back to public mirrors.
                if (UseMirrors)
                {
                    activeDownloads++;

                    DownloadFromMirror(setId, noVideo).ContinueWith(t => runOnUpdateThread(() =>
                    {
                        bool success = t.IsCompletedSuccessfully && t.GetResultSafely();

                        if (success)
                            addDownloadedBeatmaps(setId);

                        activeDownloads--;
                        onDownloadFinished(success);
                        startNextDownloads();
                    }));

                    continue;
                }

                var set = new APIBeatmapSet { OnlineID = setId };

                if (!beatmapDownloader.Download(set, noVideo))
                {
                    onDownloadFinished(false);
                    continue;
                }

                var request = beatmapDownloader.GetExistingDownload(set);

                if (request == null)
                {
                    onDownloadFinished(true);
                    continue;
                }

                activeDownloads++;

                bool finished = false;

                void finish(bool success) => runOnUpdateThread(() =>
                {
                    if (finished) return;

                    finished = true;
                    activeDownloads--;
                    onDownloadFinished(success);
                    startNextDownloads();
                });

                ((APIRequest)request).Success += () => finish(true);
                request.Failure += _ => finish(false);
            }
        }

        /// <summary>
        /// Whether beatmap sets are downloaded from public mirrors rather than the official servers.
        /// </summary>
        protected virtual bool UseMirrors => !api.IsLoggedIn;

        /// <summary>
        /// Downloads and imports a beatmap set from the first mirror which has it.
        /// </summary>
        protected virtual async Task<bool> DownloadFromMirror(int setId, bool noVideo)
        {
            foreach (string mirror in mirror_urls)
            {
                string url = string.Format(mirror, setId) + (noVideo ? "?noVideo=1" : string.Empty);
                string path = Path.Combine(Path.GetTempPath(), $"osucollector-{setId}-{Guid.NewGuid():N}.osz");

                try
                {
                    using (var response = await http_client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();

                        await using (var file = File.Create(path))
                            await response.Content.CopyToAsync(file).ConfigureAwait(false);
                    }

                    var imported = await beatmapManager.Import(new ImportTask(path)).ConfigureAwait(false);

                    if (imported != null)
                    {
                        Logger.Log($"Downloaded and imported beatmap set {setId} from {url}");
                        return true;
                    }
                }
                catch (Exception e)
                {
                    Logger.Log($"Mirror download of beatmap set {setId} from {url} failed: {e.Message}");
                }
                finally
                {
                    try
                    {
                        if (File.Exists(path))
                            File.Delete(path);
                    }
                    catch
                    {
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Runs an action on the update thread.
        /// </summary>
        /// <remarks>
        /// This component lives inside the settings overlay, which stops updating (and running scheduled tasks) while hidden,
        /// so the host's scheduler is used rather than <see cref="Drawable.Schedule"/> to keep downloads progressing.
        /// </remarks>
        private void runOnUpdateThread(Action action) => host.UpdateThread.Scheduler.Add(action);

        private void onDownloadFinished(bool success)
        {
            finishedDownloads++;
            if (!success)
                failedDownloads++;

            if (downloadNotification == null)
                return;

            downloadNotification.Progress = (float)finishedDownloads / totalDownloads;
            downloadNotification.Text = $"Downloading collection beatmaps ({finishedDownloads} of {totalDownloads}"
                                        + (failedDownloads > 0 ? $", {failedDownloads} failed)" : ")");

            if (finishedDownloads >= totalDownloads && pendingDownloads.Count == 0 && activeDownloads == 0)
            {
                if (failedDownloads > 0)
                    downloadNotification.CompletionText = $"Finished downloading collection beatmaps ({failedDownloads} failed or unavailable).";

                downloadNotification.State = ProgressNotificationState.Completed;
                downloadNotification = null;
            }
        }

        /// <summary>
        /// What a beatmap listing says about one beatmap.
        /// </summary>
        public class BeatmapLookup
        {
            [JsonProperty("id")]
            public int ID { get; set; }

            [JsonProperty("beatmapset_id")]
            public int BeatmapSetID { get; set; }

            [JsonProperty("checksum")]
            public string Checksum { get; set; } = string.Empty;
        }

        private class CollectorCollection
        {
            [JsonProperty("id")]
            public int ID { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; } = string.Empty;

            [JsonProperty("beatmapsets")]
            public List<CollectorBeatmapSet> BeatmapSets { get; set; } = new List<CollectorBeatmapSet>();
        }

        private class CollectorBeatmapSet
        {
            [JsonProperty("id")]
            public int ID { get; set; }

            [JsonProperty("beatmaps")]
            public List<CollectorBeatmap> Beatmaps { get; set; } = new List<CollectorBeatmap>();
        }

        private class CollectorBeatmap
        {
            [JsonProperty("checksum")]
            public string Checksum { get; set; } = string.Empty;
        }
    }
}
