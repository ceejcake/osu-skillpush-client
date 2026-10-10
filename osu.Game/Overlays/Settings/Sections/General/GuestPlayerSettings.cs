// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Users;
using osu.Game.Utils;
using osuTK;

namespace osu.Game.Overlays.Settings.Sections.General
{
    /// <summary>
    /// Settings for the name and avatar shown when playing without an osu! account.
    /// </summary>
    public partial class GuestPlayerSettings : SettingsSubsection
    {
        protected override LocalisableString Header => "Guest player";

        [Resolved]
        private GuestUserAvatar guestAvatar { get; set; } = null!;

        private FormFileSelector avatarSelector = null!;
        private Sprite avatarPreview = null!;

        private Texture? defaultAvatar;

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config, LargeTextureStore textures)
        {
            defaultAvatar = textures.Get(@"Online/avatar-guest");

            Children = new Drawable[]
            {
                new SettingsTextBox
                {
                    LabelText = "Guest username",
                    TooltipText = "The name shown in game and stored with local scores while logged out.",
                    Current = config.GetBindable<string>(OsuSetting.GuestUsername),
                },
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Padding = new MarginPadding { Horizontal = SettingsPanel.CONTENT_MARGINS, Vertical = 10 },
                    Child = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(10),
                        Children = new Drawable[]
                        {
                            new Container
                            {
                                Size = new Vector2(72),
                                Masking = true,
                                CornerRadius = 6,
                                Child = avatarPreview = new Sprite
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    FillMode = FillMode.Fit,
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                },
                            },
                            avatarSelector = new FormFileSelector(SupportedExtensions.IMAGE_EXTENSIONS)
                            {
                                Width = 0.7f,
                                RelativeSizeAxes = Axes.X,
                                Caption = "Profile picture",
                                PlaceholderText = "Click to select an image",
                                AllowClear = true,
                            },
                        }
                    }
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            guestAvatar.Texture.BindValueChanged(texture => avatarPreview.Texture = texture.NewValue ?? defaultAvatar, true);

            avatarSelector.Current.Value = guestAvatar.GetAvatarFile();
            avatarSelector.Current.BindValueChanged(file => updateAvatar(file.NewValue));
        }

        private void updateAvatar(FileInfo? file)
        {
            if (file == null)
                guestAvatar.ClearAvatar();
            else
                guestAvatar.SetAvatar(file);
        }
    }
}
