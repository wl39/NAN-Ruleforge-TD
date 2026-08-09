#!/usr/bin/env python3
"""Create the low-detail runtime edition of the Ruleforge UI artwork.

The authored files remain untouched.  Runtime copies are reduced to one
logical pixel for every three source pixels and share a small material
palette, so cards, controls, and panels read like the 32 px CraftPix field
art instead of miniature painted illustrations.
"""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageFilter


# Four or fewer values per material family: ink, wood, iron, olive, brass,
# parchment, and the four card-tier accents.  The common palette prevents a
# different parchment or metal ramp from appearing on every component.
RULEFORGE_FLAT_PALETTE = (
    (18, 14, 11),
    (42, 28, 20),
    (70, 43, 27),
    (103, 62, 34),
    (134, 82, 42),
    (53, 55, 51),
    (85, 88, 81),
    (137, 138, 124),
    (194, 188, 160),
    (47, 54, 31),
    (74, 82, 42),
    (108, 116, 58),
    (102, 69, 24),
    (158, 108, 34),
    (210, 154, 52),
    (239, 196, 96),
    (164, 112, 60),
    (207, 155, 85),
    (233, 195, 126),
    (248, 223, 171),
    (33, 48, 67),
    (52, 76, 101),
    (79, 109, 142),
    (50, 78, 42),
    (82, 116, 57),
    (111, 43, 34),
    (180, 65, 44),
    (218, 113, 49),
    (75, 44, 87),
    (120, 69, 132),
)

MATERIAL_COLLAPSE = {
    # Wood: outline, one body tone, one edge highlight.
    (103, 62, 34): (70, 43, 27),
    (134, 82, 42): (103, 62, 34),
    # Iron and brass: eliminate the extra glossy highlight.
    (194, 188, 160): (137, 138, 124),
    (239, 196, 96): (210, 154, 52),
    # Parchment: the two light values become one calm writing surface.
    (233, 195, 126): (248, 223, 171),
}

PARCHMENT_SURFACE_COLORS = {
    (207, 155, 85),
    (248, 223, 171),
}
PARCHMENT_BASE = (248, 223, 171)
WOOD_SURFACE_COLORS = {
    (42, 28, 20),
    (70, 43, 27),
    (103, 62, 34),
}
WOOD_BASE = (70, 43, 27)


def clean_row_spans(
    image: Image.Image,
    colors: set[tuple[int, int, int]],
    fill: tuple[int, int, int],
    minimum_ratio: float,
) -> None:
    pixels = image.load()
    for y in range(image.height):
        columns = [
            x
            for x in range(image.width)
            if pixels[x, y] in colors
        ]
        if len(columns) < round(image.width * minimum_ratio):
            continue
        left = min(columns) + 2
        right = max(columns) - 1
        for x in range(left, right):
            pixels[x, y] = fill


def build_palette() -> Image.Image:
    palette = Image.new("P", (1, 1))
    values = [channel for color in RULEFORGE_FLAT_PALETTE for channel in color]
    values.extend(
        list(RULEFORGE_FLAT_PALETTE[0]) *
        (256 - len(RULEFORGE_FLAT_PALETTE))
    )
    palette.putpalette(values)
    return palette


def simplify(
    image: Image.Image,
    cluster: int,
    median_size: int = 3,
    clean_writing_surfaces: bool = True,
) -> Image.Image:
    """Flatten detail while retaining the exact source dimensions and alpha."""
    rgba = image.convert("RGBA")
    width, height = rgba.size
    logical_size = (
        max(1, round(width / cluster)),
        max(1, round(height / cluster)),
    )
    logical = rgba.resize(logical_size, Image.Resampling.BOX)
    alpha = logical.getchannel("A").point(
        lambda value: 255 if value >= 112 else 0
    )
    filtered = logical.convert("RGB").filter(
        ImageFilter.MedianFilter(size=median_size)
    )
    rgb = filtered.quantize(
        palette=build_palette(),
        dither=Image.Dither.NONE,
    ).convert("RGB")
    pixels = rgb.load()
    for y in range(rgb.height):
        for x in range(rgb.width):
            pixels[x, y] = MATERIAL_COLLAPSE.get(
                pixels[x, y],
                pixels[x, y],
            )
    if clean_writing_surfaces:
        clean_row_spans(
            rgb,
            PARCHMENT_SURFACE_COLORS,
            PARCHMENT_BASE,
            minimum_ratio=0.28,
        )
        clean_row_spans(
            rgb,
            WOOD_SURFACE_COLORS,
            WOOD_BASE,
            minimum_ratio=0.32,
        )
    flattened = Image.merge("RGBA", (*rgb.split(), alpha))
    return flattened.resize((width, height), Image.Resampling.NEAREST)


def simplify_group(
    source_root: Path,
    output_root: Path,
    folder: str,
    cluster: int,
) -> None:
    source_dir = source_root / folder
    output_dir = output_root / folder
    output_dir.mkdir(parents=True, exist_ok=True)
    for source in sorted(source_dir.glob("*.png")):
        result = simplify(Image.open(source), cluster)
        result.save(output_dir / source.name, optimize=True)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-root", required=True, type=Path)
    parser.add_argument("--output-root", required=True, type=Path)
    parser.add_argument("--background", required=True, type=Path)
    parser.add_argument(
        "--background-name",
        default="RuleforgeLoadoutParchmentWorkbenchSimple.png",
    )
    args = parser.parse_args()

    for folder in ("Buttons", "Panels", "Cards"):
        simplify_group(args.source_root, args.output_root, folder, cluster=3)

    background_dir = args.output_root / "Backgrounds"
    background_dir.mkdir(parents=True, exist_ok=True)
    background = simplify(
        Image.open(args.background),
        cluster=4,
        median_size=5,
        clean_writing_surfaces=False,
    )
    background.save(background_dir / args.background_name, optimize=True)


if __name__ == "__main__":
    main()
