// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using osu.Framework.Extensions;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.IO;
using osu.Game.Models;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets;
using Realms;

namespace osu.Game.Beatmaps.StreamPractice
{
    /// <summary>
    /// Adds stream practice beatmaps (see <see cref="StreamPracticeMapWriter"/>) to a beatmap set of their own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every generated beatmap goes into the same set, which is created the first time and found again by the tag every
    /// generated beatmap carries. Each brings its song's audio and background into the set with it. Generating a beatmap
    /// again replaces the difficulty of the same name, rather than adding another.
    /// </para>
    /// <para>
    /// Meant to be run in the background. Everything is written in one transaction on the calling thread, rather than
    /// through the editor's saving, which only works on the update thread and works out the star rating of every difficulty
    /// in the set each time; this one grows with every beatmap generated.
    /// </para>
    /// </remarks>
    public class StreamPracticeGenerator
    {
        /// <summary>
        /// Generations are made one at a time, so that two at once can't both decide to create the set.
        /// </summary>
        private static readonly SemaphoreSlim generation_lock = new SemaphoreSlim(1, 1);

        private readonly BeatmapManager beatmaps;
        private readonly RealmAccess realm;
        private readonly RulesetStore rulesets;
        private readonly OsuConfigManager config;

        public StreamPracticeGenerator(BeatmapManager beatmaps, RealmAccess realm, RulesetStore rulesets, OsuConfigManager config)
        {
            this.beatmaps = beatmaps;
            this.realm = realm;
            this.rulesets = rulesets;
            this.config = config;
        }

        /// <summary>
        /// Generates a stream practice beatmap following the timing of <paramref name="sourceBeatmap"/>.
        /// </summary>
        /// <param name="sourceBeatmap">The beatmap to follow.</param>
        /// <param name="creator">The player to credit with the generated beatmap.</param>
        /// <param name="options">How the stream is laid out.</param>
        /// <returns>The generated beatmap.</returns>
        public BeatmapInfo Generate(BeatmapInfo sourceBeatmap, APIUser creator, StreamPracticeOptions options)
        {
            generation_lock.Wait();

            try
            {
                return generate(sourceBeatmap, creator, options);
            }
            finally
            {
                generation_lock.Release();
            }
        }

        private BeatmapInfo generate(BeatmapInfo sourceBeatmap, APIUser creator, StreamPracticeOptions options)
        {
            var source = beatmaps.GetWorkingBeatmap(sourceBeatmap);
            var sourceContent = source.Beatmap;

            // Everything which can go wrong with the source is checked before anything is created, so that a beatmap which
            // can't be followed leaves nothing behind.
            StreamPracticeMapWriter.CreateNotes(sourceContent, options);

            var audio = string.IsNullOrEmpty(source.Metadata.AudioFile) ? null : source.BeatmapSetInfo.GetFile(source.Metadata.AudioFile);

            if (audio == null)
                throw new InvalidOperationException("The beatmap has no audio track to play the stream to.");

            var background = string.IsNullOrEmpty(source.Metadata.BackgroundFile) ? null : source.BeatmapSetInfo.GetFile(source.Metadata.BackgroundFile);

            string audioName = getTargetName(audio);
            string? backgroundName = background == null ? null : getTargetName(background);

            string text = StreamPracticeMapWriter.Write(sourceContent, options, audioName, backgroundName, creator.Username);
            byte[] content = Encoding.UTF8.GetBytes(text);
            var generated = decode(content);

            var osu = rulesets.GetRuleset(0) ?? throw new InvalidOperationException("osu! ruleset is unavailable.");

            // Worked out here for this difficulty alone, rather than for the whole set as saving through the editor would.
            double starRating = osu.CreateInstance().CreateDifficultyCalculator(new FlatWorkingBeatmap(generated)).Calculate().StarRating;

            removeLeftoverSet();

            Guid setId = findSet() ?? beatmaps.CreateNew(osu, creator).BeatmapSetInfo.ID;

            Guid beatmapId = realm.Write(r =>
            {
                var set = r.Find<BeatmapSetInfo>(setId) ?? throw new InvalidOperationException("The stream practice set has gone missing.");

                // A new set is created with an empty difficulty, which is of no use here.
                foreach (var empty in set.Beatmaps.Where(b => b.File == null).ToList())
                    removeDifficulty(r, set, empty);

                addFileIfMissing(r, set, source, audio, audioName);

                if (background != null && backgroundName != null)
                    addFileIfMissing(r, set, source, background, backgroundName);

                string difficultyName = generated.BeatmapInfo.DifficultyName;
                var target = set.Beatmaps.FirstOrDefault(b => b.DifficultyName == difficultyName);

                if (target == null)
                {
                    target = new BeatmapInfo(r.Find<RulesetInfo>(osu.ShortName)!, new BeatmapDifficulty(), generated.Metadata)
                    {
                        DifficultyName = difficultyName,
                        BeatmapSet = set,
                    };

                    set.Beatmaps.Add(target);
                }
                else
                {
                    if (target.File != null)
                        beatmaps.DeleteFile(set, target.File, r);

                    // Each generated difficulty has metadata of its own, as the audio and background differ between them.
                    var previousMetadata = target.Metadata;
                    target.Metadata = generated.Metadata;
                    r.Remove(previousMetadata);
                }

                generated.Difficulty.CopyTo(target.Difficulty);

                using (var stream = new MemoryStream(content))
                    target.MD5Hash = stream.ComputeMD5Hash();

                using (var stream = new MemoryStream(content))
                    target.Hash = stream.ComputeSHA2Hash();

                target.StarRating = starRating;
                target.UpdateStatisticsFromBeatmap(generated);
                target.Status = BeatmapOnlineStatus.LocallyModified;
                target.LastLocalUpdate = DateTimeOffset.Now;

                string filename = $"{generated.Metadata.Artist} - {generated.Metadata.Title} ({generated.Metadata.Author.Username}) [{difficultyName}].osu".GetValidFilename();

                using (var stream = new MemoryStream(content))
                    beatmaps.AddFile(set, stream, filename, r);

                beatmaps.UpdateHashAndMarkDirty(set);

                return target.ID;
            });

            var result = realm.Run(r => r.Find<BeatmapInfo>(beatmapId)!.Detach());

            // Anything already loaded from the set has to be read again.
            ((IWorkingBeatmapCache)beatmaps).Invalidate(result.BeatmapSet!);

            return result;
        }

        /// <returns>The set generated beatmaps go into, if one exists.</returns>
        private Guid? findSet() => realm.Run(r =>
        {
            var generated = r.All<BeatmapInfo>()
                             .Filter($"{nameof(BeatmapInfo.Metadata)}.{nameof(BeatmapMetadata.Tags)} CONTAINS $0", StreamPracticeMapWriter.MARKER_TAG)
                             .AsEnumerable()
                             .FirstOrDefault(b => b.BeatmapSet != null && !b.BeatmapSet.DeletePending);

            return generated?.BeatmapSet!.ID;
        });

        /// <summary>
        /// Removes the empty set an earlier version could leave behind, when it failed partway through generating a beatmap.
        /// </summary>
        private void removeLeftoverSet()
        {
            if (!Guid.TryParse(config.Get<string>(OsuSetting.StreamPracticeBeatmapSetID), out var id))
                return;

            realm.Write(r =>
            {
                var set = r.Find<BeatmapSetInfo>(id);

                if (set != null && !set.DeletePending && set.Beatmaps.All(b => b.File == null))
                    set.DeletePending = true;
            });
        }

        private static void removeDifficulty(Realm r, BeatmapSetInfo set, BeatmapInfo beatmap)
        {
            set.Beatmaps.Remove(beatmap);
            r.Remove(beatmap.Metadata);
            r.Remove(beatmap);
        }

        /// <summary>
        /// Copies a file from the source beatmap's set into the generated set, unless it is already there.
        /// </summary>
        private void addFileIfMissing(Realm r, BeatmapSetInfo set, WorkingBeatmap source, RealmNamedFileUsage file, string targetName)
        {
            if (set.GetFile(targetName) != null)
                return;

            using (var stream = source.GetStream(file.File.GetStoragePath()))
                beatmaps.AddFile(set, stream, targetName, r);
        }

        /// <summary>
        /// The name a file from the source beatmap's set is given in the generated set.
        /// </summary>
        /// <remarks>
        /// Songs from different sets often share file names ("audio.mp3"), so the content's hash is included to keep them
        /// apart. The same file brought in twice ends up under the same name, so is only stored once.
        /// </remarks>
        private static string getTargetName(RealmNamedFileUsage file) =>
            $"{Path.GetFileNameWithoutExtension(file.Filename)} {file.File.Hash.Remove(8)}{Path.GetExtension(file.Filename)}".GetValidFilename();

        private static Beatmap decode(byte[] content)
        {
            using (var stream = new MemoryStream(content))
            using (var reader = new LineBufferedReader(stream))
                return Formats.Decoder.GetDecoder<Beatmap>(reader).Decode(reader);
        }
    }
}
