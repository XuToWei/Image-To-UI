# Image to UI

[中文说明](README.zh-CN.md)

Turn a UI mockup and its sliced sprites into a validated, reviewable
`ui_structure.json` for Unity/Cocos-style game UI.

Image to UI does more than extract approximate coordinates. The bundled Codex
skill inventories assets, measures the design on a grid, builds a parent-relative
UI hierarchy, renders a reconstruction, audits the result, and requires an
evidence-backed review before marking a task complete.

## Highlights

- Grid-based measurement at the design's native resolution.
- Recursive PNG/JPG sprite inventory with duplicate-name detection and optional
  Unity `.png.meta` border support.
- Structured containers, rows, columns, images, text, generated rectangles, and
  overlays.
- Required nine-position anchor alignment metadata on every structure node,
  with a migration tool for older JSON.
- Opt-in `list` / `listItem` roles chosen from explicit context or holistic
  visual semantics, then carried into element annotation legends.
- Nine-slice rendering, font tracing, tint, opacity, and hue-shift support.
- Strict structure validation plus visual-audit reports.
- Side-by-side comparison, element bounding boxes, and focused recheck images.
- Resumable workflow state and a final `completion_report.json`.

## Example: Emberfall HUD

This repository includes a complete example:

- Design: [`test/source/design/emberfall-ui-mockup.png`](test/source/design/emberfall-ui-mockup.png)
- Sliced sprites: [`test/source/sprites/`](test/source/sprites/)
- Final structure: [`test/output/ui_structure.json`](test/output/ui_structure.json)
- Completion report: [`test/output/completion_report.json`](test/output/completion_report.json)

[![Emberfall design and reconstruction comparison](test/output/comparison.png)](test/output/comparison.png)

The checked-in run reconstructs a `1672 × 941` HUD with:

- 71 structured elements;
- 71/71 nodes with required anchor alignment metadata;
- 42 sprite references;
- 5 row/column layouts;
- 1 explicitly identified quest list with 2 list items;
- 70/70 reviewed element paths;
- zero validation or visual-audit warnings.

The mockup also contains artwork that was not supplied as a sliced sprite.
Those limits—such as the hero/portrait layer and font substitutions—are
recorded explicitly in `metadata.approximations` instead of being hidden.

The final review used focused evidence to correct the lower-right
[`ENTER` button](test/output/target_root_enter_button.png) and both QUEST row
backgrounds ([fire](test/output/target_root_quest_panel_quest_list_fire_quest_background.png),
[frost](test/output/target_root_quest_panel_quest_list_frost_quest_background.png)).

## Quick start with Codex

Clone or open this repository in Codex, then ask it to use the skill under
`image-to-ui/`.

The checked-in example already uses `test/output`. For a new run, always choose
a fresh output directory:

```text
Use the image-to-ui skill in this repository.
The design is test/source/design/emberfall-ui-mockup.png and the sliced assets
are under test/source/sprites. Reconstruct the UI into test/output-local,
validate it, review every element, and finalize the task.
```

Codex follows [`image-to-ui/SKILL.md`](image-to-ui/SKILL.md) and drives the
workflow from preparation through final review.

## Workflow commands

Normal runs should be driven by the skill. The commands below are useful when
debugging or inspecting one stage manually. Replace `python` with `py` on
Windows if needed.

### 1. Prepare a fresh task

```bash
python -B image-to-ui/scripts/workflow.py prepare --design test/source/design/emberfall-ui-mockup.png --assets test/source/sprites --output test/output-local
```

This creates the measurement grid, asset inventory, grouped contact sheets,
and `workflow_state.json`.

### 2. Write the structure

Create `test/output-local/ui_structure.json` using the grid and asset inventory.
The Codex skill performs this hierarchy and layout work. Every node must include
`anchor.horizontal` and `anchor.vertical`.

For an older structure, fill missing anchors without changing its bboxes:

```bash
python -B image-to-ui/scripts/backfill_anchors.py --structure old.json --output upgraded.json
```

The accepted combinations use horizontal `left` / `center` / `right` and
vertical `top` / `middle` / `bottom`. See the
[schema reference](image-to-ui/references/schema.md#anchor-alignment) for the
selection rules and atomic in-place upgrade form.

### 3. Validate and render

```bash
python -B image-to-ui/scripts/workflow.py check --output test/output-local
```

`check` validates the JSON and regenerates the reconstruction, render trace,
element boxes, visual audit, and side-by-side comparison.

For an ambiguous region, create focused evidence after a successful check:

```bash
python -B image-to-ui/scripts/workflow.py target --output test/output-local --element-path "root/quest_panel/quest_list"
```

### 4. Review and finalize

After reviewing every path from `all_elements_legend.json`, write
`alignment_review.md` with the review binding printed by the latest `check` or
`target`, then run:

```bash
python -B image-to-ui/scripts/workflow.py finalize --output test/output-local
```

The task is complete only when `completion_report.json` contains
`"complete": true`.

## Output artifacts

| Artifact | Purpose |
| --- | --- |
| `ui_structure.json` | Engine-oriented UI hierarchy, anchors, geometry, text, and asset references. |
| `reconstruction.png` | Rendered result from the current structure. |
| `comparison.png` | Gridded design and reconstruction shown side by side. |
| `all_elements.png` / `all_elements_legend.json` | Overview of resolved element bounds, paths, and explicit list roles. |
| `target_*.png` / `target_*_legend.json` | Optional focused evidence for crowded regions. |
| `render_trace.json` | Resolved bboxes, visible pixels, fonts, layers, and render details. |
| `validate_report.json` / `visual_audit.json` | Structural and rendered-output diagnostics. |
| `alignment_review.md` | Human/AI review record for every foreground element. |
| `completion_report.json` | Final completion, coverage, warning, and approximation summary. |
| `workflow_state.json` | Input snapshots, revisions, checks, and resumable workflow state. |
| `assets/` and `design_grid.*` | Asset inventories, contact sheets, and grid measurements. |

## Input requirements

Each task needs:

1. one native-resolution PNG or JPG design;
2. one directory of sliced PNG/JPG assets, optionally with Unity metadata and
   font files;
3. one fresh output directory dedicated to that design.

For best results, provide every foreground visual as a slice, keep atomic icons
at their natural aspect ratio, and bundle the intended fonts with the task.
Intentional substitutions and accepted audit exceptions must be recorded in
the structure metadata.

## Repository layout

```text
image-to-ui/
  SKILL.md
  agents/openai.yaml
  references/
  scripts/
test/
  source/
    design/
    sprites/
  output/
README.md
README.zh-CN.md
```

## License

[MIT](LICENSE)
