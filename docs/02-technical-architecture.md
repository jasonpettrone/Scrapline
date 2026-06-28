# 02 — Technical Architecture

## The central idea: a headless simulation Core

The most important architectural decision — and the one that makes this codebase a joy to
test — is splitting the game into two layers separated by a hard, narrow seam:

```
┌──────────────────────────────────────────────────────────────────┐
│  GODOT LAYER  (game/)                                              │
│  Rendering · Input · Audio · UI · Scene flow · Steam               │
│  REAL-TIME RACE PHYSICS (RigidBody2D, collisions, hazards)         │
│  Enemy AI (steering/driving)                                       │
│                                                                    │
│   depends on ▼  (one-way)                                          │
├──────────────────────────────────────────────────────────────────┤
│  CORE  (src/Core/)   — pure C#, ZERO Godot references              │
│  Run/meta state machine · Map generation · Upgrade & Mod system    │
│  Car stat derivation · Economy/rewards · Save model · Seeded RNG   │
│  Race RULES (win conditions, placement, scoring) — not the physics │
└──────────────────────────────────────────────────────────────────┘
```

**Why this matters for you specifically:** the Core is exactly the kind of deterministic,
side-effect-free business logic you already write in Node backends. It has no engine, no
frame loop, no rendering — so it runs under plain `dotnet test` in milliseconds, in CI, with
no Godot install. The hard-to-test, feel-based stuff (physics, AI, rendering) is quarantined
in the Godot layer and covered by a smaller set of in-engine integration tests.

**Dependency rule (enforced):** `Core` must never reference `Godot`. `game/` references
`Core`, never the reverse. A unit test asserts the Core assembly has no Godot dependency so
this can't rot.

## The seam: how a race happens

The Core and the Godot layer communicate through two plain data objects (DTOs). The Core
never touches a `RigidBody2D`; the physics layer never decides a reward.

```
                 Core builds                         Godot runs the race
                 a RaceConfig                        and returns a RaceResult
   ┌────────┐   ───────────────►   ┌─────────────┐   ───────────────►   ┌────────┐
   │  CORE  │                      │ GODOT RACE  │                      │  CORE  │
   │ (run   │   RaceConfig {       │  SCENE      │   RaceResult {       │ applies│
   │  state)│     trackId,         │ (physics,   │     placement,       │ result │
   │        │     playerStats,     │  AI, input) │     hpRemaining,     │ to run │
   │        │     opponents[],     │             │     takedowns,       │ state  │
   │        │     fieldSize,       │             │     scrapEarned,     │        │
   │        │     hazardSeed }     │             │     outcome }        │        │
   └────────┘                      └─────────────┘                      └────────┘
```

1. Player picks a node on the map. The Core assembles a **`RaceConfig`**: which track, the
   field size (from node type), the player's *derived* stats (base + parts + mods, all computed
   in Core), the opponent roster for that node, and the seed for hazard/prop placement.
2. The Godot race scene reads the `RaceConfig`, spawns cars with those stats, runs the
   real-time physics race, handles input and AI.
3. When the race ends, Godot returns a **`RaceResult`**: final placement, HP remaining,
   takedown count, scrap earned, outcome (won/wrecked/finished-behind).
4. The Core applies the result to run state — updates HP, awards Scrap, advances the map,
   generates the upgrade draft.

This seam is the backbone of the whole codebase. Keep it narrow and serializable.

## Determinism policy

- **Generation is deterministic.** Map layout, node contents, reward drafts, hazard/prop
  placement are produced by a **seeded RNG** in the Core. A given seed → the same run shape.
  This is fully unit-testable and enables reproducible bug reports ("run seed 4815").
- **The race sim is NOT deterministic.** Godot physics + real-time input is not reproducible
  frame-for-frame, and we don't pretend otherwise (see GDD non-goals). We never feed physics
  results back into anything that needs determinism beyond the coarse `RaceResult`.

## Destruction & deformation

A signature system (see GDD §4). It lives **almost entirely in the Godot layer** and is
**non-deterministic** — consistent with the determinism policy above (it's presentation +
physics, never fed back into anything Core relies on beyond HP).

- **Cars are a texture on a low-poly deformable 2D mesh.** Impacts push the struck zone's
  vertices inward by force × direction (front/side/rear localization).
- **Collision deforms with the visual — via a *simplified* polygon.** A coarse collision shape
  is re-derived from the mesh at a lower resolution/cadence than the visual, so a crushed car
  genuinely changes hitbox without paying full per-vertex collision cost. **This is the
  riskiest part of the system** (perf + balance) — keep the collision proxy cheap and its
  update rate tunable; visual-only deformation is the fallback if the hitbox proves
  unbalanceable.
- **Core stays authoritative for what matters:** HP, death (HP→0), takedown resolution, repair,
  and run-long persistence remain Core logic crossing the seam as today. The deformation layer
  *reads* that state (e.g., the player's near-death threshold drives the sparks/smoke tell) and
  adds immediate impact kicks reconciled to it. Deformation severity carries **no stat
  penalty** — the changed hitbox **is** the only mechanical consequence.
- **Asymmetry is a presentation rule**, keyed off role: the player car deforms soberly; AI cars
  exaggerate, shed panels, and split on a kill; the player gets full destruction on death.
  `RaceConfig` may carry a per-car **deformation profile** (restrained/exaggerated) so this is
  data-tunable rather than hardcoded.
- **Debris** are short-lived physics bodies that act as collidable hazards, then fade (capped
  for the 4–6-car grid; target a modern mid-range GPU).

## Core — internal modules

| Module                | Responsibility                                                            |
|-----------------------|---------------------------------------------------------------------------|
| `Run`                 | Run state machine, act/node progression, persistent HP & Scrap            |
| `Map`                 | Procedural branching map generation (seeded), node graph, path validity   |
| `Cars`                | Car definitions, base stats                                               |
| `Upgrades`            | Parts (tiered) + Mods (stackable); pools per car; **stat derivation**     |
| `Drafting`            | Reward generation: offer N-of-M, rarity weighting, pool filtering         |
| `Economy`            | Scrap rewards, shop pricing, repair costs, Cores (meta) payout            |
| `Content`             | Loads & validates content data (cars, mods, enemies, tracks) by id        |
| `Save`                | Serializable run + meta save model; versioned for forward-compat          |
| `Rng`                 | Seeded, injectable RNG abstraction (no `System.Random` calls scattered)   |
| `Contracts`           | `RaceConfig`, `RaceResult`, and shared DTOs crossing the seam             |

**Stat derivation** is the crown jewel for testing: `BaseStats + Parts + Mods → DerivedStats`
is a pure function. Mod stacking order, caps, and synergies all get exhaustive unit tests.

## Godot layer — key systems

| System            | Notes                                                                       |
|-------------------|------------------------------------------------------------------------------|
| **Scene flow**    | A top-level `GameDirector` autoload owns the Core run object and swaps scenes (Menu → Map → Race → Reward). |
| **Race scene**    | Spawns `CarController` nodes from `RaceConfig`; owns the finish/placement logic that feeds `RaceResult`. |
| **CarController** | `RigidBody2D`-based; consumes `DerivedStats` (mass, engine curve, grip). Shared by player & AI; the difference is the input source. |
| **Input**         | An `IDriveInput` interface with two implementations: `PlayerInput` (keyboard/controller via Godot Input actions) and `AiInput` (driving brain). Cars don't know who's driving. |
| **AI driving**    | Steering behaviors: follow a per-track racing-line path, with aggression/avoidance layers per enemy archetype. The meatiest Godot-side subsystem — budget time. |
| **Hazards/traffic** | Area/body nodes (barrels, oil, boost pads, neutral traffic) applying effects on contact; placement seeded from `RaceConfig`. No item-box pickups (cut). |
| **Destruction**   | Deformable-mesh cars/walls/props; impacts crush localized zones; a **simplified collision polygon** tracks the deform (the perf/balance risk); debris bodies linger then fade. Non-deterministic; playtested. |
| **Weapons**       | Per-weapon cooldown components on cars; mixed auto-lock / fixed-mount; the only active offense. |
| **Map UI**        | Renders the Core's node graph; sends the chosen node back to the director.   |
| **Steam**         | Thin wrapper behind an `IPlatform` interface so the game runs without Steam in dev/CI. (M7) |

## Content as data

Gameplay-relevant content (cars, mods, enemies, track metadata, economy tables) is authored
as **data the Core can load and validate** (JSON or Godot `.tres` resources exported to a
Core-readable form — decided at M0). One source of truth:
- The **Core** reads content for logic (stats, pools, costs) and validates integrity at load
  (no dangling ids, costs in range, every car pool non-empty) — these validations are unit
  tests *and* runtime guards.
- The **Godot layer** reads the same content ids for presentation (sprites, scenes, audio).

This keeps balance tuning in data files (fast iteration, diffable, testable) rather than
hardcoded in either layer.

## Technology choices & integrations

- **Godot 4 .NET build** (Mono/.NET 8). C# everywhere.
- **xUnit** for Core unit tests; **FluentAssertions** for readable asserts (optional).
- **GdUnit4** for in-engine integration tests (runnable headless for CI).
- **Steamworks:** `GodotSteam` (GDExtension) or `Facepunch.Steamworks` (pure C#) — evaluate
  both at M7; abstract behind `IPlatform` so the choice is swappable and dev builds need no
  Steam.
- **Save location:** `user://` (Godot's per-user app data); JSON via the Core `Save` model.

## Risks the architecture must respect

- **Feel can't be unit-tested.** The Core proves *correctness*; only playtesting proves
  *fun*. The architecture's job is to make the feel-tuning loop fast, not to verify it.
- **AI is a real subsystem,** not a weekend job. Keep it behind `IDriveInput` so it can be
  iterated and tested in isolation (e.g., "AI completes track without getting stuck").
- **Don't leak Godot into Core** under deadline pressure. The no-Godot-reference test is the
  guardrail; respect it.
- **Deforming collision is the destruction system's sharp edge.** A hitbox that changes shape
  is emergent but hard to balance and to optimize. Mitigate with a *simplified*, low-cadence
  collision proxy (not per-visual-vertex), capped debris, and the small 4–6-car grid; keep
  visual-only deformation available as a fallback. Treat it as a dedicated tuning surface in M1.
