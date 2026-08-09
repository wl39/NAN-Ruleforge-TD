# Ruleforge TD Pixel UI Style Guide

## Direction

The interface should feel like a medieval workshop that uses restrained
magic. The world art supplies the palette: dark iron, walnut, moss, aged
brass, parchment, and muted brick. Cyan and violet are reserved for magical
state accents rather than large button surfaces.

Pixel styling belongs in silhouettes, borders, state changes, and icons.
Body copy remains readable and does not need to imitate a low-resolution
arcade font.

## Button roles

| Role | Material | Use |
| --- | --- | --- |
| Primary | Walnut and brass | Start, confirm, upgrade |
| Secondary | Charcoal iron | Navigation and regular actions |
| Utility | Moss and iron | Speed, filters, compact controls |
| Selected | Brass with cyan rune | Current speed or selected tab |
| Danger | Muted brick and iron | Destructive or hostile action |

Target type colors such as projectile orange and enemy red are small accents
or restrained tints. They do not change an ordinary selection button into a
danger action.

## Construction

- Base sprite: 16 x 16 pixels.
- Filter mode: Point.
- Wrap mode: Clamp.
- Border: 5 pixels on every side.
- Rendering: Unity `Image.Type.Sliced`.
- Corners: stepped transparent corners, never a smooth rounded rectangle.
- Surface: one sparse pixel cluster pattern; no gradients.
- Rim: bright upper brass edge and dark lower edge for physical depth.

## Interaction states

- Normal: restrained material color.
- Hovered: surface and brass rim brighten slightly.
- Pressed: surface darkens and the upper face loses height.
- Selected: cyan rune rivets mark selection without recoloring the full body.
- Disabled: desaturated stone/iron sprite rather than opacity alone.

Runtime controls use `RuleforgePixelUi` and `RuleforgePixelButtonSkin` so new
buttons inherit the same state language instead of defining local `ColorTint`
values.

## Scaling

Button rectangles and offsets should use whole-number canvas units. Avoid
sub-pixel anchored positions for compact HUD controls. The sliced centre may
stretch, but the five-pixel border must retain point filtering.

## Card construction

- Outer frame: 24 x 32 pixel walnut and brass sprite, sliced at 6 pixels.
- Body: muted projectile orange or enemy brick red beneath a neutral inset.
- Tier color: limited to the tier badge and narrow side accent.
- Name, artwork, and description: separate recessed pixel panels.
- Equipped: cyan rune rivets and a compact brass badge.
- Disabled: desaturated frame sprite; card information remains readable.

Tier accents use aged silver, moss green, muted arcane blue, warm gold, and
crimson-violet from common through mythic. They identify rarity without
recoloring the entire card.

## Reference

`Docs/ArtDirection/RuleforgePixelButtonStyleReference.png` is the generated
material and silhouette reference. Production UI uses the deterministic
16 x 16 runtime sprites so results are stable across WebGL resolutions.

`Docs/ArtDirection/RuleforgePixelCardStyleReference.png` is the corresponding
card material reference. Production cards use `RuleforgePixelCardUi` rather
than slicing this concept sheet directly.
