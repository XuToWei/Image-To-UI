using System;
using System.IO;
using System.Linq;
using AgentBridge;
using ImageToUI.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ImageToUI.Tests
{
    public sealed class UiPrefabBuilderTests
    {
        private string m_Temporary;
        private string m_Output;

        [SetUp]
        public void Setup()
        {
            m_Temporary = Path.Combine(Path.GetTempPath(), "image-to-ui-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Temporary);
            m_Output = "Assets/ImageToUITests_" + Guid.NewGuid().ToString("N");
            var texture = new Texture2D(12, 8, TextureFormat.RGBA32, false);
            var pixels = Enumerable.Repeat(new Color(1, 0, 0, 1), 96).ToArray();
            pixels[0].a = .5f;
            texture.SetPixels(pixels); texture.Apply();
            File.WriteAllBytes(Path.Combine(m_Temporary, "icon.png"), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        [TearDown]
        public void Cleanup()
        {
            Assert.IsTrue(m_Output.StartsWith("Assets/ImageToUITests_", StringComparison.Ordinal));
            AssetDatabase.DeleteAsset(m_Output);
            if (Directory.Exists(m_Temporary))
            {
                Assert.IsTrue(Path.GetFullPath(m_Temporary).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase));
                Assert.IsTrue(Path.GetFileName(m_Temporary).StartsWith("image-to-ui-test-", StringComparison.Ordinal));
                Directory.Delete(m_Temporary, true);
            }
        }

        private static JObject Element(string name, string type, int x, int y, int width, int height)
        {
            return new JObject
            {
                ["type"] = type,
                ["name"] = name,
                ["position"] = new JObject { ["x"] = x, ["y"] = y },
                ["size"] = new JObject { ["width"] = width, ["height"] = height },
                ["anchor"] = new JObject { ["horizontal"] = "left", ["vertical"] = "top" }
            };
        }

        private static JObject Document(params JObject[] children)
        {
            var root = Element("root", "container", 0, 0, 200, 120);
            root["children"] = new JArray(children);
            return new JObject { ["canvas"] = new JObject { ["width"] = 200, ["height"] = 120 }, ["root"] = root };
        }

        private JObject Parameters(JObject document, string name = "View")
        {
            var path = Path.Combine(m_Temporary, name + ".json"); File.WriteAllText(path, document.ToString());
            return new JObject { ["structurePath"] = path, ["assetsPath"] = m_Temporary, ["prefabPath"] = m_Output + "/" + name + ".prefab" };
        }

        private static JObject Run(JObject parameters) => JObject.FromObject(new BuildUiPrefabHandler().ExecuteAsync(parameters).GetAwaiter().GetResult());
        private static Image OwnImage(Transform node) => node.Find("__ImageToUI_Image")?.GetComponent<Image>();
        private static Text OwnText(Transform node) => node.Find("__ImageToUI_Text").GetComponent<Text>();

        private static void AssertIndependent(GameObject prefab, string path)
        {
            foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                Assert.NotNull(component, "Missing script in " + path);
                Assert.IsFalse(component.GetType().Assembly.GetName().Name.StartsWith("ImageToUI", StringComparison.Ordinal), component.GetType().FullName);
            }
            var dependencies = AssetDatabase.GetDependencies(path, true);
            Assert.IsFalse(dependencies.Any(p => p.StartsWith("Packages/me.xw.imagetoui/", StringComparison.Ordinal)), string.Join("\n", dependencies));
            Assert.IsFalse(File.ReadAllText(path).Contains("m_StructureJson"));
        }

        [Test]
        public void HandlerIsDiscoveredAsAnEditorOnlyExportCommand()
        {
            CommandRegistry.Rebuild();
            var item = CommandRegistry.GetAll().Single(c => c.Command == "build_ui_prefab");
            Assert.IsFalse(item.BatchAllowed);
            CollectionAssert.AreEquivalent(new[] { "structurePath", "assetsPath", "prefabPath" }, ((JArray)item.ParamsSchema["required"]).Values<string>().ToArray());
            Assert.IsNull(item.ParamsSchema["properties"]["useDeviceSafeArea"]);
        }

        [Test]
        public void PersistentSpritesHueAndSceneIsolationNeedNoCustomRuntimeAssets()
        {
            var image = Element("image", "image", 10, 12, 60, 30);
            image["asset"] = "icon.png"; image["nineSlice"] = 2; image["hueShift"] = 120;
            var scene = SceneManager.GetActiveScene(); var dirty = scene.isDirty;
            var roots = scene.GetRootGameObjects().Select(o => o.GetInstanceID()).ToArray();
            var result = Run(Parameters(Document(image)));
            Assert.AreEqual(2, (int)result["elements"]);
            Assert.AreEqual(dirty, scene.isDirty);
            CollectionAssert.AreEquivalent(roots, scene.GetRootGameObjects().Select(o => o.GetInstanceID()).ToArray());
            var path = (string)result["prefabPath"];
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            AssertIndependent(prefab, path);
            Assert.NotNull(prefab.GetComponent<CanvasScaler>());
            var graphic = OwnImage(prefab.transform.Find("root/image"));
            Assert.IsTrue(AssetDatabase.Contains(graphic.sprite));
            Assert.AreEqual(new Vector4(2, 2, 2, 2), graphic.sprite.border);
            Assert.AreEqual(Image.Type.Sliced, graphic.type);
            Assert.IsNull(new SerializedObject(graphic).FindProperty("m_Material").objectReferenceValue);
            var decoded = new Texture2D(2, 2);
            try
            {
                ImageConversion.LoadImage(decoded, File.ReadAllBytes(AssetDatabase.GetAssetPath(graphic.sprite.texture)));
                var pixel = (Color32)decoded.GetPixel(0, 0);
                Assert.AreEqual(0, pixel.r); Assert.AreEqual(255, pixel.g); Assert.AreEqual(0, pixel.b);
                Assert.That(pixel.a, Is.InRange(127, 128));
            }
            finally { Object.DestroyImmediate(decoded); }
        }

        [Test]
        public void StatesAreCompleteNativeBranchesAndOnlyCurrentStateIsActive()
        {
            var label = Element("label", "text", 0, 0, 100, 24); label["text"] = "SELECT";
            var check = Element("check", "image", 75, 0, 24, 24); check["asset"] = "icon.png"; check["visible"] = false;
            var button = Element("mode", "button", 10, 10, 100, 24); button["children"] = new JArray(label, check);
            button["state"] = new JObject
            {
                ["current"] = "selected",
                ["variants"] = new JObject
                {
                    ["normal"] = new JObject(),
                    ["selected"] = new JObject
                    {
                        ["."] = new JObject { ["color"] = "#00FF00" },
                        ["label"] = new JObject { ["text"] = "SELECTED" },
                        ["check"] = new JObject { ["visible"] = true }
                    }
                }
            };
            var result = Run(Parameters(Document(button)));
            Assert.AreEqual(2, ((JArray)result["stateObjects"]).Count);
            var path = (string)result["prefabPath"];
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                AssertIndependent(root, path);
                var normal = root.transform.Find("root/mode/States/normal");
                var selected = root.transform.Find("root/mode/States/selected");
                Assert.IsFalse(normal.gameObject.activeSelf); Assert.IsTrue(selected.gameObject.activeSelf);
                Assert.IsNull(OwnImage(normal.Find("Content")));
                Assert.AreEqual(Color.green, OwnImage(selected.Find("Content")).color);
                Assert.AreEqual("SELECT", OwnText(normal.Find("Content/label")).text);
                Assert.AreEqual("SELECTED", OwnText(selected.Find("Content/label")).text);
                Assert.IsFalse(normal.Find("Content/check").gameObject.activeSelf);
                Assert.IsTrue(selected.Find("Content/check").gameObject.activeSelf);
                Assert.NotNull(normal.Find("Content").GetComponent<Button>());
                Assert.AreEqual(0, selected.Find("Content").GetComponent<Button>().onClick.GetPersistentEventCount());
                selected.gameObject.SetActive(false); normal.gameObject.SetActive(true);
                Canvas.ForceUpdateCanvases();
                Assert.IsTrue(normal.gameObject.activeSelf); Assert.IsFalse(selected.gameObject.activeSelf);
                Assert.IsTrue(normal.Find("Content").GetComponent<Button>().targetGraphic.raycastTarget);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [Test]
        public void InvisibleStateKeepsAnEmptySelectedBranchAndPreservesAlternativeArtwork()
        {
            var control = Element("mode", "rect", 0, 0, 30, 20); control["color"] = "#FFFFFF";
            control["state"] = new JObject
            {
                ["current"] = "hidden",
                ["variants"] = new JObject { ["normal"] = new JObject(), ["hidden"] = new JObject { ["."] = new JObject { ["visible"] = false } } }
            };
            var result = Run(Parameters(Document(control)));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>((string)result["prefabPath"]);
            var group = prefab.transform.Find("root/mode/States");
            Assert.IsTrue(group.Find("hidden").gameObject.activeSelf);
            Assert.IsFalse(group.Find("hidden/Content").gameObject.activeSelf);
            Assert.IsFalse(group.Find("normal").gameObject.activeSelf);
            Assert.IsTrue(group.Find("normal/Content").gameObject.activeSelf);
            Assert.NotNull(OwnImage(group.Find("normal/Content")));
        }

        [Test]
        public void NestedStatesBakeAncestorOverridesIntoEachAlternative()
        {
            var icon = Element("icon", "image", 0, 0, 12, 8); icon["asset"] = "icon.png";
            icon["state"] = new JObject
            {
                ["current"] = "normal",
                ["variants"] = new JObject
                {
                    ["normal"] = new JObject(),
                    ["blue"] = new JObject { ["."] = new JObject { ["color"] = "#0000FF" } }
                }
            };
            var outer = Element("outer", "container", 10, 10, 40, 30); outer["children"] = new JArray(icon);
            outer["state"] = new JObject
            {
                ["current"] = "selected",
                ["variants"] = new JObject
                {
                    ["normal"] = new JObject(),
                    ["selected"] = new JObject { ["icon"] = new JObject { ["color"] = "#00FF00" } }
                }
            };
            var result = Run(Parameters(Document(outer)));
            Assert.AreEqual(6, ((JArray)result["stateObjects"]).Count);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>((string)result["prefabPath"]);
            Assert.AreEqual(Color.white, OwnImage(prefab.transform.Find("root/outer/States/normal/Content/icon/States/normal/Content")).color);
            Assert.AreEqual(Color.green, OwnImage(prefab.transform.Find("root/outer/States/selected/Content/icon/States/normal/Content")).color);
            Assert.AreEqual(Color.blue, OwnImage(prefab.transform.Find("root/outer/States/selected/Content/icon/States/blue/Content")).color);
        }

        [TestCase("left-to-right", .25f)]
        [TestCase("right-to-left", .25f)]
        [TestCase("top-to-bottom", .25f)]
        [TestCase("bottom-to-top", .25f)]
        [TestCase("left-to-right", 0f)]
        [TestCase("left-to-right", 1f)]
        public void ProgressAndScrollAreBakedAsNativeComponents(string direction, float ratio)
        {
            var fill = Element("fill", "image", 0, 0, 80, 16); fill["asset"] = "icon.png";
            var label = Element("label", "text", 0, 0, 80, 16); label["text"] = "25%"; label["fontSize"] = 12;
            var bar = Element("bar", "container", 10, 10, 80, 16); bar["children"] = new JArray(fill, label);
            bar["progress"] = new JObject { ["value"] = ratio, ["fill"] = "fill", ["direction"] = direction, ["label"] = "label" };
            var item = Element("item", "rect", 0, 0, 80, 100); item["color"] = "#00FF00";
            var content = Element("content", "container", 0, 0, 80, 100); content["children"] = new JArray(item);
            var viewport = Element("viewport", "container", 10, 40, 80, 40); viewport["children"] = new JArray(content);
            viewport["scroll"] = new JObject { ["content"] = "content", ["direction"] = "vertical", ["offset"] = new JObject { ["x"] = 0, ["y"] = 20 } };
            var result = Run(Parameters(Document(bar, viewport)));
            var path = (string)result["prefabPath"];
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                SetCanvasSize(root, new Vector2(200, 120));
                AssertIndependent(root, path);
                var clip = (RectTransform)root.transform.Find("root/bar/fill/__ImageToUI_ProgressClip");
                Assert.NotNull(clip.GetComponent<RectMask2D>());
                Assert.AreEqual(ratio > 0, clip.gameObject.activeSelf);
                Assert.AreEqual((ratio * 100).ToString("F0") + "%", OwnText(root.transform.Find("root/bar/label")).text);
                if (ratio > 0)
                {
                    var visual = (RectTransform)clip.Find("Content/__ImageToUI_Image");
                    Assert.That(visual.rect.width, Is.EqualTo(80).Within(.01));
                    Assert.That(visual.rect.height, Is.EqualTo(16).Within(.01));
                    var horizontal = direction.EndsWith("right") || direction.EndsWith("left");
                    Assert.That(horizontal ? clip.rect.width : clip.rect.height, Is.EqualTo((horizontal ? 80 : 16) * ratio).Within(.01));
                }
                var scroll = root.transform.Find("root/viewport").GetComponent<ScrollRect>();
                Assert.NotNull(scroll.viewport.GetComponent<RectMask2D>());
                Assert.AreEqual(new Vector2(0, 20), scroll.content.anchoredPosition);
                scroll.content.anchoredPosition = new Vector2(0, 50);
                Assert.AreEqual(new Vector2(0, 50), scroll.content.anchoredPosition);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [Test]
        public void StateObjectPathsResolveAfterScrollAndProgressReparenting()
        {
            var fill = Element("fill", "image", 0, 0, 80, 16); fill["asset"] = "icon.png";
            fill["state"] = new JObject
            {
                ["current"] = "normal",
                ["variants"] = new JObject
                {
                    ["normal"] = new JObject(),
                    ["selected"] = new JObject { ["."] = new JObject { ["color"] = "#00FF00" } }
                }
            };
            var bar = Element("bar", "container", 0, 0, 80, 16); bar["children"] = new JArray(fill);
            bar["progress"] = new JObject { ["fill"] = "fill", ["value"] = .5, ["direction"] = "left-to-right" };
            var content = Element("content", "container", 0, 0, 80, 100); content["children"] = new JArray(bar);
            var viewport = Element("viewport", "container", 10, 10, 80, 40); viewport["children"] = new JArray(content);
            viewport["scroll"] = new JObject { ["content"] = "content", ["direction"] = "vertical" };
            var result = Run(Parameters(Document(viewport)));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>((string)result["prefabPath"]);
            var entries = (JArray)result["stateObjects"];
            Assert.AreEqual(2, entries.Count);
            foreach (var entry in entries)
            {
                var path = (string)entry["objectPath"];
                StringAssert.Contains("__ImageToUI_Viewport/content", path);
                StringAssert.Contains("__ImageToUI_ProgressClip/Content/States", path);
                var branch = prefab.transform.Find(path);
                Assert.NotNull(branch, path);
                Assert.AreEqual((bool)entry["activeSelf"], branch.gameObject.activeSelf);
                Assert.IsNull(entry["target"]);
            }
        }

        [Test]
        public void NativeAnchorsPreserveMarginsAndLayoutAfterResize()
        {
            var right = Element("right", "rect", 160, 90, 30, 20); right["color"] = "#FFFFFF";
            right["anchor"] = new JObject { ["horizontal"] = "right", ["vertical"] = "bottom" };
            var stretch = Element("stretch", "rect", 10, 10, 180, 20); stretch["color"] = "#FFFFFF";
            stretch["responsive"] = new JObject
            {
                ["min"] = new JObject { ["x"] = 0, ["y"] = 0 },
                ["max"] = new JObject { ["x"] = 1, ["y"] = 0 },
                ["offsetMin"] = new JObject { ["x"] = 10, ["y"] = 10 },
                ["offsetMax"] = new JObject { ["x"] = -10, ["y"] = 30 }
            };
            var result = Run(Parameters(Document(right, stretch)));
            var root = PrefabUtility.LoadPrefabContents((string)result["prefabPath"]);
            try
            {
                SetCanvasSize(root, new Vector2(200, 120));
                Assert.AreEqual(new Rect(160, 90, 30, 20), Box((RectTransform)root.transform.Find("root/right")));
                SetCanvasSize(root, new Vector2(300, 180));
                Assert.AreEqual(new Rect(260, 150, 30, 20), Box((RectTransform)root.transform.Find("root/right")));
                Assert.AreEqual(new Rect(10, 10, 280, 20), Box((RectTransform)root.transform.Find("root/stretch")));
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [Test]
        public void EvenLayoutKeepsHiddenSlotsAndUsesNativeAnchorFractions()
        {
            var a = Element("a", "rect", 0, 0, 21, 20); a["color"] = "#FFFFFF"; a["visible"] = false; a.Remove("position");
            var b = (JObject)a.DeepClone(); b["name"] = "b"; b["visible"] = true;
            var document = Document(a, b); document["root"]["layout"] = new JObject { ["type"] = "row", ["spacing"] = "even", ["vAlign"] = "middle" };
            var result = Run(Parameters(document));
            var root = PrefabUtility.LoadPrefabContents((string)result["prefabPath"]);
            try
            {
                SetCanvasSize(root, new Vector2(200, 120));
                Assert.That(Box((RectTransform)root.transform.Find("root/a")).x, Is.EqualTo(52).Within(.001));
                Assert.That(Box((RectTransform)root.transform.Find("root/b")).x, Is.EqualTo(126).Within(.001));
                Assert.IsFalse(root.transform.Find("root/a").gameObject.activeSelf);
                SetCanvasSize(root, new Vector2(300, 120));
                Assert.That(Box((RectTransform)root.transform.Find("root/a")).x, Is.EqualTo(85).Within(1));
                Assert.That(Box((RectTransform)root.transform.Find("root/b")).x, Is.EqualTo(193).Within(1));
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [Test]
        public void MissingInactiveStateSpriteFailsBeforeCreatingAssets()
        {
            var image = Element("image", "image", 0, 0, 12, 8); image["asset"] = "icon.png";
            image["state"] = new JObject { ["current"] = "normal", ["variants"] = new JObject { ["normal"] = new JObject(), ["selected"] = new JObject { ["."] = new JObject { ["asset"] = "missing.png" } } } };
            Assert.Throws<CommandException>(() => Run(Parameters(Document(image))));
            Assert.IsFalse(AssetDatabase.IsValidFolder(m_Output));
        }

        [Test]
        public void ExistingPrefabRequiresOverwriteAndKeepsGuid()
        {
            var item = Element("item", "rect", 0, 0, 20, 20); item["color"] = "#FFFFFF";
            var parameters = Parameters(Document(item)); var first = Run(parameters);
            Assert.AreEqual("UI_PREFAB_EXISTS", Assert.Throws<CommandException>(() => Run(parameters)).Code);
            parameters["overwrite"] = true; var second = Run(parameters);
            Assert.AreEqual((string)first["guid"], (string)second["guid"]);
            Assert.IsTrue((bool)second["overwritten"]);
        }

        [Test]
        public void InvalidOutputAndRuntimeSafeAreaRequestAreRejected()
        {
            var item = Element("item", "rect", 0, 0, 20, 20); item["color"] = "#FFFFFF";
            var parameters = Parameters(Document(item)); parameters["prefabPath"] = "Assets/../Outside.prefab";
            Assert.Throws<CommandException>(() => Run(parameters));
            parameters = Parameters(Document(item)); parameters["useDeviceSafeArea"] = true;
            Assert.Throws<CommandException>(() => Run(parameters));
            Assert.IsFalse(AssetDatabase.IsValidFolder(m_Output));
        }

        [Test]
        public void RepositoryEmberfallFixtureBuildsWithoutExporterDependencies()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(BuildUiPrefabHandler).Assembly);
            var repository = Directory.GetParent(package.resolvedPath).FullName;
            var structure = Path.Combine(repository, "test/output/ui_structure.json");
            if (!File.Exists(structure)) Assert.Ignore("The full repository fixture is not installed with this package.");
            var result = Run(new JObject
            {
                ["structurePath"] = structure,
                ["assetsPath"] = Path.Combine(repository, "test/source/sprites"),
                ["prefabPath"] = m_Output + "/Emberfall.prefab"
            });
            Assert.AreEqual(71, (int)result["elements"]); Assert.AreEqual(42, (int)result["images"]); Assert.AreEqual(3, (int)result["texts"]);
            var path = (string)result["prefabPath"];
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            AssertIndependent(prefab, path);
            foreach (var image in prefab.GetComponentsInChildren<Image>(true)) Assert.IsTrue(AssetDatabase.Contains(image.sprite), image.name);
            foreach (var text in prefab.GetComponentsInChildren<Text>(true)) Assert.NotNull(text.font, text.name);
        }

        private static void SetCanvasSize(GameObject root, Vector2 size)
        {
            root.GetComponent<CanvasScaler>().enabled = false;
            root.GetComponent<Canvas>().enabled = false;
            ((RectTransform)root.transform).sizeDelta = size;
        }

        private static Rect Box(RectTransform node)
        {
            var corners = new Vector3[4]; node.GetWorldCorners(corners);
            var parent = (RectTransform)node.parent;
            var topLeft = parent.InverseTransformPoint(corners[1]);
            return new Rect(topLeft.x - parent.rect.xMin, parent.rect.yMax - topLeft.y, node.rect.width, node.rect.height);
        }
    }
}
