// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using osu.Framework.Bindables;

namespace osu.Game.Skinning
{
    /// <summary>
    /// Remembers the gameplay cursor size separately for each skin, restoring it whenever that skin is switched back to.
    /// </summary>
    /// <remarks>
    /// A skin which has never had a size chosen for it starts from whatever size was in use when it was first switched to.
    /// </remarks>
    public sealed class SkinCursorSizeMemory : IDisposable
    {
        private readonly IBindable<string> skin;
        private readonly Bindable<float> cursorSize;
        private readonly Bindable<string> saved;

        private readonly Dictionary<string, float> sizes;

        /// <param name="skin">The ID of the skin in use.</param>
        /// <param name="cursorSize">The gameplay cursor size.</param>
        /// <param name="saved">Where the size of each skin is kept between sessions.</param>
        public SkinCursorSizeMemory(IBindable<string> skin, Bindable<float> cursorSize, Bindable<string> saved)
        {
            this.skin = skin.GetBoundCopy();
            this.cursorSize = cursorSize.GetBoundCopy();
            this.saved = saved.GetBoundCopy();

            sizes = read(saved.Value);

            // The size in use at startup is the one last used with the skin in use at startup.
            remember();

            this.skin.BindValueChanged(_ => skinChanged());
            this.cursorSize.BindValueChanged(_ => remember());
        }

        private void skinChanged()
        {
            if (sizes.TryGetValue(skin.Value, out float size))
                cursorSize.Value = size;
            else
                remember();
        }

        private void remember()
        {
            if (string.IsNullOrEmpty(skin.Value))
                return;

            if (sizes.TryGetValue(skin.Value, out float existing) && existing == cursorSize.Value)
                return;

            sizes[skin.Value] = cursorSize.Value;
            saved.Value = JsonConvert.SerializeObject(sizes);
        }

        private static Dictionary<string, float> read(string? json)
        {
            if (string.IsNullOrEmpty(json))
                return new Dictionary<string, float>();

            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, float>>(json) ?? new Dictionary<string, float>();
            }
            catch (JsonException)
            {
                return new Dictionary<string, float>();
            }
        }

        public void Dispose()
        {
            skin.UnbindAll();
            cursorSize.UnbindAll();
            saved.UnbindAll();
        }
    }
}
