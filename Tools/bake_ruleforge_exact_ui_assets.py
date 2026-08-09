#!/usr/bin/env python3
"""Bake one native PNG per Ruleforge UI rect without non-uniform scaling.

The alpha-clean source art supplies pixel materials and end caps.  Each output
is assembled at three source pixels per Unity design unit: end caps are kept,
the center is cropped or tiled, and the completed image already has the exact
aspect ratio used by its RectTransform.  Runtime UI must display these sprites
as Image.Type.Simple rather than 9-slicing them.
"""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageOps


SCALE = 3

ACTION_OUTPUTS = {
    "RuleforgeButton_Main_330x72.png": (330, 72),
    "RuleforgeButton_Launch_204x60.png": (204, 60),
    "RuleforgeButton_Back_172x44.png": (172, 44),
    "RuleforgeButton_TowerAction_188x58.png": (188, 58),
    "RuleforgeButton_Upgrade_132x44.png": (132, 44),
    "RuleforgeButton_UpgradePortrait_200x90.png": (200, 90),
    "RuleforgeButton_HudPlay_118x36.png": (118, 36),
    "RuleforgeButton_StageReturn_184x38.png": (184, 38),
}

PARCHMENT_OUTPUTS = {
    "RuleforgePanel_Effect_876x120.png": (876, 120),
    "RuleforgePanel_Slot_670x80.png": (670, 80),
    "RuleforgePanel_EffectLandscapeMiddle_670x120.png": (670, 120),
    "RuleforgePanel_SlotLandscapeMiddle_480x80.png": (480, 80),
    "RuleforgePanel_Inventory_862x208.png": (862, 208),
    "RuleforgePanel_EffectPortrait_666x170.png": (666, 170),
    "RuleforgePanel_SlotPortrait_448x94.png": (448, 94),
    "RuleforgePanel_InventoryPortrait_646x252.png": (646, 252),
}

SPEED_OUTPUTS = {
    "RuleforgeButton_SpeedSlow_50x36.png": (55, 170, 190),
    "RuleforgeButton_SpeedNormal_50x36.png": (146, 180, 82),
    "RuleforgeButton_SpeedFast_50x36.png": (238, 157, 42),
    "RuleforgeButton_SpeedUltra_50x36.png": (221, 76, 39),
}

PICKER_PANEL_OUTPUTS = {
    "RuleforgePanel_TowerPicker1_380x176.png": (380, 176),
    "RuleforgePanel_TowerPicker2_380x272.png": (380, 272),
    "RuleforgePanel_TowerPicker3_380x368.png": (380, 368),
    "RuleforgePanel_TowerPicker4_380x464.png": (380, 464),
}

WOOD_LOADOUT_OUTPUTS = {
    "RuleforgePanel_InventoryLandscapeSide_400x660.png": (400, 660),
    "RuleforgePanel_TowerPreviewLandscape_280x660.png": (280, 660),
    "RuleforgePanel_InventoryLandscapeSide_380x660.png": (380, 660),
}

SQUARE_OUTPUTS = {
    "RuleforgeButton_Square80.png": 80,
    "RuleforgeButton_Square54.png": 54,
    "RuleforgeButton_Square36.png": 36,
    "RuleforgeButton_Square44.png": 44,
    "RuleforgeButton_Square94.png": 94,
    "RuleforgeButton_Square90.png": 90,
}


def trim_alpha(image: Image.Image) -> Image.Image:
    rgba = image.convert("RGBA")
    bbox = rgba.getchannel("A").getbbox()
    if bbox is None:
        raise ValueError("source contains no opaque pixels")
    return rgba.crop(bbox)


def tile_center(
    source: Image.Image,
    width: int,
    height: int,
) -> Image.Image:
    if width <= 0:
        return Image.new("RGBA", (0, height), (0, 0, 0, 0))
    strip_width = min(source.width, max(12, height // 3))
    left = max(0, (source.width - strip_width) // 2)
    strip = source.crop((left, 0, left + strip_width, height))
    output = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    cursor = 0
    flip = False
    while cursor < width:
        tile = ImageOps.mirror(strip) if flip else strip
        take = min(tile.width, width - cursor)
        output.alpha_composite(tile.crop((0, 0, take, height)), (cursor, 0))
        cursor += take
        flip = not flip
    return output


def bake_horizontal(
    source: Image.Image,
    logical_width: int,
    logical_height: int,
    cap_ratio: float,
) -> Image.Image:
    target_width = logical_width * SCALE
    target_height = logical_height * SCALE
    trimmed = trim_alpha(source)
    uniform_scale = target_height / trimmed.height
    scaled_width = max(1, round(trimmed.width * uniform_scale))
    scaled = trimmed.resize(
        (scaled_width, target_height),
        Image.Resampling.NEAREST,
    )

    cap_width = min(
        round(target_height * cap_ratio),
        scaled.width // 3,
        target_width // 3,
    )
    left_cap = scaled.crop((0, 0, cap_width, target_height))
    right_cap = scaled.crop(
        (scaled.width - cap_width, 0, scaled.width, target_height)
    )
    body = scaled.crop(
        (cap_width, 0, scaled.width - cap_width, target_height)
    )
    body_width = target_width - cap_width * 2

    seam_width = min(
        max(2, target_height // 10),
        body.width // 4,
        body_width // 4,
    )
    middle_width = max(0, body_width - seam_width * 2)
    center = tile_center(body, middle_width, target_height)

    output = Image.new(
        "RGBA",
        (target_width, target_height),
        (0, 0, 0, 0),
    )
    output.alpha_composite(left_cap, (0, 0))
    if seam_width > 0:
        output.alpha_composite(
            body.crop((0, 0, seam_width, target_height)),
            (cap_width, 0),
        )
    output.alpha_composite(center, (cap_width + seam_width, 0))
    if seam_width > 0:
        output.alpha_composite(
            body.crop(
                (
                    body.width - seam_width,
                    0,
                    body.width,
                    target_height,
                )
            ),
            (target_width - cap_width - seam_width, 0),
        )
    output.alpha_composite(right_cap, (target_width - cap_width, 0))
    return output


def bake_square(
    source: Image.Image,
    logical_size: int,
) -> Image.Image:
    target = logical_size * SCALE
    trimmed = trim_alpha(source)
    scale = min(target / trimmed.width, target / trimmed.height)
    resized = trimmed.resize(
        (
            max(1, round(trimmed.width * scale)),
            max(1, round(trimmed.height * scale)),
        ),
        Image.Resampling.NEAREST,
    )
    output = Image.new("RGBA", (target, target), (0, 0, 0, 0))
    output.alpha_composite(
        resized,
        ((target - resized.width) // 2, (target - resized.height) // 2),
    )
    return output


def tile_patch(
    patch: Image.Image,
    width: int,
    height: int,
) -> Image.Image:
    """Fill a rectangle with cropped whole-pixel tiles, never stretching."""
    output = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    if width <= 0 or height <= 0 or patch.width <= 0 or patch.height <= 0:
        return output

    y = 0
    row = 0
    while y < height:
        x = 0
        column = 0
        while x < width:
            tile = patch
            if (row + column) % 2:
                tile = ImageOps.mirror(tile)
            if row % 2:
                tile = ImageOps.flip(tile)
            take_width = min(tile.width, width - x)
            take_height = min(tile.height, height - y)
            output.alpha_composite(
                tile.crop((0, 0, take_width, take_height)),
                (x, y),
            )
            x += take_width
            column += 1
        y += min(patch.height, height - y)
        row += 1
    return output


def bake_panel_2d(
    source: Image.Image,
    logical_width: int,
    logical_height: int,
    source_inset_ratio: float = 0.058,
    center_tile_size: int | None = None,
    flat_center_color: tuple[int, int, int, int] | None = None,
) -> Image.Image:
    """Assemble a native-size panel from fixed corners and tiled surfaces."""
    target_width = logical_width * SCALE
    target_height = logical_height * SCALE
    trimmed = trim_alpha(source)

    source_inset = max(
        12,
        round(min(trimmed.size) * source_inset_ratio),
    )
    target_inset = 10 * SCALE
    uniform_scale = target_inset / source_inset
    scaled = trimmed.resize(
        (
            max(target_inset * 3, round(trimmed.width * uniform_scale)),
            max(target_inset * 3, round(trimmed.height * uniform_scale)),
        ),
        Image.Resampling.NEAREST,
    )
    inset = target_inset

    left = inset
    right = scaled.width - inset
    top = inset
    bottom = scaled.height - inset
    center_width = target_width - inset * 2
    center_height = target_height - inset * 2

    output = Image.new(
        "RGBA",
        (target_width, target_height),
        (0, 0, 0, 0),
    )
    center_region = scaled.crop((left, top, right, bottom))
    center_patch_width = min(
        center_tile_size or center_region.width,
        center_region.width,
    )
    center_patch_height = min(
        center_tile_size or center_region.height,
        center_region.height,
    )
    center_patch_x = (center_region.width - center_patch_width) // 2
    center_patch_y = (center_region.height - center_patch_height) // 2
    center_patch = center_region.crop(
        (
            center_patch_x,
            center_patch_y,
            center_patch_x + center_patch_width,
            center_patch_y + center_patch_height,
        )
    )
    center_surface = (
        Image.new(
            "RGBA",
            (center_width, center_height),
            flat_center_color,
        )
        if flat_center_color is not None
        else tile_patch(center_patch, center_width, center_height)
    )
    output.alpha_composite(center_surface, (inset, inset))
    top_region = scaled.crop((left, 0, right, inset))
    top_patch_width = min(
        center_tile_size or top_region.width,
        top_region.width,
    )
    top_patch_x = (top_region.width - top_patch_width) // 2
    top_patch = top_region.crop(
        (top_patch_x, 0, top_patch_x + top_patch_width, inset)
    )
    output.alpha_composite(
        tile_patch(
            top_patch,
            center_width,
            inset,
        ),
        (inset, 0),
    )
    bottom_region = scaled.crop((left, bottom, right, scaled.height))
    bottom_patch_width = min(
        center_tile_size or bottom_region.width,
        bottom_region.width,
    )
    bottom_patch_x = (bottom_region.width - bottom_patch_width) // 2
    bottom_patch = bottom_region.crop(
        (
            bottom_patch_x,
            0,
            bottom_patch_x + bottom_patch_width,
            inset,
        )
    )
    output.alpha_composite(
        tile_patch(
            bottom_patch,
            center_width,
            inset,
        ),
        (inset, target_height - inset),
    )
    left_region = scaled.crop((0, top, inset, bottom))
    left_patch_height = min(
        center_tile_size or left_region.height,
        left_region.height,
    )
    left_patch_y = (left_region.height - left_patch_height) // 2
    left_patch = left_region.crop(
        (0, left_patch_y, inset, left_patch_y + left_patch_height)
    )
    output.alpha_composite(
        tile_patch(
            left_patch,
            inset,
            center_height,
        ),
        (0, inset),
    )
    right_region = scaled.crop((right, top, scaled.width, bottom))
    right_patch_height = min(
        center_tile_size or right_region.height,
        right_region.height,
    )
    right_patch_y = (right_region.height - right_patch_height) // 2
    right_patch = right_region.crop(
        (0, right_patch_y, inset, right_patch_y + right_patch_height)
    )
    output.alpha_composite(
        tile_patch(
            right_patch,
            inset,
            center_height,
        ),
        (target_width - inset, inset),
    )

    output.alpha_composite(scaled.crop((0, 0, inset, inset)), (0, 0))
    output.alpha_composite(
        scaled.crop((right, 0, scaled.width, inset)),
        (target_width - inset, 0),
    )
    output.alpha_composite(
        scaled.crop((0, bottom, inset, scaled.height)),
        (0, target_height - inset),
    )
    output.alpha_composite(
        scaled.crop((right, bottom, scaled.width, scaled.height)),
        (target_width - inset, target_height - inset),
    )
    return output


def recolor_speed_indicator(
    image: Image.Image,
    color: tuple[int, int, int],
) -> Image.Image:
    """Give each time-speed key a distinct lower indicator color."""
    rgba = image.convert("RGBA")
    pixels = rgba.load()
    start_y = round(rgba.height * 0.58)
    target_r, target_g, target_b = color
    for y in range(start_y, rgba.height):
        for x in range(rgba.width):
            red, green, blue, alpha = pixels[x, y]
            if alpha == 0 or red < 80 or red < green * 1.12:
                continue
            brightness = max(red, green, blue) / 255.0
            pixels[x, y] = (
                round(target_r * brightness),
                round(target_g * brightness),
                round(target_b * brightness),
                alpha,
            )
    return rgba


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--action", type=Path)
    parser.add_argument("--parchment", required=True, type=Path)
    parser.add_argument("--square", type=Path)
    parser.add_argument("--tower-option", type=Path)
    parser.add_argument("--speed", type=Path)
    parser.add_argument("--workbench", required=True, type=Path)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument(
        "--surfaces-only",
        action="store_true",
        help="Bake parchment/wood panel surfaces without rewriting buttons.",
    )
    args = parser.parse_args()

    args.out.mkdir(parents=True, exist_ok=True)
    parchment = Image.open(args.parchment).convert("RGBA")
    workbench = Image.open(args.workbench).convert("RGBA")

    for filename, (width, height) in PARCHMENT_OUTPUTS.items():
        bake_horizontal(parchment, width, height, 0.23).save(
            args.out / filename
        )
    for filename, (width, height) in WOOD_LOADOUT_OUTPUTS.items():
        bake_panel_2d(
            workbench,
            width,
            height,
            flat_center_color=(70, 43, 27, 255),
        ).save(
            args.out / filename
        )

    if args.surfaces_only:
        return

    required_button_sources = {
        "--action": args.action,
        "--square": args.square,
        "--tower-option": args.tower_option,
        "--speed": args.speed,
    }
    missing = [
        name
        for name, value in required_button_sources.items()
        if value is None
    ]
    if missing:
        parser.error(
            "button sources are required without --surfaces-only: " +
            ", ".join(missing)
        )

    action = Image.open(args.action).convert("RGBA")
    square = Image.open(args.square).convert("RGBA")
    tower_option = Image.open(args.tower_option).convert("RGBA")
    speed = Image.open(args.speed).convert("RGBA")

    for filename, (width, height) in ACTION_OUTPUTS.items():
        bake_horizontal(action, width, height, 0.58).save(
            args.out / filename
        )
    for filename, size in SQUARE_OUTPUTS.items():
        bake_square(square, size).save(args.out / filename)
    bake_horizontal(tower_option, 356, 88, 0.18).save(
        args.out / "RuleforgeButton_TowerOption_356x88.png"
    )
    speed_base = bake_horizontal(speed, 50, 36, 0.30)
    for filename, indicator_color in SPEED_OUTPUTS.items():
        variant = recolor_speed_indicator(speed_base, indicator_color)
        variant.save(args.out / filename)
        if filename == "RuleforgeButton_SpeedNormal_50x36.png":
            variant.save(
                args.out / "RuleforgeButton_HudSpeed_50x36.png"
            )
    for filename, (width, height) in PICKER_PANEL_OUTPUTS.items():
        bake_panel_2d(
            workbench,
            width,
            height,
            flat_center_color=(70, 43, 27, 255),
        ).save(
            args.out / filename
        )


if __name__ == "__main__":
    main()
