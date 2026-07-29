# Structure Validation Reference

Use this reference when `validate_structure.py` reports errors or warnings.

## Normal command

```bash
py -B <skill>/scripts/workflow.py check --output <task-dir>
```

The workflow reads prepared paths from `workflow_state.json`, runs structural
validation, and regenerates `comparison.png`, `reconstruction.png`, and
`render_trace.json`. It deletes old render artifacts first, so a failed run
cannot look current. It then writes `visual_audit.json` from the same-pass
trace. Use the lower-level validator only when debugging; it never renders.

## Fix Priority

1. Fix all errors before bbox alignment.
2. Fix warnings that affect the current design.
3. Prefer the default strict check. `--allow-warnings` is for diagnosis only;
   completion always requires a zero-warning structural report.

Visual-audit errors also fail `check`. Visual-audit warnings are diagnostic at
check time, but `finalize` requires an exact accepted `metadata.auditWaivers`
entry for each current warning and rejects stale entries.

Review-risk scores are neither structural errors nor visual-audit warnings.
They control evidence depth. `finalize` rejects a non-skipped high-risk review
row that cites only overview evidence; cite current `risk_review.png` or a
covering workflow-generated target. It also rejects changed or stale
`review_risk.json`, `risk_review.png`, and `risk_review_legend.json`.

## Common Issues

- Canvas mismatch: set `canvas.width` / `canvas.height` to the design's native
  size or rerun `scale_structure.py`.
- Missing or ambiguous asset: copy the exact path from `assets_inventory.json`.
- Invalid hue shift: use a finite degree value from `-360` to `360`; prefer
  `0` or omit the field when no hue correction is needed.
- Invalid name: use one stable path segment; `/`, `\\`, `.`, and `..` are
  reserved by element-path resolution.
- Position inside layout: remove child `position` and use `offset`, or remove
  the parent layout if the child needs an independent position.
- Empty layout: add explicit `"type": "row"` or `"type": "column"`.
- Empty foreground: remove placeholder leaves and add at least one actually
  visible image, rect, or text element beyond a full-canvas scrim.
- Image without asset: add `asset`, change the element to `rect` if it is an
  engine-generated colored rectangle, or remove the non-visual node.
- Rect without color: add `color`, or change the element type if it is only a
  logical grouping node.
- Font fallback: provide the requested font under a searched task/assets path,
  or remove the explicit `fontFamily` only when the fallback is intentional.
- Text ink overflow: enlarge the text box or correct `fontSize`, `lineHeight`,
  or `strokeWidth`; do not move an already aligned box to conceal overflow.
- Atomic asset aspect warning: preserve the source aspect for icons and
  portraits, unless the design visibly uses non-uniform scaling.
- Parent overflow warning: inspect intentional badge/focus overhangs; otherwise
  fix the child or parent bounds.
- High-risk overview-only review: inspect `risk_review.png`, then cite it or run
  `workflow.py target` for that exact element path.

Run `workflow.py finalize` after writing `alignment_review.md`. It rejects
stale artifact hashes, incomplete path coverage, visual-audit errors, and any
entry in `metadata.approximations` that is not explicitly accepted. It also
rejects structural warnings, missing/stale visual-audit waivers, reviews
without the exact current hash-binding comment, and recheck PNGs not registered
by the current check/target evidence chain.
