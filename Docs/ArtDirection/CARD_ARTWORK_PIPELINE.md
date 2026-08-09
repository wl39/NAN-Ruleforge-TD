# Ruleforge TD card artwork pipeline

## Runtime contract

- Every card artwork filename is the card's stable content ID plus `.png`.
- Runtime path: `Assets/Game/Resources/RuleforgeTD/UI/Cards/Artwork/<card-id>.png`.
- `StageOneCardArtworkCatalog` resolves this convention and caches the loaded sprite.
- `StageOneCardView.Configure` uses the catalog automatically when a caller does not provide an explicit sprite.
- An explicit sprite remains an override for tests, events, or future card variants.

## Authored format

- Canvas: 192×112 PNG.
- Filter: Point.
- Background: transparent. The tier frame's authored parchment is the card-art background; runtime artwork must not carry a black or charcoal rectangle.
- Subject: one centered magic VFX silhouette, normalized to a maximum 180×100 content area so it fills the window without touching the metal corners.
- Readability target: 96×56 pixels in the card artwork area.
- Do not add card frames, UI, scenery, text, letters, numbers, watermarks, cannonballs, cannons, skulls, or bones.
- Never generate or redraw a monster for card art. When a monster is required, composite the exact front-facing `D_Walk_00` frame from `Assets/ThirdParty/CraftPix/Raw/Enemies/goblin/D_Walk.png`.

## Deterministic finishing pass

- Generated effect sources are preserved in `Tools/CardArtworkSource/`.
- Run `python3 Tools/process_card_artwork.py` from the repository root.
- The script removes the source preview backdrop with a soft color-to-alpha conversion, normalizes the visible effect to the card window, and writes RGBA artwork to the runtime path.
- Enemy-facing compositions erase the generated creature and insert the same CraftPix front goblin frame at a consistent displayed scale. The script does not use image generation for this step.
- `python3 Tools/process_card_artwork.py --validate-only` checks the 58 runtime outputs without rewriting them.

## Image generation prompt basis

Mode: `stylized-concept`.

The shared prompt requests a polished 32×32-era fantasy magic-effect sprite, crisp hand-placed pixels, a limited palette, stepped pixel glow, one effect centered on a flat removable charcoal-purple background, and no illustrated scene. Prompts request effects only and must never request a goblin or other monster; the deterministic finishing pass supplies the canonical CraftPix goblin when needed. Each card appends only its gameplay-specific visual action. Tier palettes progress from elemental cyan/amber at T1–T2, through ghostly violet/teal at T3, to gold/crimson/violet at T4 and cosmic magenta/teal/gold at T5.

Visual direction reference: [CraftPix 10 Magic Sprite Sheet Effects Pixel Art](https://craftpix.net/product/10-magic-sprite-sheet-effects-pixel-art/).

## Validation

`CardArtworkAssetTests.EveryAuthoredCardHasPixelArtwork` parses the authoritative content JSON and fails when a card lacks a same-ID sprite, has the wrong 192×112 dimensions, is not imported with point filtering, or still has opaque card-art corners.
