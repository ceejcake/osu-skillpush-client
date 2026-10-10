// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Development;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Play;
using Realms;

namespace osu.Game.Scoring
{
    /// <summary>
    /// Works out the performance points the local player would have if their local scores had been submitted online.
    /// </summary>
    /// <remarks>
    /// Scores are given their performance as they are found, which is stored so that it only ever has to be calculated once.
    /// </remarks>
    [Cached]
    public partial class LocalPerformanceTracker : Component
    {
        /// <summary>
        /// The totals for the current ruleset.
        /// </summary>
        public IBindable<LocalPerformanceSummary> Summary => summary;

        private readonly Bindable<LocalPerformanceSummary> summary = new Bindable<LocalPerformanceSummary>(LocalPerformanceSummary.EMPTY);

        /// <summary>
        /// How long to wait after the scores change before updating, so that a burst of changes is handled together.
        /// </summary>
        private const double update_debounce = 500;

        /// <summary>
        /// How many scores to calculate between updates of the totals, so that they visibly fill in during a long calculation.
        /// </summary>
        private const int scores_between_updates = 25;

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private BeatmapDifficultyCache difficultyCache { get; set; } = null!;

        [Resolved]
        private IBindable<RulesetInfo> ruleset { get; set; } = null!;

        [Resolved]
        private ILocalUserPlayInfo? localUserPlayInfo { get; set; }

        private readonly Bindable<bool> includeUnranked = new Bindable<bool>();

        private readonly object processingLock = new object();
        private bool processing;
        private bool updatePending;

        /// <summary>
        /// Scores whose performance couldn't be calculated, which aren't retried until the game is restarted.
        /// Only accessed from the processing thread.
        /// </summary>
        private readonly HashSet<Guid> failedScores = new HashSet<Guid>();

        private readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();

        private IDisposable? scoreSubscription;
        private ScheduledDelegate? scheduledUpdate;

        // Copies of the settings, for reading from the processing thread.
        private volatile string rulesetShortName = string.Empty;
        private volatile bool countUnranked;

        /// <summary>
        /// Whether scores should be calculated automatically. Tests use the database directly, so this is left off for them.
        /// </summary>
        protected virtual bool ProcessAutomatically => !DebugUtils.IsNUnitRunning;

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            config.BindWith(OsuSetting.GuestPerformanceIncludesUnranked, includeUnranked);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            ruleset.BindValueChanged(r =>
            {
                rulesetShortName = r.NewValue?.ShortName ?? string.Empty;
                scheduleUpdate();
            }, true);

            includeUnranked.BindValueChanged(u =>
            {
                countUnranked = u.NewValue;
                scheduleUpdate();
            }, true);

            if (ProcessAutomatically)
                scoreSubscription = realm.RegisterForNotifications(r => r.All<ScoreInfo>(), (_, _) => scheduleUpdate());
        }

        /// <summary>
        /// Whether a score was set by the local player on this client, rather than being imported from someone else.
        /// </summary>
        /// <remarks>
        /// Scores set while logged out are stored against the guest user, and those set before a connection is established
        /// against a placeholder user, neither of which have an online ID.
        /// </remarks>
        public static bool IsLocalPlayerScore(ScoreInfo score) => score.RealmUser.OnlineID <= 1;

        /// <summary>
        /// Whether a score would be awarded performance points online.
        /// </summary>
        public static bool IsRankedOnline(ScoreInfo score)
        {
            if (score.Rank == ScoreRank.F)
                return false;

            if (score.BeatmapInfo?.Status.GrantsPerformancePoints() != true)
                return false;

            IEnumerable<Mod> mods = score.Mods;

            // Classic is applied automatically to imported scores from stable, which were ranked when set.
            if (score.IsLegacyScore)
                mods = mods.Where(m => m is not ModClassic);

            return mods.All(m => m.Ranked);
        }

        private void scheduleUpdate()
        {
            if (!ProcessAutomatically)
                return;

            scheduledUpdate?.Cancel();
            scheduledUpdate = Scheduler.AddDelayed(() => Refresh(), update_debounce);
        }

        /// <summary>
        /// Calculates any scores which are missing their performance, and brings <see cref="Summary"/> up to date.
        /// </summary>
        public Task Refresh()
        {
            lock (processingLock)
            {
                updatePending = true;

                if (processing)
                    return Task.CompletedTask;

                processing = true;
            }

            return Task.Factory.StartNew(processUpdates, cancellationSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private void processUpdates()
        {
            while (true)
            {
                lock (processingLock)
                {
                    if (!updatePending || cancellationSource.IsCancellationRequested)
                    {
                        processing = false;
                        return;
                    }

                    updatePending = false;
                }

                try
                {
                    process();
                }
                catch (Exception e) when (e is ObjectDisposedException or OperationCanceledException)
                {
                    lock (processingLock)
                        processing = false;

                    return;
                }
                catch (Exception e)
                {
                    Logger.Error(e, "Failed to update local performance.");
                }
            }
        }

        private void process()
        {
            var missing = realm.Run(r => r.All<ScoreInfo>()
                                          .Filter($"{nameof(ScoreInfo.DeletePending)} == false && {nameof(ScoreInfo.PP)} == null")
                                          .AsEnumerable()
                                          .Where(s => s.BeatmapInfo != null && s.Rank != ScoreRank.F && IsLocalPlayerScore(s) && !failedScores.Contains(s.ID))
                                          .Select(s => s.ID)
                                          .ToList());

            publishSummary(missing.Count);

            for (int i = 0; i < missing.Count; i++)
            {
                waitWhilePlaying();

                calculatePerformance(missing[i]);

                int remaining = missing.Count - i - 1;

                if (remaining > 0 && (i + 1) % scores_between_updates == 0)
                    publishSummary(remaining);
            }

            if (missing.Count > 0)
                publishSummary(0);
        }

        private void calculatePerformance(Guid scoreId)
        {
            var token = cancellationSource.Token;

            try
            {
                var score = realm.Run(r => r.Find<ScoreInfo>(scoreId)?.Detach());

                if (score?.BeatmapInfo == null)
                    return;

                var calculator = score.Ruleset.CreateInstance().CreatePerformanceCalculator();
                var difficulty = difficultyCache.GetDifficultyAsync(score.BeatmapInfo, score.Ruleset, score.Mods, token).GetResultSafely();

                token.ThrowIfCancellationRequested();

                if (calculator == null || difficulty?.DifficultyAttributes == null)
                {
                    failedScores.Add(scoreId);
                    return;
                }

                double performance = calculator.Calculate(score, difficulty.Value.DifficultyAttributes).Total;

                if (!double.IsFinite(performance))
                {
                    failedScores.Add(scoreId);
                    return;
                }

                realm.Write(r =>
                {
                    var stored = r.Find<ScoreInfo>(scoreId);

                    if (stored != null)
                        stored.PP = performance;
                });
            }
            catch (Exception e) when (e is not (ObjectDisposedException or OperationCanceledException))
            {
                Logger.Log($"Failed to calculate the performance of local score {scoreId}: {e.Message}");
                failedScores.Add(scoreId);
            }
        }

        private void publishSummary(int pendingScores)
        {
            string shortName = rulesetShortName;
            bool unranked = countUnranked;

            var entries = realm.Run(r => r.All<ScoreInfo>()
                                          .Filter($"{nameof(ScoreInfo.DeletePending)} == false && {nameof(ScoreInfo.PP)} != null && {nameof(ScoreInfo.Ruleset)}.{nameof(RulesetInfo.ShortName)} == $0", shortName)
                                          .AsEnumerable()
                                          .Where(s => s.BeatmapInfo != null && s.Rank != ScoreRank.F && IsLocalPlayerScore(s) && (unranked || IsRankedOnline(s)))
                                          .Select(s => new PerformanceEntry(s.ID, getBeatmapKey(s.BeatmapInfo!), s.PP!.Value, s.Accuracy))
                                          .ToList());

            (double totalPerformance, double accuracy, var counted) = LocalPerformanceSummary.Calculate(entries);

            var topPlays = realm.Run(r => counted.Take(LocalPerformanceSummary.MAXIMUM_DISPLAYED_PLAYS)
                                                 .Select(c => (score: r.Find<ScoreInfo>(c.entry.ScoreID), c.entry.Performance, c.weight))
                                                 .Where(c => c.score != null)
                                                 .Select(c => new LocalTopPlay(c.score!.Detach(), c.Performance, c.weight))
                                                 .ToArray());

            var result = new LocalPerformanceSummary(totalPerformance, accuracy, counted.Count, topPlays, pendingScores);

            Schedule(() =>
            {
                // A ruleset change while this was being worked out will have queued another update, which will be correct.
                if (shortName == rulesetShortName && unranked == countUnranked)
                    summary.Value = result;
            });
        }

        private void waitWhilePlaying()
        {
            while (localUserPlayInfo?.PlayingState.Value == LocalUserPlayingState.Playing)
            {
                cancellationSource.Token.ThrowIfCancellationRequested();
                Thread.Sleep(1000);
            }

            cancellationSource.Token.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// Online, only one score counts per beatmap. Separately imported copies of a beatmap share its online ID.
        /// </summary>
        private static string getBeatmapKey(BeatmapInfo beatmap) => beatmap.OnlineID > 0 ? $"online:{beatmap.OnlineID}" : $"local:{beatmap.ID}";

        protected override void Dispose(bool isDisposing)
        {
            cancellationSource.Cancel();
            scoreSubscription?.Dispose();

            base.Dispose(isDisposing);
        }
    }
}
