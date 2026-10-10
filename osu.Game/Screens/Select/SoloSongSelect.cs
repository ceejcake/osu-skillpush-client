// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Extensions;
using osu.Framework.Extensions.LocalisationExtensions;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Logging;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.StreamPractice;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Graphics.UserInterface;
using osu.Game.Localisation;
using osu.Game.Online.API;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Edit;
using osu.Game.Screens.Play;
using osu.Game.Users;
using osu.Game.Utils;
using WebCommonStrings = osu.Game.Resources.Localisation.Web.CommonStrings;

namespace osu.Game.Screens.Select
{
    public partial class SoloSongSelect : SongSelect
    {
        protected override UserActivity InitialActivity => new UserActivity.ChoosingBeatmap();

        private PlayerLoader? playerLoader;
        private IReadOnlyList<Mod>? modsAtGameplayStart;

        [Resolved]
        private BeatmapSetOverlay? beatmapOverlay { get; set; }

        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private INotificationOverlay? notifications { get; set; }

        [Resolved]
        private IDialogOverlay? dialogOverlay { get; set; }

        [Resolved]
        private OsuGame? game { get; set; }

        private Sample? sampleConfirmSelection { get; set; }

        [BackgroundDependencyLoader]
        private void load(AudioManager audio)
        {
            sampleConfirmSelection = audio.Samples.Get(@"SongSelect/confirm-selection");

            AddInternal(new SongSelectTouchInputDetector());
        }

        public override IEnumerable<OsuMenuItem> GetForwardActions(BeatmapInfo beatmap)
        {
            yield return new OsuMenuItem(ButtonSystemStrings.Play.ToSentence(), MenuItemType.Highlighted, () => SelectAndRun(beatmap, OnStart)) { Icon = FontAwesome.Solid.Check };
            yield return new OsuMenuItem(ButtonSystemStrings.Edit.ToSentence(), MenuItemType.Standard, () => Edit(beatmap)) { Icon = FontAwesome.Solid.PencilAlt };
            yield return new OsuMenuItem("Generate stream practice map", MenuItemType.Standard, () => GenerateStreamPracticeMap(beatmap)) { Icon = FontAwesome.Solid.CircleNotch };

            yield return new OsuMenuItemSpacer();

            if (beatmap.OnlineID > 0)
            {
                yield return new OsuMenuItem(CommonStrings.Details, MenuItemType.Standard, () => beatmapOverlay?.FetchAndShowBeatmap(beatmap.OnlineID));

                if (beatmap.GetOnlineURL(api, Ruleset.Value) is string url)
                    yield return new OsuMenuItem(CommonStrings.CopyLink, MenuItemType.Standard, () => game?.CopyToClipboard(url));

                yield return new OsuMenuItemSpacer();
            }

            foreach (var i in CreateCollectionMenuActions(beatmap))
                yield return i;

            if (beatmap.LastPlayed == null)
                yield return new OsuMenuItem(SongSelectStrings.MarkAsPlayed, MenuItemType.Standard, () => beatmaps.MarkPlayed(beatmap)) { Icon = FontAwesome.Solid.TimesCircle };
            else
                yield return new OsuMenuItem(SongSelectStrings.RemoveFromPlayed, MenuItemType.Standard, () => beatmaps.MarkNotPlayed(beatmap)) { Icon = FontAwesome.Solid.TimesCircle };

            yield return new OsuMenuItem(SongSelectStrings.ClearAllLocalScores, MenuItemType.Destructive, () => dialogOverlay?.Push(new BeatmapClearScoresDialog(beatmap)))
            {
                Icon = FontAwesome.Solid.Eraser
            };

            if (beatmaps.CanHide(beatmap))
                yield return new OsuMenuItem(WebCommonStrings.ButtonsHide.ToSentence(), MenuItemType.Standard, () => beatmaps.Hide(beatmap));
        }

        protected override void OnStart()
        {
            if (playerLoader != null) return;

            modsAtGameplayStart = Mods.Value.Select(m => m.DeepClone()).ToArray();

            // Ctrl+Enter should start map with autoplay enabled.
            if (GetContainingInputManager()?.CurrentState?.Keyboard.ControlPressed == true)
            {
                var autoInstance = getAutoplayMod();

                if (autoInstance == null)
                {
                    notifications?.Post(new SimpleNotification
                    {
                        Text = NotificationsStrings.NoAutoplayMod
                    });
                    return;
                }

                var mods = Mods.Value.Append(autoInstance).ToArray();

                if (!ModUtils.CheckCompatibleSet(mods, out var invalid))
                    mods = mods.Except(invalid).Append(autoInstance).ToArray();

                Mods.Value = mods;
            }

            sampleConfirmSelection?.Play();

            this.Push(playerLoader = new PlayerLoader(createPlayer));

            Player createPlayer()
            {
                Player player;

                var replayGeneratingMod = Mods.Value.OfType<ICreateReplayData>().FirstOrDefault();

                if (replayGeneratingMod != null)
                {
                    player = new ReplayPlayer(replayGeneratingMod.CreateScoreFromReplayData);
                }
                else
                {
                    player = new SoloPlayer();
                }

                return player;
            }
        }

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private RulesetStore rulesets { get; set; } = null!;

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        /// <summary>
        /// Generates a stream practice beatmap following the timing of <paramref name="beatmap"/>, into a set shared by all of them.
        /// </summary>
        public void GenerateStreamPracticeMap(BeatmapInfo beatmap)
        {
            var notification = new ProgressNotification
            {
                Text = $"Generating stream practice map from {beatmap.GetDisplayTitle()}",
                State = ProgressNotificationState.Active,
            };

            notifications?.Post(notification);

            var generator = new StreamPracticeGenerator(beatmaps, realm, rulesets, config);
            var creator = api.LocalUser.Value;
            var options = new StreamPracticeOptions(config.Get<StreamPracticePattern>(OsuSetting.StreamPracticePattern), config.Get<int>(OsuSetting.StreamPracticeBurstLength));

            Task.Run(() => generator.Generate(beatmap, creator, options)).ContinueWith(task => Schedule(() =>
            {
                if (task.Exception != null)
                {
                    Logger.Error(task.Exception, "Failed to generate a stream practice map");

                    notification.Text = $"Couldn't generate a stream practice map: {task.Exception.GetBaseException().Message}";
                    notification.State = ProgressNotificationState.Cancelled;
                    return;
                }

                var generated = task.GetResultSafely();

                notification.CompletionText = $"Generated {generated.DifficultyName}. Click to play it.";
                notification.CompletionClickAction = () =>
                {
                    if (generated.BeatmapSet != null)
                        game?.PresentBeatmap(generated.BeatmapSet, b => b.ID == generated.ID);

                    return true;
                };
                notification.State = ProgressNotificationState.Completed;
            }));
        }

        public void Edit(BeatmapInfo beatmap)
        {
            if (!this.IsCurrentScreen())
                return;

            SelectAndRun(beatmap, () => this.Push(new EditorLoader()));
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);
            revertMods();
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            if (base.OnExiting(e))
                return true;

            revertMods();
            return false;
        }

        private ModAutoplay? getAutoplayMod() => Ruleset.Value.CreateInstance().GetAutoplayMod();

        private void revertMods()
        {
            if (playerLoader == null) return;

            Mods.Value = modsAtGameplayStart;
            playerLoader = null;
        }

        private partial class PlayerLoader : Play.PlayerLoader
        {
            public override bool ShowFooter => !QuickRestart;

            public PlayerLoader(Func<Player> createPlayer)
                : base(createPlayer)
            {
            }
        }
    }
}
