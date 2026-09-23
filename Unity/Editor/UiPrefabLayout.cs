using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ImageToUI.Editor
{
    /// <summary>Exports layout ownership to standard UGUI components; no runtime exporter script.</summary>
    internal static class UiPrefabLayout
    {
        public static void Apply(RectTransform owner, JObject node, Dictionary<string, RectTransform> children)
        {
            var spec = node["layout"] as JObject;
            if (spec == null) return;
            var kind = UiStructureData.Text(spec, "type");
            var grid = kind == "grid";
            var row = kind == "row";
            var pad = spec["padding"] is JObject p ? UiStructureData.Pair(p) : Vector2.one * (float)UiStructureData.Number(spec, "padding");
            var mode = UiStructureData.Text(spec, "align", "start");
            if (!grid && !new[] { "space-between", "space-around", "space-evenly" }.Contains(mode) && UiStructureData.Text(spec, "spacing") == "even") mode = "space-evenly";
            var distributed = !grid && new[] { "space-between", "space-around", "space-evenly" }.Contains(mode);
            var authored = UiStructureData.Children(node).ToList();
            // Separate independent graphics from layout ownership without LayoutElement.
            if (owner.Cast<Transform>().Any(child => !children.Values.Contains(child)))
            {
                var content = NewRect("LayoutContent", owner);
                content.anchorMin = Vector2.zero; content.anchorMax = Vector2.one;
                content.offsetMin = content.offsetMax = Vector2.zero;
                foreach (var child in children.Values) child.SetParent(content, false);
                owner = content;
            }
            var mainPadding = row ? pad.x : pad.y;
            var spacing = grid || distributed ? 0 : (float)UiStructureData.Number(spec, "spacing");
            if (distributed && authored.Count > 0)
            {
                var total = authored.Sum(child => {
                    var size = UiStructureData.Size((JObject)child["size"], "layout child");
                    return row ? size.x : size.y;
                });
                var available = Mathf.Max(0, (row ? owner.rect.width : owner.rect.height) - 2 * mainPadding - total);
                spacing = mode == "space-between" ? (authored.Count > 1 ? available / (authored.Count - 1) : 0) :
                    mode == "space-around" ? available / authored.Count : available / (authored.Count + 1);
                if (mode != "space-between") mainPadding += spacing * (mode == "space-around" ? .5f : 1);
            }
            LayoutGroup group;
            if (grid)
            {
                var component = owner.gameObject.AddComponent<GridLayoutGroup>();
                component.cellSize = UiStructureData.Size((JObject)spec["cellSize"], "grid cell");
                component.spacing = UiStructureData.Pair(spec["spacing"] as JObject);
                component.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                component.constraintCount = (int)UiStructureData.Number(spec, "columns");
                component.startAxis = GridLayoutGroup.Axis.Horizontal;
                component.startCorner = GridLayoutGroup.Corner.UpperLeft;
                group = component;
            }
            else
            {
                var component = row ? (HorizontalOrVerticalLayoutGroup)owner.gameObject.AddComponent<HorizontalLayoutGroup>() : owner.gameObject.AddComponent<VerticalLayoutGroup>();
                component.spacing = spacing;
                component.childControlWidth = component.childControlHeight = false;
                component.childForceExpandWidth = component.childForceExpandHeight = false;
                component.childScaleWidth = component.childScaleHeight = false;
                group = component;
            }
            var horizontalPadding = row && distributed ? mainPadding : pad.x;
            var verticalPadding = !row && distributed ? mainPadding : pad.y;
            group.padding = new RectOffset(Mathf.RoundToInt(horizontalPadding), Mathf.RoundToInt(horizontalPadding), Mathf.RoundToInt(verticalPadding), Mathf.RoundToInt(verticalPadding));
            var main = distributed ? (mode == "space-between" ? 0 : 1) : Factor(mode);
            var cross = Factor(UiStructureData.Text(spec, "vAlign", "start"));
            group.childAlignment = (TextAnchor)(grid || row ? cross * 3 + main : main * 3 + cross);
            var order = new List<RectTransform>();
            for (var i = 0; i < authored.Count; i++)
            {
                var child = authored[i];
                var rect = children[UiStructureData.Text(child, "name")];
                var size = UiStructureData.Size((JObject)child["size"], "layout child");
                var offset = UiStructureData.Pair(child["offset"] as JObject);
                var childCross = grid ? 0 : Factor(UiStructureData.Text(child, row ? "vAlign" : "align", UiStructureData.Text(spec, "vAlign", "start")));
                var needsSlot = offset != Vector2.zero || !rect.gameObject.activeSelf || (!grid && childCross != cross);
                var slot = rect;
                if (needsSlot)
                {
                    slot = NewRect(rect.name + "_LayoutSlot", owner);
                    slot.sizeDelta = !grid && childCross != cross
                        ? (row ? new Vector2(size.x, Mathf.Max(size.y, owner.rect.height - 2 * pad.y)) : new Vector2(Mathf.Max(size.x, owner.rect.width - 2 * pad.x), size.y))
                        : size;
                    rect.SetParent(slot, false);
                    rect.pivot = new Vector2(0, 1);
                    var factor = childCross * .5f;
                    rect.anchorMin = rect.anchorMax = grid ? new Vector2(0,1) : row ? new Vector2(0, 1-factor) : new Vector2(factor,1);
                    rect.sizeDelta = size;
                    rect.anchoredPosition = grid ? new Vector2(offset.x,-offset.y) : row ?
                        new Vector2(offset.x, factor*size.y-offset.y) : new Vector2(offset.x-factor*size.x,-offset.y);
                }
                order.Add(slot);
            }
            for (var i = 0; i < order.Count; i++) order[i].SetSiblingIndex(i);
        }

        private static int Factor(string value) => new[] { "center", "middle" }.Contains(value) ? 1 : new[] { "end", "right", "bottom" }.Contains(value) ? 2 : 0;
        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(GameObjectUtility.GetUniqueNameForSibling(parent, name), typeof(RectTransform));
            go.layer = 5;
            var rect = (RectTransform)go.transform; rect.SetParent(parent, false); rect.pivot = new Vector2(0,1);
            return rect;
        }
    }
}
