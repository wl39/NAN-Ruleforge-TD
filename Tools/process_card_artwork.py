#!/usr/bin/env python3
"""Prepare Ruleforge card artwork for the authored parchment frames.

The generated effect sources use a dark preview backdrop. Runtime card art
must instead be transparent so the tier frame's parchment remains visible.
Cards that depict an enemy replace the generated creature with the first
front-facing CraftPix goblin walk frame; this script never synthesizes or
redraws the goblin.
"""

from __future__ import annotations

import argparse
import math
import statistics
from collections import deque
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

from PIL import Image, ImageChops, ImageDraw, ImageFilter


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_SOURCE_ROOT = ROOT / "Tools/CardArtworkSource"
DEFAULT_OUTPUT_ROOT = (
    ROOT / "Assets/Game/Resources/RuleforgeTD/UI/Cards/Artwork"
)
GOBLIN_SHEET = (
    ROOT / "Assets/ThirdParty/CraftPix/Raw/Enemies/goblin/D_Walk.png"
)

CANVAS_SIZE = (192, 112)
EFFECT_FIT_SIZE = (180, 100)
MAX_EFFECT_UPSCALE = 1.45
FRONT_FRAME_RECT = (0, 0, 48, 48)
GOBLIN_VISIBLE_HEIGHT = 44


@dataclass(frozen=True)
class GoblinComposition:
    erase_regions: tuple[tuple[int, int, int, int], ...]
    placements: tuple[tuple[int, int], ...]
    visible_height: int = GOBLIN_VISIBLE_HEIGHT


# Erase regions are authored against the 192x112 source art. Placements are
# center points on the final 192x112 transparent canvas. The same extracted
# front frame and visible height are used throughout unless a multi-subject
# composition needs a slightly smaller figure.
GOBLIN_COMPOSITIONS: dict[str, GoblinComposition] = {
    "airborne": GoblinComposition(((82, 24, 114, 59),), ((96, 36),)),
    "bind": GoblinComposition(((75, 31, 120, 78),), ((96, 57),)),
    "contagion": GoblinComposition(
        ((35, 28, 81, 84), (112, 29, 162, 85)),
        ((61, 59), (131, 59)),
        38,
    ),
    "corrosion": GoblinComposition(((72, 28, 121, 90),), ((96, 59),)),
    "curse": GoblinComposition(((74, 31, 123, 86),), ((96, 56),)),
    "dual_interpretation": GoblinComposition(
        ((23, 33, 63, 80),),
        ((45, 57),),
        38,
    ),
    "execute": GoblinComposition(((75, 60, 118, 105),), ((96, 79),), 38),
    "fear": GoblinComposition(((52, 43, 101, 94),), ((70, 67),)),
    "freeze": GoblinComposition(((73, 34, 120, 89),), ((96, 58),)),
    "gold_bounty": GoblinComposition(((72, 33, 121, 88),), ((96, 59),)),
    "infinite_orbit": GoblinComposition(((73, 31, 120, 86),), ((96, 56),)),
    "knockback": GoblinComposition(((79, 32, 143, 89),), ((115, 59),)),
    "mark": GoblinComposition(((72, 33, 121, 89),), ((96, 59),)),
    "mutation": GoblinComposition(((65, 22, 111, 98),), ((86, 59),)),
    "orbit": GoblinComposition(((74, 34, 117, 86),), ((96, 58),)),
    "parasite": GoblinComposition(((53, 32, 101, 90),), ((75, 59),)),
    "seal": GoblinComposition(((77, 37, 115, 89),), ((96, 58),)),
    "stun": GoblinComposition(((70, 30, 123, 91),), ((96, 59),)),
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--source-root",
        type=Path,
        default=DEFAULT_SOURCE_ROOT,
    )
    parser.add_argument(
        "--output-root",
        type=Path,
        default=DEFAULT_OUTPUT_ROOT,
    )
    parser.add_argument(
        "--validate-only",
        action="store_true",
        help="Validate existing outputs without rewriting them.",
    )
    return parser.parse_args()


def median_background(image: Image.Image) -> tuple[int, int, int]:
    rgb = image.convert("RGB")
    width, height = rgb.size
    border: list[tuple[int, int, int]] = []
    for x in range(width):
        border.append(rgb.getpixel((x, 0)))
        border.append(rgb.getpixel((x, height - 1)))
    for y in range(1, height - 1):
        border.append(rgb.getpixel((0, y)))
        border.append(rgb.getpixel((width - 1, y)))
    return tuple(
        int(statistics.median(pixel[channel] for pixel in border))
        for channel in range(3)
    )


def connected_dark_background(image: Image.Image) -> bytearray:
    """Find dark backdrop pixels connected to the canvas boundary.

    Some sources are pillarboxed with pure black while the intended preview
    backdrop is charcoal-purple. Boundary flood fill removes both without
    punching holes into enclosed dark effect details such as a singularity.
    """

    rgb = image.convert("RGB")
    width, height = rgb.size
    pixels = list(
        rgb.get_flattened_data()
        if hasattr(rgb, "get_flattened_data")
        else rgb.getdata()
    )
    connected = bytearray(width * height)
    pending: deque[int] = deque()

    def enqueue(x: int, y: int) -> None:
        index = y * width + x
        if connected[index] or max(pixels[index]) > 48:
            return
        connected[index] = 1
        pending.append(index)

    for x in range(width):
        enqueue(x, 0)
        enqueue(x, height - 1)
    for y in range(1, height - 1):
        enqueue(0, y)
        enqueue(width - 1, y)

    while pending:
        index = pending.popleft()
        x = index % width
        y = index // width
        if x > 0:
            enqueue(x - 1, y)
        if x + 1 < width:
            enqueue(x + 1, y)
        if y > 0:
            enqueue(x, y - 1)
        if y + 1 < height:
            enqueue(x, y + 1)
    return connected


def color_to_alpha(image: Image.Image) -> Image.Image:
    """Remove a nearly uniform backdrop while retaining soft magic glows."""

    rgb = image.convert("RGB")
    background = median_background(rgb)
    connected_background = connected_dark_background(rgb)
    output: list[tuple[int, int, int, int]] = []

    pixels = (
        rgb.get_flattened_data()
        if hasattr(rgb, "get_flattened_data")
        else rgb.getdata()
    )
    for index, color in enumerate(pixels):
        if connected_background[index]:
            output.append((0, 0, 0, 0))
            continue
        minimum_alpha = 0.0
        for channel, backdrop in zip(color, background):
            if channel >= backdrop:
                denominator = max(1, 255 - backdrop)
                channel_alpha = (channel - backdrop) / denominator
            else:
                denominator = max(1, backdrop)
                channel_alpha = (backdrop - channel) / denominator
            minimum_alpha = max(minimum_alpha, channel_alpha)

        if minimum_alpha <= 0.012:
            output.append((0, 0, 0, 0))
            continue

        alpha = min(1.0, minimum_alpha * 1.14)
        alpha = math.pow(alpha, 0.88)
        if alpha <= 0.02:
            output.append((0, 0, 0, 0))
            continue

        foreground = []
        for channel, backdrop in zip(color, background):
            value = (channel - (1.0 - alpha) * backdrop) / alpha
            foreground.append(max(0, min(255, round(value))))
        output.append((*foreground, max(0, min(255, round(alpha * 255)))))

    rgba = Image.new("RGBA", rgb.size)
    rgba.putdata(output)
    return rgba


def erase_generated_creatures(
    image: Image.Image,
    regions: Iterable[tuple[int, int, int, int]],
) -> Image.Image:
    keep = Image.new("L", image.size, 255)
    draw = ImageDraw.Draw(keep)
    for region in regions:
        draw.ellipse(region, fill=0)
    keep = keep.filter(ImageFilter.GaussianBlur(radius=2.0))
    result = image.copy()
    result.putalpha(ImageChops.multiply(result.getchannel("A"), keep))
    return result


def fit_effect(image: Image.Image) -> Image.Image:
    alpha = image.getchannel("A")
    threshold = alpha.point(lambda value: 255 if value >= 5 else 0)
    bounds = threshold.getbbox()
    canvas = Image.new("RGBA", CANVAS_SIZE, (0, 0, 0, 0))
    if bounds is None:
        return canvas

    left, top, right, bottom = bounds
    left = max(0, left - 2)
    top = max(0, top - 2)
    right = min(image.width, right + 2)
    bottom = min(image.height, bottom + 2)
    crop = image.crop((left, top, right, bottom))
    scale = min(
        EFFECT_FIT_SIZE[0] / crop.width,
        EFFECT_FIT_SIZE[1] / crop.height,
        MAX_EFFECT_UPSCALE,
    )
    size = (
        max(1, round(crop.width * scale)),
        max(1, round(crop.height * scale)),
    )
    if size != crop.size:
        crop = crop.resize(size, Image.Resampling.LANCZOS)
    position = (
        (CANVAS_SIZE[0] - crop.width) // 2,
        (CANVAS_SIZE[1] - crop.height) // 2,
    )
    canvas.alpha_composite(crop, position)
    return canvas


def load_front_goblin() -> Image.Image:
    sheet = Image.open(GOBLIN_SHEET).convert("RGBA")
    frame = sheet.crop(FRONT_FRAME_RECT)
    bounds = frame.getchannel("A").getbbox()
    if bounds is None:
        raise RuntimeError(f"Front goblin frame is empty: {GOBLIN_SHEET}")
    return frame.crop(bounds)


def resized_goblin(front: Image.Image, visible_height: int) -> Image.Image:
    scale = visible_height / front.height
    width = max(1, round(front.width * scale))
    return front.resize((width, visible_height), Image.Resampling.NEAREST)


def composite_goblins(
    canvas: Image.Image,
    front: Image.Image,
    composition: GoblinComposition,
) -> Image.Image:
    result = canvas.copy()
    goblin = resized_goblin(front, composition.visible_height)
    alpha = goblin.getchannel("A")
    outline_alpha = alpha.filter(ImageFilter.MaxFilter(3))
    outline = Image.new("RGBA", goblin.size, (40, 27, 19, 0))
    outline.putalpha(outline_alpha.point(lambda value: round(value * 0.9)))

    for center_x, center_y in composition.placements:
        position = (
            round(center_x - goblin.width * 0.5),
            round(center_y - goblin.height * 0.5),
        )
        result.alpha_composite(outline, position)
        result.alpha_composite(goblin, position)
    return result


def process_card(source: Path, front: Image.Image) -> Image.Image:
    effect = color_to_alpha(Image.open(source))
    composition = GOBLIN_COMPOSITIONS.get(source.stem)
    if composition is not None:
        effect = erase_generated_creatures(effect, composition.erase_regions)
    effect = fit_effect(effect)
    if composition is not None:
        effect = composite_goblins(effect, front, composition)
    return effect


def validate_output(path: Path, front: Image.Image) -> None:
    image = Image.open(path).convert("RGBA")
    if image.size != CANVAS_SIZE:
        raise RuntimeError(f"{path.name}: expected {CANVAS_SIZE}, got {image.size}")
    alpha = image.getchannel("A")
    if alpha.getbbox() is None:
        raise RuntimeError(f"{path.name}: artwork is empty")
    corners = (
        alpha.getpixel((0, 0)),
        alpha.getpixel((image.width - 1, 0)),
        alpha.getpixel((0, image.height - 1)),
        alpha.getpixel((image.width - 1, image.height - 1)),
    )
    if any(corners):
        raise RuntimeError(f"{path.name}: card-art corners must be transparent")
    alpha_values = (
        alpha.get_flattened_data()
        if hasattr(alpha, "get_flattened_data")
        else alpha.getdata()
    )
    transparent_pixels = sum(1 for value in alpha_values if value <= 8)
    if transparent_pixels < image.width * image.height * 0.35:
        raise RuntimeError(f"{path.name}: opaque preview backdrop is still visible")

    composition = GOBLIN_COMPOSITIONS.get(path.stem)
    if composition is None:
        return
    goblin = resized_goblin(front, composition.visible_height)
    goblin_pixels = goblin.load()
    output_pixels = image.load()
    for center_x, center_y in composition.placements:
        left = round(center_x - goblin.width * 0.5)
        top = round(center_y - goblin.height * 0.5)
        for y in range(goblin.height):
            for x in range(goblin.width):
                expected = goblin_pixels[x, y]
                if expected[3] == 255 and output_pixels[left + x, top + y] != expected:
                    raise RuntimeError(
                        f"{path.name}: canonical CraftPix front goblin was altered"
                    )


def main() -> int:
    args = parse_args()
    source_root = args.source_root.resolve()
    output_root = args.output_root.resolve()
    source_paths = sorted(source_root.glob("*.png"))
    if len(source_paths) != 58:
        raise RuntimeError(
            f"Expected 58 source card images in {source_root}, found {len(source_paths)}"
        )

    output_root.mkdir(parents=True, exist_ok=True)
    front = load_front_goblin()
    if not args.validate_only:
        for source in source_paths:
            result = process_card(source, front)
            result.save(output_root / source.name, optimize=True)

    output_paths = sorted(output_root.glob("*.png"))
    if len(output_paths) != 58:
        raise RuntimeError(
            f"Expected 58 output card images in {output_root}, found {len(output_paths)}"
        )
    for output in output_paths:
        validate_output(output, front)
    print(f"Validated {len(output_paths)} transparent card artworks in {output_root}")
    print(f"CraftPix front goblin source: {GOBLIN_SHEET} frame D_Walk_00")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
