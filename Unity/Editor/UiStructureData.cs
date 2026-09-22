using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ImageToUI.Editor
{
    /// <summary>The source document stays independent of selected appearance variants.</summary>
    public sealed class UiStructureData
    {
        public readonly JObject Json;
        public readonly Dictionary<string, JObject> Nodes = new Dictionary<string, JObject>(StringComparer.Ordinal);
        public readonly List<string> Order = new List<string>();
        public Vector2 ReferenceSize { get; private set; }
        public static readonly HashSet<string> AppearanceFields = new HashSet<string>
        {
            "asset", "color", "opacity", "hueShift", "nineSlice", "text", "fontFamily",
            "fontSize", "lineHeight", "textScaleX", "strokeColor", "strokeWidth",
            "alignment", "textVAlign", "visible"
        };

        public UiStructureData(string json) : this(JObject.Parse(json), true) { }

        private UiStructureData(JObject json, bool validate)
        {
            Json = json;
            var canvas = Object(json["canvas"], "canvas");
            ReferenceSize = Size(canvas, "canvas");
            Index(Object(json["root"], "root"), "root");
            if (validate) Validate();
        }

        private void Index(JObject node, string path)
        {
            if (Nodes.ContainsKey(path)) throw new ArgumentException("Duplicate element path: " + path);
            Nodes.Add(path, node);
            Order.Add(path);
            foreach (var child in Children(node))
            {
                var name = Text(child, "name");
                if (string.IsNullOrWhiteSpace(name) || name.Contains("/") || name.Contains("\\") || name == "." || name == "..")
                    throw new ArgumentException(path + ": invalid child name");
                Index(child, path + "/" + name);
            }
        }

        public UiStructureData Snapshot()
        {
            var result = new UiStructureData((JObject)Json.DeepClone(), false);
            foreach (var path in result.Order)
            {
                var node = result.Nodes[path];
                var state = node["state"] as JObject;
                if (state == null) continue;
                var patches = (JObject)state["variants"][Text(state, "current")];
                foreach (var patch in patches.Properties())
                {
                    var target = result.Relative(path, patch.Name);
                    foreach (var property in ((JObject)patch.Value).Properties()) target[property.Name] = property.Value.DeepClone();
                }
            }
            foreach (var path in result.Order)
            {
                var spec = result.Nodes[path]["progress"] as JObject;
                if (spec != null && spec["label"] != null)
                    result.Relative(path, Text(spec, "label"))["text"] = FormatProgress(spec);
            }
            return result;
        }

        public JObject Relative(string owner, string relative)
        {
            var path = relative == "." ? owner : owner + "/" + relative;
            if (string.IsNullOrEmpty(relative) || relative.StartsWith("/") || relative.Split('/').Any(s => s == ".." || (s == "." && relative != ".")) || !Nodes.TryGetValue(path, out var result))
                throw new ArgumentException(owner + ": unknown relative target " + relative);
            return result;
        }

        public static string ParentPath(string path) => path.Substring(0, path.LastIndexOf('/'));

        public IEnumerable<KeyValuePair<string, JObject>> AppearanceCandidates()
        {
            foreach (var path in Order) yield return new KeyValuePair<string, JObject>(path, Nodes[path]);
            foreach (var path in Order)
            {
                var variants = Nodes[path]["state"]?["variants"] as JObject;
                if (variants == null) continue;
                foreach (var variant in variants.Properties())
                    foreach (var patch in ((JObject)variant.Value).Properties())
                    {
                        var merged = (JObject)Relative(path, patch.Name).DeepClone();
                        foreach (var field in ((JObject)patch.Value).Properties()) merged[field.Name] = field.Value.DeepClone();
                        yield return new KeyValuePair<string, JObject>(path + "@" + variant.Name + "/" + patch.Name, merged);
                    }
            }
        }

        public HashSet<string> ImagePaths()
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in Order)
            {
                var node = Nodes[path];
                if (node["asset"] != null || (node["color"] != null && Text(node, "type") != "text")) paths.Add(path);
                var variants = node["state"]?["variants"] as JObject;
                if (variants == null) continue;
                foreach (var variant in variants.Properties())
                    foreach (var patch in ((JObject)variant.Value).Properties())
                    {
                        var targetPath = patch.Name == "." ? path : path + "/" + patch.Name;
                        if (patch.Value["asset"] != null || (patch.Value["color"] != null && Text(Nodes[targetPath], "type") != "text")) paths.Add(targetPath);
                    }
            }
            return paths;
        }

        private void Validate()
        {
            var insets = Json["canvas"]["safeArea"] as JObject;
            if (Json["canvas"]["safeArea"] != null && insets == null) throw new ArgumentException("canvas.safeArea must be an object");
            if (insets != null)
            {
                foreach (var property in insets.Properties())
                    if (!new[] { "left", "top", "right", "bottom" }.Contains(property.Name) || Number(insets, property.Name) < 0)
                        throw new ArgumentException("Invalid canvas.safeArea inset");
                if (Number(insets, "left") + Number(insets, "right") >= ReferenceSize.x || Number(insets, "top") + Number(insets, "bottom") >= ReferenceSize.y)
                    throw new ArgumentException("Safe area has no usable space");
            }
            foreach (var path in Order)
            {
                var node = Nodes[path];
                var type = Text(node, "type");
                EnumValue(type, new[] { "container", "image", "text", "rect", "overlay", "button" }, path + ".type");
                if (string.IsNullOrWhiteSpace(Text(node, "name"))) throw new ArgumentException(path + ": name is required");
                Size(Object(node["size"], path + ".size"), path);
                var anchor = Object(node["anchor"], path + ".anchor");
                EnumValue(Text(anchor, "horizontal"), new[] { "left", "center", "right" }, path + ".anchor.horizontal");
                EnumValue(Text(anchor, "vertical"), new[] { "top", "middle", "bottom" }, path + ".anchor.vertical");
                foreach (var key in new[] { "position", "offset" })
                    if (node[key] != null) Pair(Object(node[key], path + "." + key));
                if (node["align"] != null) EnumValue(Text(node, "align"), new[] { "left", "center", "right", "start", "end" }, path + ".align");
                if (node["vAlign"] != null) EnumValue(Text(node, "vAlign"), new[] { "top", "middle", "bottom", "start", "end", "center" }, path + ".vAlign");
                var layout = node["layout"] as JObject;
                if (node["layout"] != null)
                {
                    layout = Object(node["layout"], path + ".layout");
                    EnumValue(Text(layout, "type"), new[] { "row", "column" }, path + ".layout.type");
                    if (layout["spacing"]?.Type == JTokenType.String) EnumValue(Text(layout, "spacing"), new[] { "even" }, path + ".layout.spacing");
                    else Number(layout, "spacing");
                    if (layout["padding"] is JObject padding) Pair(padding); else Number(layout, "padding");
                }
                if (node["responsive"] != null)
                {
                    var spec = Object(node["responsive"], path + ".responsive");
                    if (path == "root" || Nodes[ParentPath(path)]["layout"] != null || node["align"] != null || node["vAlign"] != null || node["offset"] != null)
                        throw new ArgumentException(path + ": responsive conflicts with root/layout/alignment");
                    var low = Pair(Object(spec["min"], path + ".responsive.min"));
                    var high = Pair(Object(spec["max"], path + ".responsive.max"));
                    Pair(Object(spec["offsetMin"], path + ".responsive.offsetMin"));
                    Pair(Object(spec["offsetMax"], path + ".responsive.offsetMax"));
                    if (low.x < 0 || low.y < 0 || high.x > 1 || high.y > 1 || low.x > high.x || low.y > high.y)
                        throw new ArgumentException(path + ": invalid normalized responsive edges");
                    if (Bool(spec, "safeArea") && ParentPath(path) != "root") throw new ArgumentException(path + ": safeArea must be a root child");
                }
                if (node["state"] != null)
                {
                    var spec = Object(node["state"], path + ".state");
                    var variants = Object(spec["variants"], path + ".state.variants");
                    if (!(variants[Text(spec, "current")] is JObject)) throw new ArgumentException(path + ": unknown current state");
                    foreach (var variant in variants.Properties())
                        foreach (var patch in Object(variant.Value, path + ".state variant").Properties())
                        {
                            Relative(path, patch.Name);
                            foreach (var field in Object(patch.Value, path + ".state patch").Properties())
                                if (!AppearanceFields.Contains(field.Name)) throw new ArgumentException(path + ": unsupported state field " + field.Name);
                        }
                }
                if (node["progress"] != null)
                {
                    if (node["scroll"] != null) throw new ArgumentException(path + ": progress and scroll require separate nodes");
                    var spec = Object(node["progress"], path + ".progress");
                    DirectTarget(path, Text(spec, "fill"));
                    var min = Number(spec, "min");
                    var max = Number(spec, "max", 1);
                    var value = Number(spec, "value", double.NaN);
                    if (min >= max || value < min || value > max) throw new ArgumentException(path + ": progress value outside range");
                    EnumValue(Text(spec, "direction"), new[] { "left-to-right", "right-to-left", "top-to-bottom", "bottom-to-top" }, path + ".progress.direction");
                    if (spec["label"] != null)
                    {
                        var label = DirectTarget(path, Text(spec, "label"));
                        if (Text(label, "type") != "text" || Text(spec, "label") == Text(spec, "fill")) throw new ArgumentException(path + ": progress label must be a separate text child");
                    }
                    FormatProgress(spec);
                }
                if (node["scroll"] != null)
                {
                    var spec = Object(node["scroll"], path + ".scroll");
                    if (type != "container" || layout != null) throw new ArgumentException(path + ": scroll viewport must be a container without layout");
                    if (Text(DirectTarget(path, Text(spec, "content")), "type") != "container") throw new ArgumentException(path + ": scroll content must be a container");
                    EnumValue(Text(spec, "direction"), new[] { "horizontal", "vertical", "both" }, path + ".scroll.direction");
                    var offset = Pair(spec["offset"] as JObject);
                    if (offset.x < 0 || offset.y < 0 || (Text(spec, "direction") == "vertical" && offset.x != 0) || (Text(spec, "direction") == "horizontal" && offset.y != 0))
                        throw new ArgumentException(path + ": invalid scroll offset");
                }
            }
            foreach (var candidate in AppearanceCandidates()) ValidateAppearance(candidate.Value, candidate.Key);
            Resolve(ReferenceSize, null);
        }

        public JObject DirectTarget(string path, string name)
        {
            if (string.IsNullOrEmpty(name) || name.Contains("/") || name == ".") throw new ArgumentException(path + ": direct child reference required");
            return Relative(path, name);
        }

        public static void ValidateAppearance(JObject node, string path)
        {
            var opacity = Number(node, "opacity", 1);
            if (opacity < 0 || opacity > 1) throw new ArgumentException(path + ": opacity outside [0,1]");
            var hue = Number(node, "hueShift");
            if (hue < -360 || hue > 360) throw new ArgumentException(path + ": hueShift outside [-360,360]");
            Bool(node, "visible", true);
            foreach (var key in new[] { "color", "strokeColor" }) if (node[key] != null) ParseColor(Text(node, key));
            if (Text(node, "type") == "image" && string.IsNullOrEmpty(Text(node, "asset"))) throw new ArgumentException(path + ": image asset is required");
            if ((Text(node, "type") == "rect" || Text(node, "type") == "overlay") && node["color"] == null && node["asset"] == null) throw new ArgumentException(path + ": missing visual");
            if (Text(node, "type") == "text")
            {
                if (string.IsNullOrWhiteSpace(Text(node, "text"))) throw new ArgumentException(path + ": text is empty");
                if (Number(node, "fontSize", 24) <= 0 || Number(node, "textScaleX", 1) <= 0 || Number(node, "lineHeight", 1) <= 0 || Number(node, "strokeWidth") < 0)
                    throw new ArgumentException(path + ": invalid text metrics");
            }
            var slice = node["nineSlice"];
            if (slice != null && slice.Type != JTokenType.Boolean)
            {
                if (slice.Type == JTokenType.String) EnumValue((string)slice, new[] { "auto", "meta" }, path + ".nineSlice");
                else if (slice is JObject borders)
                {
                    foreach (var field in borders.Properties())
                        if (!new[] { "left", "right", "top", "bottom" }.Contains(field.Name) || Number(borders, field.Name) < 0)
                            throw new ArgumentException(path + ": invalid nineSlice border");
                }
                else if (Number(node, "nineSlice") < 0) throw new ArgumentException(path + ": negative nineSlice");
            }
        }

        // Boxes use the schema's native top-left coordinate system. ScrollRect applies scrolling separately.
        public Dictionary<string, Rect> Resolve(Vector2 canvasSize, Dictionary<string, Rect> reference)
        {
            var result = new Dictionary<string, Rect>(StringComparer.Ordinal);
            var root = Nodes["root"];
            var rootSize = Size((JObject)root["size"], "root");
            var position = Pair(root["position"] as JObject);
            if (rootSize == ReferenceSize && position == Vector2.zero) rootSize = canvasSize;
            result["root"] = new Rect(position, rootSize);
            ResolveChildren("root", result, reference);
            return result;
        }

        private void ResolveChildren(string path, Dictionary<string, Rect> boxes, Dictionary<string, Rect> reference)
        {
            var parent = Nodes[path];
            var parentSize = boxes[path].size;
            var children = Children(parent).ToList();
            var group = parent["layout"] as JObject;
            var positions = group == null ? null : Layout(children, group, parentSize);
            for (var i = 0; i < children.Count; i++)
            {
                var node = children[i];
                var childPath = path + "/" + Text(node, "name");
                var size = Size((JObject)node["size"], childPath);
                var pos = Pair(node["position"] as JObject);
                var offset = Pair(node["offset"] as JObject);
                var responsive = node["responsive"] as JObject;
                if (responsive != null)
                {
                    var start = Vector2.zero;
                    var extent = parentSize;
                    if (Bool(responsive, "safeArea"))
                    {
                        var safe = Json["canvas"]["safeArea"] as JObject;
                        start = new Vector2((float)Number(safe, "left"), (float)Number(safe, "top"));
                        extent -= start + new Vector2((float)Number(safe, "right"), (float)Number(safe, "bottom"));
                    }
                    pos = Round(start + Vector2.Scale(extent, Pair((JObject)responsive["min"])) + Pair((JObject)responsive["offsetMin"]));
                    var end = Round(start + Vector2.Scale(extent, Pair((JObject)responsive["max"])) + Pair((JObject)responsive["offsetMax"]));
                    size = end - pos;
                    if (size.x <= 0 || size.y <= 0) throw new ArgumentException(childPath + ": responsive size is non-positive");
                }
                else if (positions != null) pos = positions[i];
                else
                {
                    var isScrollContent = parent["scroll"] is JObject ownerScroll && Text(node, "name") == Text(ownerScroll, "content");
                    var delta = reference == null || isScrollContent ? Vector2.zero : parentSize - reference[path].size;
                    var anchor = Anchor(node);
                    pos.x = node["align"] != null ? Align(parentSize.x, size.x, Text(node, "align"), pos.x) : pos.x + delta.x * anchor.x;
                    pos.y = node["vAlign"] != null ? Align(parentSize.y, size.y, Text(node, "vAlign"), pos.y) : pos.y + delta.y * (1 - anchor.y);
                    pos += offset;
                }
                if (parent["scroll"] is JObject scroll && Text(node, "name") == Text(scroll, "content") && pos != Vector2.zero)
                    throw new ArgumentException(childPath + ": scroll content must start at (0,0)");
                boxes[childPath] = new Rect(Round(pos), Round(size));
                ResolveChildren(childPath, boxes, reference);
            }
        }

        private static List<Vector2> Layout(List<JObject> children, JObject layout, Vector2 parent)
        {
            var row = Text(layout, "type") == "row";
            var pad = layout["padding"] is JObject p ? Pair(p) : Vector2.one * (float)Number(layout, "padding");
            var inner = Vector2.Max(Vector2.zero, parent - pad * 2);
            var sizes = children.Select(c => Size((JObject)c["size"], "layout child")).ToArray();
            var main = row ? inner.x : inner.y;
            var total = sizes.Sum(s => row ? s.x : s.y);
            var leftover = Math.Max(0, main - total);
            var distribution = Text(layout, "align", "start");
            if (!new[] { "space-between", "space-around", "space-evenly" }.Contains(distribution) && Text(layout, "spacing") == "even") distribution = "space-evenly";
            var count = children.Count;
            var gaps = new float[Math.Max(0, count - 1)];
            var lead = 0f;
            Func<int, int, float> slot = (index, slots) => slots <= 0 ? 0 : (float)(Math.Floor((index + 1) * leftover / slots) - Math.Floor(index * leftover / slots));
            if (distribution == "space-between") { for (var i = 0; i < gaps.Length; i++) gaps[i] = slot(i, count - 1); }
            else if (distribution == "space-around")
            {
                lead = slot(0, 2 * count); for (var i = 0; i < gaps.Length; i++) gaps[i] = slot(2 * i + 1, 2 * count) + slot(2 * i + 2, 2 * count);
            }
            else if (distribution == "space-evenly")
            {
                lead = slot(0, count + 1); for (var i = 0; i < gaps.Length; i++) gaps[i] = slot(i + 1, count + 1);
            }
            else
            {
                var spacing = (float)Number(layout, "spacing");
                for (var i = 0; i < gaps.Length; i++) gaps[i] = spacing;
                lead = Mathf.Max(0, Align(main, total + spacing * Math.Max(0, count - 1), distribution, 0));
            }
            var result = new List<Vector2>();
            for (var i = 0; i < count; i++)
            {
                var cross = Align(row ? inner.y : inner.x, row ? sizes[i].y : sizes[i].x, Text(children[i], row ? "vAlign" : "align", Text(layout, "vAlign", "start")), 0);
                result.Add((row ? new Vector2(pad.x + lead, pad.y + cross) : new Vector2(pad.x + cross, pad.y + lead)) + Pair(children[i]["offset"] as JObject));
                lead += (row ? sizes[i].x : sizes[i].y) + (i < gaps.Length ? gaps[i] : 0);
            }
            return result;
        }

        public static Vector2 Anchor(JObject node)
        {
            var a = node["anchor"] as JObject;
            return new Vector2(Text(a, "horizontal") == "right" ? 1 : Text(a, "horizontal") == "center" ? .5f : 0,
                Text(a, "vertical") == "bottom" ? 0 : Text(a, "vertical") == "middle" ? .5f : 1);
        }
        public static float Align(float parent, float size, string alignment, float fallback)
        {
            if (new[] { "start", "left", "top" }.Contains(alignment)) return 0;
            if (new[] { "center", "middle" }.Contains(alignment)) return (float)Math.Floor((parent - size) / 2);
            if (new[] { "end", "right", "bottom" }.Contains(alignment)) return parent - size;
            return fallback;
        }
        public static JObject Object(JToken token, string label) => token as JObject ?? throw new ArgumentException(label + " must be an object");
        public static IEnumerable<JObject> Children(JObject node)
        {
            var token = node["children"];
            if (token == null || token.Type == JTokenType.Null) yield break;
            if (!(token is JArray array)) throw new ArgumentException("children must be an array");
            foreach (var item in array) yield return Object(item, "child");
        }
        public static string Text(JObject obj, string key, string fallback = "") => obj?[key] == null ? fallback : obj[key].Type == JTokenType.String ? (string)obj[key] : obj[key].ToString(Formatting.None);
        public static double Number(JObject obj, string key, double fallback = 0)
        {
            var token = obj?[key];
            if (token == null) { if (double.IsNaN(fallback)) throw new ArgumentException(key + " is required"); return fallback; }
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float) throw new ArgumentException(key + " must be a number");
            var value = (double)token;
            if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > float.MaxValue) throw new ArgumentException(key + " must be finite");
            return value;
        }
        public static bool Bool(JObject obj, string key, bool fallback = false)
        {
            if (obj?[key] == null) return fallback;
            if (obj[key].Type != JTokenType.Boolean) throw new ArgumentException(key + " must be a boolean");
            return (bool)obj[key];
        }
        public static Vector2 Pair(JObject obj) => new Vector2((float)Number(obj, "x"), (float)Number(obj, "y"));
        public static Vector2 Size(JObject obj, string label)
        {
            var result = new Vector2((float)Number(obj, "width", double.NaN), (float)Number(obj, "height", double.NaN));
            if (result.x <= 0 || result.y <= 0) throw new ArgumentException(label + ": size must be positive");
            return Round(result);
        }
        public static Vector2 Round(Vector2 v) => new Vector2((float)Math.Round(v.x, MidpointRounding.ToEven), (float)Math.Round(v.y, MidpointRounding.ToEven));
        public static void EnumValue(string value, IEnumerable<string> allowed, string path)
        { if (!allowed.Contains(value)) throw new ArgumentException(path + ": unsupported value " + value); }
        public static Color ParseColor(string value)
        { if (!ColorUtility.TryParseHtmlString(value, out var color)) throw new ArgumentException("Invalid color: " + value); return color; }
        public static bool HasSlice(JToken token) => token != null && token.Type != JTokenType.Null && !(token.Type == JTokenType.Boolean && !(bool)token) && !(token.Type == JTokenType.Integer && (int)token == 0);
        public static string FormatProgress(JObject spec)
        {
            var min = Number(spec, "min"); var max = Number(spec, "max", 1); var value = Number(spec, "value");
            var template = Text(spec, "format", "{percent:.0f}%");
            // Match the numeric forms used by the schema; reject unsupported Python formatting explicitly.
            var escaped = template.Replace("{{", "\u0001").Replace("}}", "\u0002");
            var formatted = Regex.Replace(escaped, @"\{(value|min|max|percent)(?::([^{}]*))?\}", m =>
            {
                var number = m.Groups[1].Value == "min" ? min : m.Groups[1].Value == "max" ? max : m.Groups[1].Value == "percent" ? (value - min) / (max - min) * 100 : value;
                var format = m.Groups[2].Value;
                if (format == "") return number.ToString("G", CultureInfo.InvariantCulture);
                var match = Regex.Match(format, @"^(,)?(?:\.(\d{1,2}))?([fFgGeE%])$");
                if (!match.Success) throw new ArgumentException("Unsupported progress numeric format: " + format);
                var code = match.Groups[3].Value.ToUpperInvariant();
                var precision = match.Groups[2].Success ? match.Groups[2].Value : "6";
                if (code == "%") return (number * 100).ToString("F" + precision, CultureInfo.InvariantCulture) + "%";
                if (match.Groups[1].Success && code == "F") code = "N";
                return number.ToString(code + precision, CultureInfo.InvariantCulture);
            });
            if (formatted.Contains("{") || formatted.Contains("}")) throw new ArgumentException("Unsupported progress format: " + template);
            return formatted.Replace('\u0001', '{').Replace('\u0002', '}');
        }
    }
}
