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
        private static Image OwnImage(Transform node) => node.GetComponent<Image>();
        private static Text OwnText(Transform node) => node.GetComponent<Text>();

        private static void AssertIndependent(GameObject prefab, string path)
        {
            Assert.IsEmpty(prefab.GetComponentsInChildren<LayoutElement>(true), "Export must not add LayoutElement");
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
        public void OrdinaryGraphicsAndButtonUseTheirAuthoredObjects()
        {
            var image = Element("artwork", "image", 0, 0, 60, 30); image["asset"] = "icon.png";
            var label = Element("label", "text", 0, 0, 60, 20); label["text"] = "Label";
            var button = Element("button", "button", 0, 40, 60, 30); button["asset"] = "icon.png";
            var emptyButton = Element("empty", "button", 80, 40, 60, 30);
            var result = Run(Parameters(Document(image, label, button, emptyButton)));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>((string)result["prefabPath"]);
            Assert.AreEqual(6, prefab.GetComponentsInChildren<RectTransform>(true).Length);
            Assert.NotNull(prefab.transform.Find("root/artwork").GetComponent<Image>());
            Assert.NotNull(prefab.transform.Find("root/label").GetComponent<Text>());
            foreach (var name in new[] { "button", "empty" })
            {
                var target = prefab.transform.Find("root/" + name);
                Assert.AreSame(target.GetComponent<Image>(), target.GetComponent<Button>().targetGraphic);
                Assert.IsTrue(target.GetComponent<Image>().raycastTarget);
            }
        }

        [TestCase("row")]
        [TestCase("column")]
        public void NativeLayoutGroupsReflowAfterAnItemIsRemoved(string kind)
        {
            var a = Element("a", "rect", 0, 0, 20, 20); a["color"] = "#FFFFFF"; a.Remove("position");
            var b = (JObject)a.DeepClone(); b["name"] = "b";
            var c = (JObject)a.DeepClone(); c["name"] = "c";
            var document = Document(a, b, c);
            document["root"]["layout"] = new JObject { ["type"] = kind, ["spacing"] = 5, ["padding"] = 10 };
            var result = Run(Parameters(document));
            var root = PrefabUtility.LoadPrefabContents((string)result["prefabPath"]);
            try
            {
                SetCanvasSize(root, new Vector2(200, 120));
                var group = (RectTransform)root.transform.Find("root");
                Assert.NotNull(kind == "row" ? (LayoutGroup)group.GetComponent<HorizontalLayoutGroup>() : group.GetComponent<VerticalLayoutGroup>());
                LayoutRebuilder.ForceRebuildLayoutImmediate(group);
                var before = Box((RectTransform)group.Find("c"));
                Assert.That(kind == "row" ? before.x : before.y, Is.EqualTo(60).Within(1));
                Object.DestroyImmediate(group.Find("b").gameObject);
                LayoutRebuilder.ForceRebuildLayoutImmediate(group);
                var after = Box((RectTransform)group.Find("c"));
                Assert.That(kind == "row" ? after.x : after.y, Is.EqualTo(35).Within(1));
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [Test]
        public void LayoutSlotsPreserveOffsetsAndIndividualCrossAlignment()
        {
            var a = Element("a", "rect", 0, 0, 20, 20); a["color"] = "#FFFFFF"; a.Remove("position");
            a["offset"] = new JObject { ["x"] = 3, ["y"] = 2 };
            var b = Element("b", "rect", 0, 0, 20, 20); b["color"] = "#FFFFFF"; b.Remove("position"); b["vAlign"] = "bottom";
            var document = Document(a,b); document["root"]["layout"] = new JObject { ["type"] = "row", ["spacing"] = 5, ["vAlign"] = "middle" };
            var result = Run(Parameters(document));
            var root = PrefabUtility.LoadPrefabContents((string)result["prefabPath"]);
            try
            {
                SetCanvasSize(root,new Vector2(200,120));
                var group = root.transform.Find("root");
                var aRect = (RectTransform)group.Find("a_LayoutSlot/a");
                var bRect = (RectTransform)group.Find("b_LayoutSlot/b");
                Assert.AreEqual(new Rect(3,2,20,20), Box(aRect));
                Assert.AreEqual(50, Box((RectTransform)aRect.parent).y);
                Assert.AreEqual(new Rect(0,100,20,20), Box(bRect));
                Assert.AreEqual(25, Box((RectTransform)bRect.parent).x);
                Assert.AreEqual(new Vector2(20,20), aRect.rect.size);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [Test]
        public void IndependentTextGraphicsStayOutsideTheLayoutWithoutLayoutElement()
        {
            var item = Element("item", "rect", 0, 0, 20, 20); item["color"] = "#FFFFFF"; item.Remove("position");
            var caption = Element("caption", "text", 0, 0, 100, 40); caption["text"] = "Caption"; caption["textScaleX"] = 1.2;
            caption["children"] = new JArray(item); caption["layout"] = new JObject { ["type"] = "row", ["spacing"] = 4 };
            var result = Run(Parameters(Document(caption)));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>((string)result["prefabPath"]);
            AssertIndependent(prefab, (string)result["prefabPath"]);
            var owner = prefab.transform.Find("root/caption");
            Assert.NotNull(owner.Find("Text").GetComponent<Text>());
            var content = owner.Find("LayoutContent"); Assert.NotNull(content.GetComponent<HorizontalLayoutGroup>());
            Assert.AreEqual(1, content.childCount); Assert.NotNull(content.Find("item").GetComponent<Image>());
        }

        [Test]
        public void GridLayoutGroupReflowsRowsAndUsesDeclaredCells()
        {
            var children = Enumerable.Range(0, 5).Select(i => {
                var n = Element("cell" + i, "rect", 0, 0, 40, 20); n["color"] = "#FFFFFF"; n.Remove("position"); return n;
            }).ToArray();
            var document = Document(children);
            document["root"]["layout"] = new JObject {
                ["type"] = "grid", ["columns"] = 2,
                ["cellSize"] = new JObject { ["width"] = 40, ["height"] = 20 },
                ["spacing"] = new JObject { ["x"] = 8, ["y"] = 6 },
                ["padding"] = new JObject { ["x"] = 10, ["y"] = 5 }
            };
            var result = Run(Parameters(document));
            var root = PrefabUtility.LoadPrefabContents((string)result["prefabPath"]);
            try
            {
                SetCanvasSize(root, new Vector2(200,120));
                var group = (RectTransform)root.transform.Find("root");
                var grid = group.GetComponent<GridLayoutGroup>(); Assert.NotNull(grid);
                Assert.AreEqual(GridLayoutGroup.Constraint.FixedColumnCount, grid.constraint);
                Assert.AreEqual(2, grid.constraintCount);
                LayoutRebuilder.ForceRebuildLayoutImmediate(group);
                Assert.AreEqual(new Rect(10,57,40,20), Box((RectTransform)group.Find("cell4")));
                Object.DestroyImmediate(group.Find("cell1").gameObject);
                LayoutRebuilder.ForceRebuildLayoutImmediate(group);
                Assert.AreEqual(new Rect(58,31,40,20), Box((RectTransform)group.Find("cell4")));
                AssertIndependent(root, (string)result["prefabPath"]);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
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
                Assert.AreEqual(Color.clear, OwnImage(normal.Find("Content")).color);
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
                var clip = (RectTransform)root.transform.Find("root/bar/fill/ProgressClip");
                Assert.NotNull(clip.GetComponent<RectMask2D>());
                Assert.AreEqual(ratio > 0, clip.gameObject.activeSelf);
                Assert.AreEqual((ratio * 100).ToString("F0") + "%", OwnText(root.transform.Find("root/bar/label")).text);
                if (ratio > 0)
                {
                    var visual = (RectTransform)clip.Find("Content/Image");
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
                StringAssert.Contains("Viewport/content", path);
                StringAssert.Contains("ProgressClip/Content/States", path);
                var branch = prefab.transform.Find(path);
                Assert.NotNull(branch, path);
                Assert.AreEqual((bool)entry["activeSelf"], branch.gameObject.activeSelf);
                Assert.IsNull(entry["target"]);
            }
        }

        [Test]
        public void TextKeepsGeometryOutlineAndLayerOrderWithOnlyNecessaryWrappers()
        {
            var plain = Element("plain", "text", 10, 12, 80, 20);
            plain["text"] = "Plain"; plain["strokeWidth"] = 1;
            var scaled = Element("scaled", "text", 20, 40, 100, 24);
            scaled["text"] = "Scaled"; scaled["textScaleX"] = 1.25; scaled["alignment"] = "center";
            var composite = Element("composite", "text", 20, 80, 100, 24);
            composite["text"] = "Front"; composite["asset"] = "icon.png";
            var result = Run(Parameters(Document(plain, scaled, composite)));
            var root = PrefabUtility.LoadPrefabContents((string)result["prefabPath"]);
            try
            {
                SetCanvasSize(root, new Vector2(200, 120));
                var direct = root.transform.Find("root/plain");
                Assert.NotNull(direct.GetComponent<Text>());
                Assert.NotNull(direct.GetComponent<Outline>());
                Assert.AreEqual(0, direct.childCount);
                Assert.AreEqual(new Rect(10, 12, 80, 20), Box((RectTransform)direct));
                var owner = root.transform.Find("root/scaled");
                var text = owner.GetComponentInChildren<Text>();
                Assert.AreEqual(new Rect(20, 40, 100, 24), Box((RectTransform)owner));
                Assert.That(text.rectTransform.rect.width * text.transform.localScale.x, Is.EqualTo(100).Within(.01));
                var layers = root.transform.Find("root/composite");
                var image = layers.GetComponentInChildren<Image>();
                var label = layers.GetComponentInChildren<Text>();
                Assert.AreSame(layers, image.transform.parent);
                Assert.AreSame(layers, label.transform.parent);
                Assert.Less(image.transform.GetSiblingIndex(), label.transform.GetSiblingIndex());
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [Test]
        public void EveryTabExportsBothAppearancesAndRetainsItsOwnLabel()
        {
            var tabs = new JArray();
            for (var i = 0; i < 3; i++)
            {
                var label = Element("label", "text", 0, 0, 60, 20); label["text"] = "Tab " + i;
                var tab = Element("tab" + i, "button", i * 65, 0, 60, 30);
                tab["asset"] = "icon.png"; tab["children"] = new JArray(label);
                tab["state"] = new JObject {
                    ["current"] = i == 1 ? "selected" : "normal",
                    ["variants"] = new JObject {
                        ["normal"] = new JObject { ["."] = new JObject { ["color"] = "#FFFFFF" } },
                        ["selected"] = new JObject { ["."] = new JObject { ["color"] = "#00FF00" } }
                    }
                };
                tabs.Add(tab);
            }
            var document = Document(); document["root"]["children"] = tabs;
            var result = Run(Parameters(document));
            Assert.AreEqual(3, (int)result["states"]);
            Assert.AreEqual(6, ((JArray)result["stateObjects"]).Count);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>((string)result["prefabPath"]);
            for (var i = 0; i < 3; i++)
                foreach (var state in new[] { "normal", "selected" })
                {
                    var branch = prefab.transform.Find("root/tab" + i + "/States/" + state);
                    Assert.AreEqual(state == (i == 1 ? "selected" : "normal"), branch.gameObject.activeSelf);
                    Assert.AreEqual("Tab " + i, OwnText(branch.Find("Content/label")).text);
                    Assert.AreEqual(state == "selected" ? Color.green : Color.white, OwnImage(branch.Find("Content")).color);
                }
        }

        [Test]
        public void CenteredArtworkKeepsSizeWhileScrollViewportKeepsEdgeMargins()
        {
            var background = Element("background", "image", 0, 0, 200, 120); background["asset"] = "icon.png";
            background["anchor"] = new JObject { ["horizontal"] = "center", ["vertical"] = "middle" };
            var content = Element("content", "container", 0, 0, 180, 300);
            content["responsive"] = new JObject {
                ["min"] = new JObject { ["x"] = 0, ["y"] = 0 }, ["max"] = new JObject { ["x"] = 1, ["y"] = 0 },
                ["offsetMin"] = new JObject { ["x"] = 0, ["y"] = 0 }, ["offsetMax"] = new JObject { ["x"] = 0, ["y"] = 300 }
            };
            var viewport = Element("viewport", "container", 10, 20, 180, 90);
            viewport["children"] = new JArray(content);
            viewport["scroll"] = new JObject { ["content"] = "content", ["direction"] = "vertical" };
            viewport["responsive"] = new JObject {
                ["min"] = new JObject { ["x"] = 0, ["y"] = 0 }, ["max"] = new JObject { ["x"] = 1, ["y"] = 1 },
                ["offsetMin"] = new JObject { ["x"] = 10, ["y"] = 20 }, ["offsetMax"] = new JObject { ["x"] = -10, ["y"] = -10 }
            };
            var result = Run(Parameters(Document(background, viewport)));
            var root = PrefabUtility.LoadPrefabContents((string)result["prefabPath"]);
            try
            {
                foreach (var size in new[] { new Vector2(300, 180), new Vector2(160, 100) })
                {
                    SetCanvasSize(root, size);
                    Assert.AreEqual(new Rect((size.x - 200) / 2, (size.y - 120) / 2, 200, 120), Box((RectTransform)root.transform.Find("root/background")));
                    Assert.AreEqual(new Rect(10, 20, size.x - 20, size.y - 30), Box((RectTransform)root.transform.Find("root/viewport")));
                    var scroll = root.transform.Find("root/viewport").GetComponent<ScrollRect>();
                    Assert.AreEqual(new Vector2(size.x - 20, size.y - 30), scroll.viewport.rect.size);
                    Assert.AreEqual(new Vector2(size.x - 20, 300), scroll.content.rect.size);
                    Assert.AreEqual(Vector2.zero, scroll.content.anchoredPosition);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
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
        public void EvenLayoutBakesReferenceSpacingWithoutLayoutElements()
        {
            var a = Element("a", "rect", 0, 0, 21, 20); a["color"] = "#FFFFFF"; a["visible"] = false; a.Remove("position");
            var b = (JObject)a.DeepClone(); b["name"] = "b"; b["visible"] = true;
            var document = Document(a, b); document["root"]["layout"] = new JObject { ["type"] = "row", ["spacing"] = "even", ["vAlign"] = "middle" };
            var result = Run(Parameters(document));
            var root = PrefabUtility.LoadPrefabContents((string)result["prefabPath"]);
            try
            {
                SetCanvasSize(root, new Vector2(200, 120));
                Assert.That(Box((RectTransform)root.transform.Find("root/a_LayoutSlot")).x, Is.EqualTo(52).Within(1));
                Assert.That(Box((RectTransform)root.transform.Find("root/b")).x, Is.EqualTo(126).Within(1));
                Assert.IsFalse(root.transform.Find("root/a_LayoutSlot/a").gameObject.activeSelf);
                Assert.IsEmpty(root.GetComponentsInChildren<LayoutElement>(true));
                Assert.That(root.transform.Find("root").GetComponent<HorizontalLayoutGroup>().spacing, Is.EqualTo(158f/3).Within(.01));
                SetCanvasSize(root, new Vector2(300, 120));
                Assert.That(Box((RectTransform)root.transform.Find("root/a_LayoutSlot")).x, Is.EqualTo(102).Within(1));
                Assert.That(Box((RectTransform)root.transform.Find("root/b")).x, Is.EqualTo(176).Within(1));
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

        [Test, Category("AssetRoots")]
        public void MultipleAssetDirectoriesDeduplicateOverlappingRoots()
        {
            var extraRoot = Path.Combine(m_Temporary,"more"); Directory.CreateDirectory(extraRoot);
            File.Copy(Path.Combine(m_Temporary,"icon.png"),Path.Combine(extraRoot,"other.png"));
            var a = Element("a","image",0,0,40,30); a["asset"] = "icon.png";
            var b = Element("b","image",40,0,40,30); b["asset"] = "other.png";
            var parameters = Parameters(Document(a,b)); parameters["assetsPath"] = new JArray(m_Temporary,extraRoot,m_Temporary+Path.DirectorySeparatorChar);
            var result = Run(parameters); Assert.AreEqual(2,(int)result["images"]);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>((string)result["prefabPath"]);
            Assert.NotNull(OwnImage(prefab.transform.Find("root/a")).sprite);
            Assert.NotNull(OwnImage(prefab.transform.Find("root/b")).sprite);
        }

        [Test, Category("AssetRoots")]
        public void DuplicateBasenamesAcrossRootsRequireUniqueRelativePaths()
        {
            var first = Path.Combine(m_Temporary,"first"); var second = Path.Combine(m_Temporary,"second");
            Directory.CreateDirectory(Path.Combine(first,"left")); Directory.CreateDirectory(Path.Combine(second,"right"));
            File.Copy(Path.Combine(m_Temporary,"icon.png"),Path.Combine(first,"left/icon.png"));
            File.Copy(Path.Combine(m_Temporary,"icon.png"),Path.Combine(second,"right/icon.png"));
            var a = Element("a","image",0,0,40,30); a["asset"] = "icon.png";
            var parameters = Parameters(Document(a)); parameters["assetsPath"] = new JArray(first,second);
            StringAssert.Contains("ambiguous",Assert.Throws<CommandException>(() => Run(parameters)).Message);
            Assert.IsFalse(AssetDatabase.IsValidFolder(m_Output));
            a["asset"] = "left/icon.png"; var b = (JObject)a.DeepClone(); b["name"] = "b"; b["asset"] = "right/icon.png";
            parameters = Parameters(Document(a,b)); parameters["assetsPath"] = new JArray(first,second);
            Assert.AreEqual(2,(int)Run(parameters)["images"]);
        }

        [Test, Category("AssetRoots")]
        public void SameRelativePathAcrossRootsIsRejectedWithoutCreatingOutput()
        {
            var first = Path.Combine(m_Temporary,"first"); var second = Path.Combine(m_Temporary,"second");
            foreach(var root in new[]{first,second}) { Directory.CreateDirectory(Path.Combine(root,"icons")); File.Copy(Path.Combine(m_Temporary,"icon.png"),Path.Combine(root,"icons/icon.png")); }
            var image = Element("image","image",0,0,40,30); image["asset"] = "icons/icon.png";
            var parameters = Parameters(Document(image)); parameters["assetsPath"] = new JArray(first,second);
            var error = Assert.Throws<CommandException>(() => Run(parameters));
            Assert.AreEqual("UI_INVALID_STRUCTURE",error.Code); StringAssert.Contains("ambiguous",error.Message);
            Assert.IsFalse(AssetDatabase.IsValidFolder(m_Output));
        }

        [Test, Category("AssetRoots")]
        public void InvalidAssetDirectoryListsAreRejectedBeforeCreatingOutput()
        {
            var image = Element("image","image",0,0,40,30); image["asset"] = "icon.png";
            foreach(var roots in new JToken[]{new JArray(),new JArray(7),new JArray(m_Temporary,7),new JArray(m_Temporary,""),new JArray(m_Temporary,Path.Combine(m_Temporary,"missing")),JValue.CreateNull()})
            {
                var parameters = Parameters(Document(image)); parameters["assetsPath"] = roots;
                Assert.AreEqual("UI_INVALID_STRUCTURE",Assert.Throws<CommandException>(() => Run(parameters)).Code);
                Assert.IsFalse(AssetDatabase.IsValidFolder(m_Output));
            }
        }

        [Test, Category("AssetRoots")]
        public void FontCanResolveFromTheSecondAssetDirectory()
        {
            var fontSource = AssetDatabase.FindAssets("t:Font").Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => (p.EndsWith(".ttf",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".otf",StringComparison.OrdinalIgnoreCase))&&File.Exists(p));
            if(fontSource==null)Assert.Ignore("No font fixture available");
            var first = Path.Combine(m_Temporary,"first"); var second = Path.Combine(m_Temporary,"second"); Directory.CreateDirectory(first); Directory.CreateDirectory(second);
            var fontName = "font"+Path.GetExtension(fontSource); File.Copy(fontSource,Path.Combine(second,fontName));
            var label = Element("label","text",0,0,100,30); label["text"] = "Roots"; label["fontFamily"] = fontName;
            var parameters = Parameters(Document(label)); parameters["assetsPath"] = new JArray(first,second);
            var result = Run(parameters); Assert.AreEqual(1,(int)result["resourceUsage"]["copiedFonts"]); Assert.IsEmpty((JArray)result["warnings"]);
        }

        private string ProjectImage(bool asSprite, float pixelsPerUnit = 100)
        {
            if (!AssetDatabase.IsValidFolder(m_Output)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(m_Output));
            var path = m_Output + "/icon.png";
            File.Copy(Path.Combine(m_Temporary, "icon.png"), path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = asSprite ? TextureImporterType.Sprite : TextureImporterType.Default;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.isReadable = false;
            importer.spriteBorder = new Vector4(1,1,1,1);
            importer.SaveAndReimport();
            return path;
        }

        [Test]
        public void ExistingProjectSpriteIsReferencedWithoutCopyOrImporterChanges()
        {
            var source = ProjectImage(true, 200);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(source);
            var before = File.ReadAllBytes(source + ".meta");
            var image = Element("image", "image", 0, 0, 60, 40); image["asset"] = "icon.png"; image["nineSlice"] = 1;
            var parameters = Parameters(Document(image)); parameters["assetsPath"] = Path.GetFullPath(m_Output);
            var result = Run(parameters);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>((string)result["prefabPath"]);
            var graphic = OwnImage(prefab.transform.Find("root/image"));
            Assert.AreSame(sprite, graphic.sprite);
            Assert.That(graphic.pixelsPerUnitMultiplier, Is.EqualTo(.5f).Within(.001));
            Assert.IsTrue(result["resourceFolder"] == null || result["resourceFolder"].Type == JTokenType.Null);
            Assert.IsFalse(AssetDatabase.IsValidFolder(m_Output + "/View_Resources"));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(source + ".meta"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ProjectTextureAndSlicedVariantsKeepTheOriginalTexture(bool asSprite)
        {
            var source = ProjectImage(asSprite, 200);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(source);
            var before = File.ReadAllBytes(source + ".meta");
            var image = Element("image", "image", 0, 0, 60, 40); image["asset"] = "icon.png";
            if (asSprite) image["nineSlice"] = 2;
            var parameters = Parameters(Document(image)); parameters["assetsPath"] = m_Output;
            var first = Run(parameters);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>((string)first["prefabPath"]);
            var sprite = OwnImage(prefab.transform.Find("root/image")).sprite;
            Assert.AreSame(texture, sprite.texture);
            Assert.IsTrue(AssetDatabase.Contains(sprite));
            var spritePath = AssetDatabase.GetAssetPath(sprite);
            Assert.AreEqual(0, Directory.GetFiles((string)first["resourceFolder"], "*.png", SearchOption.AllDirectories).Length);
            parameters["overwrite"] = true; var second = Run(parameters);
            Assert.AreEqual((string)first["resourceFolder"], (string)second["resourceFolder"]);
            Assert.AreEqual(spritePath, AssetDatabase.GetAssetPath(OwnImage(AssetDatabase.LoadAssetAtPath<GameObject>((string)second["prefabPath"]).transform.Find("root/image")).sprite));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(source + ".meta"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExistingProjectFontIsReferencedWithoutCopy(bool copyIntoAssets)
        {
            var source = AssetDatabase.FindAssets("t:Font").Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => (p.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)) && File.Exists(p))
                .OrderBy(p => new FileInfo(p).Length).FirstOrDefault();
            if (source == null) Assert.Ignore("No project font fixture available");
            if (copyIntoAssets)
            {
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(m_Output));
                var target = m_Output + "/font" + Path.GetExtension(source);
                File.Copy(source, target); AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
                source = target;
            }
            var font = AssetDatabase.LoadAssetAtPath<Font>(source);
            var label = Element("label", "text", 0, 0, 100, 30); label["text"] = "Font"; label["fontFamily"] = "fixture";
            var parameters = Parameters(Document(label)); parameters["fontMap"] = new JObject { ["fixture"] = source };
            var result = Run(parameters);
            Assert.AreSame(font, OwnText(AssetDatabase.LoadAssetAtPath<GameObject>((string)result["prefabPath"]).transform.Find("root/label")).font);
            Assert.IsFalse(AssetDatabase.IsValidFolder(m_Output + "/View_Resources"));
        }

        [Test]
        public void RepeatedExternalAndDerivedResourcesReusePathsAndGuids()
        {
            var image = Element("image", "image", 0, 0, 60, 40); image["asset"] = "icon.png"; image["hueShift"] = 120; image["nineSlice"] = 2;
            var parameters = Parameters(Document(image)); var first = Run(parameters);
            var folder = (string)first["resourceFolder"];
            var before = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).OrderBy(p => p).ToArray();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>((string)first["prefabPath"]);
            var oldSprite = OwnImage(prefab.transform.Find("root/image")).sprite;
            var oldPath = AssetDatabase.GetAssetPath(oldSprite); var oldGuid = AssetDatabase.AssetPathToGUID(oldPath);
            parameters["overwrite"] = true; var second = Run(parameters);
            Assert.AreEqual(folder, (string)second["resourceFolder"]);
            CollectionAssert.AreEqual(before, Directory.GetFiles(folder, "*", SearchOption.AllDirectories).OrderBy(p => p).ToArray());
            Assert.AreEqual(oldGuid, AssetDatabase.AssetPathToGUID(oldPath));
            Assert.AreSame(oldSprite, OwnImage(AssetDatabase.LoadAssetAtPath<GameObject>((string)second["prefabPath"]).transform.Find("root/image")).sprite);
            var texture = new Texture2D(12,8); texture.SetPixels(Enumerable.Repeat(Color.blue,96).ToArray()); texture.Apply();
            File.WriteAllBytes(Path.Combine(m_Temporary,"icon.png"),texture.EncodeToPNG()); Object.DestroyImmediate(texture);
            var third = Run(parameters);
            Assert.AreEqual(folder, (string)third["resourceFolder"]);
            Assert.AreNotEqual(oldPath, AssetDatabase.GetAssetPath(OwnImage(AssetDatabase.LoadAssetAtPath<GameObject>((string)third["prefabPath"]).transform.Find("root/image")).sprite));
            Assert.IsTrue(File.Exists(oldPath), "Older resources may still have external references");
        }

        [Test]
        public void FailedOverwritePreservesExistingCacheAndPrefab()
        {
            var fontSource = AssetDatabase.FindAssets("t:Font").Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => (p.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)) && File.Exists(p));
            if (fontSource == null) Assert.Ignore("No font fixture available");
            var image = Element("image", "image", 0, 0, 60, 40); image["asset"] = "icon.png";
            var parameters = Parameters(Document(image)); var first = Run(parameters);
            var path = (string)first["prefabPath"]; var before = File.ReadAllBytes(path);
            // A pre-existing file must not be renamed/overwritten as the Fonts directory.
            var blocker = (string)first["resourceFolder"] + "/Fonts"; File.WriteAllText(blocker,"user-owned");
            AssetDatabase.ImportAsset(blocker, ImportAssetOptions.ForceSynchronousImport);
            var files = Directory.GetFiles(m_Output,"*",SearchOption.AllDirectories).OrderBy(p => p).ToArray();
            var externalFont = Path.Combine(m_Temporary, "font" + Path.GetExtension(fontSource)); File.Copy(fontSource,externalFont);
            var extra = Path.Combine(m_Temporary,"extra.png"); File.WriteAllBytes(extra,File.ReadAllBytes(Path.Combine(m_Temporary,"icon.png")).Concat(new byte[]{0}).ToArray());
            var extraImage = Element("extra","image",60,0,60,40); extraImage["asset"] = "extra.png";
            var label = Element("label","text",0,40,100,30); label["text"] = "Fail"; label["fontFamily"] = externalFont;
            parameters = Parameters(Document(image,extraImage,label)); parameters["overwrite"] = true;
            Assert.Throws<CommandException>(() => Run(parameters));
            CollectionAssert.AreEqual(before,File.ReadAllBytes(path));
            CollectionAssert.AreEqual(files,Directory.GetFiles(m_Output,"*",SearchOption.AllDirectories).OrderBy(p => p).ToArray());
            Assert.AreEqual("user-owned",File.ReadAllText(blocker));
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
            Assert.AreEqual(91, (int)result["elements"]); Assert.AreEqual(72, (int)result["images"]); Assert.AreEqual(3, (int)result["texts"]);
            Assert.AreEqual(5, (int)result["states"]); Assert.AreEqual(10, ((JArray)result["stateObjects"]).Count);
            var path = (string)result["prefabPath"];
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            AssertIndependent(prefab, path);
            foreach (var image in prefab.GetComponentsInChildren<Image>(true))
            {
                if (image.sprite != null) Assert.IsTrue(AssetDatabase.Contains(image.sprite), image.name);
                else { Assert.AreEqual(0, image.color.a, image.name); Assert.IsTrue(image.raycastTarget, image.name); }
            }
            foreach (var text in prefab.GetComponentsInChildren<Text>(true)) Assert.NotNull(text.font, text.name);
        }

        private static void SetCanvasSize(GameObject root, Vector2 size)
        {
            root.GetComponent<CanvasScaler>().enabled = false;
            root.GetComponent<Canvas>().enabled = false;
            ((RectTransform)root.transform).sizeDelta = size;
            foreach (var layout in root.GetComponentsInChildren<LayoutGroup>(true))
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)layout.transform);
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
