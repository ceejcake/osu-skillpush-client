// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.Collections;
using osu.Game.IO;
using osu.Game.IO.Archives;
using osu.Game.Tests.Resources;

namespace osu.Game.Tests.Visual.Navigation
{
    /// <summary>
    /// Collections can be imported from osu!collector (with missing beatmaps downloaded from mirrors), and updated later.
    /// </summary>
    public partial class TestSceneOsuCollectorImport : OsuGameTestScene
    {
        private const int collector_id = 555;
        private const string collection_name = "RomAI test";

        private TestImporter importer = null!;

        private BeatmapInfo localA = null!;
        private BeatmapInfo localB = null!;

        /// <summary>
        /// A beatmap set which isn't available locally, and must be downloaded.
        /// </summary>
        private string remoteArchive = null!;

        private int remoteSetId;
        private int remoteBeatmapId;

        public override void SetUpSteps()
        {
            base.SetUpSteps();

            AddStep("import a beatmap set", () =>
            {
                var set = TestResources.CreateTestBeatmapSetInfo(2);
                Game.BeatmapManager.Import(set);
                localA = set.Beatmaps[0];
                localB = set.Beatmaps[1];
            });

            AddStep("prepare remote beatmap set", () =>
            {
                remoteArchive = TestResources.GetQuickTestBeatmapForImport();
                (remoteSetId, remoteBeatmapId) = readOnlineIds(remoteArchive);
            });

            AddStep("add importer", () => Game.Add(importer = new TestImporter()));
            AddUntilStep("importer loaded", () => importer.IsLoaded);
        }

        [Test]
        public void TestImport()
        {
            AddStep("set up osu!collector", () =>
            {
                importer.Respond($"collections/{collector_id}", json(collection(collector_id, collection_name,
                    (localA.BeatmapSet!.OnlineID, new[] { (localA.OnlineID, localA.MD5Hash), (localB.OnlineID, "old version of b") }),
                    (remoteSetId, new[] { (remoteBeatmapId, "listed version of remote") }))));

                // The first mirror asks to slow down, the second doesn't have the set, then the first works after waiting.
                importer.Respond($"osu.direct/api/d/{remoteSetId}", () => tooManyRequests(TimeSpan.FromSeconds(1)));
                importer.Respond($"nerinyan.moe/d/{remoteSetId}", () => new HttpResponseMessage(HttpStatusCode.NotFound));
                importer.Respond($"osu.direct/api/d/{remoteSetId}", () => file(remoteArchive));
            });

            AddStep("import", () => importer.Import($"https://osucollector.com/collections/{collector_id}/some-name"));

            AddUntilStep("collection created", () => hashes(collection_name), () => Is.Not.Null);
            AddUntilStep("local version replaces listed version", () => hashes(collection_name), () => Does.Contain(localB.MD5Hash).And.Not.Contain("old version of b"));
            AddAssert("has up to date local beatmap", () => hashes(collection_name), () => Does.Contain(localA.MD5Hash));

            AddUntilStep("remote set downloaded after waiting", () => importer.Requests.Count(r => r.Contains($"osu.direct/api/d/{remoteSetId}")), () => Is.EqualTo(2));
            AddUntilStep("remote set imported", () => remoteLocalHash(), () => Is.Not.Null);
            AddUntilStep("downloaded version replaces listed version", () => hashes(collection_name), () => Does.Contain(remoteLocalHash()).And.Not.Contain("listed version of remote"));
            AddAssert("collection size matches beatmaps", () => hashes(collection_name), () => Has.Length.EqualTo(3));

            AddAssert("catboy not used for downloads", () => importer.Requests.Any(r => r.Contains("catboy.best/d/")), () => Is.False);
            AddAssert("link remembered", () => new OsuCollectorLinks(Game.Dependencies.Get<Storage>()).Get(collectionId(collection_name)), () => Is.EqualTo(collector_id));
        }

        [Test]
        public void TestNotOnAnyMirror()
        {
            AddStep("set up osu!collector", () =>
            {
                importer.Respond($"collections/{collector_id}", json(collection(collector_id, collection_name,
                    (remoteSetId, new[] { (remoteBeatmapId, "listed version of remote") }))));

                importer.Respond($"osu.direct/api/d/{remoteSetId}", () => new HttpResponseMessage(HttpStatusCode.NotFound));
                importer.Respond($"nerinyan.moe/d/{remoteSetId}", () => new HttpResponseMessage(HttpStatusCode.NotFound));
            });

            AddStep("import", () => importer.Import(collector_id.ToString()));

            AddUntilStep("both mirrors tried", () => importer.Requests.Count(r => r.Contains($"/d/{remoteSetId}")), () => Is.EqualTo(2));
            AddWaitStep("give it time", 5);
            AddAssert("not retried", () => importer.Requests.Count(r => r.Contains($"/d/{remoteSetId}")), () => Is.EqualTo(2));
            AddAssert("listed version kept, so it shows as missing", () => hashes(collection_name), () => Is.EqualTo(new[] { "listed version of remote" }));
        }

        [Test]
        public void TestBrokenDownloadNotRetried()
        {
            AddStep("set up osu!collector", () =>
            {
                importer.Respond($"collections/{collector_id}", json(collection(collector_id, collection_name,
                    (remoteSetId, new[] { (remoteBeatmapId, "listed version of remote") }))));

                importer.Respond($"osu.direct/api/d/{remoteSetId}", () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not a beatmap archive") });
                importer.Respond($"nerinyan.moe/d/{remoteSetId}", () => new HttpResponseMessage(HttpStatusCode.NotFound));
            });

            AddStep("import", () => importer.Import(collector_id.ToString()));

            AddUntilStep("both mirrors tried", () => importer.Requests.Count(r => r.Contains($"/d/{remoteSetId}")), () => Is.EqualTo(2));
            AddWaitStep("give it time", 10);
            AddAssert("broken copy downloaded only once", () => importer.Requests.Count(r => r.Contains($"osu.direct/api/d/{remoteSetId}")), () => Is.EqualTo(1));

            AddStep("import again", () => importer.Import(collector_id.ToString()));
            AddUntilStep("collection fetched again", () => importer.Requests.Count(r => r.Contains($"collections/{collector_id}")), () => Is.EqualTo(2));
            AddWaitStep("give it time", 10);
            AddAssert("unavailable set not tried again", () => importer.Requests.Count(r => r.Contains($"/d/{remoteSetId}")), () => Is.EqualTo(2));
        }

        [Test]
        public void TestServerErrorsGiveUpOnMirror()
        {
            AddStep("set up osu!collector", () =>
            {
                importer.Respond($"collections/{collector_id}", json(collection(collector_id, collection_name,
                    (remoteSetId, new[] { (remoteBeatmapId, "listed version of remote") }))));

                importer.Respond($"osu.direct/api/d/{remoteSetId}", () => new HttpResponseMessage(HttpStatusCode.BadGateway));
                importer.Respond($"nerinyan.moe/d/{remoteSetId}", () => new HttpResponseMessage(HttpStatusCode.NotFound));
            });

            AddStep("import", () => importer.Import(collector_id.ToString()));

            AddUntilStep("failing mirror tried three times", () => importer.Requests.Count(r => r.Contains($"osu.direct/api/d/{remoteSetId}")), () => Is.EqualTo(3));
            AddWaitStep("give it time", 10);
            AddAssert("not tried any more", () => importer.Requests.Count(r => r.Contains($"osu.direct/api/d/{remoteSetId}")), () => Is.EqualTo(3));
        }

        [Test]
        public void TestSetInSeveralCollectionsDownloadedOnce()
        {
            AddStep("set up osu!collector", () =>
            {
                importer.Respond($"collections/{collector_id}", json(collection(collector_id, "first",
                    (remoteSetId, new[] { (remoteBeatmapId, "listed version of remote") }))));
                importer.Respond($"collections/{collector_id + 1}", json(collection(collector_id + 1, "second",
                    (remoteSetId, new[] { (remoteBeatmapId, "listed version of remote") }))));

                importer.Respond($"osu.direct/api/d/{remoteSetId}", () => tooManyRequests(TimeSpan.FromSeconds(1)));
                importer.Respond($"osu.direct/api/d/{remoteSetId}", () => file(remoteArchive));
                importer.Respond($"nerinyan.moe/d/{remoteSetId}", () => new HttpResponseMessage(HttpStatusCode.NotFound));
            });

            AddStep("import both", () => importer.Import($"{collector_id} {collector_id + 1}"));

            AddUntilStep("remote set imported", () => remoteLocalHash(), () => Is.Not.Null);
            AddUntilStep("both collections have the downloaded version", () => hashes("first")?.Contains(remoteLocalHash()) == true && hashes("second")?.Contains(remoteLocalHash()) == true);
            AddAssert("set downloaded once", () => importer.Requests.Count(r => r.Contains($"osu.direct/api/d/{remoteSetId}")), () => Is.EqualTo(2));
        }

        [Test]
        public void TestOtherMirrorsUsedWhileRateLimited()
        {
            AddStep("set up osu!collector", () =>
            {
                importer.Respond($"collections/{collector_id}", json(collection(collector_id, collection_name,
                    (remoteSetId, new[] { (remoteBeatmapId, "listed version of remote") }))));

                // osu.direct's hourly download limit is used up.
                importer.Respond($"osu.direct/api/d/{remoteSetId}", () => tooManyRequests(TimeSpan.FromHours(1)));

                // sayobot doesn't have the set, but says so with a web page.
                importer.Respond($"sayobot.cn/beatmaps/download/full/{remoteSetId}", () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("<!DOCTYPE html>not here", System.Text.Encoding.UTF8, "text/html")
                });

                importer.Respond($"beatconnect.io/b/{remoteSetId}", () => file(remoteArchive));
            });

            AddStep("import", () => importer.Import(collector_id.ToString()));

            AddUntilStep("downloaded from another mirror", () => remoteLocalHash(), () => Is.Not.Null);
            AddAssert("osu.direct asked once", () => importer.Requests.Count(r => r.Contains($"osu.direct/api/d/{remoteSetId}")), () => Is.EqualTo(1));
            AddAssert("sayobot asked once", () => importer.Requests.Count(r => r.Contains($"sayobot.cn/beatmaps/download/full/{remoteSetId}")), () => Is.EqualTo(1));
        }

        [Test]
        public void TestSetWhichIsHereButMissingTheBeatmapIsDownloadedAgain()
        {
            Guid incompleteSetId = Guid.Empty;

            // A copy of the remote set which doesn't have the wanted difficulty, as happens when the copy here is an older
            // or newer version of the set than the one the collection was made from.
            AddStep("import incomplete copy of remote set", () =>
            {
                var set = TestResources.CreateTestBeatmapSetInfo(1);
                set.OnlineID = remoteSetId;
                set.Beatmaps[0].OnlineID = remoteBeatmapId + 1000;
                Game.BeatmapManager.Import(set);
                incompleteSetId = set.ID;
            });

            AddStep("set up osu!collector", () =>
            {
                importer.Respond($"collections/{collector_id}", json(collection(collector_id, collection_name,
                    (remoteSetId, new[] { (remoteBeatmapId, "listed version of remote") }))));

                importer.Respond($"osu.direct/api/d/{remoteSetId}", () => file(remoteArchive));
            });

            AddStep("import", () => importer.Import(collector_id.ToString()));

            AddUntilStep("set downloaded even though it was here", () => importer.Requests.Count(r => r.Contains($"osu.direct/api/d/{remoteSetId}")), () => Is.EqualTo(1));
            AddUntilStep("remote beatmap now here", () => remoteLocalHash(), () => Is.Not.Null);
            AddUntilStep("collection has it", () => hashes(collection_name), () => Is.EqualTo(new[] { remoteLocalHash() }));
            AddAssert("incomplete copy replaced", () => Game.Realm.Run(r => r.Find<BeatmapSetInfo>(incompleteSetId)?.DeletePending ?? true), () => Is.True);

            AddStep("import again", () => importer.Import(collector_id.ToString()));
            AddUntilStep("collection fetched again", () => importer.Requests.Count(r => r.Contains($"collections/{collector_id}")), () => Is.EqualTo(2));
            AddWaitStep("give it time", 5);
            AddAssert("not downloaded again", () => importer.Requests.Count(r => r.Contains($"osu.direct/api/d/{remoteSetId}")), () => Is.EqualTo(1));
        }

        [Test]
        public void TestRepairCollections()
        {
            AddStep("create collection made elsewhere", () => Game.Realm.Write(r => r.Add(new BeatmapCollection("made elsewhere", new List<string>
            {
                localA.MD5Hash,
                "outdated version of b",
                "version which no longer exists",
                "listed version of remote",
            }))));

            AddStep("set up lookups", () =>
            {
                importer.Respond("api/v2/md5/outdated version of b", json(new { id = localB.OnlineID, beatmapset_id = localB.BeatmapSet!.OnlineID, checksum = "outdated version of b" }));
                importer.Respond("api/v2/md5/listed version of remote", json(new { id = remoteBeatmapId, beatmapset_id = remoteSetId, checksum = "listed version of remote" }));
                importer.Respond($"osu.direct/api/d/{remoteSetId}", () => file(remoteArchive));
            });

            AddStep("repair", () => importer.RepairCollections());

            AddUntilStep("missing set downloaded", () => remoteLocalHash(), () => Is.Not.Null);
            AddUntilStep("collection repaired", () => hashes("made elsewhere"),
                () => Is.EquivalentTo(new[] { localA.MD5Hash, localB.MD5Hash, "version which no longer exists", remoteLocalHash() }));
            AddAssert("present beatmap not looked up", () => importer.Requests.Any(r => r.Contains(localA.MD5Hash)), () => Is.False);

            int lookupsBefore = 0;
            AddStep("store lookup count", () => lookupsBefore = importer.Requests.Count(r => r.Contains("api/v2/md5/")));
            AddStep("repair again", () => importer.RepairCollections());
            AddWaitStep("give it time", 5);
            AddAssert("known missing version not looked up again", () => importer.Requests.Count(r => r.Contains("api/v2/md5/")), () => Is.EqualTo(lookupsBefore));
        }

        [Test]
        public void TestUpdateMatchesExistingCollectionByName()
        {
            AddStep("create collections imported before links were remembered", () => Game.Realm.Write(r =>
            {
                r.Add(new BeatmapCollection(collection_name, new List<string> { localA.MD5Hash }));
                r.Add(new BeatmapCollection("same name, different beatmaps", new List<string> { localA.MD5Hash }));
                r.Add(new BeatmapCollection("not from osu!collector", new List<string> { localA.MD5Hash }));
            }));

            AddStep("set up osu!collector", () =>
            {
                importer.Respond("search?search=RomAI", json(new
                {
                    collections = new[]
                    {
                        new { id = collector_id + 1, name = collection_name },
                        new { id = collector_id, name = collection_name },
                        new { id = collector_id + 2, name = "RomAI test 2" },
                    }
                }));

                // A collection with the same name by someone else, with none of the same beatmaps.
                importer.Respond($"collections/{collector_id + 1}", json(collection(collector_id + 1, collection_name,
                    (remoteSetId, new[] { (remoteBeatmapId, "something else") }))));

                // The collection was imported from, with a beatmap added since.
                importer.Respond($"collections/{collector_id}", json(collection(collector_id, collection_name,
                    (localA.BeatmapSet!.OnlineID, new[] { (localA.OnlineID, localA.MD5Hash), (localB.OnlineID, localB.MD5Hash) }))));

                importer.Respond("search?search=same", json(new { collections = new[] { new { id = collector_id + 3, name = "same name, different beatmaps" } } }));
                importer.Respond($"collections/{collector_id + 3}", json(collection(collector_id + 3, "same name, different beatmaps",
                    (remoteSetId, new[] { (remoteBeatmapId, "something else") }))));

                importer.Respond("search?search=not", json(new { collections = Array.Empty<object>() }));
            });

            AddStep("update", () => importer.UpdateAll());

            AddUntilStep("new beatmap added", () => hashes(collection_name), () => Is.EquivalentTo(new[] { localA.MD5Hash, localB.MD5Hash }));
            AddAssert("linked to the matching collection", () => new OsuCollectorLinks(Game.Dependencies.Get<Storage>()).Get(collectionId(collection_name)), () => Is.EqualTo(collector_id));

            AddUntilStep("all collections checked", () => importer.Requests.Any(r => r.Contains("search?search=not")));
            AddWaitStep("give it time", 5);
            AddAssert("unrelated collection with the same name left alone", () => hashes("same name, different beatmaps"), () => Is.EqualTo(new[] { localA.MD5Hash }));
            AddAssert("unrelated collection not linked", () => new OsuCollectorLinks(Game.Dependencies.Get<Storage>()).Get(collectionId("same name, different beatmaps")), () => Is.Null);
        }

        /// <summary>
        /// The checksum of the downloaded copy of the remote beatmap, or null if it hasn't been imported yet.
        /// </summary>
        private string? remoteLocalHash() => Game.Realm.Run(r => r.All<BeatmapInfo>().FirstOrDefault(b => b.OnlineID == remoteBeatmapId)?.MD5Hash);

        private string[]? hashes(string name) => Game.Realm.Run(r => r.All<BeatmapCollection>().FirstOrDefault(c => c.Name == name)?.BeatmapMD5Hashes.ToArray());

        private Guid collectionId(string name) => Game.Realm.Run(r => r.All<BeatmapCollection>().First(c => c.Name == name).ID);

        private static object collection(int id, string name, params (int setId, (int id, string checksum)[] beatmaps)[] sets) => new
        {
            id,
            name,
            beatmapsets = sets.Select(s => new
            {
                id = s.setId,
                beatmaps = s.beatmaps.Select(b => new { id = b.id, checksum = b.checksum }).ToArray(),
            }).ToArray(),
        };

        private static (int setId, int beatmapId) readOnlineIds(string archive)
        {
            using var reader = new ZipArchiveReader(File.OpenRead(archive));

            string osuFile = reader.Filenames.First(f => f.EndsWith(".osu", StringComparison.OrdinalIgnoreCase));

            using var stream = new LineBufferedReader(reader.GetStream(osuFile));
            var beatmap = Decoder.GetDecoder<Beatmap>(stream).Decode(stream);

            return (beatmap.BeatmapInfo.BeatmapSet!.OnlineID, beatmap.BeatmapInfo.OnlineID);
        }

        /// <summary>
        /// A JSON response. The content is serialised immediately, as it may contain realm objects which can't be read from the thread requests are made on.
        /// </summary>
        private static Func<HttpResponseMessage> json(object content)
        {
            string serialised = JsonConvert.SerializeObject(content);
            return () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(serialised) };
        }

        private static HttpResponseMessage file(string path) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(File.ReadAllBytes(path)),
        };

        private static HttpResponseMessage tooManyRequests(TimeSpan retryAfter)
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter);
            return response;
        }

        private partial class TestImporter : OsuCollectorImporter
        {
            public readonly ConcurrentQueue<string> Requests = new ConcurrentQueue<string>();

            private readonly List<(string url, Func<HttpResponseMessage> response)> responses = new List<(string, Func<HttpResponseMessage>)>();

            protected override bool UseMirrors => true;

            /// <summary>
            /// Responds to the next request containing the given text. Responses are used in the order they were added.
            /// </summary>
            public void Respond(string urlContains, Func<HttpResponseMessage> response)
            {
                lock (responses)
                    responses.Add((urlContains, response));
            }

            protected override Task<HttpResponseMessage> SendRequest(string url)
            {
                url = Uri.UnescapeDataString(url);
                Requests.Enqueue(url);

                lock (responses)
                {
                    int index = responses.FindIndex(r => Uri.UnescapeDataString(url).Contains(r.url, StringComparison.Ordinal));

                    // Searches and missing collections which a test doesn't care about.
                    if (index < 0)
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

                    var response = responses[index].response;

                    // The last response for a URL is reused, so that repeated requests (e.g. fetching a collection again) still work.
                    if (responses.Count(r => r.url == responses[index].url) > 1)
                        responses.RemoveAt(index);

                    return Task.FromResult(response());
                }
            }
        }
    }
}
