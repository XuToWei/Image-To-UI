using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ImageToUI.Editor
{
    /// <summary>References project assets; imports and derives immutable content-keyed assets only as needed.</summary>
    internal sealed class UiPrefabAssets
    {
        public readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, Font> Fonts = new Dictionary<string, Font>(StringComparer.OrdinalIgnoreCase);
        public Font DefaultFont;
        private readonly string m_Folder;
        private readonly Dictionary<string, Sprite> m_Variants = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private readonly List<string> m_CreatedAssets = new List<string>();
        private readonly List<string> m_CreatedFolders = new List<string>();
        private bool m_UsesGeneratedAssets;
        private int m_ReferencedImages, m_ReferencedFonts, m_CopiedImages, m_CopiedFonts, m_GeneratedSprites, m_BakedTextures, m_ReusedAssets;

        public UiPrefabAssets(string folder) { m_Folder = folder; }
        public string ResourceFolder => m_UsesGeneratedAssets ? m_Folder : null;
        public object Usage => new {
            referencedImages = m_ReferencedImages, referencedFonts = m_ReferencedFonts,
            copiedImages = m_CopiedImages, copiedFonts = m_CopiedFonts,
            generatedSprites = m_GeneratedSprites, bakedTextures = m_BakedTextures,
            reusedGeneratedAssets = m_ReusedAssets
        };
        public Font GetFont(string key) => Fonts.TryGetValue(key, out var font) ? font : DefaultFont;

        public Sprite LoadSprite(string sourcePath)
        {
            var projectPath = ProjectPath(sourcePath);
            if (projectPath != null)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(projectPath);
                if (texture == null)
                {
                    AssetDatabase.ImportAsset(projectPath, ImportAssetOptions.ForceSynchronousImport);
                    texture = AssetDatabase.LoadAssetAtPath<Texture2D>(projectPath);
                }
                if (texture == null) throw new ArgumentException("Unity could not load project texture: " + projectPath);
                var rect = new Rect(0, 0, texture.width, texture.height);
                // A filename in this schema denotes the entire image, not the first atlas sub-sprite.
                var sprite = AssetDatabase.LoadAllAssetsAtPath(projectPath).OfType<Sprite>()
                    .FirstOrDefault(s => s.texture == texture && s.rect == rect);
                m_ReferencedImages++;
                if (sprite != null) return sprite;
                return SpriteAsset(texture, rect, new Vector2(.5f, .5f), 100,
                    UiPrefabBuilder.MetaBorder(sourcePath + ".meta"), "full-image-v2|" + Identity(texture));
            }
            var border = UiPrefabBuilder.MetaBorder(sourcePath + ".meta");
            var bytes = File.ReadAllBytes(sourcePath);
            var path = GeneratedPath("Sprites", "image_" + Hash("image-v2|" + BytesHash(bytes) + "|" + Values(border)) + Path.GetExtension(sourcePath).ToLowerInvariant());
            var cached = Existing<Sprite>(path);
            if (cached != null) return cached;
            m_CreatedAssets.Add(path);
            File.WriteAllBytes(DiskPath(path), bytes);
            var imported = UiPrefabBuilder.ImportSprite(path, border);
            m_CopiedImages++;
            return imported;
        }

        public Font LoadFont(string sourcePath)
        {
            var projectPath = ProjectPath(sourcePath);
            if (projectPath != null)
            {
                var font = AssetDatabase.LoadAssetAtPath<Font>(projectPath);
                if (font == null)
                {
                    AssetDatabase.ImportAsset(projectPath, ImportAssetOptions.ForceSynchronousImport);
                    font = AssetDatabase.LoadAssetAtPath<Font>(projectPath);
                }
                if (font == null) throw new ArgumentException("Unity could not load project font: " + projectPath);
                m_ReferencedFonts++;
                return font;
            }
            var bytes = File.ReadAllBytes(sourcePath);
            var path = GeneratedPath("Fonts", "font_" + Hash("font-v2|" + BytesHash(bytes)) + Path.GetExtension(sourcePath).ToLowerInvariant());
            var cached = Existing<Font>(path);
            if (cached != null) return cached;
            m_CreatedAssets.Add(path);
            File.WriteAllBytes(DiskPath(path), bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imported = AssetDatabase.LoadAssetAtPath<Font>(path);
            if (imported == null) throw new ArgumentException("Unity could not import font: " + sourcePath);
            m_CopiedFonts++;
            return imported;
        }

        public Sprite GetSprite(string key, JToken slice, float hue)
        {
            var source = Sprites[key];
            if (!Mathf.Approximately(hue % 360, 0)) source = BakeHue(source, Mathf.Repeat(hue, 360));
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
            return SpriteAsset(source.texture, source.rect, Pivot(source), source.pixelsPerUnit, border, "slice-v2|" + Identity(source));
        }

        private Sprite SpriteAsset(Texture2D texture, Rect rect, Vector2 pivot, float ppu, Vector4 border, string sourceKey)
        {
            var signature = sourceKey + "|" + Values(new Vector4(rect.x, rect.y, rect.width, rect.height)) + "|" + Values(new Vector4(pivot.x, pivot.y, ppu, 0)) + "|" + Values(border);
            if (m_Variants.TryGetValue(signature, out var found)) return found;
            var path = GeneratedPath("Sprites", "sprite_" + Hash(signature) + ".asset");
            var cached = Existing<Sprite>(path);
            if (cached != null) { m_Variants[signature] = cached; return cached; }
            var sprite = Sprite.Create(texture, rect, pivot, ppu, 0, SpriteMeshType.FullRect, border);
            if (sprite == null) throw new ArgumentException("Could not create sprite: " + path);
            sprite.name = texture.name + "_Sprite";
            m_CreatedAssets.Add(path);
            try
            {
                AssetDatabase.CreateAsset(sprite, path);
                AssetDatabase.SaveAssetIfDirty(sprite);
            }
            catch { if (!AssetDatabase.Contains(sprite)) Object.DestroyImmediate(sprite); throw; }
            m_GeneratedSprites++;
            m_Variants[signature] = sprite;
            return sprite;
        }

        private Sprite BakeHue(Sprite source, float degrees)
        {
            var signature = "hue-v2|" + Identity(source) + "|" + degrees.ToString("R", CultureInfo.InvariantCulture);
            if (m_Variants.TryGetValue(signature, out var found)) return found;
            var path = GeneratedPath("Sprites", "hue_" + Hash(signature) + ".png");
            var cached = Existing<Sprite>(path);
            if (cached != null) { m_Variants[signature] = cached; return cached; }
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            try
            {
                var sourcePath = AssetDatabase.GetAssetPath(source.texture);
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(DiskPath(sourcePath))))
                    throw new ArgumentException("Could not decode sprite for hue baking: " + sourcePath);
                var pixels = texture.GetPixels32();
                for (var i = 0; i < pixels.Length; i++)
                {
                    var alpha = pixels[i].a;
                    Color.RGBToHSV(pixels[i], out var h, out var s, out var v);
                    var shifted = (Color32)Color.HSVToRGB(Mathf.Repeat(h + degrees / 360, 1), s, v);
                    shifted.a = alpha; pixels[i] = shifted;
                }
                texture.SetPixels32(pixels); texture.Apply();
                m_CreatedAssets.Add(path);
                File.WriteAllBytes(DiskPath(path), texture.EncodeToPNG());
                var sprite = UiPrefabBuilder.ImportSprite(path, source.border);
                m_BakedTextures++;
                m_Variants[signature] = sprite;
                return sprite;
            }
            finally { Object.DestroyImmediate(texture); }
        }

        public void Rollback()
        {
            // Never delete an old cache entry, user source, or pre-existing resource folder.
            for (var i = m_CreatedAssets.Count - 1; i >= 0; i--) AssetDatabase.DeleteAsset(m_CreatedAssets[i]);
            for (var i = m_CreatedFolders.Count - 1; i >= 0; i--)
                if (Directory.Exists(DiskPath(m_CreatedFolders[i])) && Directory.GetFileSystemEntries(DiskPath(m_CreatedFolders[i])).Length == 0)
                    AssetDatabase.DeleteAsset(m_CreatedFolders[i]);
        }

        private string GeneratedPath(string category, string file)
        {
            m_UsesGeneratedAssets = true;
            var folder = m_Folder + "/" + category; EnsureFolder(folder);
            return folder + "/" + file;
        }
        private void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            if (File.Exists(DiskPath(path))) throw new ArgumentException("Resource folder path is occupied by a file: " + path);
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            var guid = AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
            if (string.IsNullOrEmpty(guid) || !AssetDatabase.IsValidFolder(path)) throw new IOException("Could not create resource folder: " + path);
            m_CreatedFolders.Add(path);
        }
        private T Existing<T>(string path) where T : Object
        {
            if (!File.Exists(DiskPath(path))) return null;
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new ArgumentException("Generated asset cache has incompatible content: " + path);
            m_ReusedAssets++;
            return asset;
        }
        private static string ProjectPath(string source)
        {
            var root = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(source);
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return "Assets/" + full.Substring(root.Length).Replace('\\', '/');
            foreach (var package in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().OrderByDescending(p => p.resolvedPath?.Length ?? 0))
            {
                if (string.IsNullOrEmpty(package.resolvedPath)) continue;
                var packageRoot = Path.GetFullPath(package.resolvedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (full.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase))
                    return package.assetPath + "/" + full.Substring(packageRoot.Length).Replace('\\', '/');
            }
            return null;
        }
        private static string Identity(Object asset)
        {
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId)) throw new ArgumentException("Asset is not persistent: " + asset.name);
            return guid + ":" + localId.ToString(CultureInfo.InvariantCulture) + ":" + AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(asset));
        }
        private static Vector2 Pivot(Sprite sprite) => new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height);
        private static string Values(Vector4 value) => string.Join(",", new[] { value.x, value.y, value.z, value.w }.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
        private static string Hash(string value)
        { using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Substring(0, 24); }
        private static string BytesHash(byte[] bytes)
        { using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(bytes)); }
        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "");
        private static string DiskPath(string assetPath) => Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);
    }
}
