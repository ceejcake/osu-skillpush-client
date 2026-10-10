// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Extensions;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.StreamPractice;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets;
using osu.Game.Tests.Resources;

namespace osu.Game.Tests.Visual.SongSelect
{
    public partial class TestSceneStreamPracticeGenerator : OsuTestScene
    {
        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        private RulesetStore rulesetStore = null!;
        private BeatmapManager beatmapManager = null!;

        private StreamPracticeGenerator generator = null!;

        private readonly APIUser creator = new APIUser { Username = "practice enjoyer" };

        private static readonly StreamPracticeOptions continuous = new StreamPracticeOptions(StreamPracticePattern.Continuous);

        private BeatmapInfo renatus = null!;
        private BeatmapInfo otherSong = null!;

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));

            dependencies.Cache(rulesetStore = new RealmRulesetStore(Realm));
            dependencies.Cache(beatmapManager = new BeatmapManager(LocalStorage, Realm, null, dependencies.Get<AudioManager>(), Resources, dependencies.Get<GameHost>(), Beatmap.Default));
            dependencies.Cache(Realm);

            return dependencies;
        }

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("forget generated set", () => config.SetValue(OsuSetting.StreamPracticeBeatmapSetID, string.Empty));
            AddStep("delete generated sets", () => beatmapManager.Delete(generatedSets(), silent: true));

            AddStep("import beatmaps", () =>
            {
                renatus = importOsuBeatmap(TestResources.GetTestBeatmapForImport(), "Renatus");
                otherSong = importOsuBeatmap(createSecondSong(), "Second Song");
            });

            AddStep("create generator", () => generator = new StreamPracticeGenerator(beatmapManager, Realm, rulesetStore, config));
        }

        [Test]
        public void TestGeneratedBeatmapIsPlayable()
        {
            BeatmapInfo generated = null!;

            AddStep("generate", () => generated = generate(renatus, creator, continuous));

            AddAssert("in its own set", () => generated.BeatmapSet!.ID, () => Is.Not.EqualTo(renatus.BeatmapSet!.ID));
            AddAssert("star rating worked out", () => fetch(generated).StarRating, () => Is.GreaterThan(0));
            AddAssert("object count stored", () => fetch(generated).TotalObjectCount, () => Is.GreaterThan(0));
            AddAssert("only one difficulty", () => setOf(generated).Beatmaps, () => Has.Count.EqualTo(1));

            AddAssert("named after the song", () => generated.DifficultyName, () => Does.Match(@"^Renatus - \[\d+ BPM\]$"));
            AddAssert("credited to the player", () => generated.Metadata.Author.Username, () => Is.EqualTo("practice enjoyer"));

            AddAssert("stream follows the song", () =>
            {
                var source = beatmapManager.GetWorkingBeatmap(renatus).Beatmap;
                var content = beatmapManager.GetWorkingBeatmap(fetch(generated)).Beatmap;

                return content.HitObjects.Count == StreamPracticeMapWriter.CreateNotes(source, continuous).Count;
            });

            AddAssert("audio brought along", () =>
            {
                var beatmap = fetch(generated);
                return filesOf(generated).Contains(beatmap.Metadata.AudioFile);
            });

            AddAssert("track loads", () => beatmapManager.GetWorkingBeatmap(fetch(generated)).LoadTrack().IsDummyDevice, () => Is.False);
        }

        [Test]
        public void TestEveryGeneratedBeatmapSharesOneSet()
        {
            BeatmapInfo first = null!;
            BeatmapInfo second = null!;

            AddStep("generate from first song", () => first = generate(renatus, creator, continuous));
            AddStep("generate from second song", () => second = generate(otherSong, creator, continuous));

            AddAssert("same set", () => second.BeatmapSet!.ID, () => Is.EqualTo(first.BeatmapSet!.ID));
            AddAssert("two difficulties", () => setOf(first).Beatmaps.Count, () => Is.EqualTo(2));

            AddAssert("each has its own song", () => fetch(first).Metadata.AudioFile, () => Is.Not.EqualTo(fetch(second).Metadata.AudioFile));
            AddAssert("both songs in the set", () => filesOf(first), () => Does.Contain(fetch(first).Metadata.AudioFile).And.Contain(fetch(second).Metadata.AudioFile));
        }

        [Test]
        public void TestGeneratingAgainReplaces()
        {
            BeatmapInfo first = null!;
            BeatmapInfo again = null!;

            AddStep("generate", () => first = generate(renatus, creator, continuous));
            AddStep("generate from second song", () => generate(otherSong, creator, continuous));
            AddStep("generate first again", () => again = generate(renatus, creator, continuous));

            AddAssert("still two difficulties", () => setOf(first).Beatmaps.Count, () => Is.EqualTo(2));
            AddAssert("same difficulty updated", () => again.ID, () => Is.EqualTo(first.ID));
        }

        [Test]
        public void TestPatternsOfOneSongAreSeparateDifficulties()
        {
            BeatmapInfo stream = null!;
            BeatmapInfo bursts = null!;
            BeatmapInfo longerBursts = null!;

            AddStep("generate stream", () => stream = generate(renatus, creator, continuous));
            AddStep("generate 16 note bursts", () => bursts = generate(renatus, creator, new StreamPracticeOptions(StreamPracticePattern.Bursts, 16)));
            AddStep("generate 32 note bursts", () => longerBursts = generate(renatus, creator, new StreamPracticeOptions(StreamPracticePattern.Bursts, 32)));

            AddAssert("three difficulties", () => setOf(stream).Beatmaps.Count, () => Is.EqualTo(3));
            AddAssert("bursts named by length", () => new[] { bursts.DifficultyName, longerBursts.DifficultyName },
                () => Has.Exactly(1).EndsWith("- 16 notes").And.Exactly(1).EndsWith("- 32 notes"));

            AddAssert("bursts have rest sliders", () =>
                beatmapManager.GetWorkingBeatmap(fetch(bursts)).Beatmap.HitObjects.Any(h => h is osu.Game.Rulesets.Objects.Types.IHasPathWithRepeats));

            AddAssert("song only stored once", () => filesOf(stream).Count(f => f == fetch(stream).Metadata.AudioFile), () => Is.EqualTo(1));
        }

        [Test]
        public void TestDeletedSetIsRecreated()
        {
            BeatmapInfo first = null!;
            BeatmapInfo second = null!;

            AddStep("generate", () => first = generate(renatus, creator, continuous));
            AddStep("delete set", () => beatmapManager.Delete(setOf(first)));
            AddStep("generate again", () => second = generate(renatus, creator, continuous));

            AddAssert("new set", () => second.BeatmapSet!.ID, () => Is.Not.EqualTo(first.BeatmapSet!.ID));
            AddAssert("only one difficulty", () => setOf(second).Beatmaps, () => Has.Count.EqualTo(1));
        }

        [Test]
        public void TestEmptySetLeftByEarlierVersionIsRemoved()
        {
            Guid leftover = Guid.Empty;

            // An earlier version created the set, remembered it, and then failed before anything was put in it.
            AddStep("leave an empty set behind", () =>
            {
                leftover = beatmapManager.CreateNew(rulesetStore.GetRuleset(0)!, creator).BeatmapSetInfo.ID;
                config.SetValue(OsuSetting.StreamPracticeBeatmapSetID, leftover.ToString());
            });

            BeatmapInfo generated = null!;
            AddStep("generate", () => generated = generate(renatus, creator, continuous));

            AddAssert("empty set removed", () => Realm.Run(r => r.Find<BeatmapSetInfo>(leftover)!.DeletePending));
            AddAssert("generated into a set of its own", () => generated.BeatmapSet!.ID, () => Is.Not.EqualTo(leftover));
        }

        [Test]
        public void TestBeatmapWithoutAudioLeavesNothingBehind()
        {
            BeatmapInfo silent = null!;

            AddStep("import beatmap without audio", () => silent = importAnyBeatmap(TestResources.GetQuickTestBeatmapForImport(), "RenatusQuick"));

            AddAssert("refused", () =>
            {
                try
                {
                    generate(silent, creator, continuous);
                    return false;
                }
                catch (Exception e) when (e.GetBaseException() is InvalidOperationException)
                {
                    return true;
                }
            });

            AddAssert("no set created", () => generatedSets(), () => Is.Empty);
        }

        private BeatmapInfo importOsuBeatmap(string path, string title)
            => importAny(path, title, b => b.Ruleset.ShortName == "osu");

        private BeatmapInfo importAnyBeatmap(string path, string title)
            => importAny(path, title, _ => true);

        private BeatmapInfo importAny(string path, string title, Func<BeatmapInfo, bool> predicate)
        {
            beatmapManager.Import(path).WaitSafely();

            return beatmapManager.GetAllUsableBeatmapSets()
                                 .SelectMany(s => s.Beatmaps)
                                 .First(b => b.Metadata.Title == title && predicate(b));
        }

        /// <summary>
        /// A short beatmap of a different song, at a different tempo, with an audio file of the same name as other songs often have.
        /// </summary>
        private static string createSecondSong()
        {
            string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.osz");

            const string beatmap = @"osu file format v14

[General]
AudioFilename: audio.mp3
Mode: 0

[Metadata]
Title:Second Song
Artist:Someone
Creator:Someone
Version:Hard

[Difficulty]
CircleSize:4
OverallDifficulty:8
ApproachRate:9
HPDrainRate:5
SliderMultiplier:1.4
SliderTickRate:1

[TimingPoints]
100,300,4,2,0,75,1,0

[HitObjects]
256,192,100,1,0,0:0:0:0:
256,192,6100,1,0,0:0:0:0:
";

            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("Someone - Second Song (Someone) [Hard].osu").Open()))
                    writer.Write(beatmap);

                using (var audio = archive.CreateEntry("audio.mp3").Open())
                using (var renatusAudio = TestResources.OpenResource("Archives/241526 Soleily - Renatus.osz"))
                    renatusAudio.CopyTo(audio);
            }

            return path;
        }

        private BeatmapInfo fetch(BeatmapInfo beatmap) => Realm.Run(r => r.Find<BeatmapInfo>(beatmap.ID)!.Detach());

        /// <summary>
        /// Generates on a background thread, as song select does. Saving through the editor's path only works on the update
        /// thread, which went unnoticed while these tests generated on it.
        /// </summary>
        private BeatmapInfo generate(BeatmapInfo source, APIUser user, StreamPracticeOptions options)
            => Task.Run(() => generator.Generate(source, user, options)).GetResultSafely();

        private List<BeatmapSetInfo> generatedSets() => Realm.Run(r => r.All<BeatmapSetInfo>()
                                                                        .AsEnumerable()
                                                                        .Where(s => !s.DeletePending && s.Beatmaps.Any(b => b.Metadata.Tags.Contains(StreamPracticeMapWriter.MARKER_TAG)))
                                                                        .Select(s => s.Detach())
                                                                        .ToList());

        private string[] filesOf(BeatmapInfo beatmap) => Realm.Run(r => r.Find<BeatmapInfo>(beatmap.ID)!.BeatmapSet!.Files.Select(f => f.Filename).ToArray());

        private BeatmapSetInfo setOf(BeatmapInfo beatmap) => Realm.Run(r => r.Find<BeatmapInfo>(beatmap.ID)!.BeatmapSet!.Detach());

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (rulesetStore.IsNotNull())
                rulesetStore.Dispose();
        }
    }
}
