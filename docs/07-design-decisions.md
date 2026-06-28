# 07 — Design Decisions (Ledger)

A flat, scannable record of every locked decision from the design interview. The narrative
versions live in [`01-game-design.md`](01-game-design.md) and [`06-content-bible.md`](06-content-bible.md).
Format: **Decision** — short rationale/notes. Items marked ⚖️ are flagged balance levers;
🧱 are flagged scope investments.

## Tech & process (locked earlier)
- **Engine:** Godot 4, **C#**.
- **Architecture:** pure-C# headless Core + Godot layer, joined by `RaceConfig`/`RaceResult`.
- **Scope:** solo, focused-commercial (~12–18 mo), vertical-slice-first.
- **Art approach:** placeholder-first.

## The race
- **Track format:** mix of point-to-point sprints and circuits, by layout.
- **Camera:** dynamic zoom + rival framing (combat readability first).
- **Race length:** ~90–120s.
- **Field size:** varies, **decided by node type**.
- **Handling:** weighty momentum + drift.
- **Drift→boost:** tiered mini-turbo (hold & release).
- **Boost pads:** two types — meter-refill and instant-speed.
- **Rubber-banding:** none (pure skill).
- **Wrecked opponents:** respawn after a delay, **set back ∝ takedown force**.
- **Player wreck:** **no respawn — HP→0 ends the run.**
- **Persistence:** current HP carries between races; recovery scarce.
- **Win condition:** finish **1st** (position only). No "last car standing."

## Takedowns & damage
- **Methods:** chip-HP ramming + environmental slams + equipped weapons.
- **Damage exchange:** **clean takedowns are free**; botched collisions hurt you.
- **Input:** **pure physics positioning** — no takedown button.
- **Aftertouch:** slow-mo on **big destructive takedowns only**.

## Destruction & deformation
- **Approach:** physical-*looking* approximation — **deformable 2D meshes** (texture on a
  low-poly mesh), not true soft-body.
- **What deforms:** cars, walls/barriers, obstacles/props.
- **Localization:** per-zone (front/sides/rear) on cars.
- **Mechanical effect:** **only the collision shape** deforms with the mesh — **no stat
  penalties**. The hitbox change is the entire gameplay consequence. 🧱
- **Collision:** deforms via a **simplified, low-cadence collision proxy** (not per-visual-
  vertex). The system's main perf/balance risk; **visual-only is the fallback**. 🧱
- **Asymmetry:** player car **sober/realistic** (accumulates naturally; sparks/smoke ~one hit
  from death); enemies **exaggerated** — shed panels through a fight, **split on kill**. ⚖️
- **Player death:** full enemy-grade destruction (can break apart).
- **Debris:** shed panels + wreckage **linger as collidable hazards**, then fade.
- **Sources:** car↔car, car↔wall/obstacle, weapons, explosive barrels.
- **Persistence:** car deformation across the **whole run** until a **full** repair restores it;
  environment damage **per-race** (resets next node).
- **Source of truth:** **both** — instant visual kick on impact (engine), reconciled to Core's
  authoritative HP/damage/death.
- **Determinism:** non-deterministic (presentation/physics); Core stays the deterministic part.
- **Feedback:** **force-scaled layers** (crunch SFX, sparks, smoke, screenshake); takedowns add
  **hitstop + shake** atop the existing big-takedown slow-mo.
- **Grid / perf:** small **4–6 car** field; target **modern mid-range GPU**.
- **Roadmap:** **full-ish deformation built inside M1** (part of proving the verb). 🧱

## Boost & items
- **Boost meter:** single, free-spend; **multi-source weighted by car/build**.
- **Item boxes:** **CUT.** Offense = weapons + physics + hazards.

## Weapons (only active offense)
- **Slots:** car-scaled (slot count is a stat; Ordnance most).
- **Resource:** per-weapon cooldown.
- **Categories (all four):** direct-fire projectiles, auto-lock homing, rear traps/droppers,
  defensive & ram augments (incl. passive spikes/plows).
- **Aiming:** mixed — some auto-lock, some fixed-mount (front/rear/side) aimed by positioning.

## Upgrades & builds
- **Draft:** pick 1 of 3, **skip for Scrap**.
- **Channel:** Parts and Mods both from drafts + shops (one channel).
- **Permanence:** Parts swap/sell; **Mods commit — no removal.**
- **Power curve:** **build-defining combos** (high ceiling; enemies scale per act). ⚖️
- **Synergy emphasis:** **trigger-chain combos**.
- **Pool size:** lean & curated (~15–20 car-specific + ~15 neutral per car).
- **Draft mix:** mostly car-specific.
- **Active abilities:** **none** — only weapons are active; signatures/mods are passive.

## Cars
- **Count:** 4 deep at launch. **Start with Vanguard + Juggernaut**; unlock Comet + Ordnance
  via Cores (some gated behind milestones).
- **Identity:** stats + signature passive + biased boost source + biased pool.
- **Signatures:** Vanguard = post-race HP heal + balanced stats ⚖️ · Juggernaut = Wrecking
  Momentum (takedown engine) · Comet = passive boost generation · Ordnance = Overcharge Array
  (weapon-cooldown engine).
- **Pools:** each car has its **own unlockable pool** + shared neutral pool.
- **Flavor:** distinct placeholder looks now; narrative deferred.

## Enemies & AI
- **AI:** archetype personalities with **readable tells**.
- **Targeting:** player-focused.
- **Kits:** asymmetric hand-tuned.
- **Roster:** **4 normals + 2 elites + 1 boss per act** (~21 designs). 🧱
- **Elites:** mixed — some coordinated **packs**, some **1v1/1v2 duels**.
- **Lethality:** attrition is the main threat.
- **Base difficulty:** normals are **even/winnable-not-trivial**.
- **Curve:** curated per-act escalation.

## Tracks & hazards
- **Authoring:** handcrafted layouts + randomized props.
- **Count:** ~4–5 layouts per act.
- **Hazards:** focused set (barrels, oil, boost pads, static obstacles).
- **Traffic:** neutral traffic on **some** themed tracks.

## Run structure & nodes
- **Map:** full StS act map, visible upfront.
- **Length:** ~10–12 nodes/act → ~35–45 min run.
- **Repair:** scarce dedicated Repair Bay nodes + pricey shop repairs.
- **Pre-race:** partial preview (node type, field size, track theme).
- **Bosses:** bespoke arena setpieces, unique per act. 🧱
- **Events:** curated text risk/reward. 🧱
- **Shops:** standard + sell-back.
- **Victory:** Act 3 = climactic final boss; escalating arc.

## Economy & meta
- **Currencies:** Scrap (in-run) + Cores (meta).
- **Cores earned:** every run, win or lose, scaled to progress.
- **Meta:** unlocks only — **no power creep**. Cars + pool injections.
- **Replay:** Ascension/Heat ladder after beating the game.

## Controls, platform, UX
- **Controls:** controller-first, full keyboard rebind.
- **Steam Deck:** nice-to-have, not a gate.
- **Onboarding:** integrated through Act 1.
- **Save:** single save, resume mid-run.
- **Steam features:** achievements at launch; dailies/leaderboards later.
- **Accessibility:** robust from M0 (colorblind, remap, shake/slow-mo toggles, assists). 🧱

## Presentation
- **Setting:** anti-grav combat cars; industrial space (belts/yards/stations/planets).
- **Art:** gritty industrial, readability enforced (color-coding, glowing accents, silhouettes).
- **Music:** aggressive rock/metal (royalty-free/original to start).
- **Game-feel:** punchy but readability-first; slow-mo on big takedowns.
- **HUD:** rich but clean (HP, boost, signature, position, weapon cooldowns, progress).
- **Run summary:** detailed.
- **Hub:** lean menu.

## Flagged for playtesting / balance
- ⚖️ Vanguard post-race heal vs. scarce-repair economy.
- ⚖️ "Finish not-1st" → run-ending vs. reward-denial only.
- ⚖️ Build-defining combo ceiling vs. degenerate builds.
- 🧱 Heaviest content lines: bespoke bosses, curated events, ~21 enemy designs, robust
  accessibility — budget explicitly (see Roadmap).
- 🧱 Deforming-collision destruction (perf + balance); added to M1 scope. Fallback: visual-only.
- ⚖️ Player crumple thresholds — how late the "near-death" sparks/smoke tell triggers.
