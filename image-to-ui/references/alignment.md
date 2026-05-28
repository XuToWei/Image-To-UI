# Alignment and Verification Reference

Use this reference when bbox alignment or final comparison needs more detail than the main
workflow in `SKILL.md`.

## BBox Images

Every bbox image is drawn on the full design image with the same grid overlay
as `annotate_grid.py`.

`--all-elements` mode:

- Draws every non-full-canvas resolved element in one overview image.
- Also draws full-canvas `overlay` and `rect` elements because they are active
  UI layers.
- Uses numeric labels to keep the image readable.
- Writes a JSON legend with `label`, `path`, `name`, and `abs_bbox`.
- Best first pass for token efficiency.

Targeted `--element-path` mode:

- Draws one element bbox.
- If the path points to a container with `layout`, draws one bbox per direct
  child for group alignment.
- Use only for crowded regions, suspicious elements, or verifying edits.

## AI Review Loop

Step 4 requires an explicit AI judgment pass. The agent must inspect the bbox
image, compare every active foreground element against the design, and record
the result in `alignment_review.md`.

Use these statuses:

- **aligned**: bbox edges match the visible element within the precision
  expectations below.
- **adjusted**: JSON was edited; include old/new `position`, `size`, layout, or
  parent values and the recheck artifact path.
- **needs-targeted-check**: overview is too crowded or ambiguous; run a
  targeted annotation before accepting or editing the element.
- **skipped**: element is hidden, occluded, or not judgeable; include the reason.

Do not treat `all_elements.png` existing on disk as Step 4 completion. Step 4 is
complete only when every reviewed foreground element is `aligned` or `skipped`,
and every `adjusted` element has been rechecked.

## Position Correction

Compare bbox edges to the actual element using grid lines.

- **Aligned**: edges match within a few pixels; move on.
- **Needs adjustment**: apply `(dx, dy)` to `position` and `(dw, dh)` to
  `size`. If the parent did not move, design-absolute deltas can be applied
  directly to the element's parent-relative position.
- **Skip**: element is hidden, occluded, or not judgeable.

Regenerate `out/all_elements.png` when a parent/container or several elements
changed. Regenerate only the targeted image for one leaf edit. Cap targeted
rechecks at 3 iterations per element.

After each JSON edit, rerun `validate_structure.py` before generating the next
bbox image. If a parent/container moves, recheck its visible descendants because
their absolute bboxes changed too.

Use group-level fixes first:

- Whole foreground cluster drift: move the parent container.
- Correct parent with one wrong child: adjust the child.
- Repeated siblings with uneven spacing: adjust `layout.spacing`, padding,
  `align`, or `vAlign`.
- Bbox aligned but rendered pixels look wrong later: inspect render fields and
  assets before changing coordinates.

## Derived Positioning

Prefer derived positioning where the design intent supports it:

- Use `layout` for similar siblings in a row or column.
- Use `align` / `vAlign` for centered or edge-aligned elements.
- Use explicit `position` only for free placements.

Children inside a layout container should usually omit `position`. For layout
field semantics, including child cross-axis alignment rules, see
`references/schema.md`.

## Structural Debugging

When `comparison.png` differs from the design:

- **Original has element, reconstruction does not**:
  - If no bbox exists in all-elements overview, add the element to
    `ui_structure.json` with correct parent, layer order, type, asset/text/color,
    and grid-derived `position` / `size`.
  - If bbox exists, inspect render fields: `asset`, path, `opacity`, `color`,
    `size`, and layer occlusion.
- **Reconstruction has wrong visual**: fix `asset`, `type`, `text`,
  `fontSize`, `color`, `opacity`, `nineSlice`, or layer order before changing
  coordinates.
- **Reconstruction has extra element**: remove it or move it under the correct
  conditional/active UI surface.

## Precision Expectations

Large images may be downscaled by the image inspection tool. With the grid
overlay, expect roughly:

- Large elements over 150 px: about +/-5 px.
- Medium elements 60-150 px: about +/-10 px.
- Small elements under 60 px: about +/-15 px.

Stop once visually acceptable unless the target engine requires later manual
fine tuning.

## Why This Workflow Avoids Template Matching

- Nine-slice scaling does not matter because the bbox is the outer rectangle.
- Tinting and color shifts do not confuse a human bbox check.
- Occlusion can be skipped honestly instead of producing a false match.
- Every fix is an explicit JSON edit.
