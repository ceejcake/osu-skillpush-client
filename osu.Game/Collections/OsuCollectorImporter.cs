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
using Realms;

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
        /// Mirrors rate limit aggressively, so this is kept low.
        /// </summary>
        private const int max_concurrent_downloads = 3;

        /// <summary>
        /// How many download requests are made for a beatmap set before giving up on it (when mirrors fail for reasons other than not having the set).
        /// </summary>
        private const int max_download_attempts = 12;

        /// <summary>
        /// The longest a mirror is left alone for after asking for requests to slow down.
        /// osu.direct allows a number of downloads each hour, and asks to wait until the start of the next hour once they're used up.
        /// </summary>
        private static readonly TimeSpan max_mirror_cooldown = TimeSpan.FromMinutes(65);

        private static readonly HttpClient http_client = createHttpClient();

        /// <summary>
        /// Sends a GET request to osu!collector or a beatmap mirror. The response body is read as it arrives.
        /// </summary>
        protected virtual Task<HttpResponseMessage> SendRequest(string url) => http_client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

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
        /// <summary>
        /// Looks up a beatmap by its checksum, with the checksum in place of {0}.
        /// </summary>
        private const string checksum_lookup_url = "https://osu.direct/api/v2/md5/{0}";

        private static readonly string[] lookup_urls =
        {
            "https://osu.direct/api/v2/b/{0}",
            "https://catboy.best/api/v2/b/{0}",
        };

        /// <summary>
        /// Public beatmap mirrors used when not logged in, tried in order. While one is rate limited, the others are used.
        /// </summary>
        /// <remarks>
        /// catboy.best isn't used for downloads, as it bans clients which download a lot.
        /// </remarks>
        private static readonly Mirror[] mirror_urls =
        {
            new Mirror("osu.direct", "https://osu.direct/api/d/{0}", "https://osu.direct/api/d/{0}?noVideo=1"),
            new Mirror("sayobot", "https://dl.sayobot.cn/beatmaps/download/full/{0}", "https://dl.sayobot.cn/beatmaps/download/novideo/{0}"),
            new Mirror("beatconnect", "https://beatconnect.io/b/{0}/", "https://beatconnect.io/b/{0}/"),
            new Mirror("nerinyan", "https://api.nerinyan.moe/d/{0}", "https://api.nerinyan.moe/d/{0}?noVideo=1"),
        };

        /// <param name="Name">The name of the mirror, shown to the user.</param>
        /// <param name="Url">The download URL of a beatmap set, with the set's ID in place of {0}.</param>
        /// <param name="NoVideoUrl">The download URL of a beatmap set without its video, with the set's ID in place of {0}.</param>
        private record Mirror(string Name, string Url, string NoVideoUrl);

        /// <summary>
        /// When every mirror is rate limited, the time the first of them may be used again.
        /// </summary>
        private DateTimeOffset? waitingForMirrorsUntil;

        /// <summary>
        /// When each mirror may next be used, after it asked for requests to slow down (429 Too Many Requests).
        /// </summary>
        private readonly Dictionary<Mirror, DateTimeOffset> mirrorCooldowns = new Dictionary<Mirror, DateTimeOffset>();

        /// <summary>
        /// How many times in a row each mirror has asked for requests to slow down.
        /// </summary>
        private readonly Dictionary<Mirror, int> mirrorStrikes = new Dictionary<Mirror, int>();

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

        [Resolved]
        private Storage storage { get; set; } = null!;

        private OsuCollectorLinks? links;

        /// <summary>
        /// Which osu!collector collection each local collection was imported from.
        /// </summary>
        private OsuCollectorLinks collectorLinks => links ??= new OsuCollectorLinks(storage);

        private readonly Queue<int> pendingDownloads = new Queue<int>();
        private int activeDownloads;

        /// <summary>
        /// Beatmap sets which are waiting to be downloaded or being downloaded.
        /// A set inside several collections is only downloaded once.
        /// </summary>
        private readonly HashSet<int> queuedSets = new HashSet<int>();

        /// <summary>
        /// Beatmap sets which were already available locally, but were missing a wanted beatmap (or had a different version of it)
        /// and so were downloaded again. Each is only downloaded again once per session: if a fresh copy still doesn't have the
        /// beatmap, it no longer exists online in that form.
        /// </summary>
        private readonly HashSet<int> redownloadedSets = new HashSet<int>();

        private BeatmapChecksumLookups? checksumLookups;

        private BeatmapChecksumLookups lookups => checksumLookups ??= new BeatmapChecksumLookups(storage);

        /// <summary>
        /// How long to wait between checksum lookups while repairing collections, to stay within the mirror's rate limit.
        /// </summary>
        private static readonly TimeSpan checksum_lookup_interval = TimeSpan.FromMilliseconds(550);

        /// <summary>
        /// Beatmap sets which no mirror has (or which mirrors only have broken copies of). These aren't tried again until the game is restarted.
        /// </summary>
        private readonly HashSet<int> unavailableSets = new HashSet<int>();

        /// <summary>
        /// How many times a mirror may fail for a beatmap set with a server or connection error before it isn't tried for that set any more.
        /// </summary>
        private const int max_mirror_errors = 3;

        /// <summary>
        /// The beatmaps wanted from each beatmap set, and the collection they are wanted for.
        /// Once a set is available locally, the local versions of these beatmaps are put in their collections.
        /// </summary>
        private readonly Dictionary<int, List<WantedBeatmap>> wantedFromDownloads = new Dictionary<int, List<WantedBeatmap>>();

        private ProgressNotification? downloadNotification;
        private int totalDownloads;
        private int finishedDownloads;
        private int failedDownloads;

        /// <summary>
        /// Parses any number of osu!collector collection IDs from bare numbers or collection URLs, separated by anything.
        /// </summary>
        public static IReadOnlyList<int> ParseCollectionIds(string input)
        {
            var ids = new List<int>();

            foreach (string token in input.Split(new[] { ' ', '\t', '\r', '\n', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (ParseCollectionId(token) is int id && !ids.Contains(id))
                    ids.Add(id);
            }

            return ids;
        }

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
        public static IReadOnlyList<int> ParseBeatmapIds(string input) => ParseBeatmapIds(input, out _);

        /// <inheritdoc cref="ParseBeatmapIds(string)"/>
        /// <param name="input">The pasted text.</param>
        /// <param name="duplicates">How many IDs were left out because they were already listed earlier in the text.</param>
        public static IReadOnlyList<int> ParseBeatmapIds(string input, out int duplicates)
        {
            var ids = new List<int>();
            var seen = new HashSet<int>();

            duplicates = 0;

            foreach (string token in input.Split(new[] { ' ', '\t', '\r', '\n', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string? number = null;

                var match = Regex.Match(token, @"beatmapsets/\d+#\w+/(\d+)");
                if (!match.Success) match = Regex.Match(token, @"(?:beatmaps|/b)/(\d+)");

                if (match.Success)
                    number = match.Groups[1].Value;
                else if (token.All(char.IsAsciiDigit))
                    number = token;

                if (number == null || !int.TryParse(number, out int id) || id <= 0)
                    continue;

                if (seen.Add(id))
                    ids.Add(id);
                else
                    duplicates++;
            }

            return ids;
        }

        /// <summary>
        /// Joins the lines of a pasted list into one line, as text boxes are a single line and would otherwise run the entries together.
        /// </summary>
        public static string JoinPastedLines(string pasted) => string.Join(' ', pasted.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries));

        /// <summary>
        /// Creates a collection (or adds to the one with the same name) from a pasted list of beatmap IDs,
        /// downloading the beatmap sets which aren't available locally.
        /// IDs listed more than once, and beatmaps already in the collection, are skipped.
        /// </summary>
        public void ImportBeatmapIds(string name, string input)
        {
            name = name.Trim();

            if (name.Length == 0)
            {
                notifications?.Post(new SimpleErrorNotification { Text = "Give the collection a name first." });
                return;
            }

            var ids = ParseBeatmapIds(input, out int duplicates);

            if (ids.Count == 0)
            {
                notifications?.Post(new SimpleErrorNotification { Text = "Couldn't find any beatmap IDs in that text." });
                return;
            }

            var request = new BeatmapIdsRequest(name, ids.Count, duplicates, realm.Run(r => r.All<BeatmapCollection>().Any(c => c.Name == name)));

            // Beatmaps which are already here don't need looking up.
            var local = getLocalHashes(ids);
            var unknown = ids.Where(id => !local.ContainsKey(id)).ToList();

            if (unknown.Count == 0)
            {
                onBeatmapsResolved(request, local.Values.ToList(), new List<BeatmapLookup>(), 0);
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

                    onBeatmapsResolved(request, local.Values.ToList(), found, notFound);
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

        private void onBeatmapsResolved(BeatmapIdsRequest request, List<string> localHashes, List<BeatmapLookup> found, int notFound)
        {
            string name = request.Name;

            var hashes = localHashes.Concat(found.Select(f => f.Checksum)).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
            int added = addToCollection(name, hashes);

            foreach (var lookup in found)
                addWanted(lookup.BeatmapSetID, new WantedBeatmap(name, lookup.ID, lookup.Checksum));

            // Beatmaps whose set is here put the version which is here in the collection, then any sets which are missing
            // entirely or are missing one of the beatmaps are downloaded.
            foreach (int setId in found.Select(f => f.BeatmapSetID).Distinct().ToList())
                addDownloadedBeatmaps(setId);

            var missingSetIds = getSetsNeedingDownload(found.Select(f => (f.BeatmapSetID, f.ID, f.Checksum)));

            int alreadyInCollection = hashes.Count - added;

            string summary = request.CollectionExisted
                ? $"Updated \"{name}\": {added} added, {alreadyInCollection} already in it."
                : $"Created \"{name}\" from {request.UniqueIds} beatmap IDs.";

            if (request.Duplicates > 0)
                summary += $" Skipped {request.Duplicates} duplicate IDs.";

            if (notFound > 0)
                summary += $" {notFound} IDs couldn't be found.";

            summary += missingSetIds.Count > 0 ? $" Downloading {missingSetIds.Count} missing beatmap sets." : " Nothing needs downloading.";

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
                        using var response = await SendRequest(url).ConfigureAwait(false);

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

        private void addWanted(int setId, WantedBeatmap beatmap)
        {
            if (!wantedFromDownloads.TryGetValue(setId, out var wanted))
                wantedFromDownloads[setId] = wanted = new List<WantedBeatmap>();

            wanted.Add(beatmap);
        }

        /// <summary>
        /// Puts the local copies of the beatmaps wanted from a beatmap set in their collections.
        /// </summary>
        /// <remarks>
        /// The local copy is not always the version which was listed (for instance, if a mirror hands out a newer version, or the
        /// beatmap was updated since the listing was made), in which case its checksum differs from the one already in the collection.
        /// The listed version can then never be found locally, so it is replaced by the local version, keeping the collection's size accurate.
        /// </remarks>
        /// <returns>The number of beatmaps whose listed version was replaced by a different local version.</returns>
        private int addDownloadedBeatmaps(int setId)
        {
            if (!wantedFromDownloads.Remove(setId, out var wanted))
                return 0;

            var hashes = getLocalHashes(wanted.Select(w => w.BeatmapID).ToList());
            int replaced = 0;

            // Beatmaps which still aren't here stay wanted, so that they are put in their collections if the set is downloaded.
            var unresolved = wanted.Where(w => !hashes.ContainsKey(w.BeatmapID)).ToList();

            if (unresolved.Count > 0)
                wantedFromDownloads[setId] = unresolved;

            foreach (var group in wanted.GroupBy(w => w.Collection))
            {
                var versions = group.Where(w => hashes.ContainsKey(w.BeatmapID))
                                    .Select(w => (listed: w.ListedChecksum, local: hashes[w.BeatmapID]))
                                    .ToList();

                if (versions.Count > 0)
                    replaced += replaceVersions(group.Key, versions);
            }

            return replaced;
        }

        /// <summary>
        /// Adds the local versions of beatmaps to a collection, removing the listed versions where they differ.
        /// </summary>
        /// <returns>The number of listed versions which were replaced.</returns>
        private int replaceVersions(string name, List<(string listed, string local)> versions) => realm.Write(r =>
        {
            var collection = r.All<BeatmapCollection>().FirstOrDefault(c => c.Name == name);

            if (collection == null)
                return 0;

            int replaced = 0;
            bool changed = false;

            foreach (var (listed, local) in versions)
            {
                if (!collection.BeatmapMD5Hashes.Contains(local))
                {
                    collection.BeatmapMD5Hashes.Add(local);
                    changed = true;
                }

                if (!string.IsNullOrEmpty(listed) && listed != local && collection.BeatmapMD5Hashes.Remove(listed))
                {
                    replaced++;
                    changed = true;
                }
            }

            if (changed)
                collection.LastModified = DateTimeOffset.UtcNow;

            return replaced;
        });

        /// <summary>
        /// Adds beatmaps to the collection with the given name, creating it if there isn't one.
        /// </summary>
        /// <returns>The number of beatmaps which weren't already in the collection.</returns>
        private int addToCollection(string name, List<string> hashes) => addToCollection(name, hashes, out _);

        /// <inheritdoc cref="addToCollection(string, List{string})"/>
        private int addToCollection(string name, List<string> hashes, out Guid collectionId)
        {
            Guid id = Guid.Empty;

            int added = realm.Write(r =>
            {
                var existing = r.All<BeatmapCollection>().FirstOrDefault(c => c.Name == name);

                if (existing == null)
                {
                    var created = new BeatmapCollection(name, hashes);
                    r.Add(created);
                    id = created.ID;
                    return hashes.Count;
                }

                id = existing.ID;
                return addHashes(existing, hashes);
            });

            collectionId = id;
            return added;
        }

        private static int addHashes(BeatmapCollection existing, List<string> hashes)
        {
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
        }

        /// <summary>
        /// Works out which beatmap sets have to be downloaded to get the given beatmaps: those which aren't here at all, and
        /// those which are here but don't have one of the beatmaps (in any version). The latter happens when the copy here is
        /// older or newer than the one the list was made from, which can have different difficulties.
        /// </summary>
        /// <param name="wanted">The beatmap set, online ID and listed checksum of each wanted beatmap.</param>
        private List<int> getSetsNeedingDownload(IEnumerable<(int setId, int beatmapId, string checksum)> wanted) => realm.Run(r =>
        {
            // A beatmap set imported on another thread a moment ago may not have reached this thread yet.
            r.Refresh();

            var localSetIds = r.All<BeatmapSetInfo>().Where(s => !s.DeletePending).AsEnumerable().Select(s => s.OnlineID).ToHashSet();
            var result = new List<int>();

            foreach (var group in wanted.Where(w => w.setId > 0).GroupBy(w => w.setId))
            {
                if (!localSetIds.Contains(group.Key))
                {
                    result.Add(group.Key);
                    continue;
                }

                if (redownloadedSets.Contains(group.Key))
                    continue;

                bool incomplete = group.Any(w => !hasLocalBeatmap(r, w.beatmapId) && !hasLocalChecksum(r, w.checksum));

                if (incomplete)
                {
                    redownloadedSets.Add(group.Key);
                    result.Add(group.Key);
                }
            }

            return result;
        });

        private static bool hasLocalBeatmap(Realm r, int beatmapId) =>
            beatmapId > 0 && r.All<BeatmapInfo>().Filter("OnlineID == $0 AND BeatmapSet.DeletePending == false", beatmapId).Any();

        private static bool hasLocalChecksum(Realm r, string checksum) =>
            !string.IsNullOrEmpty(checksum) && r.All<BeatmapInfo>().Filter("MD5Hash == $0 AND BeatmapSet.DeletePending == false", checksum).Any();

        /// <summary>
        /// Finds every beatmap in every collection which isn't available locally, works out what it is from its checksum,
        /// and fixes what it can: puts the version which is here in place of an outdated one, and downloads any beatmap sets
        /// which are missing or don't have the beatmap. Beatmaps which are never removed from collections, even when they can't
        /// be found online at all (e.g. a version of a beatmap which has since been updated).
        /// </summary>
        public void RepairCollections()
        {
            var missing = realm.Run(r =>
            {
                r.Refresh();

                var localHashes = r.All<BeatmapInfo>().AsEnumerable().Where(b => b.BeatmapSet?.DeletePending == false).Select(b => b.MD5Hash).ToHashSet();

                return r.All<BeatmapCollection>().AsEnumerable()
                        .SelectMany(c => c.BeatmapMD5Hashes.Where(h => !string.IsNullOrEmpty(h) && !localHashes.Contains(h)).Select(h => (collection: c.Name, hash: h)))
                        .ToList();
            });

            if (missing.Count == 0)
            {
                notifications?.Post(new ProgressCompletionNotification { Text = "Every beatmap in your collections is already here." });
                return;
            }

            var distinct = missing.Select(m => m.hash).Distinct().ToList();

            var notification = new ProgressNotification
            {
                Text = $"Repairing collections: looking up {distinct.Count} missing beatmaps...",
                CompletionText = "Finished checking collections.",
                State = ProgressNotificationState.Active,
            };

            notifications?.Post(notification);

            Task.Run(async () =>
            {
                int done = 0;
                int failed = 0;

                foreach (string hash in distinct)
                {
                    if (notification.CancellationToken.IsCancellationRequested)
                        break;

                    if (!lookups.TryGet(hash, out _))
                    {
                        if (await lookUpChecksum(hash).ConfigureAwait(false))
                            await Task.Delay(checksum_lookup_interval).ConfigureAwait(false);
                        else
                            failed++;

                        // Saved as it goes, so that an interrupted repair picks up where it left off.
                        if (done % 50 == 0)
                            lookups.Save();
                    }

                    int doneNow = ++done;

                    runOnUpdateThread(() =>
                    {
                        if (notification.State != ProgressNotificationState.Active)
                            return;

                        notification.Progress = (float)doneNow / distinct.Count;
                        notification.Text = $"Repairing collections: looking up missing beatmaps ({doneNow} of {distinct.Count})";
                    });
                }

                lookups.Save();

                runOnUpdateThread(() =>
                {
                    var summary = applyRepairs(missing);

                    if (failed > 0)
                        summary += $" {failed} couldn't be looked up right now; repair again later to retry them.";

                    if (notification.CancellationToken.IsCancellationRequested)
                        summary = "Stopped part way. " + summary;

                    notification.CompletionText = summary;
                    notification.State = ProgressNotificationState.Completed;
                });
            });
        }

        /// <summary>
        /// Looks up which beatmap a checksum belongs to, and remembers the answer.
        /// </summary>
        /// <returns>Whether an answer was found (including that the checksum doesn't exist online).</returns>
        private async Task<bool> lookUpChecksum(string hash)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    using var response = await SendRequest(string.Format(checksum_lookup_url, hash)).ConfigureAwait(false);

                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        await Task.Delay(getRetryDelay(response, TimeSpan.FromSeconds(10 * (attempt + 1)))).ConfigureAwait(false);
                        continue;
                    }

                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        lookups.Set(hash, null);
                        return true;
                    }

                    if (!response.IsSuccessStatusCode)
                        return false;

                    var result = JsonConvert.DeserializeObject<BeatmapLookup>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

                    if (result == null || result.ID <= 0 || result.BeatmapSetID <= 0)
                        lookups.Set(hash, null);
                    else
                        lookups.Set(hash, new BeatmapChecksumLookups.Entry(result.ID, result.BeatmapSetID));

                    return true;
                }
                catch (Exception e)
                {
                    Logger.Log($"Lookup of checksum {hash} failed: {e.Message}");
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// Uses the looked up checksums to fix collections: the local version of a beatmap replaces a missing version of it,
        /// and beatmap sets which are missing (or don't have the beatmap) are downloaded.
        /// </summary>
        /// <returns>A summary of what was done.</returns>
        private string applyRepairs(List<(string collection, string hash)> missing)
        {
            var found = new List<(int setId, int beatmapId, string checksum)>();
            int unavailable = 0;

            foreach (var group in missing.GroupBy(m => m.hash))
            {
                if (!lookups.TryGet(group.Key, out var entry))
                    continue;

                if (entry == null)
                {
                    unavailable++;
                    continue;
                }

                found.Add((entry.BeatmapSetID, entry.BeatmapID, group.Key));

                foreach (var (collection, _) in group)
                    addWanted(entry.BeatmapSetID, new WantedBeatmap(collection, entry.BeatmapID, group.Key));
            }

            int replaced = found.Select(f => f.setId).Distinct().ToList().Sum(addDownloadedBeatmaps);
            var setsToDownload = getSetsNeedingDownload(found);

            string summary = $"Checked {missing.Select(m => m.hash).Distinct().Count()} beatmaps missing from your collections.";

            if (replaced > 0)
                summary += $" Matched {replaced} to the version you have.";

            summary += setsToDownload.Count > 0 ? $" Downloading {setsToDownload.Count} beatmap sets." : " Nothing needs downloading.";

            if (unavailable > 0)
                summary += $" {unavailable} are versions which no longer exist online, so can't be fixed.";

            queueDownloads(setsToDownload);
            return summary;
        }

        /// <summary>
        /// Imports any number of osu!collector collections (from URLs or IDs, separated by anything).
        /// Importing a collection which was imported before updates it.
        /// </summary>
        public void Import(string input)
        {
            var collectionIds = ParseCollectionIds(input);

            if (collectionIds.Count == 0)
            {
                notifications?.Post(new SimpleErrorNotification { Text = "Couldn't read an osu!collector collection ID from that input." });
                return;
            }

            Task.Run(async () =>
            {
                foreach (int collectionId in collectionIds)
                {
                    var collection = await fetchCollection(collectionId).ConfigureAwait(false);

                    if (collection == null)
                        runOnUpdateThread(() => notifications?.Post(new SimpleErrorNotification { Text = $"Failed to fetch osu!collector collection {collectionId}." }));
                    else
                        runOnUpdateThread(() => onCollectionFetched(collection));
                }
            });
        }

        /// <summary>
        /// Updates all collections which were imported from osu!collector: adds beatmaps which were added online since,
        /// makes sure the versions of beatmaps which are available locally are in the collections, and downloads any beatmap sets which are still missing.
        /// Beatmaps are never removed from collections.
        /// </summary>
        /// <remarks>
        /// Collections imported before osu!collector links were remembered are matched up with osu!collector by name,
        /// as long as they mostly contain the same beatmaps.
        /// </remarks>
        public void UpdateAll()
        {
            var localCollections = realm.Run(r => r.All<BeatmapCollection>().AsEnumerable().Select(c => (c.ID, c.Name, hashes: c.BeatmapMD5Hashes.ToHashSet())).ToList());

            collectorLinks.RemoveMissingCollections(localCollections.Select(c => c.ID));

            var notification = new ProgressNotification
            {
                Text = "Updating osu!collector collections...",
                CompletionText = "Finished updating osu!collector collections.",
                State = ProgressNotificationState.Active,
            };

            notifications?.Post(notification);

            Task.Run(async () =>
            {
                int updated = 0;
                int linked = 0;
                var failed = new List<string>();

                for (int i = 0; i < localCollections.Count; i++)
                {
                    if (notification.CancellationToken.IsCancellationRequested)
                        return;

                    var (id, name, hashes) = localCollections[i];
                    int index = i;

                    runOnUpdateThread(() =>
                    {
                        notification.Text = $"Updating osu!collector collections ({index + 1} of {localCollections.Count}): {name}";
                        notification.Progress = (float)index / localCollections.Count;
                    });

                    CollectorCollection? online;

                    if (collectorLinks.Get(id) is int collectorId)
                        online = await fetchCollection(collectorId).ConfigureAwait(false);
                    else
                    {
                        online = await findByName(name, hashes).ConfigureAwait(false);

                        // Not from osu!collector.
                        if (online == null)
                            continue;

                        linked++;
                    }

                    if (online == null)
                    {
                        failed.Add(name);
                        continue;
                    }

                    var fetched = online;
                    runOnUpdateThread(() => onCollectionFetched(fetched, name, quiet: true));
                    updated++;
                }

                runOnUpdateThread(() =>
                {
                    string summary = updated == 0
                        ? "No collections from osu!collector were found. Import one by URL first."
                        : $"Updated {updated} osu!collector collections" + (linked > 0 ? $" ({linked} matched up by name)." : ".");

                    if (failed.Count > 0)
                        summary += $" Couldn't reach osu!collector for: {string.Join(", ", failed)}.";

                    notification.CompletionText = summary;
                    notification.State = ProgressNotificationState.Completed;
                });
            });
        }

        /// <summary>
        /// Finds the osu!collector collection a local collection was imported from, by name.
        /// </summary>
        /// <param name="name">The name of the local collection.</param>
        /// <param name="localHashes">The beatmaps in the local collection, used to make sure it is the same collection.</param>
        private async Task<CollectorCollection?> findByName(string name, HashSet<string> localHashes)
        {
            CollectorSearchResults? results;

            try
            {
                using var response = await SendRequest($"https://osucollector.com/api/collections/search?search={Uri.EscapeDataString(name)}&perPage=10").ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                results = JsonConvert.DeserializeObject<CollectorSearchResults>(json);
            }
            catch (Exception e)
            {
                Logger.Log($"osu!collector search for \"{name}\" failed: {e.Message}");
                return null;
            }

            CollectorCollection? best = null;
            double bestOverlap = 0;

            // Several collections may share a name, so pick the one with the most beatmaps in common.
            foreach (var candidate in results?.Collections.Where(c => string.Equals(c.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)).Take(3) ?? Enumerable.Empty<CollectorCollectionSummary>())
            {
                var collection = await fetchCollection(candidate.ID).ConfigureAwait(false);

                if (collection == null)
                    continue;

                var listed = collection.BeatmapSets.SelectMany(s => s.Beatmaps).Select(b => b.Checksum).Where(c => !string.IsNullOrEmpty(c)).ToHashSet();

                if (listed.Count == 0)
                    continue;

                double overlap = (double)listed.Count(localHashes.Contains) / listed.Count;

                if (overlap > bestOverlap)
                {
                    best = collection;
                    bestOverlap = overlap;
                }
            }

            // A collection which only shares a name with one on osu!collector is left alone.
            return bestOverlap >= 0.5 ? best : null;
        }

        private async Task<CollectorCollection?> fetchCollection(int collectionId)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using var response = await SendRequest($"https://osucollector.com/api/collections/{collectionId}").ConfigureAwait(false);

                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        await Task.Delay(getRetryDelay(response, TimeSpan.FromSeconds(5 * (attempt + 1)))).ConfigureAwait(false);
                        continue;
                    }

                    response.EnsureSuccessStatusCode();

                    var collection = JsonConvert.DeserializeObject<CollectorCollection>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

                    if (collection?.BeatmapSets == null)
                        throw new InvalidOperationException("Collection response was empty.");

                    collection.ID = collectionId;
                    return collection;
                }
                catch (Exception e)
                {
                    Logger.Log($"Failed to fetch osu!collector collection {collectionId} (attempt {attempt + 1}): {e.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                }
            }

            return null;
        }

        /// <param name="collection">The collection from osu!collector.</param>
        /// <param name="localName">The name of the local collection to update, or null to use (or create) the collection with the same name.</param>
        /// <param name="quiet">Whether to skip posting a notification when nothing changed.</param>
        private void onCollectionFetched(CollectorCollection collection, string? localName = null, bool quiet = false)
        {
            string name = localName ?? (string.IsNullOrWhiteSpace(collection.Name) ? $"osu!collector {collection.ID}" : collection.Name.Trim());

            var beatmaps = collection.BeatmapSets.SelectMany(s => s.Beatmaps.Select(b => (set: s.ID, beatmap: b))).ToList();

            var hashes = beatmaps.Select(b => b.beatmap.Checksum)
                                 .Where(c => !string.IsNullOrEmpty(c))
                                 .Distinct()
                                 .ToList();

            int added = addToCollection(name, hashes, out var localId);

            collectorLinks.Set(localId, collection.ID);
            Logger.Log($"Imported osu!collector collection {collection.ID} into \"{name}\" ({added} new of {hashes.Count} beatmaps)");

            foreach (var (set, beatmap) in beatmaps)
            {
                if (beatmap.ID > 0)
                    addWanted(set, new WantedBeatmap(name, beatmap.ID, beatmap.Checksum));
            }

            var setIds = collection.BeatmapSets.Select(s => s.ID).ToList();

            // Sets which are already here may have a different version of a beatmap than the one listed.
            int replaced = setIds.Distinct().ToList().Sum(addDownloadedBeatmaps);

            var missingSetIds = getSetsNeedingDownload(beatmaps.Where(b => b.beatmap.ID > 0).Select(b => (b.set, b.beatmap.ID, b.beatmap.Checksum)));

            if (!quiet || added > 0 || replaced > 0 || missingSetIds.Count > 0)
            {
                string summary = $"Imported \"{name}\" ({added} new of {hashes.Count} beatmaps).";

                if (replaced > 0)
                    summary += $" Matched {replaced} beatmaps to the version you have.";

                summary += $" {missingSetIds.Count} beatmap sets need downloading.";

                notifications?.Post(new ProgressCompletionNotification { Text = summary });
            }

            queueDownloads(missingSetIds);
        }

        private void queueDownloads(List<int> missingSetIds)
        {
            lock (unavailableSets)
                missingSetIds = missingSetIds.Where(id => !queuedSets.Contains(id) && !unavailableSets.Contains(id)).Distinct().ToList();

            if (missingSetIds.Count == 0)
                return;

            foreach (int id in missingSetIds)
            {
                queuedSets.Add(id);
                pendingDownloads.Enqueue(id);
            }

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

                        queuedSets.Remove(setId);
                        activeDownloads--;
                        onDownloadFinished(success);
                        startNextDownloads();
                    }));

                    continue;
                }

                queuedSets.Remove(setId);

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
        /// <remarks>
        /// Mirrors which ask for requests to slow down are left alone for a while and then tried again, rather than giving up on the set.
        /// A set is only given up on if every mirror says it doesn't have it, or after retrying for a while.
        /// </remarks>
        protected virtual async Task<bool> DownloadFromMirror(int setId, bool noVideo)
        {
            // Mirrors which won't be tried again for this set: they don't have it, only have a broken copy, or keep failing.
            var finishedMirrors = new HashSet<Mirror>();
            var errors = new Dictionary<Mirror, int>();
            int attempts = 0;

            while (attempts < max_download_attempts)
            {
                bool attempted = false;

                foreach (var mirror in mirror_urls)
                {
                    if (finishedMirrors.Contains(mirror) || getCooldown(mirror) > TimeSpan.Zero)
                        continue;

                    attempted = true;
                    attempts++;

                    switch (await tryDownloadFromMirror(mirror, setId, noVideo).ConfigureAwait(false))
                    {
                        case MirrorResult.Success:
                            return true;

                        case MirrorResult.NotFound:
                        case MirrorResult.Unusable:
                            finishedMirrors.Add(mirror);
                            break;

                        case MirrorResult.Error:
                            errors[mirror] = errors.GetValueOrDefault(mirror) + 1;

                            if (errors[mirror] >= max_mirror_errors)
                                finishedMirrors.Add(mirror);
                            break;
                    }
                }

                if (finishedMirrors.Count == mirror_urls.Length)
                {
                    Logger.Log($"Beatmap set {setId} isn't available on any mirror.");

                    lock (unavailableSets)
                        unavailableSets.Add(setId);

                    return false;
                }

                // Wait until a mirror is available again, or a little while if they failed for another reason.
                // Waiting for a mirror to become available doesn't count as an attempt.
                var wait = mirror_urls.Where(m => !finishedMirrors.Contains(m)).Select(getCooldown).Min();

                if (wait > TimeSpan.Zero)
                {
                    if (wait > TimeSpan.FromSeconds(30))
                        showWaitingForMirrors(DateTimeOffset.UtcNow + wait);

                    await Task.Delay(wait + TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
                }
                else if (attempted)
                    await Task.Delay(TimeSpan.FromSeconds(2 * Math.Max(1, errors.Values.DefaultIfEmpty().Max()))).ConfigureAwait(false);
            }

            Logger.Log($"Gave up downloading beatmap set {setId} after {max_download_attempts} attempts.");
            return false;
        }

        private void showWaitingForMirrors(DateTimeOffset until) => runOnUpdateThread(() =>
        {
            waitingForMirrorsUntil = until;
            updateDownloadText();
        });

        private async Task<MirrorResult> tryDownloadFromMirror(Mirror mirror, int setId, bool noVideo)
        {
            string url = string.Format(noVideo ? mirror.NoVideoUrl : mirror.Url, setId);
            string path = Path.Combine(Path.GetTempPath(), $"osucollector-{setId}-{Guid.NewGuid():N}.osz");

            try
            {
                using (var response = await SendRequest(url).ConfigureAwait(false))
                {
                    switch (response.StatusCode)
                    {
                        case System.Net.HttpStatusCode.NotFound:
                            Logger.Log($"Beatmap set {setId} isn't on {url}");
                            return MirrorResult.NotFound;

                        case System.Net.HttpStatusCode.TooManyRequests:
                            var delay = startCooldown(mirror, response);
                            Logger.Log($"Mirror {url} asked to slow down, waiting {delay.TotalSeconds:N0}s before using it again.");
                            return MirrorResult.RateLimited;
                    }

                    // Some mirrors respond with a web page (sometimes even with a success status) when they don't have a set.
                    string? mediaType = response.Content.Headers.ContentType?.MediaType;

                    if (mediaType != null && (mediaType.Contains("html", StringComparison.OrdinalIgnoreCase) || mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)))
                    {
                        Logger.Log($"Beatmap set {setId} isn't on {url} ({(int)response.StatusCode}, {mediaType})");
                        return MirrorResult.NotFound;
                    }

                    response.EnsureSuccessStatusCode();

                    await using (var file = File.Create(path))
                        await response.Content.CopyToAsync(file).ConfigureAwait(false);
                }

                lock (mirrorCooldowns)
                    mirrorStrikes.Remove(mirror);

                // A newer copy of a set which is already here replaces it as an update, which keeps scores and collections.
                var existing = realm.Run(r => r.All<BeatmapSetInfo>().Where(s => s.OnlineID == setId && !s.DeletePending).AsEnumerable().FirstOrDefault()?.Detach());

                var imported = existing != null
                    ? await beatmapManager.ImportAsUpdate(new ProgressNotification(), new ImportTask(path), existing).ConfigureAwait(false)
                    : await beatmapManager.Import(new ImportTask(path)).ConfigureAwait(false);

                if (imported != null)
                {
                    Logger.Log($"Downloaded and imported beatmap set {setId} from {url}");
                    return MirrorResult.Success;
                }

                // Downloading the same broken file again won't help.
                Logger.Log($"Beatmap set {setId} downloaded from {url} couldn't be imported.");
                return MirrorResult.Unusable;
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

            return MirrorResult.Error;
        }

        private TimeSpan getCooldown(Mirror mirror)
        {
            lock (mirrorCooldowns)
            {
                if (!mirrorCooldowns.TryGetValue(mirror, out var until))
                    return TimeSpan.Zero;

                var remaining = until - DateTimeOffset.UtcNow;
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }

        /// <summary>
        /// Leaves a mirror alone for a while after it asked for requests to slow down.
        /// Uses the time the mirror asks for, or otherwise waits longer each time it asks in a row.
        /// </summary>
        private TimeSpan startCooldown(Mirror mirror, HttpResponseMessage response)
        {
            lock (mirrorCooldowns)
            {
                // Several downloads running at once all get told to slow down at about the same time, which only counts once.
                int strikes = mirrorStrikes.GetValueOrDefault(mirror);

                if (strikes == 0 || !mirrorCooldowns.TryGetValue(mirror, out var current) || current < DateTimeOffset.UtcNow)
                    strikes++;

                mirrorStrikes[mirror] = strikes;

                var delay = getRetryDelay(response, TimeSpan.FromSeconds(10 * Math.Pow(2, Math.Min(strikes - 1, 4))));

                if (delay > max_mirror_cooldown)
                    delay = max_mirror_cooldown;

                var until = DateTimeOffset.UtcNow + delay;

                if (!mirrorCooldowns.TryGetValue(mirror, out var existing) || existing < until)
                    mirrorCooldowns[mirror] = until;

                return delay;
            }
        }

        /// <summary>
        /// Reads how long a server asked to wait before trying again.
        /// </summary>
        private static TimeSpan getRetryDelay(HttpResponseMessage response, TimeSpan fallback)
        {
            var retryAfter = response.Headers.RetryAfter;

            if (retryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero)
                return delta;

            if (retryAfter?.Date is DateTimeOffset date && date > DateTimeOffset.UtcNow)
                return date - DateTimeOffset.UtcNow;

            return fallback;
        }

        private enum MirrorResult
        {
            Success,

            /// <summary>
            /// The mirror doesn't have the set.
            /// </summary>
            NotFound,

            /// <summary>
            /// The mirror's copy of the set couldn't be imported.
            /// </summary>
            Unusable,

            /// <summary>
            /// The mirror asked for requests to slow down.
            /// </summary>
            RateLimited,

            /// <summary>
            /// A server or connection error, which may work if tried again.
            /// </summary>
            Error,
        }

        /// <summary>
        /// Runs an action on the update thread.
        /// </summary>
        /// <remarks>
        /// This component lives inside the settings overlay, which stops updating (and running scheduled tasks) while hidden,
        /// so the host's scheduler is used rather than <see cref="Drawable.Schedule"/> to keep downloads progressing.
        /// </remarks>
        private void runOnUpdateThread(Action action) => host.UpdateThread.Scheduler.Add(action);

        private void updateDownloadText()
        {
            if (downloadNotification == null)
                return;

            downloadNotification.Progress = (float)finishedDownloads / totalDownloads;
            downloadNotification.Text = $"Downloading collection beatmaps ({finishedDownloads} of {totalDownloads}"
                                        + (failedDownloads > 0 ? $", {failedDownloads} failed)" : ")");

            if (waitingForMirrorsUntil is DateTimeOffset until && until > DateTimeOffset.UtcNow)
                downloadNotification.Text += $". Mirror download limits reached, continuing at {until.ToLocalTime():HH:mm}.";
        }

        private void onDownloadFinished(bool success)
        {
            finishedDownloads++;
            if (!success)
                failedDownloads++;

            if (downloadNotification == null)
                return;

            if (success)
                waitingForMirrorsUntil = null;

            updateDownloadText();

            if (finishedDownloads >= totalDownloads && pendingDownloads.Count == 0 && activeDownloads == 0)
            {
                if (failedDownloads > 0)
                {
                    downloadNotification.CompletionText = $"Finished downloading collection beatmaps ({failedDownloads} failed or unavailable). "
                                                          + "Use \"Update osu!collector collections\" to try again later.";
                }

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
            [JsonProperty("id")]
            public int ID { get; set; }

            [JsonProperty("checksum")]
            public string Checksum { get; set; } = string.Empty;
        }

        private class CollectorSearchResults
        {
            [JsonProperty("collections")]
            public List<CollectorCollectionSummary> Collections { get; set; } = new List<CollectorCollectionSummary>();
        }

        private class CollectorCollectionSummary
        {
            [JsonProperty("id")]
            public int ID { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; } = string.Empty;
        }

        /// <param name="Name">The name of the collection.</param>
        /// <param name="UniqueIds">How many different beatmap IDs were given.</param>
        /// <param name="Duplicates">How many IDs were given more than once (and only used once).</param>
        /// <param name="CollectionExisted">Whether the collection already existed, and is being added to.</param>
        private record BeatmapIdsRequest(string Name, int UniqueIds, int Duplicates, bool CollectionExisted);

        /// <summary>
        /// A beatmap wanted for a collection.
        /// </summary>
        /// <param name="Collection">The name of the collection.</param>
        /// <param name="BeatmapID">The online ID of the beatmap.</param>
        /// <param name="ListedChecksum">The checksum of the version of the beatmap which was listed (and so already added to the collection).</param>
        private record WantedBeatmap(string Collection, int BeatmapID, string ListedChecksum);
    }
}
