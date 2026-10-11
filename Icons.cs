using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace ForagersGarden
{
    /// <summary>
    /// Item icons embedded from Icons/*.png (the spores). Same loading as StoreAndCraft's UI sprites: PNG through
    /// ImageConversion (by reflection, it lives in a module net472 cannot reference), near-black made transparent.
    /// </summary>
    internal static class Icons
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        private static MethodInfo _loadImage;
        private static bool _tried;
        private static bool _loadResolved;

        public static Sprite Get(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return null;
            EnsureLoaded();
            Sprite cached;
            return Cache.TryGetValue(fileName.ToLowerInvariant(), out cached) ? cached : null;
        }

        private static void EnsureLoaded()
        {
            if (_tried)
                return;
            _tried = true;
            Assembly asm = typeof(Icons).Assembly;
            foreach (string res in asm.GetManifestResourceNames())
            {
                if (res == null || !res.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                string[] parts = res.Split('.');
                string file = (parts.Length >= 2 ? parts[parts.Length - 2] + ".png" : res).ToLowerInvariant();
                try
                {
                    using (Stream stream = asm.GetManifestResourceStream(res))
                    {
                        if (stream == null)
                            continue;
                        byte[] bytes;
                        using (var ms = new MemoryStream())
                        {
                            stream.CopyTo(ms);
                            bytes = ms.ToArray();
                        }
                        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (!LoadPng(tex, bytes))
                            continue;
                        tex.wrapMode = TextureWrapMode.Clamp;
                        tex.filterMode = FilterMode.Bilinear;
                        MakeNearBlackTransparent(tex);
                        Cache[file] = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                    }
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogWarning("Icon " + res + ": " + ex.Message);
                }
            }
        }

        private static bool LoadPng(Texture2D tex, byte[] bytes)
        {
            if (!_loadResolved)
            {
                _loadResolved = true;
                System.Type t = System.Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                if (t != null)
                    _loadImage = t.GetMethod("LoadImage", BindingFlags.Public | BindingFlags.Static, null,
                        new[] { typeof(Texture2D), typeof(byte[]) }, null);
                if (_loadImage == null)
                    _loadImage = typeof(Texture2D).GetMethod("LoadImage", BindingFlags.Public | BindingFlags.Instance, null,
                        new[] { typeof(byte[]) }, null);
            }
            if (_loadImage == null)
                return false;
            try
            {
                if (_loadImage.IsStatic)
                    return (bool)_loadImage.Invoke(null, new object[] { tex, bytes });
                return (bool)_loadImage.Invoke(tex, new object[] { bytes });
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("PNG load failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>PSD exports often use solid black instead of alpha.</summary>
        private static void MakeNearBlackTransparent(Texture2D tex)
        {
            Color32[] pixels = tex.GetPixels32();
            bool changed = false;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                if (p.a > 0 && p.r <= 6 && p.g <= 6 && p.b <= 6)
                {
                    p.a = 0;
                    pixels[i] = p;
                    changed = true;
                }
            }
            if (changed)
            {
                tex.SetPixels32(pixels);
                tex.Apply(false, false);
            }
        }
    }
}
