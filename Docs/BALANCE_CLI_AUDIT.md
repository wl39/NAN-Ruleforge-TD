# Ruleforge TD Balance CLI Phase 1 Audit

> Audit date: 2026-08-02  
> Scope: current workspace content, GameLogic, supporting rules, existing tests,
> Unity assembly definitions, and generated C# projects  
> Execution status: repository inspection only; no tests, Unity Editor, WebGL
> build, or gameplay run was executed for this audit
>
> This is the pre-implementation Phase 1 snapshot. The subsequently implemented
> system and measured pass/fail results are recorded in `BALANCE_CLI_README.md`
> and `BALANCE_RESULTS.md`; statements below about missing APIs describe the
> workspace at audit start, not the final handoff.

## 1. Authority order

The balance CLI must resolve conflicting information in this order:

1. `Assets/Game/Data/Logic/phase1-content.json`
2. `Assets/Game/GameLogic` command validation and effect execution
3. `AI_GAME_CONTEXT_MANUAL.md`, titled
   `Ruleforge TD 현재 게임 설명서 및 AI 상황 판단 기준`
4. `BALANCE_RULES.md`, `CARD_RULES.md`, `TOWER_RULES.md`, and other
   supporting design documents
5. Unity prefabs, VFX, animation, and rendered presentation

`AGENTS.md` remains the top-level product design contract. When that design
contract differs from the currently executable run, the CLI must use compiled
content and GameLogic for simulation results and record the difference as an
implementation gap. It must not silently redefine the product design.

The Unity scene, `GameObject`, `Transform`, prefab health, animation state, and
render frame rate are never authoritative inputs for a CLI result.

## 2. Current authoritative content

The current `phase1-content.json` has content version `5` and compiles the
following catalog:

| Item | Current value |
| --- | ---: |
| Active cards | 58 |
| Common / Uncommon / Rare / Legendary / Mythic | 14 / 18 / 12 / 9 / 5 |
| Active towers | 3 |
| Active enemy definitions | 7 |
| Waves | 9 |
| Build spots | 8 |
| Base health | 20 |
| Starting gold | 0 |
| Free initial placements | 1 |
| Fixed simulation rate | 30 Hz |

Current start and reward configuration:

- Starting tower choices: `ballista`, `mutation_obelisk`
- Additionally owned tower definition: `death_engine`
- Starting cards: `split`, `burn`, `explode`, `poison`
- All eight build-spot unlock costs are currently `0`
- Regular wave-end drafts are disabled
- Boss card packs occur after waves `3` and `6`
- One kill-progress card pack can occur at `3,500,000` progress
- Wave `9` contains the final time-walker boss
- The first placed tower is free; it is not required to be the tower selected
  on the starting-tower screen
- Combat construction is legal, but ordinary card editing and tower upgrades
  are locked during Combat
- `CardPackLoadout` pauses logical ticks and temporarily permits card editing
  and upgrades; resuming recompiles the current wave's tower programs

Normal runs own only the selected starting tower definition plus
`death_engine`. The third active definition is available simultaneously only
in Test Lab fixtures.

The current safety contract is also authored data, not a tuning suggestion:

| Limit | Value |
| --- | ---: |
| RootChain depth | 8 |
| Events per RootChain | 256 |
| Projectile spawns per RootChain | 64 |
| Card executions per RootChain | 32 |
| Mythic repeats per RootChain | 3 |
| Emergency enemy lineage members | 256 |
| Projectile bounces / pierces | 8 / 12 |
| Projectile lifetime | 450 ticks |
| Events per tick | 4,096 |
| Queued events | 16,384 |
| Active hazards | 2,048 |

Difficulty profiles must not alter card meaning, SubjectType rules, command
meaning, victory/defeat conditions, RootChain semantics, or these safety rules
merely to force a target win rate.

## 3. Public simulation boundary

The external policy boundary is:

```text
observe SimulationSnapshot
-> choose one legal GameCommand
-> submit and record CommandResult
-> call GameSimulation.Step() when Combat can advance
-> observe the next SimulationSnapshot
```

Policies must not inspect private simulation fields or future random state.
They must not reproduce build costs, upgrade costs, slot capacity, damage,
armor, resistance, or lineage reward arithmetic. They must consume snapshot
data and GameLogic's authoritative quote/query APIs.

Rejected commands are observable results and leave gameplay state unchanged.
They must be retained in replay and telemetry output rather than discarded.

The phase-specific command surface currently required by a legal-action
generator is:

| Run phase | Core actions |
| --- | --- |
| `AwaitingStartingTower` | `ChooseStartingTower` |
| `Planning` | build, upgrade, equip, move, unequip, reorder, set subject, start wave |
| `Combat` | build, open world card pack, or advance a logical tick |
| `CardPackChoice` | select one card-pack offer |
| `CardPackLoadout` | edit loadout, upgrade, then resume |
| `Draft` | select one draft offer |
| `Victory`, `Defeat` | no normal gameplay command |

## 4. Pure .NET reuse path

`Assets/Game/GameLogic/RuleforgeTD.GameLogic.asmdef` has no assembly
references and sets `noEngineReferences` to `true`. The source under
`Assets/Game/GameLogic` is therefore the correct shared implementation for a
pure .NET CLI. Combat formulas must not be copied into a CLI-specific
simulator.

The following Unity-side components are not valid pure .NET dependencies:

- `Assets/Game/Runtime/Simulation/LogicContentJsonLoader.cs` uses
  `UnityEngine.TextAsset` and `JsonUtility`.
- `Assets/Game/Runtime/Simulation/HeadlessReplayDriver.cs` belongs to the
  Unity Runtime assembly.
- `Assets/Game/Runtime/Simulation/HeadlessSimulationHarness.cs` is a
  `MonoBehaviour` driven by `Update()`.
- `Assets/Game/Editor/BuildTools/HeadlessLogicWebGLBuilder.cs` is Editor-only
  and builds a WebGL scene.

The CLI therefore needs a pure .NET JSON composition root that deserializes
the existing DTO fields, discovers card modules in ordinal path order, calls
`CardContentCatalogComposer.Compose`, and compiles through the same
`EffectContentCompiler` and operation registry used by GameLogic. Unknown
presentation/localization fields may be ignored, but validation failures must
not be suppressed.

At audit time `Assets/Game/Data/Cards` contains documentation but no production
card-module JSON files; all 58 active cards are in the base content. Module
discovery still needs to remain functional so future cards are not omitted.

## 5. Generated SDK project blocker

The root Unity-generated `.csproj` files cannot be used as the clean CLI
dependency boundary.

- `ProjectSettings/ProjectVersion.txt` specifies Unity `2022.3.62f2`.
- The generated projects contain Unity `6000.3.12f1` and absolute references
  into that editor installation.
- `RuleforgeTD.GameLogic.csproj` targets `netstandard2.1` with C# 9, but lists
  only 29 of the 53 current GameLogic source files.
- Missing entries include content composition, effect descriptors, high-tier
  executors, program grammar, tower triggers, and sandbox sources.
- `RuleforgeTD.GameLogic.EditModeTests.csproj` lists only one of the fifteen
  current GameLogic test files.
- The generated files explicitly state that manual edits will be overwritten.

Consequently, a new SDK-style project must include or otherwise share the
complete GameLogic source set independently of Unity-generated projects. It
must not patch those generated projects or reference stale Unity editor
assemblies. `AssemblyInfo.cs` currently exposes internals only to
`RuleforgeTD.GameLogic.EditModeTests`; the balance CLI should stay on public
APIs, and a new test assembly should request internal access only if a focused
test truly cannot use the public boundary.

## 6. Existing headless support and limitations

The existing `HeadlessReplayDriver` provides useful control-flow examples:

- `Create`
- `AdvanceRunTransition`
- `ComputeVictoryHash`

`PhaseOneGameLogicTests.PhaseOneHeadlessRun_TerminatesWithinBudget` also drives
a full nine-wave run to Victory within 60,000 steps.

These are not the requested balance runner or replay implementation:

- They always choose and build `ballista`.
- They use `GrantDebugGold` to force starter tower upgrades and unlock slots.
- They choose offers with a hard-coded damage-card score table.
- They do not serialize every submitted command and result.
- They do not replay a recorded command stream without invoking a policy.
- They require Unity's loader or test environment.
- `HeadlessSimulationHarness` advances work from rendered frames, even though
  its underlying GameLogic ticks are deterministic.

The legal command loop, phase transition handling, terminal budget, and
`ComputeStateHash()` checks are reusable patterns. The debug funding and
hard-coded winning strategy are not valid policy behavior or baseline data.

## 7. Existing tests worth reusing

`Assets/Game/Tests/EditMode/GameLogic/PhaseOneGameLogicTests.cs` already covers:

- compilation of 58 cards, 3 towers, and 9 waves
- construction and upgrade quotes, payment, rejection, and maximum levels
- Planning and Combat construction
- draft and card-pack transitions
- `CardPackLoadout` pause/recompile behavior
- Combat loadout-command rejection
- per-slot SubjectType execution
- same-seed, same-command per-tick state-hash equality
- order-sensitive card programs
- split reward preservation and state deep-copy behavior
- final-boss leak defeat
- legal-program fuzzing and safety diagnostics
- a terminal full-run headless smoke test

Other useful suites:

- `CardContentCatalogComposerTests`: deterministic module composition
- `EffectOperationDescriptorTests`: executor/validator and Subject contracts
- `TowerTriggerDispatchTests`: trigger grammar validation
- Rare/Legendary/Mythic suites: bounded lifecycle and repeat behavior
- `SandboxSimulationControlTests`: test-only card and enemy fixtures
- `BurnTrailGameLogicTests` and `UncommonCardGameLogicTests`: deterministic
  state hashes for time-based effects

Sandbox APIs are appropriate for isolated coverage fixtures only. Production
policies and normal balance runs must not use them to mutate inventory, gold,
health, enemies, or progression.

The latest stored, not newly executed, verification artifacts are:

- `test-results/final4/editmode-results.xml`: 173/173 passed
- `test-results/final4/playmode-results.xml`: 65/65 passed
- `test-results/final4/webgl-build.log`: Stage01 and Test Lab builds succeeded

Files named `EditMode-results-final.xml` and `PlayMode-results-final.xml` are
older and contain smaller suites, so their names must not be mistaken for the
latest baseline.

## 8. APIs and systems missing at audit start

At the start of this audit, no implementation provided the complete requested
boundary for:

- pure .NET content loading and compilation
- difficulty overlay schema and allowed-field validation
- legal-action enumeration with stable `actionId` values
- separate deterministic policy RNG and policy versioning
- serialized scenario and seed-set definitions
- command/result recording and policy-free replay
- stable snapshot serialization
- a telemetry sink proven not to affect RNG, event order, or state hash
- run-level economy, leak, card, tower, status, and mid-wave-build ledgers
- batch evaluation, Wilson intervals, and percentile reports
- card-strength and ordered/Subject-aware synergy indexes
- matched-seed profile comparison and bounded patch optimization
- Train/Validation/Holdout overlap and freeze enforcement
- policy, target, profile, and prompt hashes

The current manual also explicitly lists the complete card/kill/chain result
ledger as unfinished. New telemetry must be read-only with respect to combat:
attaching or removing it must produce the same command results and every
gameplay state hash.

## 9. Exact document and implementation discrepancies

| Source | Documented claim | Current authoritative behavior |
| --- | --- | --- |
| `AGENTS.md:234-236` | MVP has 8 waves and an eighth-wave final boss | Current JSON has 9 waves and the final boss is wave 9 |
| `AGENTS.md:2022-2024` | `PlaceForTower2` sites are locked and unlocked during Planning | All eight current JSON unlock costs are zero |
| `CARD_RULES.md:34,49` | A newly split enemy does not inherit pre-split status | GameLogic deep-copies status runtime to the new enemy; pre-split death bindings are not copied |
| `AI_GAME_CONTEXT_MANUAL.md:113-116,849` | The burn-before-split example also says the new enemy does not inherit burn | Current `MutationSplit_PreservesRewardDeepCopiesStatusesAndSkipsDeathBindings` test proves the opposite for enemy status |
| `CARD_RULES.md:52` | Enemy split is capped at two splits/eight lineage members | Current code treats the split-count field as a legacy hint, naturally stops on health floor, and retains an emergency 256-member cap |
| `CARD_RULES.md:38` | Phase 1 grammar-card executors are not registered | Current GameLogic implements and tests all Rare, Legendary, and Mythic cards |
| `TOWER_RULES.md:53` | Loadout editing is allowed only in Planning | It is also allowed in `CardPackLoadout` |
| `TOWER_RULES.md:83` | Ballista is Projectile-only | Ballista supports independent Projectile/Enemy subjects per slot |
| `BALANCE_RULES.md:111` | Death Engine uses radius 2,500 and target limit 16 | Current level-1 JSON values are radius 2,000 and target limit 3, with authored level progression |
| Generated `.csproj` files | Appear to describe the current Unity solution | They reference a different Unity version and omit many current files |

Minor defects in `AI_GAME_CONTEXT_MANUAL.md` include a duplicated
initialization step, duplicated base-health observation, and duplicated
`duality_core` design-table row. These do not override runtime behavior.

## 10. Dirty-worktree preservation

The audit began with a heavily modified worktree containing many existing
modified and untracked GameLogic, Runtime, content, scene, documentation, and
test files. Those changes are treated as user-owned current workspace state.

Balance CLI work must:

- avoid resets, checkouts, broad formatting, or generated-project rewrites
- avoid staging or committing unrelated existing files
- inspect overlapping files immediately before patching
- keep new CLI, balance data, artifacts, and documentation in their scoped
  directories
- report any unavoidable overlap instead of silently replacing user changes

## 11. User-specific no-Unity/WebGL exception

`AGENTS.md:2926-2929` and `AGENTS.md:3053-3074` normally require repository
changes to run relevant Unity tests and regenerate the Stage01 WebGL build.

The attached Balance CLI request is later and more specific: this work must be
completed without a Unity build, WebGL build, Stage01 render run, prefab
loading, VFX/SFX/camera initialization, or frame-based combat progression.
That explicit constraint is the controlling exception for this task.

Validation for the new system must therefore use pure `dotnet` CLI and test
commands against the shared GameLogic. If the repository execution policy is
being applied, those commands still run through the required `$spark-test`
execution path, but no Unity or WebGL command is added. The exception and the
exact validation performed must be repeated in the final handoff so a skipped
Unity build is not mistaken for an accidental omission.

## 12. Phase 1 implementation conclusion

Before difficulty tuning begins, the implementation must prove all of the
following using the current profile:

1. A clean SDK project compiles the complete shared GameLogic without Unity.
2. The pure loader produces the same compiled content counts and content hash.
3. A policy drives a real run from `AwaitingStartingTower` to a terminal phase.
4. Every observation and action goes through snapshot, quote/query, command,
   result, and `Step()` boundaries.
5. Re-running the same scenario produces the same commands and final hash.
6. Replaying the recorded commands without a policy produces the same result.

Only after those checks pass should difficulty overlays, policy tiers,
telemetry, card discovery, optimization, and holdout evaluation be trusted.
