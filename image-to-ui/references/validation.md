# Structure Validation Reference

Use this reference when `validate_structure.py` reports errors or warnings.

## Command

```bash
py -B <skill>/scripts/validate_structure.py --structure out/<design-stem>/ui_structure.json --design design.png --assets sprite_dir --report out/<design-stem>/validate_report.json
```

Default stdout is compact. Read `validate_report.json` for full errors and
warnings. Add `--json` only when the full report must be printed to stdout.

## Fix Priority

1. Fix all errors before bbox alignment.
2. Fix warnings that affect the current design.
3. Use `--warnings-as-errors` for strict handoff.

## Common Issues

- Canvas mismatch: set `canvas.width` / `canvas.height` to the design's native
  size or rerun `scale_structure.py`.
- Missing or ambiguous asset: copy the exact path from `assets_inventory.json`.
- Position inside layout: remove child `position` and use `offset`, or remove
  the parent layout if the child needs an independent position.
- Image without asset: add `asset`, change the element to `rect` if it is an
  engine-generated colored rectangle, or remove the non-visual node.
- Rect without color: add `color`, or change the element type if it is only a
  logical grouping node.
