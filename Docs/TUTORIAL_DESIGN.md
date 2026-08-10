# Ruleforge TD tutorial contract

## Goals

The tutorial is presentation-only. It may pause Unity presentation time and
filter player input, but it never grants currency, changes content, submits a
hidden `GameCommand`, or participates in the deterministic simulation hash.

Stage 01 automatically starts the current tutorial version once. Completing
or skipping it suppresses later automatic starts. The game guide can request a
fresh Stage 01 tutorial run without resetting campaign progress.

## Three layers

1. **Core guided run** teaches the battlefield objective, wave forecast,
   tower construction, card drag-and-drop, per-slot projectile/enemy
   interpretation, left-to-right ordering, combat controls, enemy inspection,
   tower upgrades, and the first real three-card reward.
2. **Contextual tips** introduce later unlocked slots, compute capacity,
   additional construction cost, combat loadout locking, enemy archetypes,
   elites, bosses, card packs, status effects, victory, defeat, and the Stage
   02/03 identities. Each stable tip ID is shown at most once.
3. **Game guide** is available from the main menu and battle settings. It
   contains system-level reference pages plus the eleven unique demo starter
   cards, the four core enemy archetypes, and summaries of the three bosses.

## Core flow

The guided run continues through the first real three-card reward. The current
Stage 01 content has no regular wave draft and presents its first card pack
after wave three, so the presentation waits for that authored reward instead
of changing battle content or manufacturing a tutorial-only offer:

1. Explain spawn, route, base health, gold, wave, and phase HUD.
2. Require opening the next-wave forecast, then return to the compact view.
3. Require selecting the highlighted free build site and Ballista option.
4. Require selecting the placed tower and opening its card blueprint.
5. Require dragging an owned card into slot one. Click and double-click
   shortcuts are described but do not satisfy this step.
6. Require changing that slot from Projectile to Enemy and back to Projectile.
7. Explain left-to-right execution, locked slots, compute capacity, and combat
   editing locks.
8. Require closing the blueprint and starting wave one.
9. Explain pause and speed controls, then pause on the first live enemy and
   require opening its inspector.
10. After wave one, require a level-two tower upgrade once it is affordable.
11. Let the player continue the authored waves and select one of the first
    real three-card offers (currently the wave-three boss card pack).
12. If a combat-resume card pack requires its pending card to be equipped,
    guide tower selection, blueprint opening, and card placement before
    resuming. Planning-return packs resume normally and are followed by the
    one-shot card-equipping tip.
13. Mark the tutorial version complete and return full control.

Only action steps filter input. Informational and observation steps leave the
game interactive. A missing target or unaffordable upgrade releases filtering
until the requirement can be met again. Skipping, defeat, and scene teardown
always release tutorial-owned time and input state.

## Persistence and compatibility

Tutorial completion, dismissal, replay request, and contextual-tip IDs use
versioned PlayerPrefs keys so WebGL stores them in browser persistence. Core
progress inside an unfinished run is not saved; re-entering Stage 01 restarts
the lesson. Automated batch-mode scene tests do not auto-open the tutorial,
but may explicitly request a replay to exercise it.

Card upgrades and the fifteen unplayable tower designs are intentionally not
presented as available features. New content should add a contextual tip or
guide entry through data rather than adding simulation-specific tutorial
branches.
