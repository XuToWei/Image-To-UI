"""
Annotate a UI design image with a coordinate grid so an agent can read
positions off it instead of guessing.

Grid layout:
  - Target cell size ~45-50 px (tuned for phone-sized designs); the actual
    cell count is computed from the design's pixel dimensions so the cells
    stay roughly square regardless of aspect ratio.
  - Cells are numbered along the top (col) and left edges (row).
  - Lines are thin and semi-transparent so they don't obscure UI detail.
  - Every 5th line is drawn slightly thicker for easy scanning.

The draw_grid() function is reused by annotate_element.py and
render_comparison.py so every reference image carries the same ruler.

Usage:
    py -B annotate_grid.py --design path/to/design.png --output path/to/design_grid.png
"""

import argparse
import json
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


DEFAULT_CELL_SIZE = 45
MAJOR_EVERY = 5

LINE_COLOR_MINOR = (60, 80, 110, 90)    # faint blue-grey
LINE_COLOR_MAJOR = (40, 60, 90, 170)    # darker for every 5th line
LABEL_COLOR = (255, 60, 60, 230)        # red, easy to spot against any UI
LABEL_BG    = (255, 255, 255, 200)


def find_font(size: int) -> ImageFont.FreeTypeFont:
    candidates = [
        "C:/Windows/Fonts/arialbd.ttf",
        "C:/Windows/Fonts/arial.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "/System/Library/Fonts/Helvetica.ttc",
    ]
    for c in candidates:
        if Path(c).exists():
            try:
                return ImageFont.truetype(c, size)
            except Exception:
                continue
    return ImageFont.load_default()


def compute_grid(W: int, H: int, target_cell: int = DEFAULT_CELL_SIZE):
    """Return (cols, rows, sx, sy) for a grid sized to W x H so each cell is
    approximately target_cell pixels."""
    cols = max(1, round(W / target_cell))
    rows = max(1, round(H / target_cell))
    return cols, rows, W / cols, H / rows


def draw_grid(image: Image.Image, target_cell: int = DEFAULT_CELL_SIZE) -> Image.Image:
    """Overlay a labeled grid on an RGBA image. Returns a new image; does not
    mutate the input. The grid sizing is computed from the image's own
    dimensions, so each call produces a grid intrinsic to that image - handy
    when annotating both the design and a reconstruction of the same canvas.
    """
    base = image.convert("RGBA")
    W, H = base.size
    cols, rows, sx, sy = compute_grid(W, H, target_cell)

    overlay = Image.new("RGBA", base.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)

    # Vertical lines
    for c in range(cols + 1):
        x = int(round(c * sx))
        is_major = (c % MAJOR_EVERY == 0) or c == cols
        color = LINE_COLOR_MAJOR if is_major else LINE_COLOR_MINOR
        width = 2 if is_major else 1
        draw.line([(x, 0), (x, H)], fill=color, width=width)

    # Horizontal lines
    for r in range(rows + 1):
        y = int(round(r * sy))
        is_major = (r % MAJOR_EVERY == 0) or r == rows
        color = LINE_COLOR_MAJOR if is_major else LINE_COLOR_MINOR
        width = 2 if is_major else 1
        draw.line([(0, y), (W, y)], fill=color, width=width)

    label_size = max(14, int(min(sx, sy) * 0.45))
    font = find_font(label_size)

    # Column labels
    for c in range(0, cols + 1, MAJOR_EVERY):
        x = int(round(c * sx))
        label = str(c)
        try:
            bbox = font.getbbox(label)
            tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
        except Exception:
            tw, th = label_size * len(label), label_size
        pad = 2
        draw.rectangle([x + 2, 2, x + 2 + tw + 2 * pad, 2 + th + 2 * pad],
                       fill=LABEL_BG)
        draw.text((x + 2 + pad, 2 + pad), label, font=font, fill=LABEL_COLOR)

    # Row labels
    for r in range(0, rows + 1, MAJOR_EVERY):
        y = int(round(r * sy))
        label = str(r)
        try:
            bbox = font.getbbox(label)
            tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
        except Exception:
            tw, th = label_size * len(label), label_size
        pad = 2
        draw.rectangle([2, y + 2, 2 + tw + 2 * pad, y + 2 + th + 2 * pad],
                       fill=LABEL_BG)
        draw.text((2 + pad, y + 2 + pad), label, font=font, fill=LABEL_COLOR)

    return Image.alpha_composite(base, overlay)


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--design", required=True)
    ap.add_argument("--output", required=True)
    ap.add_argument("--metrics",
                    help="Optional path for grid metrics JSON. Defaults to "
                         "<output-stem>_metrics.json")
    ap.add_argument("--cell-size", type=int, default=DEFAULT_CELL_SIZE,
                    help=f"Target grid cell size in px (default {DEFAULT_CELL_SIZE})")
    args = ap.parse_args()

    design_path = Path(args.design)
    out_path = Path(args.output)
    if not design_path.exists():
        print(f"Design not found: {design_path}", file=sys.stderr)
        sys.exit(1)

    base = Image.open(design_path).convert("RGBA")
    W, H = base.size
    cols, rows, sx, sy = compute_grid(W, H, args.cell_size)
    out = draw_grid(base, args.cell_size)

    out_path.parent.mkdir(parents=True, exist_ok=True)
    out.convert("RGB").save(out_path)
    metrics_path = Path(args.metrics) if args.metrics else out_path.with_name(
        f"{out_path.stem}_metrics.json"
    )
    metrics = {
        "design": str(design_path),
        "grid_image": str(out_path),
        "canvas": {"width": W, "height": H},
        "grid": {"cols": cols, "rows": rows},
        "px_per_cell_x": sx,
        "px_per_cell_y": sy,
        "cell_size_target": args.cell_size,
    }
    metrics_path.parent.mkdir(parents=True, exist_ok=True)
    metrics_path.write_text(json.dumps(metrics, indent=2) + "\n",
                            encoding="utf-8")
    print(f"Wrote {out_path}")
    print(f"Wrote {metrics_path}")
    print(f"  canvas: {W} x {H}")
    print(f"  grid:   {cols} cols x {rows} rows (~{sx:.1f} x {sy:.1f} px/cell)")
    print(f"  px_per_cell_x: {sx:.1f}")
    print(f"  px_per_cell_y: {sy:.1f}")


if __name__ == "__main__":
    main()
