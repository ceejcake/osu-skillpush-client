// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Threading;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Play;

namespace osu.Game.Beatmaps
{
    /// <summary>
    /// Works out the star ratings of beatmaps under the current ruleset and mods, so that song select can filter, sort and
    /// group by the same star ratings it displays.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the beatmaps something has asked for are worked out (see <see cref="BeginTracking"/>) — in practice, those in
    /// the collection selected in song select, since the whole library can take a long time. They are worked out in the
    /// background and published in batches through <see cref="Current"/>. Until a beatmap has been worked out, its stored
    /// star rating stands in for it.
    /// </para>
    /// <para>
    /// Results are saved to disk for each ruleset and set of mods, so returning to a combination that has been seen before
    /// is instant. They are tied to the exact build of the game and ruleset, as a change to a mod can change the star
    /// ratings it gives without anything else noticing.
    /// </para>
    /// </remarks>
    [Cached]
    public partial class ModdedStarRatingCache : Component
    {
        /// <summary>
        /// The star ratings worked out so far for the current ruleset and mods.
        /// </summary>
        public StarRatingSnapshot Current { get; private set; } = StarRatingSnapshot.EMPTY;

        /// <summary>
        /// Invoked on the update thread whenever <see cref="Current"/> is replaced.
        /// </summary>
        public event Action? RatingsChanged;

        /// <summary>
        /// Whether star ratings are currently being worked out.
        /// </summary>
        public IBindable<bool> IsCalculating => isCalculating;

        private readonly BindableBool isCalculating = new BindableBool();

        /// <summary>
        /// The number of difficulty calculations performed, rather than read back from disk. For testing.
        /// </summary>
        public int CalculationCount => calculationCount;

        private int calculationCount;

        /// <summary>
        /// How often newly worked out star ratings are published while a calculation is in progress.
        /// </summary>
        private const double publish_interval = 2000;

        /// <summary>
        /// How long to wait for a mod's settings to stop changing before starting over, as sliders change them rapidly.
        /// </summary>
        private const double mod_setting_debounce = 500;

        /// <summary>
        /// The number of ruleset and mod combinations kept on disk.
        /// </summary>
        private const int maximum_saved_combinations = 32;

        private const string storage_directory = @"cache/star-ratings";

        /// <summary>
        /// How long a pass has to run before its progress is shown, so that short ones don't flash up a notification.
        /// </summary>
        private const double notification_delay = 1000;

        private const double notification_update_interval = 250;

        /// <summary>
        /// How long a pass has to run before its progress is shown.
        /// </summary>
        protected virtual double NotificationDelay => notification_delay;

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmapManager { get; set; } = null!;

        [Resolved]
        private Bindable<RulesetInfo> ruleset { get; set; } = null!;

        [Resolved]
        private Bindable<IReadOnlyList<Mod>> mods { get; set; } = null!;

        [Resolved]
        private ILocalUserPlayInfo? localUserPlayInfo { get; set; }

        [Resolved]
        private INotificationOverlay? notifications { get; set; }

        /// <summary>
        /// Whether progress can be shown, which relies on this having been loaded after the notification overlay.
        /// </summary>
        internal bool CanShowProgress => notifications != null;

        private readonly Bindable<bool> includeConvertedBeatmaps = new Bindable<bool>();

        private Storage? storage;

        private RatingSet? currentSet;

        private readonly List<Tracking> trackers = new List<Tracking>();

        private ModSettingChangeTracker? modSettingChangeTracker;
        private ScheduledDelegate? debouncedModSettingChange;
        private ScheduledDelegate? publishLoop;

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config, Storage gameStorage)
        {
            config.BindWith(OsuSetting.ShowConvertedBeatmaps, includeConvertedBeatmaps);

            try
            {
                storage = gameStorage.GetStorageForDirectory(storage_directory);
            }
            catch (Exception e)
            {
                Logger.Log($"Star ratings with mods will not be saved: {e.Message}");
            }
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            ruleset.BindValueChanged(_ => Scheduler.AddOnce(invalidate));

            mods.BindValueChanged(m =>
            {
                modSettingChangeTracker?.Dispose();
                modSettingChangeTracker = new ModSettingChangeTracker(m.NewValue);
                modSettingChangeTracker.SettingChanged += _ =>
                {
                    debouncedModSettingChange?.Cancel();
                    debouncedModSettingChange = Scheduler.AddDelayed(invalidate, mod_setting_debounce);
                };

                Scheduler.AddOnce(invalidate);
            }, true);

            // Converted beatmaps only need working out while they're shown.
            includeConvertedBeatmaps.BindValueChanged(_ => Scheduler.AddOnce(startRun));
        }

        /// <summary>
        /// Starts working out star ratings for the beatmaps given to the returned object, for as long as it is not disposed.
        /// </summary>
        public Tracking BeginTracking()
        {
            var tracking = new Tracking(this);
            trackers.Add(tracking);
            return tracking;
        }

        /// <summary>
        /// The beatmaps which need working out, from everything tracking them, or <see langword="null"/> if there are none.
        /// </summary>
        private HashSet<string>? getScope()
        {
            HashSet<string>? scope = null;

            foreach (var tracker in trackers)
            {
                if (tracker.Beatmaps == null)
                    continue;

                scope ??= new HashSet<string>();
                scope.UnionWith(tracker.Beatmaps);
            }

            return scope;
        }

        private void scopeChanged()
        {
            // Beatmaps which are no longer needed shouldn't hold up the ones which are, so any pass underway is cut short.
            // What it worked out is kept, and the pass is picked up again with the new beatmaps once it has stopped.
            if (currentSet != null)
                currentSet.StoppedByUser = false;

            if (currentSet?.IsRunning == true)
                currentSet.Cancel();
            else
                startRun();
        }

        private void stopTracking(Tracking tracking)
        {
            trackers.Remove(tracking);

            // Nobody is looking, so there's no reason to spend time on it. What was worked out so far is kept.
            if (trackers.Count == 0)
                currentSet?.Cancel();
            else
                scopeChanged();
        }

        /// <summary>
        /// Called when the ruleset or mods may have changed.
        /// </summary>
        private void invalidate()
        {
            debouncedModSettingChange?.Cancel();

            // There is no ruleset yet while the game is starting up.
            var set = ruleset.Value == null ? null : new RatingSet(ruleset.Value, mods.Value);

            if (set?.Key == currentSet?.Key)
                return;

            currentSet?.Cancel();
            currentSet = set;

            // Ratings for the previous mods no longer apply, so they are dropped straight away rather than left in use
            // until the first new ones arrive.
            Current = StarRatingSnapshot.EMPTY;
            RatingsChanged?.Invoke();

            startRun();
        }

        private void startRun()
        {
            var set = currentSet;

            var scope = getScope();

            // Once stopped from its notification, a pass waits for something to change before it starts again.
            if (set == null || scope == null || scope.Count == 0 || set.StoppedByUser)
                return;

            // Picked up once the current pass is finished.
            if (set.IsRunning)
            {
                set.RequestRestart();
                return;
            }

            set.Start();

            isCalculating.Value = true;

            publishLoop?.Cancel();
            publishLoop = Scheduler.AddDelayed(() => publish(set), publish_interval, true);

            var progress = new RunProgress(Time.Current);
            progress.UpdateLoop = Scheduler.AddDelayed(() => updateNotification(set, progress), notification_update_interval, true);

            var known = set.Ratings.Keys.ToHashSet();
            bool loadFromDisk = !set.LoadedFromDisk;
            bool includeConverts = includeConvertedBeatmaps.Value;
            var token = set.Token;

            set.LoadedFromDisk = true;

            Task.Factory.StartNew(() =>
            {
                try
                {
                    if (loadFromDisk)
                        load(set, known);

                    calculate(set, scope, known, includeConverts, progress, token);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception e)
                {
                    Logger.Error(e, "Failed to work out star ratings with mods");
                }
                finally
                {
                    Schedule(() => finishRun(set, progress));
                }
            }, TaskCreationOptions.LongRunning);
        }

        private void finishRun(RatingSet set, RunProgress progress)
        {
            set.Stop();

            progress.UpdateLoop?.Cancel();
            closeNotification(set, progress);

            publish(set);

            if (set != currentSet)
                return;

            publishLoop?.Cancel();
            publishLoop = null;

            isCalculating.Value = false;

            // A change made while that was underway (a newly shown converted beatmap, say) may need another pass.
            if (set.RestartRequested)
                startRun();
        }

        /// <summary>
        /// Adds the star ratings worked out since the last call to <see cref="Current"/>, and saves them.
        /// </summary>
        private void publish(RatingSet set)
        {
            var newRatings = new List<(string hash, double stars)>();
            bool changed = false;

            while (set.Pending.TryDequeue(out var result))
            {
                set.Ratings[result.hash] = result.stars;
                changed = true;

                if (!result.fromDisk)
                    newRatings.Add((result.hash, result.stars));
            }

            if (newRatings.Count > 0)
                save(set, newRatings);

            if (!changed || set != currentSet)
                return;

            Current = new StarRatingSnapshot(new Dictionary<string, double>(set.Ratings));
            RatingsChanged?.Invoke();
        }

        private void calculate(RatingSet set, HashSet<string> scope, HashSet<string> known, bool includeConverts, RunProgress progress, CancellationToken token)
        {
            var targetRuleset = set.Ruleset;
            bool hasMods = set.Mods.Length > 0;

            // Beatmaps in their own ruleset without mods already have the right star rating stored with them.
            var beatmaps = realm.Run(r => r.All<BeatmapInfo>()
                                           .Where(b => !b.Hidden && b.BeatmapSet != null)
                                           .AsEnumerable()
                                           .Where(b => !b.BeatmapSet!.DeletePending && b.AllowGameplayWithRuleset(targetRuleset, includeConverts))
                                           .Select(b => (id: b.ID, hash: b.MD5Hash, converted: b.Ruleset.ShortName != targetRuleset.ShortName))
                                           .Where(b => (hasMods || b.converted) && scope.Contains(b.hash) && !known.Contains(b.hash))
                                           .DistinctBy(b => b.hash)
                                           // The beatmaps actually made for the ruleset come first, as they are what song select shows by default.
                                           .OrderBy(b => b.converted)
                                           .ToList());

            progress.Total = beatmaps.Count;

            token.ThrowIfCancellationRequested();

            if (beatmaps.Count == 0)
                return;

            var work = new ConcurrentQueue<(Guid id, string hash, bool converted)>(beatmaps);

            int threadCount = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
            var threads = new Thread[threadCount];

            for (int i = 0; i < threadCount; i++)
            {
                threads[i] = new Thread(() => calculateQueue(set, work, progress, token))
                {
                    Name = $"{nameof(ModdedStarRatingCache)} {i}",
                    IsBackground = true,
                    // Song select has to stay smooth while this runs.
                    Priority = ThreadPriority.BelowNormal,
                };

                threads[i].Start();
            }

            foreach (var thread in threads)
                thread.Join();

            token.ThrowIfCancellationRequested();
        }

        private void calculateQueue(RatingSet set, ConcurrentQueue<(Guid id, string hash, bool converted)> work, RunProgress progress, CancellationToken token)
        {
            var rulesetInstance = set.Ruleset.CreateInstance();

            while (!token.IsCancellationRequested && work.TryDequeue(out var item))
            {
                waitWhilePlaying(token);

                if (token.IsCancellationRequested)
                    return;

                try
                {
                    var beatmap = realm.Run(r => r.Find<BeatmapInfo>(item.id)?.Detach());

                    if (beatmap == null)
                        continue;

                    var working = beatmapManager.GetWorkingBeatmap(beatmap);

                    // Mods may hold state which changes from one beatmap to the next (a bpm lock settles on a rate for
                    // each one, for instance), so every calculation gets its own copies.
                    var modsForBeatmap = set.Mods.Select(m => m.DeepClone()).ToArray();

                    double stars = rulesetInstance.CreateDifficultyCalculator(working).Calculate(modsForBeatmap, token).StarRating;

                    Interlocked.Increment(ref calculationCount);

                    if (double.IsFinite(stars))
                        set.Pending.Enqueue((item.hash, stars, false));
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (BeatmapInvalidForRulesetException)
                {
                    // Not every beatmap can be converted to every ruleset. It will be left out of song select anyway.
                }
                catch (Exception e)
                {
                    Logger.Log($"Failed to work out the star rating of {item.id} with mods: {e.Message}");
                }
                finally
                {
                    // Counted whether or not it worked, as either way it's no longer waiting.
                    Interlocked.Increment(ref progress.Completed);
                }
            }
        }

        #region Progress

        private void updateNotification(RatingSet set, RunProgress progress)
        {
            int total = progress.Total;
            int completed = Math.Min(progress.Completed, total);

            if (total <= 0)
                return;

            if (progress.Notification == null)
            {
                if (notifications == null || completed >= total || Time.Current - progress.StartTime < NotificationDelay)
                    return;

                progress.Notification = new ProgressNotification
                {
                    State = ProgressNotificationState.Active,
                    CancelRequested = () =>
                    {
                        // Stopped until the mods, collection or ruleset change, rather than starting straight back up.
                        progress.StoppedByUser = true;
                        set.StoppedByUser = true;
                        set.Cancel();
                        return true;
                    },
                };

                notifications.Post(progress.Notification);
            }

            progress.Notification.Text = $"Working out star ratings {describe(set)} ({completed:N0} of {total:N0})";
            progress.Notification.Progress = (float)completed / total;
        }

        private void closeNotification(RatingSet set, RunProgress progress)
        {
            var notification = progress.Notification;

            // Stopping it from the notification has already left it cancelled.
            if (notification == null || progress.StoppedByUser)
                return;

            if (progress.Total > 0 && progress.Completed >= progress.Total)
            {
                notification.CompletionText = $"Worked out star ratings {describe(set)} for {progress.Total:N0} beatmaps.";
                notification.Progress = 1;
                notification.State = ProgressNotificationState.Completed;
            }
            else
            {
                // Cut short by a change of mods or collection. The pass that replaces it shows its own progress.
                notification.CompleteSilently();
            }
        }

        private static string describe(RatingSet set) => set.Mods.Length > 0
            ? $"with {string.Join(", ", set.Mods.Select(m => m.Acronym))}"
            : $"for {set.Ruleset.Name}";

        /// <summary>
        /// The progress of one pass over a <see cref="RatingSet"/>.
        /// </summary>
        private class RunProgress
        {
            public readonly double StartTime;

            /// <summary>
            /// The number of beatmaps being worked out, or -1 until they have been looked up.
            /// </summary>
            public volatile int Total = -1;

            public int Completed;

            public ProgressNotification? Notification;

            public ScheduledDelegate? UpdateLoop;

            public bool StoppedByUser;

            public RunProgress(double startTime)
            {
                StartTime = startTime;
            }
        }

        #endregion

        private void waitWhilePlaying(CancellationToken token)
        {
            while (localUserPlayInfo?.PlayingState.Value == LocalUserPlayingState.Playing && !token.IsCancellationRequested)
                Thread.Sleep(500);
        }

        #region Saving

        private void load(RatingSet set, HashSet<string> known)
        {
            if (storage == null || set.FileName == null || !storage.Exists(set.FileName))
                return;

            try
            {
                using (var stream = storage.GetStream(set.FileName))
                using (var reader = new StreamReader(stream))
                {
                    while (reader.ReadLine() is string line)
                    {
                        string[] parts = line.Split(' ');

                        if (parts.Length != 2 || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double stars))
                            continue;

                        if (known.Add(parts[0]))
                            set.Pending.Enqueue((parts[0], stars, true));
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Log($"Failed to read saved star ratings: {e.Message}");
            }
        }

        private void save(RatingSet set, List<(string hash, double stars)> ratings)
        {
            if (storage == null || set.FileName == null)
                return;

            var text = new StringBuilder();

            foreach (var (hash, stars) in ratings)
                text.Append(hash).Append(' ').Append(stars.ToString("R", CultureInfo.InvariantCulture)).Append('\n');

            string fileName = set.FileName;
            var targetStorage = storage;

            Task.Run(() =>
            {
                lock (targetStorage)
                {
                    try
                    {
                        using (var stream = targetStorage.GetStream(fileName, FileAccess.Write, FileMode.Append))
                        using (var writer = new StreamWriter(stream))
                            writer.Write(text.ToString());

                        removeOldFiles(targetStorage, fileName);
                    }
                    catch (Exception e)
                    {
                        Logger.Log($"Failed to save star ratings: {e.Message}");
                    }
                }
            });
        }

        private static void removeOldFiles(Storage targetStorage, string keep)
        {
            var files = targetStorage.GetFiles(string.Empty)
                                     .Select(f => new FileInfo(targetStorage.GetFullPath(f)))
                                     .OrderByDescending(f => f.LastWriteTimeUtc)
                                     .ToList();

            foreach (var file in files.Skip(maximum_saved_combinations))
            {
                if (file.Name != keep)
                    file.Delete();
            }
        }

        #endregion

        protected override void Dispose(bool isDisposing)
        {
            currentSet?.Cancel();
            modSettingChangeTracker?.Dispose();

            base.Dispose(isDisposing);
        }

        /// <summary>
        /// Keeps star ratings being worked out for a set of beatmaps, until disposed.
        /// </summary>
        public sealed class Tracking : IDisposable
        {
            private readonly ModdedStarRatingCache cache;

            /// <summary>
            /// The <see cref="BeatmapInfo.MD5Hash"/>es of the beatmaps to work out, or <see langword="null"/> for none.
            /// </summary>
            internal IReadOnlySet<string>? Beatmaps { get; private set; }

            private bool disposed;

            internal Tracking(ModdedStarRatingCache cache)
            {
                this.cache = cache;
            }

            /// <summary>
            /// Sets the beatmaps to work out, by <see cref="BeatmapInfo.MD5Hash"/>.
            /// </summary>
            /// <param name="md5Hashes">The beatmaps, or <see langword="null"/> to work out none.</param>
            public void SetBeatmaps(IEnumerable<string>? md5Hashes)
            {
                var beatmaps = md5Hashes?.ToHashSet();

                if (beatmaps == null ? Beatmaps == null : Beatmaps?.SetEquals(beatmaps) == true)
                    return;

                Beatmaps = beatmaps;
                cache.Schedule(cache.scopeChanged);
            }

            public void Dispose()
            {
                if (disposed)
                    return;

                disposed = true;
                cache.Schedule(() => cache.stopTracking(this));
            }
        }

        /// <summary>
        /// The star ratings for one ruleset and set of mods.
        /// </summary>
        private class RatingSet
        {
            public readonly RulesetInfo Ruleset;

            /// <summary>
            /// Copies of the mods, which don't change when the originals do.
            /// </summary>
            public readonly Mod[] Mods;

            /// <summary>
            /// Identifies the ruleset and the mods with their settings.
            /// </summary>
            public readonly string Key;

            /// <summary>
            /// The file these star ratings are saved to, or <see langword="null"/> if they can't be saved.
            /// </summary>
            public readonly string? FileName;

            /// <summary>
            /// Star ratings keyed by <see cref="BeatmapInfo.MD5Hash"/>. Only accessed from the update thread.
            /// </summary>
            public readonly Dictionary<string, double> Ratings = new Dictionary<string, double>();

            /// <summary>
            /// Star ratings which are yet to be added to <see cref="Ratings"/>.
            /// </summary>
            public readonly ConcurrentQueue<(string hash, double stars, bool fromDisk)> Pending = new ConcurrentQueue<(string, double, bool)>();

            public bool LoadedFromDisk;

            /// <summary>
            /// Whether working these out was stopped from its notification.
            /// </summary>
            public bool StoppedByUser;

            public bool IsRunning { get; private set; }

            public bool RestartRequested { get; private set; }

            public CancellationToken Token => cancellation.Token;

            private CancellationTokenSource cancellation = new CancellationTokenSource();

            public RatingSet(RulesetInfo ruleset, IEnumerable<Mod> mods)
            {
                Ruleset = ruleset;
                // Anything which star ratings aren't sorted by is left out, so that changing it doesn't start over.
                Mods = StarRatingSortingMods.Normalise(mods);

                Key = $"{ruleset.ShortName}|{StarRatingSortingMods.GetKey(Mods)}";

                string? buildHash = getBuildHash(ruleset);

                if (buildHash != null)
                    FileName = $"{$"{buildHash}|{Key}".ComputeMD5Hash()}.txt";
            }

            public void Start()
            {
                if (cancellation.IsCancellationRequested)
                    cancellation = new CancellationTokenSource();

                IsRunning = true;
                RestartRequested = false;
            }

            public void Stop() => IsRunning = false;

            public void RequestRestart() => RestartRequested = true;

            public void Cancel()
            {
                if (IsRunning)
                    RestartRequested = true;

                cancellation.Cancel();
            }

            private static readonly ConcurrentDictionary<string, string?> build_hashes = new ConcurrentDictionary<string, string?>();

            /// <summary>
            /// Identifies the exact build of the game and of the ruleset.
            /// </summary>
            private static string? getBuildHash(RulesetInfo ruleset)
            {
                string? rulesetAssembly = ruleset.CreateInstance().GetType().Assembly.Location;

                return build_hashes.GetOrAdd(rulesetAssembly, location =>
                {
                    try
                    {
                        string gameHash = hashFile(typeof(ModdedStarRatingCache).Assembly.Location);
                        string rulesetHash = hashFile(location);

                        return $"{gameHash}{rulesetHash}";
                    }
                    catch
                    {
                        return null;
                    }
                });

                static string hashFile(string path)
                {
                    using (var stream = File.OpenRead(path))
                        return stream.ComputeMD5Hash();
                }
            }
        }
    }
}
