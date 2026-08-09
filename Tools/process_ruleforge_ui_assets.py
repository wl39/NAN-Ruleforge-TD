#!/usr/bin/env python3
"""Prepare the authored Ruleforge TD card and button UI sprites.

The card reference is supplied as five frames on black. The matching button
sheet is generated on chroma-key magenta and cleaned before this script runs.
This tool keeps the source resolution, removes only border-connected black
from the card crops, and writes individual transparent PNGs for Unity.
"""

from __future__ import annotations

import argparse
from collections import deque
from pathlib import Path

from PIL import Image


CARD_NAMES = (
    "RuleforgeCardFrame_T1.png",
    "RuleforgeCardFrame_T2.png",
    "RuleforgeCardFrame_T3.png",
    "RuleforgeCardFrame_T4.png",
    "RuleforgeCardFrame_T5.png",
)

BUTTON_NAMES = (
    "RuleforgeButtonPrimary.png",
    "RuleforgeButtonSecondary.png",
    "RuleforgeButtonSquare.png",
)

LOADOUT_PANEL_NAMES = (
    "RuleforgeActionButtonCompact.png",
    "RuleforgeInfoPanel.png",
    "RuleforgeWorkbenchPanel.png",
)


def active_column_ranges(
    image: Image.Image,
    is_active,
    minimum_pixels: int = 8,
) -> list[tuple[int, int]]:
    width, height = image.size
    columns: list[int] = []
    pixels = image.load()
    for x in range(width):
        count = sum(1 for y in range(height) if is_active(pixels[x, y]))
        if count > minimum_pixels:
            columns.append(x)

    if not columns:
        return []

    ranges: list[tuple[int, int]] = []
    start = previous = columns[0]
    for x in columns[1:]:
        # Pixel-art antialiasing can leave one or two nearly isolated edge
        # columns. Treat tiny gaps as part of the same authored sprite.
        if x > previous + 6:
            ranges.append((start, previous))
            start = x
        previous = x
    ranges.append((start, previous))
    return ranges


def content_bounds(
    image: Image.Image,
    x_range: tuple[int, int],
    is_active,
) -> tuple[int, int, int, int]:
    x0, x1 = x_range
    pixels = image.load()
    rows = [
        y
        for y in range(image.height)
        if sum(1 for x in range(x0, x1 + 1) if is_active(pixels[x, y])) > 8
    ]
    if not rows:
        raise ValueError(f"No content found in x range {x_range}")
    return x0, min(rows), x1 + 1, max(rows) + 1


def remove_border_connected_black(image: Image.Image) -> Image.Image:
    rgba = image.convert("RGBA")
    pixels = rgba.load()
    width, height = rgba.size
    visited = bytearray(width * height)
    queue: deque[tuple[int, int]] = deque()

    def near_black(x: int, y: int) -> bool:
        red, green, blue, _ = pixels[x, y]
        return max(red, green, blue) <= 10

    def enqueue(x: int, y: int) -> None:
        index = y * width + x
        if visited[index] or not near_black(x, y):
            return
        visited[index] = 1
        queue.append((x, y))

    for x in range(width):
        enqueue(x, 0)
        enqueue(x, height - 1)
    for y in range(height):
        enqueue(0, y)
        enqueue(width - 1, y)

    while queue:
        x, y = queue.popleft()
        pixels[x, y] = (0, 0, 0, 0)
        if x > 0:
            enqueue(x - 1, y)
        if x + 1 < width:
            enqueue(x + 1, y)
        if y > 0:
            enqueue(x, y - 1)
        if y + 1 < height:
            enqueue(x, y + 1)

    return rgba


def trim_alpha(image: Image.Image, padding: int = 2) -> Image.Image:
    alpha = image.getchannel("A")
    bounds = alpha.getbbox()
    if bounds is None:
        raise ValueError("Image is fully transparent")
    left, top, right, bottom = bounds
    left = max(0, left - padding)
    top = max(0, top - padding)
    right = min(image.width, right + padding)
    bottom = min(image.height, bottom + padding)
    return image.crop((left, top, right, bottom))


def remove_magenta_residue(image: Image.Image) -> Image.Image:
    """Drop chroma pixels the soft matte leaves at hard pixel-art corners."""
    rgba = image.convert("RGBA")
    pixels = rgba.load()
    for y in range(rgba.height):
        for x in range(rgba.width):
            red, green, blue, alpha = pixels[x, y]
            if (
                red > 100
                and blue > 100
                and green < min(red, blue) * 0.58
            ):
                pixels[x, y] = (0, 0, 0, 0)
            elif alpha < 220 and red > green * 1.3 and blue > green * 1.3:
                pixels[x, y] = (0, 0, 0, 0)
    return rgba


def recompose_card_layout(image: Image.Image) -> Image.Image:
    """Trade artwork height for a real multi-line rules-text panel.

    Metal separators and the header/footer remain at native resolution. Only
    the long parchment/rail interiors are vertically resampled, so all five
    tier frames keep identical geometry without asking generated art to spell
    or position gameplay text.
    """
    width, height = image.size
    cuts = [
        0,
        round(height * 125 / 574),
        round(height * 165 / 574),
        round(height * 365 / 574),
        round(height * 420 / 574),
        round(height * 500 / 574),
        height,
    ]
    target_heights = [
        cuts[1] - cuts[0],
        cuts[2] - cuts[1],
        round(height * 115 / 574),
        cuts[4] - cuts[3],
        round(height * 165 / 574),
        cuts[6] - cuts[5],
    ]

    bands: list[Image.Image] = []
    for index, target_height in enumerate(target_heights):
        band = image.crop((0, cuts[index], width, cuts[index + 1]))
        if band.height != target_height:
            band = band.resize(
                (width, target_height),
                resample=Image.Resampling.NEAREST,
            )
        bands.append(band)

    output = Image.new("RGBA", (width, sum(target_heights)), (0, 0, 0, 0))
    y = 0
    for band in bands:
        output.alpha_composite(band, (0, y))
        y += band.height
    if output.height != height:
        output = output.resize((width, height), Image.Resampling.NEAREST)
    return output


def extract_cards(source: Path, output_dir: Path) -> None:
    image = Image.open(source).convert("RGBA")
    is_active = lambda pixel: max(pixel[0], pixel[1], pixel[2]) > 18
    ranges = active_column_ranges(image, is_active)
    if len(ranges) != len(CARD_NAMES):
        raise ValueError(f"Expected 5 card frames, found {len(ranges)}: {ranges}")

    output_dir.mkdir(parents=True, exist_ok=True)
    for name, x_range in zip(CARD_NAMES, ranges):
        crop = image.crop(content_bounds(image, x_range, is_active))
        crop = trim_alpha(remove_border_connected_black(crop))
        crop = recompose_card_layout(crop)
        crop.save(output_dir / name, optimize=True)


def extract_buttons(source: Path, output_dir: Path) -> None:
    image = Image.open(source).convert("RGBA")
    is_active = lambda pixel: pixel[3] > 24
    ranges = active_column_ranges(image, is_active)
    if len(ranges) != len(BUTTON_NAMES):
        raise ValueError(f"Expected 3 button frames, found {len(ranges)}: {ranges}")

    output_dir.mkdir(parents=True, exist_ok=True)
    for name, x_range in zip(BUTTON_NAMES, ranges):
        crop = image.crop(content_bounds(image, x_range, is_active))
        crop = remove_magenta_residue(crop)
        trim_alpha(crop).save(output_dir / name, optimize=True)


def extract_loadout_panels(source: Path, output_dir: Path) -> None:
    image = Image.open(source).convert("RGBA")
    is_active = lambda pixel: pixel[3] > 24
    ranges = active_column_ranges(image, is_active)
    if len(ranges) != len(LOADOUT_PANEL_NAMES):
        raise ValueError(
            f"Expected 3 loadout panels, found {len(ranges)}: {ranges}"
        )

    output_dir.mkdir(parents=True, exist_ok=True)
    for name, x_range in zip(LOADOUT_PANEL_NAMES, ranges):
        crop = image.crop(content_bounds(image, x_range, is_active))
        crop = remove_magenta_residue(crop)
        trim_alpha(crop).save(output_dir / name, optimize=True)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--cards", type=Path, required=True)
    parser.add_argument("--buttons", type=Path, required=True)
    parser.add_argument("--loadout-panels", type=Path)
    parser.add_argument("--output-root", type=Path, required=True)
    args = parser.parse_args()

    extract_cards(args.cards, args.output_root / "Cards")
    extract_buttons(args.buttons, args.output_root / "Buttons")
    if args.loadout_panels is not None:
        extract_loadout_panels(
            args.loadout_panels,
            args.output_root / "Panels",
        )


if __name__ == "__main__":
    main()
