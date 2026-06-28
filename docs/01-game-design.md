# 01 — Game Design Document

Working title: **SCRAPLINE**

> This document reflects the full design-interview decisions. A flat, scannable list of every
> locked decision lives in [`07-design-decisions.md`](07-design-decisions.md). Concrete
> rosters (cars, enemies, weapons, mods, economy) live in [`06-content-bible.md`](06-content-bible.md).

## 1. Design pillars

Every feature serves one of these. If it serves none, cut it.

1. **The takedown feels incredible.** (Burnout DNA) Slamming a rival into a wall, barrel, or
   traffic is the core verb — weighty, readable, satisfying every time. Cars and the
   environment **visibly deform and break** under impact: enemies crumple and split apart on a
   kill, while your own car wears its damage more soberly (see §4).
2. **Builds are expressive and car-defined.** (Slay the Spire DNA) Your car is your character;
   it dictates which passive upgrades you see and how you win.
3. **Damage is a run-long resource.** Persistent HP with scarce repair turns every race into
   risk/reward. There are no clean losses — losing ends the run.
4. **Curated, not random.** (Hades DNA) A small, hand-tuned cast of enemies, bosses, and tracks
   that players learn and master. Variety comes from *combination*, not volume.

## 2. Setting & fiction

**Anti-grav combat cars** racing curated tracks across a used, industrial corner of space —
salvage belts, freight yards, station interiors, planet surfaces. The cars handle like
*weighty vehicles* (mass, momentum, drift all matter), with a sci-fi skin. Tone is **gritty
industrial**: grime, sparks, rust, hard impacts. Narrative/story is deliberately deferred
(see Roadmap); for now cars and the world just need **distinct, readable placeholder looks**.

## 3. Core loop

```
LEAN MENU (Play · Unlocks · Achievements · Settings)
   │  pick a car (start: Vanguard or Juggernaut)
   ▼
RUN ── ACT 1 ─► ACT 2 ─► ACT 3 ─► VICTORY
        │ full Slay-the-Spire branching map, visible upfront (~10–12 nodes)
        │ route through:
        │   ├─ Normal race      (small field, 3–4 cars)
        │   ├─ Elite race       (duel 1v1/1v2  OR  coordinated elite pack)
        │   ├─ Swarm/Gauntlet    (big pack, 5–6 cars)   [special node]
        │   ├─ Shop             (buy parts/mods/weapons, repair, sell-back)
        │   ├─ Repair Bay       (scarce HP recovery)
        │   ├─ Event            (curated text risk/reward choice)
        │   └─ Boss             (bespoke arena setpiece; unique per act)
        │ win a race ─► draft 1 of 3 upgrades (or skip for Scrap)
        │ HP→0 at any point ─► RUN OVER (no respawn for the player)
        ▼
DEATH/VICTORY ─► detailed run summary ─► bank Cores ─► unlock cars/pools ─► run again
```

Run length target ~**35–45 min**. After clearing the game, an **Ascension/Heat ladder** of
stacking difficulty modifiers provides the long-tail challenge.

### Win / lose conditions
- **Clear a race:** finish **1st** (position only). Opponents respawn when wrecked, so "last
  car standing" is *not* a win path — takedowns win you **position and tempo**, not kills.
- **Run ends (death):** your HP hits **0**. You never respawn; the run is over. HP carries
  between races and repairs are scarce, so death usually comes from **attrition** across a run,
  not a single hit.

## 4. The race (moment-to-moment)

Top-down 2D, physics cars (`RigidBody2D`). **Tracks are a mix of point-to-point sprints and
circuits** depending on layout. **Field size is set by node type** (duels → small fields →
packs → bespoke bosses). Race length ~**90–120s**. **Camera** dynamically follows you, zooming
out at speed and to keep a clashing rival framed (combat readability first).

**Active inputs are only: drive · drift · boost · fire weapon.** Everything else (signatures,
mods) is passive. No per-car active abilities.

**Driving & boost**
- **Weighty momentum handling with drift.** Mass matters for cornering and takedowns.
- **Mini-turbo drift:** hold a drift through a corner to charge a tiered turbo, release for a
  boost burst. The core skill mechanic and a universal way to earn boost.
- **Boost** is a single free-spend meter. It fills from **multiple sources weighted by your
  car/build** — drifting (all cars), takedowns (Juggernaut leans here), passive trickle
  (Comet), etc.
- **Boost pads** come in two kinds: some **refill boost meter**, some give an **instant speed
  kick**. Nailing the racing line through them is rewarded.

**Takedowns (the core verb)**
- Three damage vectors: **ram-to-chip-HP**, **environmental slams** (into walls, explosive
  barrels, oil, neutral traffic — the flashy payoff), and **equipped weapons**.
- **No takedown button** — your speed, mass, and angle are the weapon (pure physics).
- **Clean takedowns cost you nothing**; only botched/awkward collisions damage you. Skill =
  landing it clean.
- **Aftertouch:** big, destructive takedowns trigger a brief **slow-mo** for spectacle and a
  chance to line up the next hit.
- Wrecking an opponent **respawns them after a delay, set back proportionally to how hard you
  hit them** — a clean high-speed slam is a big tempo swing; a glancing wreck is minor.

**Dynamic destruction & deformation**
- **Everything deforms.** Cars, walls, and obstacles crumple under impact via a
  physical-*looking* approximation (deformable meshes, not full soft-body). Damage is
  **localized** — a rear-ended car is crushed at the back.
- **Asymmetric by design (readability vs spectacle).** Your car deforms **soberly** — dents
  accumulate naturally and stay readable, with sparks/smoke only when you're ~one hit from
  death (a danger tell). Enemies are **exaggerated**: they shed panels through a fight and
  **break apart on a kill** for maximum payoff. On death, the player gets the full
  enemy-grade destruction too.
- **The only gameplay effect is the changing collision shape.** Deformation alters a car's
  hitbox (a crushed car drives/feels different) but applies **no separate stat penalties** —
  feel and emergence over bookkeeping.
- **Debris is alive briefly.** Shed panels and wreckage **linger as collidable hazards**
  (slam a rival into them) before fading.
- **Sources:** car↔car, car↔wall/obstacle, weapons, and explosive barrels.
- **Persistence:** car deformation persists across the **whole run** (a battered car tells
  your story) until a repair, which **fully restores** it; environment damage lasts the race
  and resets at the next node.
- **Feedback scales with force:** crunch SFX, sparks, smoke, and screenshake all ramp with
  impact; takedowns punch with **hitstop + shake** (atop the existing aftertouch slow-mo on
  the biggest hits).

**Persistent damage**
- HP carries between races. Recovery is scarce (Repair Bay nodes, pricey shop repairs, the
  Vanguard's signature heal, some Event outcomes). A battered win makes the next race scarier.

**Hazards & traffic (focused sandbox)**
- Explosive barrels, oil slicks, boost pads, static obstacles. **Neutral traffic on some
  (themed) tracks** — dodge it, get wrecked by it, or slam rivals through it.

## 5. Cars (the "characters")

Four deep cars at launch. **Start with Vanguard + Juggernaut**; unlock **Comet + Ordnance**
via Cores (some cars gated behind milestones). All share the same base max HP; they diverge
through **stats, a signature passive, a biased boost source, and a biased upgrade pool**. No
active abilities — identity is passive + how the pool steers your build.

| Car | Role | Signature passive (starting Mod) | Boost lean | Pool bias |
|-----|------|----------------------------------|-----------|-----------|
| **Vanguard** | All-rounder / learning car | **Heals HP after every race** + strong balanced stats (partly opts out of the scarce-repair pressure) | Drift | Even, flexible |
| **Juggernaut** | Takedown bruiser | **Wrecking Momentum** — takedowns refill boost and stack a temporary ram-damage bonus (self-sustaining takedown engine) | Takedowns | Mass, ram, armor, takedown-trigger mods |
| **Comet** | Speed / evasion | **Passive boost generation** — always fueled, can lean on speed/boost constantly | Passive trickle | Handling, top speed, drift/boost synergies |
| **Ordnance** | Weapons platform | **Overcharge Array** — landing weapon hits shaves all weapon cooldowns (weapon engine) | Weapon hits / drift | Extra weapon slots, ammo/cooldown, targeting |

Each car has its **own unlockable pool** of parts/mods/weapons plus access to the **shared
neutral pool**.

## 6. Upgrades & builds

- **Reward draft:** after a win, **pick 1 of 3** (drawn mostly from your car's pool, some
  neutral). **Skip for bonus Scrap** to steer toward a shop plan.
- **One acquisition channel:** both Parts and Mods come from drafts *and* shops.
- **Core Parts (4-slot backbone):** Engine (top-speed vs. acceleration trade-offs), Chassis
  (mass: takedown/stability vs. accel), Armor (HP/resistance), Weapon hardpoints
  (**count scales by car**). **Parts are swappable/sellable.**
- **Mods (the combo layer):** stackable passives. **Mods commit permanently — no removal.**
- **Synergy design favors trigger-chain combos**: on-takedown / on-drift / on-low-HP effects
  that feed loops (takedown → boost → bigger slam → takedown). Power ceiling is high —
  **build-defining combos** are the goal, with enemies scaling per act to match.
- **Pools are lean & curated** (~15–20 car-specific + ~15 neutral per car).

## 7. Weapons (the only active offense)

- **Car-scaled slots** (slot count is itself a stat; Ordnance gets the most).
- **Per-weapon cooldowns** (no ammo bookkeeping).
- **Four categories:** direct-fire projectiles (forward/side), auto-lock homing (missiles/
  seekers), rear traps/droppers (mines/oil), and defensive & ram augments (shields/EMP plus
  passive physical augments like spikes/plows that amplify ramming takedowns).
- **Aiming is mixed:** some weapons auto-lock; fixed-mounts (front/rear/side) aim by
  positioning your car. No twin-stick manual aim.

## 8. Enemies & AI

- **Archetype personalities with readable tells** — players learn to counter each type.
- **Player-focused** targeting (enemies race the line and pressure *you*).
- **Asymmetric hand-tuned kits** (enemies don't roll the player's upgrade system) for tight,
  testable difficulty.
- **Per-act unique roster:** **4 normals + 2 elites + 1 boss per act** (~21 designs total).
  Elites come in two flavors — **coordinated packs** or **marquee 1v1/1v2 duels**.
- **Lethality = attrition:** enemies chip and pressure but rarely outright wreck you unless
  you're careless or already low. Normal races are **genuine, winnable contests** (not fodder).
- **Curated per-act difficulty escalation** (tougher kits, more aggressive AI, denser hazards,
  trickier tracks). **No rubber-banding.**

## 9. Run structure & nodes

- **Full StS branching map**, visible upfront — route to hit the shops/repairs/elites you want.
- ~**10–12 nodes/act**, three acts.
- **Partial pre-race preview:** you see node type, field size, and track theme — not the exact
  opponents or hazard placement.
- **Repair is scarce** (dedicated Repair Bay nodes + pricey shop repairs).
- **Bosses are bespoke arena setpieces**, unique per act; **Act 3 is a climactic final boss**.
- **Events are curated text** risk/reward choices.

## 10. Economy & meta-progression

- **Scrap** (in-run): earned from races with **bonus for takedowns/style**; spent on parts,
  mods, weapons, and repairs (shops support **sell-back** of parts).
- **Cores** (meta): earned every run **win or lose, scaled to progress**; spent on **car
  unlocks** and injecting new Mods/parts into pools.
- **Meta is unlocks-only — no permanent power creep.** A win always means *you* won.
- **Ascension/Heat ladder** unlocks after beating the game.

## 11. Presentation & feel

- **Art:** gritty industrial, but **readability enforced** via car color-coding, glowing hazard
  accents, and clean silhouettes against the grime.
- **Audio:** aggressive rock/metal energy (royalty-free/original to start to dodge licensing).
- **Game-feel:** punchy but **readability-first** — generous hitstop/shake/debris,
  **force-scaled crunch & deformation**, and slow-mo on big takedowns, always tuned to keep the
  top-down picture clear (your car stays legible; enemies get the spectacle).
- **HUD:** rich but clean — HP, boost meter, signature/charge state, race position, weapon
  cooldowns, track-progress indicator, all glanceable.
- **Run summary:** detailed (distance/acts, takedowns, biggest hit, build highlights, Cores,
  unlock progress) → fast restart.

## 12. Controls, platform, onboarding, save

- **Controller-first**, full keyboard rebinding. (Weapon scheme needs no mouse aim.)
- **Steam Deck:** nice-to-have, not a launch gate (but keep UI/perf Deck-friendly).
- **Integrated onboarding** through Act 1 — forgiving enemies teach one concept at a time.
- **Save:** single save, **resume mid-run**.
- **Steam features:** achievements at launch; dailies/leaderboards are a post-launch add (the
  seeded-generation architecture already supports them).
- **Accessibility: robust from M0** — colorblind-safe palettes/markers, full remap, screen-
  shake & slow-mo intensity toggles, assist/difficulty options.

## 13. Explicit non-goals

- No online multiplayer. No deterministic physics/replays (determinism lives only in the
  seeded *generation* layer — see Technical Architecture). No open world. No item-box
  consumables (cut — weapons + physics + hazards carry offense). No per-car active abilities.
  No sprawling content — curated roster, variety from combination.

## 14. Open questions for playtesting

- Does "finish not-1st = no clear" feel right, or should 2nd survive-but-no-reward? (Currently:
  you simply need 1st; failing to place 1st means you didn't clear — tune whether that ends the
  run or just denies reward.)
- Is the Vanguard post-race heal too strong against the scarce-repair economy? (Key balance lever.)
- Right run length / node count for the ~35–45 min target.
- How lethal should elites/bosses be relative to the "attrition" normal-race model.
- Whether Swarm/Gauntlet pack nodes are common or rare.
