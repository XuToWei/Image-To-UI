"""
Shared layout / alignment resolver.

Every script that needs to know the absolute position of every element
(annotate_grid, annotate_element, render_comparison) calls
`resolve_positions(structure)` once, then reads each element's `_abs`
field for its computed absolute (x, y, w, h) in canvas coordinates.

Position decision order for a child:
  1. Parent has `layout` → layout controls the main axis
     - row:    x comes from layout; child `vAlign` may override cross-axis y
     - column: y comes from layout; child `align` may override cross-axis x
  2. No parent layout, child has `align` or `vAlign` → derived from parent
  3. No parent layout, child has explicit `position` → use as-is
  4. Otherwise                                      → (0, 0)

Supported layout types:
  - "row" / "column"  — linear distribution along axis

Layout fields (all optional):
  - type:    "row" | "column"            (required if layout present)
  - spacing: number (fixed px) | "even"  (auto-distribute leftover space)
  - padding: { x: number, y: number }
  - align:   "start" | "center" | "end" | "space-between" | "space-around"
             (main axis)
  - vAlign:  "start" | "middle" | "end"  (cross axis)

Element-level alignment:
  - align:   "left" | "center" | "right"
  - vAlign:  "top"  | "middle" | "bottom"
  - offset:  { x: number, y: number }     — micro-adjustment after alignment

Within a layout group, element-level alignment only affects the cross axis.
Use `offset` for small per-child adjustments; remove the parent layout when a
child needs a fully independent position.

Backwards compatibility:
  - Elements without layout/align use their explicit `position` field as before.
  - This module never deletes or overwrites authored fields; it only annotates
    the tree with `_abs` for downstream consumers.
"""

from __future__ import annotations

from typing import Any, Dict, List, Optional, Tuple


def _to_int(x: Any, default: int = 0) -> int:
    try:
        return int(round(float(x)))
    except (TypeError, ValueError):
        return default


def _get_size(elem: dict) -> Tuple[int, int]:
    size = elem.get("size") or {}
    return _to_int(size.get("width")), _to_int(size.get("height"))


def _get_position(elem: dict) -> Tuple[int, int]:
    pos = elem.get("position") or {}
    return _to_int(pos.get("x")), _to_int(pos.get("y"))


def _get_offset(elem: dict) -> Tuple[int, int]:
    off = elem.get("offset") or {}
    return _to_int(off.get("x")), _to_int(off.get("y"))


def _get_padding(layout: dict) -> Tuple[int, int]:
    pad = layout.get("padding")
    if isinstance(pad, (int, float)):
        v = _to_int(pad)
        return v, v
    if isinstance(pad, dict):
        return _to_int(pad.get("x")), _to_int(pad.get("y"))
    return 0, 0


def _children(elem: dict) -> List[dict]:
    return elem.get("children") or []


# ---------------------------------------------------------------------------
# Per-element alignment within a parent box (no layout group involved).
# ---------------------------------------------------------------------------

def _align_axis(parent_extent: int, elem_extent: int, align: str,
                explicit: Optional[int]) -> int:
    """Return the start coordinate of `elem_extent` inside `parent_extent`
    given an alignment keyword. If `align` is not recognized, fall back to
    `explicit` (the authored position) or 0.
    """
    if align in ("start", "left", "top"):
        return 0
    if align in ("center", "middle"):
        return (parent_extent - elem_extent) // 2
    if align in ("end", "right", "bottom"):
        return parent_extent - elem_extent
    return explicit if explicit is not None else 0


def _resolve_alignment(elem: dict, parent_w: int, parent_h: int
                       ) -> Tuple[int, int]:
    """Resolve an element's (x, y) inside its parent given align/vAlign +
    offset. Falls back to the element's authored position for any axis
    that isn't aligned.
    """
    ew, eh = _get_size(elem)
    ax = elem.get("align")
    ay = elem.get("vAlign")
    px, py = _get_position(elem)
    ox, oy = _get_offset(elem)

    if ax is not None:
        x = _align_axis(parent_w, ew, ax, px) + ox
    else:
        x = px + ox
    if ay is not None:
        y = _align_axis(parent_h, eh, ay, py) + oy
    else:
        y = py + oy
    return x, y


# ---------------------------------------------------------------------------
# Layout group resolution (row / column).
# ---------------------------------------------------------------------------

def _layout_group(parent: dict, layout: dict) -> List[Tuple[int, int]]:
    """Compute (x, y) for each child of `parent` according to `layout`.
    Returns a list aligned with `_children(parent)`.

    Coordinate space: relative to parent's top-left (inside padding).
    """
    children = _children(parent)
    if not children:
        return []

    pw, ph = _get_size(parent)
    pad_x, pad_y = _get_padding(layout)
    inner_w = max(0, pw - 2 * pad_x)
    inner_h = max(0, ph - 2 * pad_y)

    ltype = layout.get("type", "row")
    main_axis = "x" if ltype == "row" else "y"
    main_extent = inner_w if main_axis == "x" else inner_h
    cross_extent = inner_h if main_axis == "x" else inner_w

    sizes_main = []
    sizes_cross = []
    for c in children:
        cw, ch = _get_size(c)
        if main_axis == "x":
            sizes_main.append(cw); sizes_cross.append(ch)
        else:
            sizes_main.append(ch); sizes_cross.append(cw)

    n = len(children)
    total_main = sum(sizes_main)
    leftover = max(0, main_extent - total_main)

    spacing_cfg = layout.get("spacing", 0)
    align = layout.get("align", "start")
    v_align = layout.get("vAlign", "start")

    # Determine per-gap spacing and leading offset.
    if spacing_cfg == "even" or align in (
            "space-between", "space-around", "space-evenly"):
        if n == 1:
            # Single child: place by align like a normal aligned element
            lead = _align_axis(main_extent, sizes_main[0], align, 0)
            gaps = [0]
        elif align == "space-between":
            lead = 0
            gap = leftover // (n - 1) if n > 1 else 0
            gaps = [gap] * (n - 1)
        elif align == "space-around":
            half = leftover // (2 * n) if n > 0 else 0
            lead = half
            gap = (leftover - 2 * half) // (n - 1) if n > 1 else 0
            gaps = [gap] * (n - 1)
        else:  # "even" or space-evenly: equal gaps including ends
            gap = leftover // (n + 1)
            lead = gap
            gaps = [gap] * (n - 1)
    else:
        # Fixed spacing
        fixed = _to_int(spacing_cfg)
        total_with_gaps = total_main + fixed * max(0, n - 1)
        if align in ("center", "middle"):
            lead = max(0, (main_extent - total_with_gaps) // 2)
        elif align in ("end", "right", "bottom"):
            lead = max(0, main_extent - total_with_gaps)
        else:  # start
            lead = 0
        gaps = [fixed] * (n - 1)

    # Walk children, accumulate main-axis position
    positions: List[Tuple[int, int]] = []
    cursor = lead
    for i, c in enumerate(children):
        # Cross-axis position
        cross = _align_axis(cross_extent, sizes_cross[i], v_align, 0)
        if main_axis == "x":
            x = pad_x + cursor
            y = pad_y + cross
        else:
            x = pad_x + cross
            y = pad_y + cursor
        # Child-level alignment can override only the cross axis. The layout
        # still owns the main axis so a row/column cannot collapse because a
        # child uses vAlign/align for centering.
        if main_axis == "x":
            child_cross = c.get("vAlign")
            if child_cross is not None:
                y = pad_y + _align_axis(cross_extent, sizes_cross[i], child_cross, cross)
        else:
            child_cross = c.get("align")
            if child_cross is not None:
                x = pad_x + _align_axis(cross_extent, sizes_cross[i], child_cross, cross)

        ox, oy = _get_offset(c)
        positions.append((x + ox, y + oy))
        cursor += sizes_main[i]
        if i < n - 1:
            cursor += gaps[i]
    return positions


# ---------------------------------------------------------------------------
# Public entry points.
# ---------------------------------------------------------------------------

def resolve_positions(structure: dict) -> dict:
    """Annotate every element in the tree with `_abs` = [x, y, w, h] in
    canvas coordinates. Returns the same dict (mutated). Safe to call
    multiple times; later calls recompute from current authored fields.

    For each element we attach:
        elem["_abs"] = [abs_x, abs_y, w, h]
        elem["_rel"] = [rel_x, rel_y, w, h]   # relative to parent
    """
    root = structure.get("root", {})

    def walk(elem: dict, parent_abs_x: int, parent_abs_y: int,
             parent_w: int, parent_h: int):
        layout = elem.get("layout")
        children = _children(elem)

        # Precompute child positions when this element drives a layout.
        layout_positions: Optional[List[Tuple[int, int]]] = None
        if layout and children:
            layout_positions = _layout_group(elem, layout)

        for i, child in enumerate(children):
            cw, ch = _get_size(child)
            if layout is not None and layout_positions is not None:
                rx, ry = layout_positions[i]
            else:
                rx, ry = _resolve_alignment(child, _get_size(elem)[0], _get_size(elem)[1])

            ax = parent_abs_x + rx
            ay = parent_abs_y + ry
            child["_rel"] = [rx, ry, cw, ch]
            child["_abs"] = [ax, ay, cw, ch]
            walk(child, ax, ay, cw, ch)

    # Root: use authored position/size; default to canvas size.
    canvas = structure.get("canvas") or {}
    cw = _to_int(canvas.get("width"))
    ch = _to_int(canvas.get("height"))
    rw, rh = _get_size(root)
    if rw == 0: rw = cw
    if rh == 0: rh = ch
    rx, ry = _get_position(root)
    root["_rel"] = [rx, ry, rw, rh]
    root["_abs"] = [rx, ry, rw, rh]
    walk(root, rx, ry, rw, rh)
    return structure


def find_by_path(structure: dict, path: str) -> Optional[dict]:
    """Look up an element by slash-separated name path, e.g.
    'root/level_popup/popup_header/close_button'. Names are matched by the
    `name` field of each element. Returns the element dict, or None.
    """
    parts = [p for p in path.split("/") if p]
    if not parts:
        return None
    node = structure.get("root", {})
    if node.get("name") != parts[0]:
        return None
    for part in parts[1:]:
        next_node = None
        for c in _children(node):
            if c.get("name") == part:
                next_node = c
                break
        if next_node is None:
            return None
        node = next_node
    return node


def dfs_paths(structure: dict, include_root: bool = False) -> List[str]:
    """Return every element's path in DFS pre-order. Useful for driving the
    alignment loop top-down (parent before children).
    """
    out: List[str] = []

    def walk(elem: dict, prefix: str):
        name = elem.get("name", "")
        path = f"{prefix}/{name}" if prefix else name
        if prefix or include_root:
            out.append(path)
        else:
            # First call: root itself; only add if include_root
            if include_root:
                out.append(path)
        for c in _children(elem):
            walk(c, path)

    root = structure.get("root", {})
    walk(root, "")
    if include_root and (not out or out[0] != root.get("name")):
        out.insert(0, root.get("name", "root"))
    return out
