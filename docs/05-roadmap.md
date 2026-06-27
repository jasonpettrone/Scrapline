# 05 — Roadmap

Sequencing for a **solo, focused-commercial** build (~12–18 months). The spine of this plan
is **vertical-slice-first**: prove the game is fun on the smallest possible slice before
building breadth, and hit a Steam-page/wishlist milestone early.

## Milestone overview

| #   | Milestone                     | Proves / delivers                                   | Exit criteria |
|-----|-------------------------------|-----------------------------------------------------|---------------|
| M0  | Foundations                   | Repo, CI, the seam, a drivable grey-box car         | A car you can drive on one track; Core/Game/Tests projects build; CI green |
| M1  | **Takedown vertical slice**   | *Is ramming fun?* — the make-or-break question      | 1 player car + 1 AI on 1 track; takedowns, damage, win/lose a single race. **It feels great.** |
| M2  | Run-loop vertical slice       | The roguelite loop holds together                   | Mini Act-1 map (3 node types), upgrade draft, Scrap, persistent damage across ~4 races, 1 boss. A full tiny run, start→finish. |
| M3  | Act 1 content-complete (demo) | A polished, shippable slice                         | All Act-1 enemies/tracks, full Vanguard pool, Shop + Repair + Event, 1 boss. Externally playtestable. |
| M4  | **Steam page + demo**         | Wishlists; market validation                        | Capsule art, trailer, store page live; demo build (Next Fest-ready). Commercial gate. |
| M5  | Cars 2–4                      | Build diversity                                     | Comet, Ordnance, Juggernaut implemented with distinct pools & signature passives. |
| M6  | Acts 2 & 3                    | Full content arc                                    | All enemies, 2 more bosses, act-specific tracks/mechanics; difficulty curve roughed in. |
| M7  | Meta + platform polish        | A real product                                      | Meta-progression/unlocks, settings, controller, Steam Deck pass, achievements, hardened saves, audio pass. |
| M8  | Beta → launch (EA or 1.0)     | Ship it                                             | Closed beta, balance passes, bug-bash, localization prep, launch checklist. |

## Detail on the early, critical milestones

### M0 — Foundations
- Create the solution, `Core`, `game/` Godot project, `Core.Tests`, `Game.Tests`.
- Stand up CI (`dotnet test` green on an empty-but-real test).
- Implement the **seam contracts** (`RaceConfig`/`RaceResult`) as empty-but-real DTOs.
- Grey-box: a `RigidBody2D` car you can drive on a flat track with placeholder handling.
- *Deliverable:* "I can drive a box around a box." Boring on purpose.

### M1 — Takedown vertical slice (THE milestone)
Everything here serves pillar #1. Nothing else matters until this is fun.
- Real car handling tuning pass: weighty momentum + **mini-turbo drift** (hold/release), boost.
- Damage model: ram chip-HP + **environmental slams** (walls, barrels) + HP. **Clean takedowns
  cost you nothing**; botched collisions hurt — tune that line until it feels fair and great.
- **Aftertouch slow-mo on big destructive takedowns.**
- One player-focused AI opponent that drives and can be taken down (and pressures you).
- Wrecked opponent **respawns set back ∝ hit force**; player **does not respawn** (HP→0 ends it).
- Win by **finishing 1st** on one track.
- **Gate:** if slamming a rival into a wall/barrel isn't satisfying after honest tuning,
  **stop and rethink the core verb** before spending months on the meta layers. This milestone
  is a go/no-go on the whole concept.

> **Build accessibility hooks from M0/M1** (input remap, screen-shake & slow-mo intensity
> toggles, colorblind-safe palette) — far cheaper in than bolted on. 🧱

### M2 — Run-loop vertical slice
- Core: map generation (small), run state machine, persistent HP (no player respawn), Scrap.
- Upgrade draft (1-of-3, **skippable for Scrap**) wired through the seam into the next race's
  `RaceConfig`. Parts swap/sell; **Mods commit** (no removal).
- Node types: Normal race, Repair Bay (scarce), Shop (minimal, with sell-back).
- One bespoke Act-1 boss arena. 🧱
- *Deliverable:* a 10–15 min run you can win or die in, with builds that visibly change play.

### M3 — Act 1 demo
- Flesh Act 1 to content-complete for the **two starter cars (Vanguard + Juggernaut)**: all
  Act-1 enemies (4 normals + 2 elites), ~4–5 tracks, curated Events, full Shop, the bespoke
  Act-1 boss, reward/economy tuning. Field sizes per node type (duel/small/swarm).
- This is your external playtest build and the basis of the Steam demo.

### M4 — Steam page + demo (commercial gate)
- Store page, capsule, short trailer from M3 footage, wishlist campaign begins.
- Polish the M3 slice into a public demo (aim at a Steam Next Fest window).
- *Why here:* wishlists compound over time; getting the page up early is the highest-ROI
  marketing action for a solo commercial title.

## Sequencing principles

- **Breadth last.** One car, fully realized (M3), before four cars (M5). One act before three.
- **Content as data from M2 on**, so Acts 2–3 (M6) are mostly authoring, not engineering.
- **Keep `main` shippable.** Every milestone ends on a playable build.
- **Test as you go:** Core logic lands with its xUnit tests in the same PR; integration tests
  follow each new engine system. CI green is the definition of "done."

## Top risks & mitigations

| Risk                                              | Mitigation |
|---------------------------------------------------|------------|
| Takedowns aren't fun (kills the concept)          | M1 is a dedicated go/no-go gate before any breadth spend. |
| Scope explosion (4 cars × 3 acts is a lot, solo)  | Vertical-slice-first; ship 1 car + Act 1 fully before breadth; content-as-data to cut per-item cost. |
| AI driving is harder than expected                | Treat AI as a first-class subsystem behind `IDriveInput`; integration tests for "doesn't get stuck"; iterate per archetype. |
| Physics feel eats unbounded time                  | Time-box tuning passes; lean on playtest notes; remember Core correctness ≠ fun, only playtesting proves fun. |
| First-game-on-Steam unknowns (launch, ports)      | M4 store page early surfaces process unknowns 6–12 months before they're critical. |
| Burnout (the human kind), solo over 12–18 mo      | Milestones end in playable builds = steady morale wins; market feedback at M4 sustains motivation. |

## Rough time feel (not a commitment)

M0–M1 are the highest-uncertainty, highest-value months — give them room. M2–M4 carry you to
a public demo. M5–M8 are more predictable content + polish work. If M1 slips, that's the
plan *working* — better to learn the verb is wrong in month 2 than month 10.
