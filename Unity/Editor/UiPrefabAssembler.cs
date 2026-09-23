using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ImageToUI.Editor
{
    /// <summary>Constructs native UGUI objects once. Nothing from this class is serialized into the prefab.</summary>
    internal sealed class UiPrefabAssembler
    {
        private readonly UiStructureData m_Document;
        private readonly UiPrefabAssets m_Assets;
        private readonly Dictionary<string, Rect> m_Boxes;
        private readonly Scene m_Preview;
        private GameObject m_Root;
        public int ImageCount { get; private set; }
        public int TextCount { get; private set; }
        public readonly List<StateObject> StateObjects = new List<StateObject>();

        internal sealed class StateObject
        {
            public string elementPath;
            public string state;
            public string objectPath;
            public bool activeSelf;
            [Newtonsoft.Json.JsonIgnore] public Transform target;
        }

        public UiPrefabAssembler(UiStructureData document, UiPrefabAssets assets, Scene preview)
        {
            m_Document = document; m_Assets = assets; m_Preview = preview;
            m_Boxes = document.Resolve(document.ReferenceSize, null);
        }

        public GameObject Build(string name)
        {
            m_Root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(m_Root, m_Preview);
            m_Root.layer = 5;
            m_Root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = m_Root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = m_Document.ReferenceSize;
            scaler.matchWidthOrHeight = .5f;
            ((RectTransform)m_Root.transform).sizeDelta = m_Document.ReferenceSize;
            BuildNode(m_Document, m_Document.Snapshot(), "root", m_Root.transform);
            foreach (var layout in m_Root.GetComponentsInChildren<LayoutGroup>(true))
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)layout.transform);
            foreach (var state in StateObjects)
            {
                state.objectPath = RelativePath(state.target);
                state.target = null;
            }
            return m_Root;
        }

        private RectTransform BuildNode(UiStructureData source, UiStructureData snapshot, string path, Transform parent)
        {
            var node = snapshot.Nodes[path];
            var rect = NewRect(UiStructureData.Text(node, "name"), parent);
            var parentPath = path == "root" ? null : UiStructureData.ParentPath(path);
            SetRectangle(rect, node, m_Boxes[path], parentPath == null ? m_Document.ReferenceSize : m_Boxes[parentPath].size,
                parentPath == null ? null : snapshot.Nodes[parentPath], path == "root" && m_Boxes[path].size == m_Document.ReferenceSize);
            var state = source.Nodes[path]["state"] as JObject;
            if (state == null)
            {
                BuildBody(source, snapshot, path, rect);
                return rect;
            }

            // Stable owner + independent complete visual branches. Developers switch the branch GameObjects.
            var group = NewRect("States", rect); Stretch(group);
            var current = UiStructureData.Text(state, "current");
            foreach (var variant in ((JObject)state["variants"]).Properties())
            {
                var variantSource = new UiStructureData(source.Json.ToString());
                variantSource.Nodes[path]["state"]["current"] = variant.Name;
                var variantSnapshot = variantSource.Snapshot();
                var selector = NewRect(GameObjectUtility.GetUniqueNameForSibling(group, StateName(variant.Name)), group); Stretch(selector);
                selector.gameObject.SetActive(variant.Name == current);
                var content = NewRect("Content", selector); Stretch(content);
                BuildBody(variantSource, variantSnapshot, path, content);
                StateObjects.Add(new StateObject
                {
                    elementPath = path,
                    state = variant.Name,
                    target = selector,
                    activeSelf = selector.gameObject.activeSelf
                });
            }
            return rect;
        }

        private void BuildBody(UiStructureData source, UiStructureData snapshot, string path, RectTransform rect)
        {
            var node = snapshot.Nodes[path];
            var type = UiStructureData.Text(node, "type");
            var color = node["color"] == null ? Color.white : UiStructureData.ParseColor(UiStructureData.Text(node, "color"));
            color.a *= (float)UiStructureData.Number(node, "opacity", 1);
            var asset = UiStructureData.Text(node, "asset");
            // Fill graphics must remain below the clipping transform. Ordinary visuals
            // use the authored object so their actual anchors stay directly editable.
            var parentNode = path == "root" ? null : snapshot.Nodes[UiStructureData.ParentPath(path)];
            var separateVisual = parentNode?["progress"] is JObject ownerProgress &&
                UiStructureData.Text(ownerProgress, "fill") == UiStructureData.Text(node, "name");
            if (asset.Length > 0 || (node["color"] != null && type != "text"))
            {
                var visual = separateVisual || type == "text" ? NewRect(HelperName(node, "Image"), rect) : rect;
                if (visual != rect) Stretch(visual);
                var image = visual.gameObject.AddComponent<Image>();
                image.color = color; image.raycastTarget = type == "overlay";
                if (asset.Length > 0)
                {
                    image.sprite = m_Assets.GetSprite(asset, node["nineSlice"], (float)UiStructureData.Number(node, "hueShift"));
                    image.type = UiStructureData.HasSlice(node["nineSlice"]) ? Image.Type.Sliced : Image.Type.Simple;
                    image.pixelsPerUnitMultiplier = 100f / image.sprite.pixelsPerUnit;
                }
                ImageCount++;
            }
            if (type == "text") AddText(rect, node, color, separateVisual);

            var children = new Dictionary<string, RectTransform>(StringComparer.Ordinal);
            foreach (var child in UiStructureData.Children(node))
            {
                var name = UiStructureData.Text(child, "name");
                children[name] = BuildNode(source, snapshot, path + "/" + name, rect);
            }
            if (node["progress"] is JObject progress)
            {
                var fill = children[UiStructureData.Text(progress, "fill")];
                var ratio = (float)((UiStructureData.Number(progress, "value") - UiStructureData.Number(progress, "min")) /
                    (UiStructureData.Number(progress, "max", 1) - UiStructureData.Number(progress, "min")));
                AddProgressClip(fill, ratio, UiStructureData.Text(progress, "direction"), m_Boxes[path + "/" + UiStructureData.Text(progress, "fill")].size);
            }
            if (node["scroll"] is JObject scroll) AddScroll(rect, children[UiStructureData.Text(scroll, "content")], scroll, path);
            if (type == "button")
            {
                var button = rect.gameObject.AddComponent<Button>(); button.transition = Selectable.Transition.None;
                var graphic = rect.GetComponent<Graphic>();
                if (graphic == null && separateVisual)
                    graphic = rect.GetComponentsInChildren<Graphic>(true).FirstOrDefault();
                if (graphic == null)
                {
                    var hit = separateVisual ? NewRect(HelperName(node, "HitArea"), rect) : rect;
                    if (hit != rect) { Stretch(hit); hit.SetAsFirstSibling(); }
                    graphic = hit.gameObject.AddComponent<Image>(); graphic.color = Color.clear;
                }
                graphic.raycastTarget = true;
                button.targetGraphic = graphic;
            }
            UiPrefabLayout.Apply(rect, node, children);
            rect.gameObject.SetActive(UiStructureData.Bool(node, "visible", true));
        }

        private void AddText(RectTransform parent, JObject node, Color color, bool separateVisual)
        {
            var scale = (float)UiStructureData.Number(node, "textScaleX", 1);
            var visual = separateVisual || UiStructureData.Text(node, "asset").Length > 0 || !Mathf.Approximately(scale, 1)
                ? NewRect(HelperName(node, "Text"), parent) : parent;
            var text = visual.gameObject.AddComponent<Text>();
            text.raycastTarget = false; text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow; text.verticalOverflow = VerticalWrapMode.Overflow;
            text.font = m_Assets.GetFont(UiStructureData.Text(node, "fontFamily"));
            text.text = UiStructureData.Text(node, "text"); text.color = color;
            text.fontSize = Mathf.Max(1, (int)Math.Round(UiStructureData.Number(node, "fontSize", 24)));
            var horizontal = UiStructureData.Text(node, "alignment", "left");
            var vertical = UiStructureData.Text(node, "textVAlign", "top");
            var x = horizontal == "right" ? 2 : horizontal == "center" ? 1 : 0;
            var y = new[] { "bottom", "end" }.Contains(vertical) ? 2 : new[] { "middle", "center" }.Contains(vertical) ? 1 : 0;
            text.alignment = (TextAnchor)(y * 3 + x);
            text.lineSpacing = 1;
            if (node["lineHeight"] != null && text.font != null && text.font.lineHeight > 0 && text.font.fontSize > 0)
                text.lineSpacing = (float)UiStructureData.Number(node, "lineHeight") / (text.font.lineHeight * (float)text.fontSize / text.font.fontSize);
            if (visual != parent)
            {
                var pivot = x * .5f;
                visual.pivot = new Vector2(pivot, 1 - y * .5f);
                visual.anchorMin = new Vector2(pivot * (1 - 1 / scale), 0);
                visual.anchorMax = new Vector2(pivot + (1 - pivot) / scale, 1);
                visual.offsetMin = visual.offsetMax = Vector2.zero;
                visual.localScale = new Vector3(scale, 1, 1);
            }
            var stroke = (float)UiStructureData.Number(node, "strokeWidth");
            if (stroke > 0)
            {
                var outline = visual.gameObject.AddComponent<Outline>(); outline.useGraphicAlpha = false;
                outline.effectDistance = new Vector2(stroke, -stroke);
                var strokeColor = UiStructureData.ParseColor(UiStructureData.Text(node, "strokeColor", "#000000"));
                strokeColor.a *= (float)UiStructureData.Number(node, "opacity", 1);
                outline.effectColor = strokeColor;
            }
            TextCount++;
        }

        private void AddScroll(RectTransform owner, RectTransform content, JObject spec, string path)
        {
            var index = content.GetSiblingIndex();
            var viewport = NewRect(GameObjectUtility.GetUniqueNameForSibling(owner, "Viewport"), owner);
            Stretch(viewport); viewport.SetSiblingIndex(index);
            viewport.gameObject.AddComponent<RectMask2D>();
            var hit = viewport.gameObject.AddComponent<Image>(); hit.color = Color.clear; hit.raycastTarget = true;
            content.SetParent(viewport, false);
            var size = m_Boxes[path + "/" + UiStructureData.Text(spec, "content")].size;
            var extent = m_Boxes[path].size;
            var span = content.anchorMax - content.anchorMin;
            content.anchorMin = new Vector2(0, 1 - span.y); content.anchorMax = new Vector2(span.x, 1);
            content.sizeDelta = size - Vector2.Scale(extent, span);
            var offset = UiStructureData.Pair(spec["offset"] as JObject);
            offset = Vector2.Min(offset, Vector2.Max(Vector2.zero, size - extent));
            content.anchoredPosition = new Vector2(-offset.x, offset.y);
            var scroll = owner.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = content;
            scroll.horizontal = UiStructureData.Text(spec, "direction") != "vertical";
            scroll.vertical = UiStructureData.Text(spec, "direction") != "horizontal";
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 20;
        }

        private static void AddProgressClip(RectTransform fill, float ratio, string direction, Vector2 referenceSize)
        {
            var originalChildren = new List<Transform>();
            foreach (Transform child in fill) originalChildren.Add(child);
            var clip = NewRect(GameObjectUtility.GetUniqueNameForSibling(fill, "ProgressClip"), fill);
            Stretch(clip); clip.gameObject.AddComponent<RectMask2D>();
            var low = Vector2.zero; var high = Vector2.one;
            switch (direction)
            {
                case "left-to-right": high.x = ratio; break;
                case "right-to-left": low.x = 1 - ratio; break;
                case "top-to-bottom": low.y = 1 - ratio; break;
                case "bottom-to-top": high.y = ratio; break;
            }
            clip.anchorMin = low; clip.anchorMax = high;
            var content = NewRect("Content", clip); Stretch(content);
            if (ratio > 0)
            {
                var span = high - low;
                // Restore full-capacity geometry inside the cropped viewport; textures do not squeeze.
                content.anchorMin = new Vector2(-low.x / span.x, -low.y / span.y);
                content.anchorMax = new Vector2((1 - low.x) / span.x, (1 - low.y) / span.y);
            }
            else
            {
                clip.gameObject.SetActive(false);
                content.anchorMin = content.anchorMax = new Vector2(0, 1);
                content.sizeDelta = referenceSize;
            }
            foreach (var child in originalChildren) child.SetParent(content, false);
        }

        private static void SetRectangle(RectTransform rect, JObject node, Rect box, Vector2 parent, JObject parentNode, bool stretchRoot)
        {
            var low = UiStructureData.Anchor(node); var high = low;
            if (stretchRoot) { low = Vector2.zero; high = Vector2.one; }
            else if (node["responsive"] is JObject responsive)
            {
                var first = UiStructureData.Pair((JObject)responsive["min"]); var last = UiStructureData.Pair((JObject)responsive["max"]);
                low = new Vector2(first.x, 1 - last.y); high = new Vector2(last.x, 1 - first.y);
            }
            else if (parentNode?["layout"] is JObject layout)
            {
                var children = UiStructureData.Children(parentNode).ToList();
                var index = children.FindIndex(n => UiStructureData.Text(n, "name") == UiStructureData.Text(node, "name"));
                var row = UiStructureData.Text(layout, "type") == "row";
                var mode = UiStructureData.Text(layout, "align", "start");
                if (!new[] { "space-between", "space-around", "space-evenly" }.Contains(mode) && UiStructureData.Text(layout, "spacing") == "even") mode = "space-evenly";
                var main = mode == "space-evenly" ? (index + 1f) / (children.Count + 1) : mode == "space-around" ? (index + .5f) / children.Count :
                    mode == "space-between" ? (children.Count == 1 ? 0 : index / (children.Count - 1f)) : Factor(mode);
                var cross = Factor(UiStructureData.Text(node, row ? "vAlign" : "align", UiStructureData.Text(layout, "vAlign", "start")));
                low = high = row ? new Vector2(main, 1 - cross) : new Vector2(cross, 1 - main);
            }
            else
            {
                if (node["align"] != null) low.x = high.x = Factor(UiStructureData.Text(node, "align"));
                if (node["vAlign"] != null) low.y = high.y = 1 - Factor(UiStructureData.Text(node, "vAlign"));
            }
            rect.pivot = new Vector2(0, 1); rect.anchorMin = low; rect.anchorMax = high;
            rect.offsetMin = new Vector2(box.x - low.x * parent.x, parent.y - box.y - box.height - low.y * parent.y);
            rect.offsetMax = new Vector2(box.x + box.width - high.x * parent.x, parent.y - box.y - high.y * parent.y);
        }

        private static float Factor(string value) => new[] { "center", "middle" }.Contains(value) ? .5f : new[] { "end", "right", "bottom" }.Contains(value) ? 1 : 0;
        private static string StateName(string value) => value == "." || value == ".." ? "State_" + value : value.Replace("%", "%25").Replace("/", "%2F").Replace("\\", "%5C");
        private static string HelperName(JObject node, string prefix)
        {
            var names = new HashSet<string>(UiStructureData.Children(node).Select(n => UiStructureData.Text(n, "name")));
            var result = prefix;
            for (var i = 1; names.Contains(result); i++) result = prefix + "_" + i;
            return result;
        }
        private string RelativePath(Transform target)
        {
            var parts = new List<string>();
            for (var current = target; current != m_Root.transform; current = current.parent) parts.Add(current.name);
            parts.Reverse(); return string.Join("/", parts);
        }
        private static RectTransform NewRect(string name, Transform parent)
        {
            var result = new GameObject(name, typeof(RectTransform)); result.layer = 5;
            var rect = (RectTransform)result.transform; rect.SetParent(parent, false); rect.pivot = new Vector2(0, 1);
            return rect;
        }
        private static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    }
}
