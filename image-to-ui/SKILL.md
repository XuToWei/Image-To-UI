---
name: image-to-ui
description: "Extract structured UI data from design images. Use when a UI screenshot or mockup plus sliced assets must become layout hierarchy, positions, text, asset references, and ui_structure.json for Unity/Cocos-style game UI. Trigger for UI analysis, screenshot/design to UI JSON, parse design, extract UI structure, Unity/Cocos UI configuration, or reconstruct UI from a design image and Sprite directory."
---

# Image to UI

Create `ui_structure.json` from one design image and one sliced-asset directory.
Use the workflow wrapper for normal runs; call individual scripts only when
debugging the skill itself.

## Inputs and task folder

Require:

- a PNG/JPG design at native resolution;
- a directory of sliced PNG assets, optionally nested and accompanied by Unity
  `.png.meta` files;
- a fresh output directory for this design.

Keep each design in its own task folder. The workflow records input snapshots
in `workflow_state.json` and refuses mismatched or stale task folders.

## 1. Prepare

```bash
py -B <skill>/scripts/workflow.py prepare --design <design.png> --assets <sprite-dir> --output <task-dir>
```

This checks Python dependencies, inventories assets, creates grouped contact
sheets, overlays a measurement grid using a complementary color selected from
the design's dominant tone plus a contrast halo, and writes grid metrics. If
dependencies are missing, install only those named by the error and rerun the
same command.

Inspect these files first:

- `<task-dir>/design_grid.png`
- `<task-dir>/design_grid_metrics.json`
- `<task-dir>/assets/assets_inventory.json`
- relevant `<task-dir>/assets/assets_contact_sheet_usage_*.png`

Read [references/assets.md](references/assets.md) when selecting sprites.

## 2. Write the structure

Write `<task-dir>/ui_structure.json`. Use the design grid for every bbox and
the inventory/contact sheets for every asset choice. Read
[references/schema.md](references/schema.md) for fields and examples.

Apply these rules:

1. Set `canvas` to the native design size from the metrics file.
2. Build parent-relative hierarchy before fine coordinates.
3. Give every element, including `root`, an `anchor` object with horizontal
   `left` / `center` / `right` and vertical `top` / `middle` / `bottom`.
   Choose the parent edge or center the element is intended to remain attached
   to. `anchor` records engine-facing alignment intent; it does not change the
   existing top-left `position` coordinates.
4. Use `layout`, `align`, and `vAlign` for derived positions; use explicit
   `position` only for free placement. Every `layout` object must declare
   `"type": "row"` or `"type": "column"`.
5. Model composite controls as layered children and flat generated shapes as
   `rect` or `overlay`. Give the frame/base the control's outer bbox; keep an
   icon glyph at its own visible aspect ratio and center it inside the frame.
   Never stretch a small glyph to the full button bbox.
6. Include the active UI surface and its scrim; omit unrelated scene content
   behind it.
7. Use exact relative asset paths when basenames are duplicated. Prefer Unity
   sprite-border metadata for stretchable panels and buttons.
8. Treat a text element's bbox as its text box. Use `alignment` and
   `textVAlign` for visible-glyph alignment inside that box; use `align` and
   `vAlign` only to position the box inside its parent.
9. For repeated siblings, measure their centers and use one row/column layout.
   Tune the parent position, spacing, and cross-axis alignment before adding
   child offsets. A layout describes geometry only; decide list roles from the
   overall UI meaning. Mark a group as `"role": "list"` with direct-child
   `"role": "listItem"` values when its siblings visually read as one
   repeatable collection: many peer records use the same item template,
   primarily vary by content, follow one continuous order, and could naturally
   be added, removed, or reordered. A large collection can be a list without
   visible scrolling or pagination. Explicit specs, data bindings, scrolling,
   pagination, and clipped continuation strengthen the judgment but are not
   required. Use no hard item-count threshold, and never use equal spacing or
   a `list`-like name alone. Fixed-purpose slots, layered composites, toolbars,
   resource bars, navigation, and button groups remain ordinary layouts.
10. When choosing a family asset, inspect its sibling layers. A `Bg` commonly
   needs the matching `Shadow`, `BgLight`, `Glow`, `Border`, or `FocusLine`;
   include only layers visible in the design, in back-to-front child order.
11. Do not add empty text or visual-less leaf containers as placeholders. They
    cannot count as foreground or review coverage.

Convert grid readings with both axis scales:

```text
x = column * px_per_cell_x
y = row * px_per_cell_y
width = column_span * px_per_cell_x
height = row_span * px_per_cell_y
```

When adapting an older draft, scale it once before manual review:

```bash
py -B <skill>/scripts/scale_structure.py --structure <draft.json> --target-design <design.png> --output <task-dir>/ui_structure.json
```

Then add any missing anchors without changing geometry:

```bash
py -B <skill>/scripts/backfill_anchors.py --structure <task-dir>/ui_structure.json --output <task-dir>/ui_structure.json
```

The backfill preserves complete valid anchors. It first uses explicit
element/layout alignment intent, then chooses the nearest resolved parent edge
or center for missing axes. Use `--overwrite` only when every existing anchor
must be recomputed.

## 3. Check and iterate

```bash
py -B <skill>/scripts/workflow.py check --output <task-dir>
```

`check` is strict by default: structural warnings fail the run.
`--allow-warnings` is diagnostic only; `finalize` still requires a warning-free
structural report. Add `--transparent-bg` when scene content behind an active
popup was intentionally omitted.

The command stops immediately on invalid JSON or structure errors. On success
it writes:

- `validate_report.json`
- `all_elements.png` and `all_elements_legend.json`
- `comparison.png`
- `reconstruction.png` and `render_trace.json`
- `visual_audit.json`
- `review_risk.json`, `risk_review.png`, and `risk_review_legend.json`

The render trace is collected during the same render pass. The audit blocks
missing requested fonts and visible text ink outside its box. Atomic sprite
aspect changes and child boxes outside their parents are diagnostic warnings;
review them in context instead of moving elements automatically. Every current
visual-audit warning requires an exact accepted entry in
`metadata.auditWaivers`; stale waivers also block completion.

Use `review_risk.json` to prioritize inspection. It scores every rendered
visual leaf from visual salience (visible area, text size, local contrast, and
foreground order) plus the residual between the native design crop and the
same reconstruction crop. The score is name-agnostic. It selects evidence
depth only: never treat it as a similarity verdict or use it to change
coordinates automatically. Inspect every high-risk row in `risk_review.png`;
orange outlines mark design bboxes, cyan outlines mark reconstruction bboxes,
and green outlines mark rendered text-line ink.

Every successful `check` or `target` must rewrite `comparison.png` from the
current structure. Before validation, the workflow removes the old comparison
so a failed run cannot leave a stale image that looks current. Do not use
`validate_structure.py` directly during normal iteration because that
low-level validator does not render.

Inspect both images. Fix hierarchy or parent placement before adjusting child
coordinates; fix asset, nine-slice, tint, opacity, text, or layer order when
bboxes align but rendering differs. Rerun `check` after every JSON edit.
When an asset's shape and shading match but its hue does not, use a small
validated `hueShift` before replacing the asset or distorting its geometry.
Use `visible_bbox` and text `line_ink_bboxes` in `render_trace.json` for pixel
edges. Correct the font, size, line height, stroke, and alignment first; use a
small positive `textScaleX` only when the supplied font has the wrong width.

For crowded or ambiguous regions, validate once and generate one or more
focused views:

```bash
py -B <skill>/scripts/workflow.py target --output <task-dir> --element-path "root/popup/close" --element-path "root/popup/rewards"
```

`target` requires a successful full `check`, refreshes the render artifacts,
and reuses the transparent-background mode from that check. Pass
`--transparent-bg` only when changing the recorded mode intentionally.

Cap focused rechecks at three per element. Read
[references/alignment.md](references/alignment.md) for correction heuristics and
[references/validation.md](references/validation.md) when a check fails.

## 4. Review and finalize

Inspect `all_elements.png` and `comparison.png`. After the latest `check` or
`target`, write `<task-dir>/alignment_review.md` as a four-column Markdown table
with every path from `all_elements_legend.json`: element path, status
(`aligned`, `adjusted`, or `skipped`), observed issue/change, and recheck PNG.
Copy the exact `Review binding:` HTML comment printed by that final command
into the review. An aligned/adjusted row must cite current `all_elements.png`,
`comparison.png`, `risk_review.png`, or a current workflow-generated
`target_*.png`; focused evidence is valid only for paths covered by its
companion legend. A skipped row must explain why.

Every non-skipped high-risk row must cite `risk_review.png` or a current
covering `target_*.png`. Overview-only evidence cannot finalize a high-risk
row. A high residual remains review guidance rather than a blocking visual
error; after inspecting the focused evidence, correct the structure or record
an accepted approximation as appropriate. Read
[references/alignment.md](references/alignment.md) for score interpretation.

Record intentional substitutions or known visual limits in
`metadata.approximations`; each needs an element `path`, `kind`, `reason`, and
`"accepted": true`. Do not hide approximations in prose notes only.
Record every accepted visual-audit warning in `metadata.auditWaivers` with its
exact `path`, `code`, reason, and `"accepted": true`.

```bash
py -B <skill>/scripts/workflow.py finalize --output <task-dir>
```

`finalize` does not rerender elements. It verifies current artifact hashes,
review coverage, validation, and accepted approximations, then crops only
suspicious regions from the existing images into `detail_comparison.png`.
Finish only when it writes `completion_report.json` with `complete: true` and
sets workflow status to `completed`.

## Do not

- Do not estimate coordinates from the ungridded design.
- Do not guess assets from filenames without checking the inventory.
- Do not reuse another design's output directory.
- Do not use pixel/template matching or automatic coordinate correction.
- Do not change coordinates to hide an asset, text, layering, or nine-slice
  problem.

## Deliver

Keep `ui_structure.json`, `comparison.png`, `reconstruction.png`, render/audit
reports, review-risk report/evidence, `alignment_review.md`,
`completion_report.json`, `workflow_state.json`, inventories, grid files, bbox
images, and legends in the task folder. Report element/anchor/layout counts,
structure revision and check counts, high-risk review coverage, accepted
warnings and approximations, and anything skipped or uncertain.
