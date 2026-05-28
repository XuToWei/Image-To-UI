# Asset Inventory Reference

Use this reference when selecting sliced sprites for `ui_structure.json`.

## Outputs

`inventory_assets.py` writes:

- `assets_inventory.json`: exact relative paths, dimensions, alpha bounds,
  duplicate basename data, usage hints, and Unity `spriteBorder` metadata.
- `assets_contact_sheet.png`: all assets.
- `assets_contact_sheet_usage_*.png`: grouped sheets. Prefer these first to
  reduce visual context.

## Selection Rules

1. Use grouped sheets first:
   - `assets_contact_sheet_usage_button_or_panel.png` for panels, frames,
     buttons, bubbles, bars, and stretchable backgrounds.
   - `assets_contact_sheet_usage_icon.png` for icons and item art.
   - Fall back to the full sheet only when grouped sheets do not contain the
     needed sprite.
2. If `duplicate_basenames` is non-empty, write `asset` as the relative path
   from `assets_inventory.json`, not as the basename.
3. Prefer `"nineSlice": "meta"` or `true` when the inventory entry is
   `button_or_panel` and `has_meta_border` is true.
4. Do not use nine-slice for `icon`, `portrait`, or item art unless the design
   clearly stretches that asset.
