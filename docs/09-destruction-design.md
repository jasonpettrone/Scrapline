# 09 — Destruction & Deformation Design

The build spec for M1 Feature 4 (docs/08 §4). Narrative intent is in [`01`](01-game-design.md) §4,
the architecture seam in [`02`](02-technical-architecture.md), the locked decisions in
[`07`](07-design-decisions.md). This doc is where those become a concrete, buildable system.

> **Status:** in build (M1). Decisions below are locked unless marked _open_.

---

## 1. Why this system matters

Destruction is the **primary spectacle** and a top performance risk at once. The whole point of
the takedown verb is that a hard, clean slam *looks and feels* devastating. This system is the
visible payoff of the damage model — it never decides damage, it **dramatizes** it.

## 2. Locked decisions

From the design vet plus the M1 build vet:

- **Art basis:** build on the **current placeholder polygon cars** now. The vertex/UV deform tech
  drops onto textured low-poly cars later with no rework.
- **Collision:** the hitbox **deforms from the start** (not visual-only). A crushed car genuinely
  drives differently. Mitigated by a low-cadence convex-decomposition proxy (§6) and a single
  `VisualOnly` kill-switch that reverts to the doc's blessed fallback if balance/perf bites. 🧱
- **Debris:** **plow & scatter** — one physical debris model. The player is near-unaffected
  (low debris mass), enemy AI gets jostled off its line. Asymmetry from mass + AI reaction, not
  separate collision layers.
- **Deformation magnitude scales with the hit's force** (closing speed) — high damage = high
  crumple. Same source the damage model already uses.
- **Asymmetry (presentation rule, keyed off role):** player **sober** (low multiplier, sheds
  little, sparks/smoke only ~one hit from death, full destruction only on death); enemies
  **exaggerated** (shed panels through a fight, **split on kill**). ⚖️
- **No stat penalty.** The changing hitbox is the *entire* mechanical consequence.
- **Persistence:** car deformation persists across the run until a full repair; environment
  damage is per-race. (Run-flow lands in M2 — see §10.)

## 3. The seam: where the math lives

Consistent with `DamageModel` ↔ `CarController`, we split the system:

| Layer | Owns | Lives in |
|-------|------|----------|
| **Core** (`Destruction/`) | The pure crumple **geometry math**: hit → per-vertex displacement, easing, accumulation, zone tagging, panel-shed accounting. Godot-free (`System.Numerics.Vector2`). Unit-tested. | `src/Core/Destruction/` |
| **Godot** (`race/destruction/`) | Everything physical: rendering the deformed mesh, rebuilding the collision proxy, spawning/pooling debris bodies, VFX (sparks/smoke), camera shake, hitstop. | `game/scripts/race/` |

This is a deliberate, minor reinterpretation of docs/02 ("destruction lives almost entirely in
the Godot layer"). The *inputs* remain non-deterministic; the *transform* is pure and worth
testing. The Core dependency rule is unbroken — `Destruction/` references no Godot.

**The hook into existing code:** every damaging contact already funnels through
`CarController.ApplyHit(damage, hitForce, …)` with the impact zone known. We broaden the existing
`Wrecked(hitForce)` signal into a general impact notification carrying `(force, zone, localPoint,
isWreck)` that the deformation + VFX + shake systems subscribe to. The damage code itself is
untouched in behaviour.

## 4. The five layers

### 4.1 Crumple data model (Core)
A car silhouette is a ring of local-space vertices (40 on the placeholder: 10/edge), each tagged
Front/Side/Rear (reusing `Combat.ImpactZone`). A hit is described by an **`Indenter`** — a contact
point, an inward **push direction** (the struck face normal), a **half-width**, and a **sharpness**.
Only vertices on the **struck-facing half** move (gate: `outward · push < 0`), and they're driven
**along the push direction** (not toward the centroid — that was the bug that caved the far side).
The depth across the surface follows the impactor's shape: full inside the flat `half-width` core,
then a straight ramp to zero — a **flat impactor leaves a wide, flat-bottomed (rectangular) imprint;
a corner leaves a narrow V (triangular)**. Magnitude is `damage × profile.CrumpleScale`, clamped so
a vertex can't cross the centre. Displacement **accumulates** and **eases in over ~0.3s** so a hit
*crunches* rather than snapping. The Godot layer derives the `Indenter`'s shape from the two bodies'
relative orientation (a parallel face → flat/wide; a 45°-cocked car or corner-first wall hit → sharp).

### 4.2 Visual mesh (Godot)
Replace the flat `Polygon2D` body with a deformable one whose `Polygon` (and later `UV`) is set
from `DeformableSilhouette.GetVertices()` each frame deformation changes. Placeholder = warped
colored polygon; textured cars later = warped texture, same code.

### 4.3 Chunks / debris (Godot)
Panels (M1: one per zone — front bumper, side skirt, rear) detach when a zone's accumulated hit
force crosses `profile.PanelShedThreshold`. On shed: spawn a pooled debris `RigidBody2D` with that
panel's polygon, fling it along the hit direction, deepen the crumple where it tore off. Debris
lingers ~3–5 s as a collidable hazard, then fades (alpha tween) and returns to the pool. Pool is
**capped** (~24 active); oldest recycled first.

### 4.4 Collision proxy (Godot) 🧱
The deformed silhouette is concave; a `RigidBody2D` can't use a concave shape directly. So we
**decompose the deformed silhouette into convex pieces** (`Geometry2D.DecomposePolygonInConvex`)
and swap them onto the body — but only at **low cadence** (rebuild when accumulated deformation
delta crosses a threshold, and no more than ~5×/s), never per-frame. `VisualOnly = true` skips
this entirely and keeps the rest shape. This is the system's sharp edge; treat it as a dedicated
tuning surface.

### 4.5 Feedback + asymmetry (Godot)
Force-scaled layers, all reading the same impact force:
- **Sparks** at the contact point (every solid hit), **smoke** trailing a zone once it's badly
  crumpled, scaling with severity.
- **Crunch SFX** pitched/leveled by force.
- **Camera shake** + brief **hitstop** (~40–90 ms freeze) on solid hits; the existing big-takedown
  **slow-mo** ([RaceScene](../game/scripts/race/RaceScene.cs)) stays on top for wrecks.
- **Player danger tell:** sparks/smoke on the *player* only fire when `HpFraction` is below a
  near-death threshold (the model already exposes this) — a readable "one more hit" warning.

## 5. Profiles (the asymmetry knob)

A `DeformationProfile` record carries the tunables; the player gets a **sober** profile, enemies an
**exaggerated** one. This is the per-car deformation profile docs/02 anticipated on `RaceConfig`.

| Field | Sober (player) | Exaggerated (enemy) |
|-------|----------------|----------------------|
| `CrumpleScale` | low | high |
| `MaxCrumpleDepth` | shallow | deep |
| `PanelShedThreshold` | very high (rarely sheds pre-death) | low (sheds through a fight) |
| `SplitOnDeath` | true (player only on death) | true |
| `SparksBelowHpFraction` | ~0.2 (danger tell) | 1.0 (always) |

## 6. Persistence

Deformation state lives on the car and survives respawns only for the **player** (enemies reset
clean on respawn — a fresh rival). Run-long persistence and repair-restores HP+deform need the M2
`GameDirector`/run-flow; for M1 the state is held in-memory on the player `CarController`. The state
is kept in a **serializable** shape so M2 can carry it across nodes without redesign.

## 7. Performance budget

4–6 cars, modern mid-range GPU. Levers: ~14 verts/car; deform updates only while easing or on a
hit (not perpetually); collision proxy rebuilt at ≤5 Hz and only past a delta threshold; debris
pool capped at ~24 with fade-and-reclaim; pooled `GpuParticles2D` for sparks/smoke.

## 8. Open / playtest levers

- ⚖️ Player crumple + spark thresholds — how late the near-death tell triggers.
- 🧱 Collision-proxy cadence and decomposition cost — the perf/balance watch item; `VisualOnly` is
  the escape hatch.
- _open_ Panel granularity — M1 is one panel per zone; finer panels are a later polish lever.
- _open_ Debris mass tuning for the plow-vs-jostle asymmetry.

## 9. Build phases

1. ✅ **Core crumple model + tests** — `DeformationProfile`, `DeformableSilhouette` (hit, ease, zone,
   shed accounting), 13 unit tests. _No Godot._
2. ✅ **Visual deform** — `Impact` signal broadened on `CarController`; `CarDeformer` renders the
   deformed silhouette onto the body polygon; player→Sober, rivals→Exaggerated. _Needs a feel pass
   in-engine._
3. **Debris + shed** — pooled debris bodies, plow & scatter, enemy split-on-kill.
4. ✅ **Collision proxy** — `CollisionPolygon2D` (Solids) driven from a decimated silhouette at ≤10 Hz,
   behind `VisualOnly`. The carved hitbox lets you drive into the dent and crumple further. _Needs a
   balance/perf pass in-engine._
5. **Feedback** — sparks/smoke/crunch/shake/hitstop, force-scaled; player danger tell.

### Wall-backed impacts (replaces the "smush") ✅
The old time-based crush DPS is gone. A car pinned against a wall on the side **away from the
attacker** can't recede, so the discrete impact is amplified by `WallBackedMultiplier` (it also still
takes its own wall-impact). The clean attacker pays nothing — the "clean takedowns are free" rule
holds. Detection: a wall contact direction aligned with the side opposite the attacker.

### Environment as a full participant ✅
Walls are first-class — they **deform**, their **hitboxes change**, and car↔wall exchanges apply
damage + destruction both ways. The car's deformer was generalized into a shared `Deformable` (visual
polygon + `CollisionPolygon2D` + Core silhouette) used by both cars and `DeformableWall`. Walls use a
density-based silhouette (`BoxWithSpacing`, capped for their extreme aspect ratios) and the `Wall`
profile (shallow dents, no shedding, no HP/wreck). The struck car localizes the hit and calls the
wall's `TakeImpact`; the wall builds the `Indenter` in its own frame (square slam → wide dent,
glancing → sharp). Dents persist for the race.

### Takedowns temporarily disabled
`CarController.TakedownsEnabled = false` (M1 temp): cars take damage and deform fully but never wreck/
vanish/respawn, while we tune takedown balance and build the split-in-half theatrics. HP floors at 1.

### Player destruction
The player's Sober pipeline already deforms (less). In the test scene that means **driving into
walls**; car↔car player destruction is deferred to the AI-opponent task for playtesting.

Each phase is independently playtestable, matching the milestone's feel-first cadence.
