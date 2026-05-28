"""
Render a side-by-side comparison between a UI design image and the reconstruction
produced from ui_structure.json + sliced assets.

Usage:
    py -B render_comparison.py \
        --design path/to/design.png \
        --structure path/to/ui_structure.json \
        --assets path/to/sprite_dir \
        --output path/to/comparison.png

The left panel shows the original design, the right panel shows the reconstruction.
Both are scaled to the same height for visual comparison.
"""

import argparse
import json
import os
import re
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

# Make sibling layout.py importable when run from anywhere
sys.path.insert(0, str(Path(__file__).resolve().parent))
import layout as layout_mod  # noqa: E402
from annotate_grid import draw_grid  # noqa: E402


# --- Asset loading ---------------------------------------------------------

class AssetCache:
    """Loads images from the assets directory tree on demand."""

    def __init__(self, assets_dir: Path):
        self.assets_dir = assets_dir
        # Build case-insensitive indexes so "Foo.Png" matches "foo.png".
        # Basenames are accepted when unique; relative paths disambiguate
        # duplicate filenames in nested asset folders.
        self._index = {}
        self._borders = {}
        self._file_count = 0
        self._border_file_count = 0
        duplicates = set()
        for p in assets_dir.rglob("*"):
            if p.is_file() and p.suffix.lower() in (".png", ".jpg", ".jpeg"):
                self._file_count += 1
                rel_key = p.relative_to(assets_dir).as_posix().lower()
                name_key = p.name.lower()
                self._index[rel_key] = p
                if name_key in self._index and self._index[name_key] != p:
                    duplicates.add(name_key)
                else:
                    self._index[name_key] = p
                border = self._read_unity_sprite_border(p)
                if border:
                    self._border_file_count += 1
                    self._borders[rel_key] = border
                    if name_key not in duplicates:
                        self._borders[name_key] = border
        for name_key in duplicates:
            self._index.pop(name_key, None)
            self._borders.pop(name_key, None)
        self._cache = {}

    @staticmethod
    def _read_unity_sprite_border(path: Path):
        """Read Unity spriteBorder from the sidecar .meta file, if present.

        Unity stores Sprite.border as x=left, y=bottom, z=right, w=top.
        The renderer wants left/top/right/bottom.
        """
        meta_path = Path(str(path) + ".meta")
        if not meta_path.exists():
            return None
        try:
            text = meta_path.read_text(encoding="utf-8")
        except OSError:
            return None
        match = re.search(
            r"spriteBorder:\s*\{x:\s*([^,]+),\s*y:\s*([^,]+),\s*z:\s*([^,]+),\s*w:\s*([^}]+)\}",
            text,
        )
        if not match:
            return None
        left, bottom, right, top = (int(float(v.strip())) for v in match.groups())
        if left == top == right == bottom == 0:
            return None
        return {"left": left, "top": top, "right": right, "bottom": bottom}

    def get(self, filename: str):
        if not filename:
            return None
        key = filename.replace("\\", "/").lower()
        if key in self._cache:
            return self._cache[key]
        path = self._index.get(key)
        if path is None:
            print(f"  [warn] asset not found: {filename}", file=sys.stderr)
            self._cache[key] = None
            return None
        img = Image.open(path).convert("RGBA")
        self._cache[key] = img
        return img

    def border_for(self, filename: str):
        if not filename:
            return None
        return self._borders.get(filename.replace("\\", "/").lower())


# --- Rendering helpers -----------------------------------------------------

def hex_to_rgba(hex_color: str, alpha: float = 1.0):
    if not hex_color:
        return None
    s = hex_color.lstrip("#")
    if len(s) == 6:
        r, g, b = int(s[0:2], 16), int(s[2:4], 16), int(s[4:6], 16)
        a = int(255 * alpha)
    elif len(s) == 8:
        r, g, b, a = (int(s[i:i + 2], 16) for i in (0, 2, 4, 6))
        a = int(a * alpha)
    else:
        return None
    return (r, g, b, a)


def apply_tint(img: Image.Image, hex_color: str) -> Image.Image:
    """Multiply-tint an RGBA image by a hex color (preserves alpha)."""
    if not hex_color:
        return img
    tint = hex_to_rgba(hex_color)
    if tint is None:
        return img
    tr, tg, tb, _ = tint
    arr = np.array(img, dtype=np.float32)
    arr[..., 0] *= tr / 255.0
    arr[..., 1] *= tg / 255.0
    arr[..., 2] *= tb / 255.0
    arr = np.clip(arr, 0, 255).astype(np.uint8)
    return Image.fromarray(arr, mode="RGBA")


def render_nine_slice(src: Image.Image, target_w: int, target_h: int,
                      margins: dict) -> Image.Image:
    """Render `src` at (target_w, target_h) using 9-slice scaling.

    margins: dict with integer keys "left", "top", "right", "bottom" giving
    the non-stretching corner widths/heights in source-pixel units. The four
    corners are pasted unchanged, the four edges stretch along one axis, and
    the center stretches on both axes.
    """
    sw, sh = src.size
    l = int(margins.get("left", 0))
    t = int(margins.get("top", 0))
    r = int(margins.get("right", 0))
    b = int(margins.get("bottom", 0))

    def clamp_pair(first: int, second: int, source_extent: int,
                   target_extent: int) -> tuple[int, int]:
        # Some Unity sprites intentionally use borders whose pair sums to the
        # full source dimension. PIL crop/resize needs a non-empty stretch
        # strip, so keep at least one source and target pixel for the center.
        source_limit = max(0, source_extent - 1)
        target_limit = max(0, target_extent - 1)
        first = max(0, min(first, source_limit, target_limit))
        second = max(0, min(second, source_limit, target_limit))
        for limit in (source_limit, target_limit):
            total = first + second
            if limit > 0 and total > limit:
                first = int(round(first * limit / total))
                second = limit - first
        return first, second

    # Clamp margins so they don't overlap in either source or target.
    l, r = clamp_pair(l, r, sw, target_w)
    t, b = clamp_pair(t, b, sh, target_h)

    out = Image.new("RGBA", (max(1, target_w), max(1, target_h)), (0, 0, 0, 0))

    # Source crops: corners, edges, center
    tl = src.crop((0, 0, l, t))
    tr = src.crop((sw - r, 0, sw, t))
    bl = src.crop((0, sh - b, l, sh))
    br = src.crop((sw - r, sh - b, sw, sh))

    top_edge = src.crop((l, 0, sw - r, t))
    bot_edge = src.crop((l, sh - b, sw - r, sh))
    left_edge = src.crop((0, t, l, sh - b))
    right_edge = src.crop((sw - r, t, sw, sh - b))

    center = src.crop((l, t, sw - r, sh - b))

    # Stretch sizes in target
    cx = target_w - l - r
    cy = target_h - t - b

    # Corners (no stretch)
    if l > 0 and t > 0:
        out.paste(tl, (0, 0))
    if r > 0 and t > 0:
        out.paste(tr, (target_w - r, 0))
    if l > 0 and b > 0:
        out.paste(bl, (0, target_h - b))
    if r > 0 and b > 0:
        out.paste(br, (target_w - r, target_h - b))

    # Edges (stretch one axis)
    if cx > 0 and t > 0 and top_edge.width > 0:
        out.paste(top_edge.resize((cx, t), Image.LANCZOS), (l, 0))
    if cx > 0 and b > 0 and bot_edge.width > 0:
        out.paste(bot_edge.resize((cx, b), Image.LANCZOS), (l, target_h - b))
    if cy > 0 and l > 0 and left_edge.height > 0:
        out.paste(left_edge.resize((l, cy), Image.LANCZOS), (0, t))
    if cy > 0 and r > 0 and right_edge.height > 0:
        out.paste(right_edge.resize((r, cy), Image.LANCZOS), (target_w - r, t))

    # Center (stretch both)
    if cx > 0 and cy > 0 and center.width > 0 and center.height > 0:
        out.paste(center.resize((cx, cy), Image.LANCZOS), (l, t))

    return out


def resolve_nine_slice_margins(nine_slice, src_w: int, src_h: int,
                               meta_border: dict | None = None) -> dict:
    """Convert `nineSlice` JSON value into explicit margins dict.

    Accepts:
      - True / "auto" / "meta": use Unity .meta spriteBorder if present;
                     otherwise use min(sw, sh) // 4, capped at 60 px
      - int N:       N pixels on all 4 sides
      - dict:        {"left": ..., "top": ..., "right": ..., "bottom": ...}
                     Missing keys default to 0 (useful for things like a bar
                     that only stretches horizontally: {"left": 20, "right": 20})
    """
    if nine_slice in (True, "auto", "meta"):
        if meta_border:
            return meta_border
        m = max(4, min(60, min(src_w, src_h) // 4))
        return {"left": m, "top": m, "right": m, "bottom": m}
    if isinstance(nine_slice, (int, float)):
        m = int(nine_slice)
        return {"left": m, "top": m, "right": m, "bottom": m}
    if isinstance(nine_slice, dict):
        return {
            "left": int(nine_slice.get("left", 0)),
            "top": int(nine_slice.get("top", 0)),
            "right": int(nine_slice.get("right", 0)),
            "bottom": int(nine_slice.get("bottom", 0)),
        }
    return {"left": 0, "top": 0, "right": 0, "bottom": 0}


def apply_opacity(img: Image.Image, opacity: float) -> Image.Image:
    if opacity is None or opacity >= 1.0:
        return img
    arr = np.array(img, dtype=np.float32)
    arr[..., 3] *= max(0.0, min(1.0, opacity))
    arr = np.clip(arr, 0, 255).astype(np.uint8)
    return Image.fromarray(arr, mode="RGBA")


FONT_SEARCH_DIRS: list[Path] = []


def find_font(size: int, preferred: str | None = None) -> ImageFont.FreeTypeFont:
    # Try a few common fonts; fall back to default bitmap font
    candidates = []
    if preferred:
        preferred_path = Path(preferred)
        candidates.append(str(preferred_path))
        for root in FONT_SEARCH_DIRS:
            candidates.append(str(root / preferred))
        if preferred_path.suffix:
            stem = preferred_path.stem
        else:
            stem = preferred
            for root in FONT_SEARCH_DIRS:
                candidates.extend(str(p) for p in root.glob(f"{stem}*.ttf"))
                candidates.extend(str(p) for p in root.glob(f"{stem}*.otf"))
    candidates = [
        *candidates,
        "C:/Windows/Fonts/arialbd.ttf",
        "C:/Windows/Fonts/arial.ttf",
        "C:/Windows/Fonts/segoeui.ttf",
        "C:/Windows/Fonts/msyh.ttc",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/System/Library/Fonts/Helvetica.ttc",
    ]
    for c in candidates:
        if os.path.exists(c):
            try:
                return ImageFont.truetype(c, size)
            except Exception:
                continue
    return ImageFont.load_default()


def draw_text(canvas: Image.Image, elem: dict, origin_x: int, origin_y: int):
    text = elem.get("text", "")
    if not text:
        return
    pos = elem.get("position", {"x": 0, "y": 0})
    size = elem.get("size", {"width": 100, "height": 40})
    x = origin_x + int(pos.get("x", 0))
    y = origin_y + int(pos.get("y", 0))
    w = int(size.get("width", 100))
    h = int(size.get("height", 40))

    font_size = int(elem.get("fontSize", 20))
    color_rgba = hex_to_rgba(elem.get("color", "#000000"), elem.get("opacity", 1.0))
    alignment = elem.get("alignment", "left")
    text_v_align = elem.get("textVAlign", "middle")
    line_h = int(elem.get("lineHeight", font_size + 4))
    stroke_width = int(elem.get("strokeWidth", 0))
    stroke_rgba = hex_to_rgba(elem.get("strokeColor", "#000000"), elem.get("opacity", 1.0))

    font = find_font(font_size, elem.get("fontFamily") or elem.get("font"))
    draw = ImageDraw.Draw(canvas)

    lines = text.split("\n")
    total_h = line_h * len(lines)
    if text_v_align in ("top", "start"):
        start_y = y
    elif text_v_align in ("bottom", "end"):
        start_y = y + max(0, h - total_h)
    else:
        start_y = y + max(0, (h - total_h) // 2)

    for i, line in enumerate(lines):
        try:
            bbox = font.getbbox(line)
            line_w = bbox[2] - bbox[0]
        except Exception:
            line_w = font_size * len(line) // 2

        if alignment == "center":
            line_x = x + max(0, (w - line_w) // 2)
        elif alignment == "right":
            line_x = x + w - line_w
        else:
            line_x = x

        draw.text(
            (line_x, start_y + i * line_h),
            line,
            font=font,
            fill=color_rgba,
            stroke_width=stroke_width,
            stroke_fill=stroke_rgba,
        )


def render_element(canvas: Image.Image, elem: dict, origin_x: int, origin_y: int,
                   assets: AssetCache):
    """Recursively render an element and its children onto the canvas.

    Position source priority:
      1. `_rel` annotated by layout.resolve_positions (preferred; honors
         layout/align/offset)
      2. `position` field (legacy / unresolved)
    """
    etype = elem.get("type", "container")
    rel = elem.get("_rel")
    if rel is not None:
        rx, ry, w, h = int(rel[0]), int(rel[1]), int(rel[2]), int(rel[3])
    else:
        pos = elem.get("position", {"x": 0, "y": 0})
        size = elem.get("size", {"width": 0, "height": 0})
        rx, ry = int(pos.get("x", 0)), int(pos.get("y", 0))
        w, h = int(size.get("width", 0)), int(size.get("height", 0))

    ax = origin_x + rx
    ay = origin_y + ry

    # Draw solid color rectangle if there's a color but no asset
    asset_name = elem.get("asset")
    color = elem.get("color")
    opacity = float(elem.get("opacity", 1.0))

    if asset_name:
        img = assets.get(asset_name)
        if img is not None:
            nine_slice = elem.get("nineSlice")
            try:
                if nine_slice:
                    margins = resolve_nine_slice_margins(
                        nine_slice,
                        img.width,
                        img.height,
                        assets.border_for(asset_name),
                    )
                    img_resized = render_nine_slice(img, max(1, w), max(1, h), margins)
                else:
                    img_resized = img.resize((max(1, w), max(1, h)), Image.LANCZOS)
            except Exception as exc:
                print(f"  [warn] render failed for {asset_name}: {exc}", file=sys.stderr)
                img_resized = img
            if color:
                img_resized = apply_tint(img_resized, color)
            if opacity < 1.0:
                img_resized = apply_opacity(img_resized, opacity)
            canvas.alpha_composite(img_resized, (ax, ay))
    elif color and etype != "text" and w > 0 and h > 0:
        fill = hex_to_rgba(color, opacity)
        if fill:
            overlay = Image.new("RGBA", (w, h), fill)
            canvas.alpha_composite(overlay, (ax, ay))

    if etype == "text":
        # draw_text reads element.position; rebuild a temp elem with the
        # resolved relative position so legacy code still works.
        temp = dict(elem)
        temp["position"] = {"x": rx, "y": ry}
        temp["size"] = {"width": w, "height": h}
        draw_text(canvas, temp, origin_x, origin_y)

    # Recurse into children
    for child in elem.get("children", []) or []:
        render_element(canvas, child, ax, ay, assets)


# --- Comparison ------------------------------------------------------------

def build_side_by_side(design_img: Image.Image, reconstruction: Image.Image,
                       gap: int = 20, label_h: int = 40) -> Image.Image:
    """Stack design (left) and reconstruction (right) at the same height."""
    target_h = max(design_img.height, reconstruction.height)

    def scale_to_h(im, h):
        ratio = h / im.height
        new_w = max(1, int(im.width * ratio))
        return im.resize((new_w, h), Image.LANCZOS)

    left = scale_to_h(design_img.convert("RGBA"), target_h)
    right = scale_to_h(reconstruction, target_h)

    total_w = left.width + right.width + gap
    total_h = target_h + label_h
    combined = Image.new("RGBA", (total_w, total_h), (240, 240, 240, 255))
    combined.paste(left, (0, label_h), left)
    combined.paste(right, (left.width + gap, label_h), right)

    draw = ImageDraw.Draw(combined)
    font = find_font(22)
    draw.text((left.width // 2 - 40, 8), "Design", font=font, fill=(30, 30, 30, 255))
    draw.text((left.width + gap + right.width // 2 - 90, 8),
              "Reconstruction", font=font, fill=(30, 30, 30, 255))

    return combined


def render_from_structure(structure: dict, assets: AssetCache,
                          background: tuple[int, int, int, int]) -> Image.Image:
    canvas_info = structure.get("canvas", {})
    cw = int(canvas_info.get("width", 720))
    ch = int(canvas_info.get("height", 1560))

    # Resolve layout/align into concrete relative positions on every node.
    # render_element prefers `_rel` when present; this call populates it.
    layout_mod.resolve_positions(structure)

    canvas = Image.new("RGBA", (cw, ch), background)
    root = structure.get("root", {})
    render_element(canvas, root, 0, 0, assets)
    return canvas


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--design", required=True, help="Path to the original design image")
    ap.add_argument("--structure", required=True, help="Path to ui_structure.json")
    ap.add_argument("--assets", required=True, help="Directory containing sliced PNG assets")
    ap.add_argument("--output", required=True, help="Path to save comparison.png")
    ap.add_argument("--no-grid", action="store_true",
                    help="Disable the grid overlay on both panels. By default a "
                         "labeled grid is drawn on the design and the "
                         "reconstruction so positions can be verified against "
                         "the same ruler used in Step 1.")
    ap.add_argument("--background-color", default="#1E1E28",
                    help="Canvas background color for transparent areas "
                         "(default: #1E1E28).")
    ap.add_argument("--transparent-bg", action="store_true",
                    help="Render transparent areas with alpha 0. Useful when "
                         "the structure intentionally skips dimmed scene "
                         "content behind an active popup.")
    args = ap.parse_args()

    design_path = Path(args.design)
    structure_path = Path(args.structure)
    assets_dir = Path(args.assets)
    output_path = Path(args.output)

    if not design_path.exists():
        print(f"Design image not found: {design_path}", file=sys.stderr)
        sys.exit(1)
    if not structure_path.exists():
        print(f"Structure JSON not found: {structure_path}", file=sys.stderr)
        sys.exit(1)
    if not assets_dir.exists():
        print(f"Assets directory not found: {assets_dir}", file=sys.stderr)
        sys.exit(1)

    print(f"Loading structure: {structure_path}")
    structure = json.loads(structure_path.read_text(encoding="utf-8"))

    print(f"Indexing assets: {assets_dir}")
    assets = AssetCache(assets_dir)
    print(f"  {assets._file_count} image files indexed")
    print(f"  {assets._border_file_count} Unity sprite borders indexed")

    global FONT_SEARCH_DIRS
    FONT_SEARCH_DIRS = [
        structure_path.parent,
        structure_path.parent.parent,
        assets_dir,
        assets_dir.parent,
        design_path.parent,
        design_path.parent.parent,
        Path.cwd(),
    ]

    if args.transparent_bg:
        background = (0, 0, 0, 0)
    else:
        background = hex_to_rgba(args.background_color) or (30, 30, 40, 255)

    print("Rendering reconstruction...")
    reconstruction = render_from_structure(structure, assets, background)

    print(f"Loading design image: {design_path}")
    design_img = Image.open(design_path).convert("RGBA")
    canvas = structure.get("canvas") or {}
    cw, ch = canvas.get("width"), canvas.get("height")
    if cw and ch and (cw, ch) != design_img.size:
        print(
            f"  [warn] canvas in structure is {cw}x{ch} but design is "
            f"{design_img.width}x{design_img.height}; grid comparison is not "
            "pixel-accurate until the JSON uses the design's native canvas.",
            file=sys.stderr,
        )

    if not args.no_grid:
        # Same grid (cell ~= 45 px) on both panels so they can be cross-read
        print("Overlaying grid on both panels...")
        design_img = draw_grid(design_img)
        reconstruction = draw_grid(reconstruction)

    print("Building side-by-side comparison...")
    comparison = build_side_by_side(design_img, reconstruction)

    output_path.parent.mkdir(parents=True, exist_ok=True)
    # Save as PNG (RGBA) and also a JPG-friendly version if requested
    comparison.convert("RGB").save(output_path)
    print(f"Saved: {output_path}")


if __name__ == "__main__":
    main()
