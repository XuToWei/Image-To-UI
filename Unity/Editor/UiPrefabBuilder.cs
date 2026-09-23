using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AgentBridge;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ImageToUI.Editor
{
    public static class UiPrefabBuilder
    {
        public static object Build(JObject parameters)
        {
            if (parameters == null) throw new ArgumentException("Command parameters are required");
            var structurePath = InputPath(Required(parameters, "structurePath"));
            var assetsPaths = AssetRoots(parameters["assetsPath"]);
            var prefabPath = PrefabPath(Required(parameters, "prefabPath"));
            if (!File.Exists(structurePath)) throw new ArgumentException("Structure file not found: " + structurePath);
            foreach (var path in assetsPaths)
                if (!Directory.Exists(path)) throw new ArgumentException("Asset directory not found: " + path);
            var overwrite = UiStructureData.Bool(parameters, "overwrite");
            if (UiStructureData.Bool(parameters, "useDeviceSafeArea"))
                throw new ArgumentException("Device safe-area updates belong to application code; the exporter bakes canvas.safeArea.");
            var existing = AssetDatabase.LoadMainAssetAtPath(prefabPath);
            var prefabFull = InputPath(prefabPath);
            if (File.Exists(prefabFull) && !overwrite) throw new CommandException("UI_PREFAB_EXISTS", "Prefab exists; pass overwrite=true to replace it: " + prefabPath);
            if (existing != null && (!(existing is GameObject) || !PrefabUtility.IsPartOfPrefabAsset(existing))) throw new ArgumentException("Target is not a prefab asset");
            var json = File.ReadAllText(structurePath, Encoding.UTF8);
            var document = new UiStructureData(json);
            var warnings = new HashSet<string>(StringComparer.Ordinal);
            var candidates = document.AppearanceCandidates().ToList();
            var imageSources = ResolveImages(candidates, assetsPaths);
            var fontSources = ResolveFonts(candidates, parameters, structurePath, assetsPaths, warnings);
            var folder = Path.GetDirectoryName(prefabPath).Replace('\\', '/');
            EnsureFolder(folder);
            var resourceFolder = folder + "/" + Path.GetFileNameWithoutExtension(prefabPath) + "_Resources";
            // Lazy, immutable generated entries share a stable folder; rollback owns only new files.
            var resources = new UiPrefabAssets(resourceFolder) { DefaultFont = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") };
            var before = File.Exists(prefabFull) ? File.ReadAllBytes(prefabFull) : null;
            GameObject root = null;
            Scene preview = default;
            var saved = false;
            try
            {
                BakeResources(resources, imageSources, fontSources, candidates, warnings);
                preview = EditorSceneManager.NewPreviewScene();
                var assembler = new UiPrefabAssembler(document, resources, preview);
                root = assembler.Build(Path.GetFileNameWithoutExtension(prefabPath));
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out var success);
                if (!success || prefab == null) throw new InvalidOperationException("Unity did not save the prefab");
                AssetDatabase.SaveAssetIfDirty(prefab);
                saved = true;
                return new
                {
                    prefabPath,
                    guid = AssetDatabase.AssetPathToGUID(prefabPath),
                    resourceFolder = resources.ResourceFolder,
                    resourceUsage = resources.Usage,
                    elements = document.Order.Count,
                    images = assembler.ImageCount,
                    texts = assembler.TextCount,
                    states = document.Nodes.Count(n => n.Value["state"] != null),
                    progressBars = document.Nodes.Count(n => n.Value["progress"] != null),
                    scrollRegions = document.Nodes.Count(n => n.Value["scroll"] != null),
                    stateObjects = assembler.StateObjects.ToArray(),
                    overwritten = before != null,
                    undoable = false,
                    warnings = warnings.OrderBy(w => w, StringComparer.Ordinal).ToArray()
                };
            }
            catch
            {
                if (before != null)
                {
                    File.WriteAllBytes(prefabFull, before);
                    AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                }
                else if (File.Exists(prefabFull)) AssetDatabase.DeleteAsset(prefabPath);
                throw;
            }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                if (!saved) resources.Rollback();
            }
        }

        private static string[] AssetRoots(JToken token)
        {
            var values = token is JArray array ? array.ToArray() : new[] { token };
            if (values.Length == 0 || values.Any(v => v == null || v.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)v)))
                throw new ArgumentException("assetsPath must be a directory string or a non-empty array of directory strings");
            return values.Select(v => {
                var full = InputPath((string)v);
                return full.Length > Path.GetPathRoot(full).Length ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
            }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static Dictionary<string, string> ResolveImages(List<KeyValuePair<string, JObject>> candidates, IEnumerable<string> roots)
        {
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var relative = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots)
            {
                var prefixLength = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length + 1;
                foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories).Where(p => new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(p).ToLowerInvariant())))
                {
                    var full = Path.GetFullPath(file);
                    files.Add(full);
                    var key = full.Substring(prefixLength).Replace('\\', '/');
                    if (!relative.TryGetValue(key, out var matches)) relative[key] = matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    matches.Add(full);
                }
            }
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in candidates)
            {
                var key = UiStructureData.Text(candidate.Value, "asset");
                if (key.Length == 0 || result.ContainsKey(key)) continue;
                var normalized = key.Replace('\\', '/');
                var matches = !normalized.Contains("/")
                    ? files.Where(p => string.Equals(Path.GetFileName(p), normalized, StringComparison.OrdinalIgnoreCase)).ToList()
                    : relative.TryGetValue(normalized, out var paths) ? paths.ToList() : new List<string>();
                if (matches.Count == 0) throw new ArgumentException(candidate.Key + ": sprite not found in assetsPath directories: " + key);
                if (matches.Count > 1) throw new ArgumentException(candidate.Key + ": ambiguous sprite across assetsPath directories: " + key + ". Use a unique relative path or adjust the supplied directories.");
                result.Add(key, matches[0]);
            }
            return result;
        }

        private static Dictionary<string, string> ResolveFonts(List<KeyValuePair<string, JObject>> candidates, JObject parameters, string structure, string[] assets, HashSet<string> warnings)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var map = parameters["fontMap"] as JObject;
            if (parameters["fontMap"] != null && map == null) throw new ArgumentException("fontMap must be an object");
            var defaultFont = UiStructureData.Text(parameters, "defaultFontPath");
            if (defaultFont.Length > 0)
            {
                var path = InputPath(defaultFont);
                RequireFont(path); result[""] = path;
            }
            foreach (var candidate in candidates.Where(c => UiStructureData.Text(c.Value, "type") == "text"))
            {
                var key = UiStructureData.Text(candidate.Value, "fontFamily");
                if (result.ContainsKey(key)) continue;
                var requested = map?[key];
                if (requested != null)
                {
                    if (requested.Type != JTokenType.String) throw new ArgumentException("fontMap values must be file paths");
                    var path = InputPath((string)requested); RequireFont(path); result[key] = path; continue;
                }
                var choices = new List<string>();
                if (key.Length > 0)
                {
                    if (Path.IsPathRooted(key)) choices.Add(key);
                    else
                    {
                        choices.Add(InputPath(key));
                        foreach (var directory in new[] { Path.GetDirectoryName(structure), Path.GetDirectoryName(Path.GetDirectoryName(structure)) }
                            .Concat(assets.SelectMany(root => new[] { root, Path.GetDirectoryName(root) })).Distinct(StringComparer.OrdinalIgnoreCase))
                            if (directory != null) choices.Add(Path.Combine(directory, key));
                    }
                }
                var found = choices.FirstOrDefault(p => File.Exists(p) && new[] { ".ttf", ".otf" }.Contains(Path.GetExtension(p).ToLowerInvariant()));
                if (found != null) result[key] = Path.GetFullPath(found);
                else
                {
                    result[key] = result.TryGetValue("", out var fallback) ? fallback : null;
                    warnings.Add(candidate.Key + ": font '" + key + "' unavailable; using " + (result[key] ?? "Unity LegacyRuntime font") + ". Supply fontMap for an exact font.");
                }
            }
            return result;
        }

        private static void BakeResources(UiPrefabAssets resources, Dictionary<string, string> images,
            Dictionary<string, string> fonts, List<KeyValuePair<string, JObject>> candidates, HashSet<string> warnings)
        {
            var importedImages = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in images)
            {
                if (!importedImages.TryGetValue(pair.Value, out var sprite))
                {
                    sprite = resources.LoadSprite(pair.Value);
                    importedImages[pair.Value] = sprite;
                }
                resources.Sprites[pair.Key] = sprite;
            }
            var importedFonts = new Dictionary<string, Font>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in fonts)
            {
                if (pair.Value == null) continue;
                if (!importedFonts.TryGetValue(pair.Value, out var font))
                {
                    font = resources.LoadFont(pair.Value);
                    importedFonts[pair.Value] = font;
                }
                resources.Fonts[pair.Key] = font;
                if (pair.Key.Length == 0) resources.DefaultFont = font;
            }
            foreach (var candidate in candidates)
            {
                var slice = candidate.Value["nineSlice"];
                var key = UiStructureData.Text(candidate.Value, "asset");
                if (key.Length > 0 && (slice?.Type == JTokenType.Boolean && (bool)slice || slice?.Type == JTokenType.String) && resources.Sprites[key].border == Vector4.zero)
                    warnings.Add(candidate.Key + ": nine-slice margins inferred because source spriteBorder metadata is absent.");
            }
        }

        internal static Sprite ImportSprite(string path, Vector4 border)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 16384;
            var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect; importer.SetTextureSettings(settings);
            importer.spriteBorder = border; importer.SaveAndReimport();
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new ArgumentException("Unity could not import sprite: " + path);
            return sprite;
        }

        private static string Required(JObject obj, string key)
        { if (obj[key]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)obj[key])) throw new ArgumentException(key + " is required"); return (string)obj[key]; }
        private static string InputPath(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Directory.GetParent(Application.dataPath).FullName, path));
        private static string PrefabPath(string path)
        {
            var normalized = path.Replace('\\', '/');
            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal) || !normalized.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) || normalized.Split('/').Any(p => p == ".." || p == "." || p.Length == 0))
                throw new ArgumentException("prefabPath must be an Assets/ path ending in .prefab without traversal segments");
            var full = InputPath(normalized); var assets = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(assets, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("prefabPath escapes Assets");
            return normalized;
        }
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        private static void RequireFont(string path)
        { if (!File.Exists(path) || !new[] { ".ttf", ".otf" }.Contains(Path.GetExtension(path).ToLowerInvariant())) throw new ArgumentException("Font must be an existing .ttf/.otf file: " + path); }
        internal static Vector4 MetaBorder(string path)
        {
            if (!File.Exists(path)) return Vector4.zero;
            var match = Regex.Match(File.ReadAllText(path), @"spriteBorder:\s*\{x:\s*([^,]+),\s*y:\s*([^,]+),\s*z:\s*([^,]+),\s*w:\s*([^}]+)\}");
            if (!match.Success) return Vector4.zero;
            var values = Enumerable.Range(1, 4).Select(i => float.Parse(match.Groups[i].Value, CultureInfo.InvariantCulture)).ToArray();
            if (values.Any(v => float.IsNaN(v) || float.IsInfinity(v) || v < 0)) throw new ArgumentException("Invalid Unity spriteBorder metadata: " + path);
            return new Vector4(values[0], values[1], values[2], values[3]);
        }
    }
}
