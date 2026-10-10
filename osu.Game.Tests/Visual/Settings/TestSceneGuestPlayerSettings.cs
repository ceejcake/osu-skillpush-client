// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Testing;
using osu.Game.Configuration;
using osu.Game.Online.API;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings.Sections.General;
using osu.Game.Users;

namespace osu.Game.Tests.Visual.Settings
{
    public partial class TestSceneGuestPlayerSettings : OsuTestScene
    {
        [Cached]
        private readonly OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Purple);

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        [Resolved]
        private GuestUserAvatar guestAvatar { get; set; } = null!;

        private LocalUserState localUserState = null!;

        private string imagePath = null!;

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("create settings", () =>
            {
                Child = new PopoverContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = new GuestPlayerSettings
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Width = 400,
                    },
                };
            });

            AddStep("create local user state", () => localUserState = new LocalUserState(API, config));
            AddStep("reset configuration", () =>
            {
                guestAvatar.ClearAvatar();
                config.SetValue(OsuSetting.GuestUsername, GuestUser.DEFAULT_USERNAME);
            });
        }

        [Test]
        public void TestGuestUsernameFollowsConfiguration()
        {
            AddAssert("guest user by default", () => localUserState.User.Value.Username, () => Is.EqualTo(GuestUser.DEFAULT_USERNAME));

            AddStep("set username", () => config.SetValue(OsuSetting.GuestUsername, "practice enjoyer"));
            AddAssert("username applied", () => localUserState.User.Value.Username, () => Is.EqualTo("practice enjoyer"));

            AddStep("clear username", () => config.SetValue(OsuSetting.GuestUsername, string.Empty));
            AddAssert("fell back to default", () => localUserState.User.Value.Username, () => Is.EqualTo(GuestUser.DEFAULT_USERNAME));
        }

        [Test]
        public void TestSavedUsernameIsAppliedOnLaunch()
        {
            AddStep("save username", () => config.SetValue(OsuSetting.GuestUsername, "angel devil fan 67"));

            // As happens on launching the game, where the name has already been read from the configuration file.
            AddStep("create local user state", () => localUserState = new LocalUserState(API, config));

            AddAssert("saved username applied", () => localUserState.User.Value.Username, () => Is.EqualTo("angel devil fan 67"));
        }

        [Test]
        public void TestAvatarIsStoredAndCleared()
        {
            AddAssert("no avatar by default", () => guestAvatar.Texture.Value == null);

            AddStep("write an image", () =>
            {
                imagePath = Path.Combine(Path.GetTempPath(), $"guest-avatar-test-{System.Guid.NewGuid()}.png");
                File.WriteAllBytes(imagePath, single_pixel_png);
            });

            AddStep("set avatar", () => guestAvatar.SetAvatar(new FileInfo(imagePath)));
            AddUntilStep("avatar loaded", () => guestAvatar.Texture.Value != null);
            AddAssert("avatar copied into storage", () => guestAvatar.GetAvatarFile() != null);

            AddStep("delete the source image", () => File.Delete(imagePath));
            AddAssert("avatar still available", () => guestAvatar.GetAvatarFile() != null && guestAvatar.Texture.Value != null);

            AddStep("clear avatar", () => guestAvatar.ClearAvatar());
            AddAssert("avatar removed", () => guestAvatar.Texture.Value == null && guestAvatar.GetAvatarFile() == null);
        }

        // A 1x1 transparent PNG.
        private static readonly byte[] single_pixel_png = System.Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
    }
}
