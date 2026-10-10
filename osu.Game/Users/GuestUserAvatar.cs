// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Configuration;

namespace osu.Game.Users
{
    /// <summary>
    /// Provides the avatar the local user has chosen for offline (guest) play.
    /// </summary>
    /// <remarks>
    /// The chosen image is copied into a folder of its own inside the osu! storage, so that it stays available
    /// even if the original file is moved or deleted.
    /// </remarks>
    [Cached]
    public partial class GuestUserAvatar : Component
    {
        public const string STORAGE_DIRECTORY = @"guest";

        /// <summary>
        /// The avatar to display for the guest user, or <see langword="null"/> if none has been chosen.
        /// </summary>
        public IBindable<Texture?> Texture => texture;

        private readonly Bindable<Texture?> texture = new Bindable<Texture?>();

        private readonly Bindable<string> configuredFile = new Bindable<string>();

        private Storage storage = null!;
        private TextureStore textureStore = null!;

        [BackgroundDependencyLoader]
        private void load(Storage gameStorage, OsuConfigManager config, GameHost host, IRenderer renderer)
        {
            storage = gameStorage.GetStorageForDirectory(STORAGE_DIRECTORY);
            textureStore = new TextureStore(renderer, host.CreateTextureLoaderStore(new StorageBackedResourceStore(storage)));

            config.BindWith(OsuSetting.GuestAvatarFile, configuredFile);
            configuredFile.BindValueChanged(_ => updateTexture(), true);
        }

        /// <summary>
        /// Adopts the given image file as the guest avatar, copying it into osu!'s storage.
        /// </summary>
        public void SetAvatar(FileInfo file)
        {
            // A new name is used for every image so that the old one is never served from the texture cache.
            string filename = $"{Guid.NewGuid()}{file.Extension}";

            try
            {
                using (var source = file.OpenRead())
                using (var destination = storage.CreateFileSafely(filename))
                    source.CopyTo(destination);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to set the guest avatar.");
                return;
            }

            string previousFilename = configuredFile.Value;

            configuredFile.Value = filename;

            deleteFile(previousFilename);
        }

        /// <summary>
        /// Removes the chosen guest avatar, falling back to osu!'s default.
        /// </summary>
        public void ClearAvatar()
        {
            string previousFilename = configuredFile.Value;

            configuredFile.Value = string.Empty;

            deleteFile(previousFilename);
        }

        /// <summary>
        /// The file currently in use as the guest avatar, if any.
        /// </summary>
        public FileInfo? GetAvatarFile()
        {
            if (string.IsNullOrEmpty(configuredFile.Value) || !storage.Exists(configuredFile.Value))
                return null;

            return new FileInfo(storage.GetFullPath(configuredFile.Value));
        }

        private void updateTexture()
        {
            if (string.IsNullOrEmpty(configuredFile.Value) || !storage.Exists(configuredFile.Value))
            {
                texture.Value = null;
                return;
            }

            texture.Value = textureStore.Get(configuredFile.Value);
        }

        private void deleteFile(string filename)
        {
            if (string.IsNullOrEmpty(filename) || filename == configuredFile.Value)
                return;

            try
            {
                if (storage.Exists(filename))
                    storage.Delete(filename);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to remove the previous guest avatar.");
            }
        }
    }
}
