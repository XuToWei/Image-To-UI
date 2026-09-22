using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ImageToUI.Editor
{
    /// <summary>Editor-only cache. Every returned object is an ordinary persistent Unity asset.</summary>
    internal sealed class UiPrefabAssets
    {
        public readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, Font> Fonts = new Dictionary<string, Font>(StringComparer.OrdinalIgnoreCase);
        public Font DefaultFont;
        private readonly string m_Folder;
        private readonly Dictionary<string, Sprite> m_Variants = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        public UiPrefabAssets(string folder) { m_Folder = folder; }

        public Font GetFont(string key) => Fonts.TryGetValue(key, out var font) ? font : DefaultFont;

        public Sprite GetSprite(string key, JToken slice, float hue)
        {
            var source = Sprites[key];
            if (!Mathf.Approximately(hue % 360, 0)) source = BakeHue(key, source, hue);
            if (!UiStructureData.HasSlice(slice)) return source;
            Vector4 border;
            if (slice.Type == JTokenType.Boolean || slice.Type == JTokenType.String)
            {
                border = source.border;
                if (border == Vector4.zero)
                    border = Vector4.one * Mathf.Max(4, Mathf.Min(60, Mathf.FloorToInt(Mathf.Min(source.rect.width, source.rect.height) / 4)));
            }
            else if (slice is JObject obj)
                border = new Vector4((float)UiStructureData.Number(obj, "left"), (float)UiStructureData.Number(obj, "bottom"),
                    (float)UiStructureData.Number(obj, "right"), (float)UiStructureData.Number(obj, "top"));
            else border = Vector4.one * (float)slice;
            border.x = Mathf.Min(border.x, source.rect.width - 1);
            border.z = Mathf.Min(border.z, source.rect.width - 1 - border.x);
            border.y = Mathf.Min(border.y, source.rect.height - 1);
            border.w = Mathf.Min(border.w, source.rect.height - 1 - border.y);
            if (source.border == border) return source;
            var cacheKey = key + "|" + hue.ToString("R", CultureInfo.InvariantCulture) + "|" + border.ToString("R");
            if (m_Variants.TryGetValue(cacheKey, out var cached)) return cached;
            var sprite = Sprite.Create(source.texture, source.rect, new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, border);
            sprite.name = source.name + "_Sliced";
            var path = AssetDatabase.GenerateUniqueAssetPath(m_Folder + "/Sprites/" + sprite.name + ".asset");
            AssetDatabase.CreateAsset(sprite, path);
            AssetDatabase.SaveAssetIfDirty(sprite);
            m_Variants[cacheKey] = sprite;
            return sprite;
        }

        private Sprite BakeHue(string key, Sprite source, float degrees)
        {
            var cacheKey = key + "|hue=" + degrees.ToString("R", CultureInfo.InvariantCulture);
            if (m_Variants.TryGetValue(cacheKey, out var cached)) return cached;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            try
            {
                var sourcePath = AssetDatabase.GetAssetPath(source.texture);
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(DiskPath(sourcePath))))
                    throw new ArgumentException("Could not decode sprite for hue baking: " + key);
                var pixels = texture.GetPixels32();
                for (var i = 0; i < pixels.Length; i++)
                {
                    var alpha = pixels[i].a;
                    Color.RGBToHSV(pixels[i], out var h, out var s, out var v);
                    var shifted = (Color32)Color.HSVToRGB(Mathf.Repeat(h + degrees / 360, 1), s, v);
                    shifted.a = alpha;
                    pixels[i] = shifted;
                }
                texture.SetPixels32(pixels); texture.Apply();
                var path = AssetDatabase.GenerateUniqueAssetPath(m_Folder + "/Sprites/" + source.name + "_Hue.png");
                File.WriteAllBytes(DiskPath(path), texture.EncodeToPNG());
                var sprite = UiPrefabBuilder.ImportSprite(path, source.border);
                m_Variants[cacheKey] = sprite;
                return sprite;
            }
            finally { Object.DestroyImmediate(texture); }
        }

        private static string DiskPath(string assetPath) => Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);
    }
}
