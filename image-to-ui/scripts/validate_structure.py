"""
Validate ui_structure.json before bbox annotation and rendering.

Checks:
  - JSON shape, required fields, positive sizes, and duplicate element paths
  - canvas size against the design image
  - asset references against the sliced asset directory
  - layout, alignment, color, opacity, and nine-slice field sanity

Usage:
    py -B validate_structure.py --structure ui_structure.json --design design.png --assets sprite_dir --report out/validate_report.json
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from collections import defaultdict
from pathlib import Path
from typing import Any

from PIL import Image


IMAGE_EXTS = {".png", ".jpg", ".jpeg"}
ELEMENT_TYPES = {"container", "image", "text", "button", "overlay", "rect"}
LAYOUT_TYPES = {"row", "column"}
LAYOUT_SPACING = {"even"}
LAYOUT_ALIGN = {
    "start", "center", "middle", "end", "left", "right", "top", "bottom",
    "space-between", "space-around", "space-evenly",
}
ALIGN_X = {"left", "center", "right", "start", "end"}
ALIGN_Y = {"top", "middle", "bottom", "start", "end", "center"}
TEXT_ALIGN = {"left", "center", "right"}
TEXT_VALIGN = {"top", "middle", "bottom", "start", "end", "center"}
COLOR_RE = re.compile(r"^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$")


class Reporter:
    def __init__(self) -> None:
        self.errors: list[str] = []
        self.warnings: list[str] = []

    def error(self, path: str, message: str) -> None:
        self.errors.append(f"{path}: {message}")

    def warn(self, path: str, message: str) -> None:
        self.warnings.append(f"{path}: {message}")


def is_number(value: Any) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool)


def positive_number(value: Any) -> bool:
    return is_number(value) and value > 0


def read_json(path: Path) -> dict[str, Any]:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        raise SystemExit(f"Invalid JSON in {path}: {exc}") from exc
    if not isinstance(data, dict):
        raise SystemExit("Structure root must be a JSON object")
    return data


def build_asset_index(assets_dir: Path) -> tuple[dict[str, Path], set[str], dict[str, dict[str, int]]]:
    by_basename: dict[str, list[Path]] = defaultdict(list)
    index: dict[str, Path] = {}
    borders: dict[str, dict[str, int]] = {}

    for path in assets_dir.rglob("*"):
        if not path.is_file() or path.suffix.lower() not in IMAGE_EXTS:
            continue
        rel = path.relative_to(assets_dir).as_posix()
        rel_key = rel.lower()
        name_key = path.name.lower()
        index[rel_key] = path
        by_basename[name_key].append(path)
        border = read_unity_sprite_border(path)
        if border:
            borders[rel_key] = border

    duplicate_basenames = {name for name, paths in by_basename.items() if len(paths) > 1}
    for name, paths in by_basename.items():
        if len(paths) == 1:
            index[name] = paths[0]
            border = read_unity_sprite_border(paths[0])
            if border:
                borders[name] = border
    return index, duplicate_basenames, borders


def read_unity_sprite_border(path: Path) -> dict[str, int] | None:
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


def check_xy_dict(obj: Any, path: str, field: str, reporter: Reporter) -> None:
    if not isinstance(obj, dict):
        reporter.error(path, f"{field} must be an object")
        return
    for key in ("x", "y"):
        if key in obj and not is_number(obj[key]):
            reporter.error(path, f"{field}.{key} must be a number")


def check_size(obj: Any, path: str, reporter: Reporter) -> None:
    if not isinstance(obj, dict):
        reporter.error(path, "size must be an object")
        return
    for key in ("width", "height"):
        if key not in obj:
            reporter.error(path, f"size.{key} is required")
        elif not positive_number(obj[key]):
            reporter.error(path, f"size.{key} must be a positive number")


def check_color(value: Any, path: str, field: str, reporter: Reporter) -> None:
    if not isinstance(value, str) or not COLOR_RE.match(value):
        reporter.warn(path, f"{field} should be #RRGGBB or #RRGGBBAA")


def check_layout(layout: Any, path: str, reporter: Reporter) -> None:
    if not isinstance(layout, dict):
        reporter.error(path, "layout must be an object")
        return
    ltype = layout.get("type", "row")
    if ltype not in LAYOUT_TYPES:
        reporter.error(path, "layout.type must be row or column")
    spacing = layout.get("spacing", 0)
    if isinstance(spacing, str) and spacing not in LAYOUT_SPACING:
        reporter.error(path, "layout.spacing string must be even")
    elif not isinstance(spacing, str) and not is_number(spacing):
        reporter.error(path, "layout.spacing must be a number or even")
    if "padding" in layout:
        padding = layout["padding"]
        if is_number(padding):
            pass
        elif isinstance(padding, dict):
            for key in ("x", "y"):
                if key in padding and not is_number(padding[key]):
                    reporter.error(path, f"layout.padding.{key} must be a number")
        else:
            reporter.error(path, "layout.padding must be a number or {x,y}")
    if "align" in layout and layout["align"] not in LAYOUT_ALIGN:
        reporter.warn(path, f"unknown layout.align: {layout['align']}")
    if "vAlign" in layout and layout["vAlign"] not in LAYOUT_ALIGN:
        reporter.warn(path, f"unknown layout.vAlign: {layout['vAlign']}")


def validate_tree(
    elem: Any,
    path: str,
    parent_layout_type: str | None,
    reporter: Reporter,
    asset_index: dict[str, Path],
    duplicate_basenames: set[str],
    asset_borders: dict[str, dict[str, int]],
    seen_paths: set[str],
    stats: dict[str, int],
) -> None:
    if not isinstance(elem, dict):
        reporter.error(path, "element must be an object")
        return

    name = elem.get("name")
    if not isinstance(name, str) or not name:
        reporter.error(path, "name is required")
        name = "?"
    etype = elem.get("type")
    if etype not in ELEMENT_TYPES:
        reporter.error(path, f"type must be one of {sorted(ELEMENT_TYPES)}")
    if "size" not in elem:
        reporter.error(path, "size is required")
    else:
        check_size(elem["size"], path, reporter)

    if path in seen_paths:
        reporter.error(path, "duplicate element path")
    seen_paths.add(path)
    stats["elements"] += 1
    if etype == "text":
        stats["texts"] += 1
    if elem.get("layout"):
        stats["layouts"] += 1
    if "position" in elem:
        stats["positioned"] += 1
        check_xy_dict(elem["position"], path, "position", reporter)
    elif parent_layout_type is None and path != "root" and not (elem.get("align") or elem.get("vAlign")):
        reporter.warn(path, "missing position outside layout/alignment derivation")

    if parent_layout_type == "row" and "align" in elem:
        reporter.warn(path, "child align is ignored in row layout; use vAlign or offset")
    if parent_layout_type == "column" and "vAlign" in elem:
        reporter.warn(path, "child vAlign is ignored in column layout; use align or offset")
    if parent_layout_type is not None and "position" in elem:
        reporter.warn(path, "position is ignored inside parent layout; use offset")

    if "align" in elem and elem["align"] not in ALIGN_X:
        reporter.warn(path, f"unknown align: {elem['align']}")
    if "vAlign" in elem and elem["vAlign"] not in ALIGN_Y:
        reporter.warn(path, f"unknown vAlign: {elem['vAlign']}")
    if "offset" in elem:
        check_xy_dict(elem["offset"], path, "offset", reporter)
    if "opacity" in elem:
        opacity = elem["opacity"]
        if not is_number(opacity) or opacity < 0 or opacity > 1:
            reporter.error(path, "opacity must be between 0 and 1")
    if "color" in elem:
        check_color(elem["color"], path, "color", reporter)
    if etype == "overlay" and not (elem.get("asset") or elem.get("color")):
        reporter.error(path, "overlay must have color or asset")
    if etype == "rect":
        if not elem.get("color"):
            reporter.error(path, "rect must have color")
        if elem.get("asset"):
            reporter.warn(path, "rect should not use asset; use image for sliced sprites")

    asset = elem.get("asset")
    if asset:
        stats["asset_refs"] += 1
        if not isinstance(asset, str):
            reporter.error(path, "asset must be a string")
        else:
            key = asset.replace("\\", "/").lower()
            if "/" not in key and key in duplicate_basenames:
                reporter.error(path, f"asset basename is ambiguous, use relative path: {asset}")
            elif key not in asset_index:
                reporter.error(path, f"asset not found: {asset}")
            nine_slice = elem.get("nineSlice")
            if nine_slice in ("meta", "auto", True) and key not in asset_borders:
                reporter.warn(path, f"nineSlice {nine_slice!r} has no Unity spriteBorder metadata; renderer will infer margins")
    elif etype == "image":
        reporter.warn(path, "image element has no asset")

    if "nineSlice" in elem:
        ns = elem["nineSlice"]
        if isinstance(ns, bool) or isinstance(ns, (int, float)) or ns in ("auto", "meta"):
            pass
        elif isinstance(ns, dict):
            for key in ("left", "top", "right", "bottom"):
                if key in ns and not is_number(ns[key]):
                    reporter.error(path, f"nineSlice.{key} must be a number")
        else:
            reporter.error(path, "nineSlice must be bool, number, auto/meta, or margins object")

    if etype == "text":
        if "text" not in elem:
            reporter.warn(path, "text element has no text field")
        if "fontSize" in elem and not positive_number(elem["fontSize"]):
            reporter.error(path, "fontSize must be positive")
        if "alignment" in elem and elem["alignment"] not in TEXT_ALIGN:
            reporter.warn(path, f"unknown text alignment: {elem['alignment']}")
        if "textVAlign" in elem and elem["textVAlign"] not in TEXT_VALIGN:
            reporter.warn(path, f"unknown textVAlign: {elem['textVAlign']}")

    layout_type = None
    if "layout" in elem:
        check_layout(elem["layout"], path, reporter)
        if isinstance(elem["layout"], dict):
            layout_type = elem["layout"].get("type", "row")

    children = elem.get("children") or []
    if not isinstance(children, list):
        reporter.error(path, "children must be a list")
        return

    sibling_names: dict[str, int] = defaultdict(int)
    for child in children:
        if isinstance(child, dict):
            child_name = child.get("name")
            if isinstance(child_name, str):
                sibling_names[child_name] += 1
    for child_name, count in sibling_names.items():
        if count > 1:
            reporter.error(path, f"duplicate child name: {child_name}")

    for child in children:
        child_name = child.get("name", "?") if isinstance(child, dict) else "?"
        validate_tree(
            child,
            f"{path}/{child_name}",
            layout_type,
            reporter,
            asset_index,
            duplicate_basenames,
            asset_borders,
            seen_paths,
            stats,
        )


def validate_structure(structure: dict[str, Any], design_path: Path, assets_dir: Path) -> tuple[Reporter, dict[str, int]]:
    reporter = Reporter()
    stats = defaultdict(int)

    canvas = structure.get("canvas")
    if not isinstance(canvas, dict):
        reporter.error("canvas", "canvas object is required")
    else:
        for key in ("width", "height"):
            if key not in canvas or not positive_number(canvas[key]):
                reporter.error("canvas", f"{key} must be a positive number")
        if design_path.exists() and positive_number(canvas.get("width")) and positive_number(canvas.get("height")):
            with Image.open(design_path) as img:
                if (int(canvas["width"]), int(canvas["height"])) != img.size:
                    reporter.error(
                        "canvas",
                        f"canvas {canvas['width']}x{canvas['height']} does not match design {img.width}x{img.height}",
                    )

    if "root" not in structure:
        reporter.error("root", "root object is required")
        return reporter, stats

    asset_index, duplicate_basenames, asset_borders = build_asset_index(assets_dir)
    validate_tree(
        structure["root"],
        "root",
        None,
        reporter,
        asset_index,
        duplicate_basenames,
        asset_borders,
        set(),
        stats,
    )
    return reporter, stats


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--structure", required=True, help="Path to ui_structure.json")
    ap.add_argument("--design", required=True, help="Path to the source design image")
    ap.add_argument("--assets", required=True, help="Directory containing sliced PNG assets")
    ap.add_argument("--json", action="store_true", help="Print machine-readable validation result")
    ap.add_argument("--report", help="Path to write the full validation JSON report")
    ap.add_argument("--warnings-as-errors", action="store_true",
                    help="Exit non-zero when warnings are present")
    args = ap.parse_args()

    structure_path = Path(args.structure)
    design_path = Path(args.design)
    assets_dir = Path(args.assets)
    if not structure_path.exists():
        raise SystemExit(f"Structure not found: {structure_path}")
    if not design_path.exists():
        raise SystemExit(f"Design image not found: {design_path}")
    if not assets_dir.exists():
        raise SystemExit(f"Assets directory not found: {assets_dir}")

    structure = read_json(structure_path)
    reporter, stats = validate_structure(structure, design_path, assets_dir)
    result = {
        "valid": not reporter.errors and not (args.warnings_as_errors and reporter.warnings),
        "error_count": len(reporter.errors),
        "warning_count": len(reporter.warnings),
        "stats": dict(stats),
        "errors": reporter.errors,
        "warnings": reporter.warnings,
    }

    if args.report:
        report_path = Path(args.report)
        report_path.parent.mkdir(parents=True, exist_ok=True)
        report_path.write_text(
            json.dumps(result, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )

    if args.json:
        print(json.dumps(result, ensure_ascii=False, indent=2))
    else:
        print(f"Validated {structure_path}")
        print(f"  elements: {stats['elements']}")
        print(f"  asset refs: {stats['asset_refs']}")
        print(f"  layouts: {stats['layouts']}")
        print(f"  text elements: {stats['texts']}")
        print(f"  errors: {len(reporter.errors)}")
        print(f"  warnings: {len(reporter.warnings)}")
        if args.report:
            print(f"  report: {args.report}")
        for item in reporter.errors[:10]:
            print(f"  [error] {item}")
        if len(reporter.errors) > 10:
            print(f"  ... {len(reporter.errors) - 10} more errors in report")
        for item in reporter.warnings[:10]:
            print(f"  [warn] {item}")
        if len(reporter.warnings) > 10:
            print(f"  ... {len(reporter.warnings) - 10} more warnings in report")
        if result["valid"]:
            print("Structure is valid")

    sys.exit(0 if result["valid"] else 1)


if __name__ == "__main__":
    main()
