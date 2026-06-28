# 08 — Feature Breakdown

The feature-level backlog, milestone by milestone. Each **feature** is a short description plus
the **locked design decisions** that came out of design vetting. Those decisions *are* the spec
the build works from — point the AI at a feature and the decisions tell it what to make.

High-level on purpose: no estimates, no step-by-step tasks. Granularity lives in the decisions.
Full system specs live in [`01-game-design.md`](01-game-design.md) and
[`02-technical-architecture.md`](02-technical-architecture.md); the milestone arc lives in
[`05-roadmap.md`](05-roadmap.md). Milestones past M1 get vetted as we approach them.

---

## M0 — Foundations

*Skeleton, CI, the `RaceConfig` contract, and a drivable car are already done. Remaining:*

### Seam contracts
The two DTOs that cross the Core↔Engine seam.
- `RaceConfig` exists; add **`RaceResult`** (empty-but-real).
- `RaceResult` carries: `placement`, `hpRemaining`, `takedowns`, `scrapEarned`, and an
  `outcome` enum (`Won` / `Wrecked` / `FinishedBehind`).

### Drivable car — physics foundation
The grey-box car the takedown slice is built on.
- **`RigidBody2D`**, driven by forces/impulses, with weighty momentum.
- Simple polygon shape for now — the **deformable mesh is deferred to M1** (built with destruction).
- Controller + WASD both work. Functional, *not* feel-tuned (that's M1).

### Integration test layer
The middle layer of the test pyramid.
- Stand up **`Game.Tests`** using **GdUnit4**, runnable headless in CI and the local gate.
- At least one smoke test (load the race scene → car spawns).

### Seam round-trip
Prove the full Core→Engine→Core loop.
- The race scene consumes `RaceConfig` (done) and now **emits a `RaceResult`** on exit (debug
  trigger) — no `GameDirector`/scene-flow yet (that's M2).
- A GdUnit4 test asserts a valid `RaceResult` comes back.

---

## M1 — Takedown vertical slice + destruction

**The make-or-break milestone: is ramming fun?** This is a go/no-go gate on the whole concept.

### 1. Driving feel
Weighty momentum, drift, and boost — the moment-to-moment of driving.
- **Drift:** hold a **dedicated drift button** to drift while steering — a **cornering &
  positioning tool** (loosens grip + adds turn authority). It does **not** generate boost.
- **Boost sources (M1):** a slow **passive trickle** (fills to full over time) plus on-track
  **pads** — **small** (partial refill) and **large** (fill to full), both respawning on a
  cooldown, and **launch pads** for an instant off-meter speed kick. *(Takedown-boost arrives
  later with the Juggernaut; weapon-hit boost with Ordnance.)*
- **Boost use:** **hold to drain** the meter continuously until released or empty.
- **Why not drift-boost:** a drift-charged meter is exploitable (wiggle to farm lateral speed →
  net free speed). Pickup pads make boost a **routing** decision instead — exploit-free and a
  better fit for the racing line.

### 2. Damage & HP
Each car has an HP pool; rams and slams chip it.
- **Clean (free) vs botched (you take damage):** decided by **angle + speed** — hitting a
  rival's side/rear while faster than them is clean; head-on, or being the slower car, is botched.
- **Wall/obstacle self-damage:** only on **hard hits above a force threshold** (light scrapes free).
- **Lethality:** the **AI can ram and damage you**, so the race can genuinely be lost.
- **Hazards in M1:** **walls only** (explosive barrels and other hazards come later).

### 3. Takedowns
Pure physics — your speed, mass, and angle are the weapon.
- **No takedown button.**
- **Wreck trigger:** HP chips down normally, **but a hard enough clean slam one-shots** regardless of HP.
- **Respawn:** a wrecked rival reappears **behind the player on the racing line**, after a
  **delay that scales with hit force** (delay only — not respawn distance).
- **Aftertouch slow-mo:** on **wrecks only**.

### 4. Dynamic destruction & deformation
Fully specced already — see [`01`](01-game-design.md) §4, [`02`](02-technical-architecture.md),
and the ledger in [`07`](07-design-decisions.md). M1 builds the full-ish system:
- Deformable-mesh cars (texture on a low-poly mesh), **localized** crumple (front/side/rear).
- Collision deforms via a **simplified, low-cadence proxy**; **visual-only is the fallback** if
  the changing hitbox proves unbalanceable.
- **Player sober, enemies exaggerated:** enemies shed panels through a fight and **split on kill**;
  the player accumulates damage soberly (sparks/smoke ~one hit from death) and gets full
  destruction on death.
- **Debris** lingers as collidable hazards, then fades. **Force-scaled crunch** (SFX/sparks/smoke/shake).

### 5. One AI opponent
A real rival to race and to take down.
- **Personality:** a **balanced racer that rams opportunistically**.
- **Kit:** **full** — drifts and boosts like the player (the meatiest build item in M1).
- **Difficulty:** **even / beatable** — M1 is a feel test, not a difficulty test.
- Can be taken down and respawns per Feature 3; targeting is player-focused.

### 6. Win / lose a single race
One track, one rival, a clear verdict.
- **Format:** **circuit** — needs **lap counting + checkpoints** so a lap actually counts.
- **Length:** **3 laps**.
- **Win:** finish **1st**. **Lose:** **wreck (HP=0) OR the AI finishes first.**

### 7. Accessibility hooks
Built early because it's far cheaper in than bolted on.
- **Included in M1:** screenshake intensity, slow-mo/hitstop intensity, colorblind-safe colors,
  and input remap.
- **Input:** built as **remappable actions** now; the rebinding **UI lands in M2**.
- **Settings home:** toggles read from a **config file** now; the settings **menu lands in M2**.

---

## M2 and beyond

High-level shape is in [`05-roadmap.md`](05-roadmap.md) (run-loop slice, Act 1 demo, Steam page,
cars 2–4, Acts 2–3, polish, launch). These get the same feature-and-decisions vetting as we
reach them.
