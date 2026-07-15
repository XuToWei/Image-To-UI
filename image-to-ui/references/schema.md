# UI Structure Schema

Use this reference when creating or validating fields beyond the core
workflow in `SKILL.md`.

## Full Example

```json
{
  "canvas": { "width": 1080, "height": 1920, "name": "MainUI" },
  "root": {
    "type": "container",
    "name": "root",
    "position": { "x": 0, "y": 0 },
    "size": { "width": 1080, "height": 1920 },
    "children": [
      {
        "type": "image",
        "name": "background",
        "position": { "x": 0, "y": 0 },
        "size": { "width": 1080, "height": 1920 },
        "asset": "bg_main.png",
        "nineSlice": true
      },
      {
        "type": "container",
        "name": "rewards_row",
        "position": { "x": 100, "y": 600 },
        "size": { "width": 880, "height": 200 },
        "layout": {
          "type": "row",
          "spacing": "even",
          "padding": { "x": 30, "y": 25 },
          "align": "space-evenly",
          "vAlign": "middle"
        },
        "children": [
          { "type": "image", "name": "gem", "size": { "width": 120, "height": 120 }, "asset": "icon_gem.png" },
          { "type": "image", "name": "coin", "size": { "width": 120, "height": 120 }, "asset": "icon_coin.png" },
          { "type": "image", "name": "key", "size": { "width": 120, "height": 120 }, "asset": "icon_key.png" }
        ]
      },
      {
        "type": "container",
        "name": "play_button",
        "position": { "x": 0, "y": 1620 },
        "size": { "width": 300, "height": 100 },
        "align": "center",
        "children": [
          { "type": "image", "name": "base", "size": { "width": 300, "height": 100 }, "asset": "btn_play.png", "nineSlice": true },
          { "type": "text", "name": "label", "size": { "width": 300, "height": 100 }, "align": "center", "vAlign": "middle", "text": "PLAY", "fontSize": 32, "color": "#FFFFFF" }
        ]
      }
    ]
  },
  "metadata": { "notes": "", "approximations": [] }
}
```

## Element Types

- `container`: logical group, no visual on its own.
- `image`: visual element with `asset`.
- `rect`: engine-generated solid rectangle with `color` and optional `opacity`.
- `text`: text label.
- `button`: interactive composite. A `container` with image and text works equally well.
- `overlay`: visual parent container for modal scrims, dim masks, and blockers.
  It draws its own `color` or `asset` first, then renders children on top.

## Required Fields

`type`, `name`, and `size { width, height }` are required. `position` is
required unless the element's position is fully derived from `align`/`vAlign`
or from the parent's `layout`. Text must be non-empty. A non-root leaf
`container` or `button` must have children or its own `asset`/`color` visual.
Each `name` is one stable path segment: it cannot contain `/` or `\\`, and it
cannot be `.` or `..`.

## Optional Fields

- `asset`: filename in the assets directory, matched case-insensitively. Nested asset directories are indexed. If duplicate basenames exist, use a relative path such as `icons/coin.png`.
- `color`: `"#RRGGBB"`; fill color on `rect` / `overlay`, multiplicative tint
  on images, text color on text.
- `hueShift`: optional image hue rotation in degrees from `-360` to `360`.
  Use it only when the source shape and shading already match; hue rotation is
  applied before multiplicative `color` tint and never changes alpha.
- `opacity`: `0.0` to `1.0`.
- `text`, `fontSize`, `alignment`: text fields. `alignment` is for text within the text box; `align` positions the text box inside its parent.
- `textScaleX`: optional positive horizontal scale for visible text ink. Keep
  it at `1` unless the exact supplied font still has the wrong width after
  font, size, stroke, and alignment are correct.
- `nineSlice`: stretchable asset handling.
- `layout`: declare this container as a row/column group. The object must
  explicitly contain `"type": "row"` or `"type": "column"`.
- `align`, `vAlign`, `offset`: derived positioning relative to parent.

Inside a parent `layout`, child-level alignment only overrides the cross axis:
`vAlign` in a row layout and `align` in a column layout. The layout keeps
ownership of the main axis. Use `offset` for small per-child nudges.

## Generated Rectangles

Use `rect` for flat rectangular UI that has no sliced asset because the engine
can generate it directly: fills, rules, progress-bar segments, simple panels,
or semi-transparent blocks that are not parents. Do not skip these just because
they are absent from the asset inventory.

```json
{
  "type": "rect",
  "name": "progress_fill",
  "position": { "x": 24, "y": 8 },
  "size": { "width": 180, "height": 18 },
  "color": "#45D86A",
  "opacity": 1
}
```

## Overlay Containers

Use `overlay` when a semi-transparent UI layer is the parent of a popup or
foreground surface. Keep scene content behind the overlay out of the structure;
model only the UI-owned scrim and its children.

```json
{
  "type": "overlay",
  "name": "modal_overlay",
  "position": { "x": 0, "y": 0 },
  "size": { "width": 1080, "height": 1920 },
  "color": "#000000",
  "opacity": 0.55,
  "children": [
    {
      "type": "container",
      "name": "level_start_popup",
      "position": { "x": 87, "y": 490 },
      "size": { "width": 906, "height": 980 },
      "children": []
    }
  ]
}
```

## Stretchable Assets

Background panels, frames, buttons, bars, popup bodies, bubbles, and slot
backgrounds should declare `nineSlice` so the renderer preserves corner detail.
Atomic icons, portraits, and character sprites should not.

```json
"nineSlice": true
"nineSlice": 30
"nineSlice": { "left": 20, "right": 20 }
"nineSlice": { "left": 30, "top": 24, "right": 30, "bottom": 40 }
```

When sliced assets come from Unity and include sidecar `.png.meta` files,
`render_comparison.py` reads `spriteBorder` automatically for
`"nineSlice": true`, `"nineSlice": "auto"`, and `"nineSlice": "meta"`. Prefer
that over hand-guessing margins; use an explicit object only when overriding
importer metadata.

## Text Rendering Hints

The verifier supports fields that make comparisons closer to game UI exports:

```json
{
  "type": "text",
  "text": "PLAY",
  "fontFamily": "Cairo-Black 1.ttf",
  "fontSize": 72,
  "color": "#FFFFFF",
  "strokeColor": "#111111",
  "strokeWidth": 5,
  "lineHeight": 56,
  "textScaleX": 1.08,
  "alignment": "center",
  "textVAlign": "middle"
}
```

`fontFamily` is searched relative to the prepared task, asset, and design
directories and their parents; it never depends on the process working
directory. `alignment` and `textVAlign` align the visible glyph pixels,
including stroke, within the text box. Leading and trailing spaces remain in
the content but do not move that visible ink. `align` and `vAlign` still
position the text box within its parent. `fontSize` selects the font size but
is not the visible glyph height. `lineHeight` is the vertical step between
successive line draw origins; the renderer measures the real multi-line ink
bounds before applying vertical alignment.
`textScaleX` changes visible glyph width around the authored text alignment;
it does not resize or reposition the text box.

## Accepted Approximations

Use structured metadata for an intentional visual substitution or a limitation
caused by supplied assets. `finalize` accepts only entries whose path appears
in `all_elements_legend.json` and whose reason is explicit.

```json
"metadata": {
  "notes": "Underlying scene intentionally omitted from the active UI scope.",
  "approximations": [
    {
      "path": "root/popup/selected_badge/check",
      "kind": "missing_asset",
      "reason": "No check-mark sprite was supplied; a text glyph is used.",
      "accepted": true
    }
  ],
  "auditWaivers": [
    {
      "path": "root/popup/count_badge",
      "code": "parent_overflow",
      "reason": "The badge intentionally overlaps the slot edge by 8px.",
      "accepted": true
    }
  ]
}
```

`path`, `reason`, and boolean `accepted` are required. `kind` is a short stable
category such as `missing_asset`, `asset_color`, or `font_substitution`.

`auditWaivers` is separate from visual substitutions. Each entry must exactly
match a current warning's `path` and `code`; missing, rejected, duplicate, and
stale entries block `finalize`.
