# 06 — Content Bible

The curated roster. Numbers are **design intent for tuning**, not final balance; they live in
data files (see Technical Architecture) and get tuned in playtesting. Reflects all interview
decisions; see [`07-design-decisions.md`](07-design-decisions.md) for the flat decision list.

---

## Cars

Four deep cars at launch. All share the same **base max HP**. Identity = **stats + a signature
passive + a biased boost source + a biased pool**. **No active abilities** (only weapons are
active). **Start with Vanguard + Juggernaut**; unlock **Comet + Ordnance** via Cores/milestones.
Stats are relative ratings (1–5) for feel.

| Car | Top Spd | Accel | Mass | Grip | Weapon slots | Signature passive | Boost lean |
|-----|:------:|:-----:|:----:|:----:|:------------:|-------------------|-----------|
| **Vanguard** | 3 | 3 | 3 | 3 | 1 | **Field Medic** — heals a chunk of HP after every race; strong balanced stats | Drift |
| **Juggernaut** | 3 | 1 | 5 | 2 | 1 | **Wrecking Momentum** — takedowns refill boost + stack a temporary ram-damage bonus | Takedowns |
| **Comet** | 5 | 4 | 1 | 4 | 1 | **Overflow** — passively generates boost over time; always fueled | Passive trickle |
| **Ordnance** | 2 | 3 | 3 | 3 | 3 | **Overcharge Array** — landing weapon hits shaves all weapon cooldowns | Weapon hits / drift |

### Pool bias
- **Vanguard:** even spread; flex mods that adapt to any build; HP/repair synergies.
- **Juggernaut:** mass, ram damage, armor, **takedown-trigger** mods (the takedown engine).
- **Comet:** handling, top speed, drift/boost economy, near-miss/draft synergies, evasion.
- **Ordnance:** extra weapon slots, cooldown reduction, targeting, **weapon-hit-trigger** mods.

Each car has its **own unlockable pool** (~15–20 entries) + access to the **shared neutral
pool** (~15 entries). Drafts skew **mostly car-specific**.

---

## Signature passives & boost economy

Boost is a **single free-spend meter** filled by **multiple sources, weighted per car/build**:
- **Drift mini-turbo** (universal): hold a drift to charge a tiered turbo, release for a burst.
- **Takedowns** (Juggernaut-leaning): wrecking refills boost.
- **Passive trickle** (Comet-leaning): boost generates over time.
- **Boost pads:** two kinds — *meter pads* (refill boost) and *speed pads* (instant kick).

---

## Core Parts (4-slot backbone — swappable/sellable, tiered T1→T3)

| Slot | Examples & trade-offs |
|------|------------------------|
| **Engine** | *Sprinter* (snappy accel, capped top speed) · *Cruiser* (slow ramp, high ceiling) · *Balanced* |
| **Chassis** | *Light* (−mass, +accel, −takedown force) · *Heavy* (+mass, +takedown/stability, −accel) |
| **Armor** | *Plating* tiers (+max HP, +damage resist; small mass cost) |
| **Weapon hardpoint(s)** | Mount points for weapons; **count scales by car** (Ordnance most) |

---

## Weapons (the only active offense — per-weapon cooldowns)

Four categories; aiming is mixed (some auto-lock, some fixed-mount aimed by positioning).

| Category | Examples | Notes |
|----------|----------|-------|
| **Direct-fire projectiles** | Auto-cannon, machine gun, side blaster | Fixed-mount front/side; drive-to-aim; skill via position |
| **Auto-lock homing** | Homing missile, seeker swarm | Locks nearest valid target; "fire and keep racing" (Ordnance core) |
| **Rear traps & droppers** | Mine, oil slick, caltrops | Dropped behind; protect a lead, zone pursuers (reborn cut item effects) |
| **Defensive & ram augments** | Shield, EMP, deflector; **spikes/plow** | Survivability + passive physical augments that amplify ramming takedowns |

---

## Mods (stackable passives — the combo layer; commit permanently, no removal)

Design favors **trigger-chain combos**. Representative examples (full pools authored later):

| Mod | Effect | Engine it feeds |
|-----|--------|-----------------|
| *Kinetic Battery* | Convert excess impact force into boost | Takedown / drift-boost |
| *Bloodlust* | Each takedown stacks +ram damage (decays out of combat) | Takedown engine (Juggernaut) |
| *Hot Barrels* | Weapon hits briefly +fire rate | Weapon engine (Ordnance) |
| *Slipstream Tactician* | +grip/handling while drafting | Comet / evasion |
| *Glass Cannon* | +ram damage, −max HP | High-risk takedown (conditional) |
| *Last Stand* | Below 25% HP: +speed and +ram damage | Clutch / conditional |
| *Ablative Barrels* | Barrel hits reflect damage to nearby cars | Hazard-heavy tracks |
| *Salvager* | +Scrap from takedowns | Economy |
| *Adaptive Tuning* | Auto-shifts a stat toward whatever you lack | Vanguard flex |

New Mods are injected into pools via meta-unlocks, so variety grows with playtime.

---

## Hazards & track elements (focused set)

| Element | Behavior |
|---------|----------|
| **Explosive barrels** | AoE burst on impact; prime environmental-takedown tool |
| **Oil slicks** | Kill grip; spin out the careless |
| **Boost pads (2 types)** | Meter-refill pads and instant-speed pads |
| **Static obstacles** | Walls/pillars/debris to pin and slam rivals against |
| **Neutral traffic** | On some themed tracks; dodge, get wrecked by, or slam rivals through |

---

## Tracks

- **Handcrafted layouts + randomized props** (hazard/boost/pickup placement randomized on a
  fixed layout). **~4–5 layouts per act.**
- **Mix of point-to-point sprints and circuits** by layout.
- **Field size set by node type.** Difficulty/complexity escalates per act (Act 1 wide &
  readable → Act 3 tight, hazard-dense, traffic-heavy).

---

## Acts, enemies & bosses

Per act: **4 normal archetypes + 2 elites + 1 bespoke boss**. Player-focused, archetype-driven
with **readable tells**, **asymmetric hand-tuned kits**. Elites are either **coordinated packs**
or **1v1/1v2 marquee duels**. (Names/themes below are placeholders.)

### Act 1 — "The Outer Scrapbelt" (teaching act: wide, readable, predictable)
**Normals:** Rookie (clean racing line) · Rammer (pursues, attempts takedowns — teaches
defense) · Sprinter (fast off the line — teaches catch-up/drafting) · Blocker (slow, body-blocks
the line — teaches angles & patience).
**Elites:** *Twin Rammers* (coordinated duo pack) · *The Hauler* (massive 1v1 armored duel).
**Boss — "BULWARK":** bespoke arena — an armored hauler that controls a hazard-laden arena;
teaches setup-over-brute-force.

### Act 2 — "The Combine Yards" (ranged threats, deployed hazards, tighter tracks, some traffic)
**Normals:** Gunner (keeps distance, shoots) · Mine-layer (drops hazards in the line) · Pack
Hunter (coordinates) · Charger (aggressive ram-and-retreat).
**Elites:** *Crossfire Pair* (two Gunners zoning a chokepoint — pack) · *Scrapjaw* (heavy
weaponized 1v1 duel).
**Boss — "SCATTERSHOT":** bespoke arena — area-fire + mines forcing constant repositioning;
phases from zoning to desperate ramming.

### Act 3 — "The Core Reaches" (hardest: combined threats, tight hazard-dense tracks, traffic)
**Normals:** Veteran (fast + aggressive) · Phantom (hazard/EMP space-denial) · Lancer (high-speed
ram specialist) · Warden Drone (escorts/support).
**Elites:** *Marauder Pair* (elite coordinated duo) · *The Executioner* (lethal 1v1 duel).
**Boss — "THE WARDEN" (climactic final boss):** bespoke multi-phase arena — adapts pressure,
uses the full toolkit, punishes greedy ramming; the run's ultimate skill check.

---

## Nodes

| Node | Contents |
|------|----------|
| Normal race | Small field (3–4); standard 1-of-3 draft (skippable for Scrap) |
| Elite race | Pack or duel of act elites; rarer draft + more Scrap |
| Swarm/Gauntlet | Big pack (5–6); chaos node, larger reward |
| Shop | Buy parts/mods/weapons, pay for repairs, sell back parts |
| Repair Bay | Scarce HP recovery |
| Event | Curated text risk/reward choice |
| Boss | Bespoke arena setpiece; large reward + Cores |

---

## Economy (starting intent)

| Source | Reward |
|--------|--------|
| Normal win | Base Scrap + per-takedown bonus + style bonus |
| Elite win | ~2× Scrap + rarer draft |
| Swarm/Gauntlet | High Scrap (many takedown targets) |
| Boss win | Large Scrap + Cores |
| Any takedown | Bonus Scrap (more with *Salvager*) |

| Sink | Feel |
|------|------|
| Repair (bay/shop) | Scales with HP restored; deliberately scarce |
| Parts (tier-ups) | Expensive backbone investment; sell-back recovers some |
| Mods | Mid-cost combo pieces (permanent) |
| Weapons | Cost scales with slots/power |

**Cores** (meta) are earned every run, win or lose, scaled to progress; spent on **car unlocks**
and **pool injections**. **Unlocks only — no power creep.**

---

## Authoring notes

- Every entry here becomes a **data record** the Core loads and validates (no dangling ids,
  costs/stats in range, non-empty pools — enforced by content-validation unit tests).
- **Keep the roster small and deep.** Don't add a 5th normal to an act before the existing four
  are individually fun and distinct. Variety is the job of *combination* (enemy mix × field size
  × track × hazards × your build), not raw count.
- **Bespoke bosses and curated text events are the heaviest content line-items** — budget them
  per act (see Roadmap). The M1–M3 slice needs only Act 1's first boss and a couple events.
