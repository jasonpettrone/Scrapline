# SCRAPLINE (working title)

A top-down 2D roguelite space-racing game with Burnout-style takedowns and a heavily
physics-driven sandbox, wrapped in a Slay the Spire / Hades run structure.

> **Status:** M0 (foundations). The plan in [`/docs`](docs) is locked and the repo skeleton is
> live: a pure-C# Core with xUnit tests, a minimal Godot 4.7 (.NET) project, and green CI.

## The 30-second pitch

You pilot a car through a branching map across **three acts**. Each node is a **race**: reach
the finish line in first, or wreck everyone who stands in your way. Slamming opponents into
walls and explosive barrels (**takedowns**) is both your weapon and your style. **Damage
persists across the entire run** — repairs are scarce — so every dent you take is a debt you
carry toward the act boss. Win a race, draft an **upgrade**. Your **car choice** is your
"character": it dictates which upgrades you'll see and how you'll win.

## Tech stack

| Concern            | Choice                                                             |
|--------------------|--------------------------------------------------------------------|
| Engine             | **Godot 4** (.NET / Mono build)                                     |
| Language           | **C#**                                                             |
| Architecture       | Headless **simulation Core** (pure C#) + Godot presentation layer   |
| Unit tests         | **xUnit** against the Core (no engine required, runs in CI)         |
| Integration tests  | **GdUnit4** in-engine (physics, AI, scene wiring)                   |
| Steam              | Steamworks via GodotSteam / Facepunch.Steamworks (M7)               |
| CI                 | GitHub Actions (`dotnet test` + headless Godot test run)            |

## Running tests locally

Run the **same checks CI runs**, before you push — a green local run means green CI:

```powershell
./tools/test.ps1          # full gate: Core unit tests + headless Godot smoke test
./tools/test.ps1 -Core    # fast loop: Core unit tests only (no engine)
```

`tools/test.ps1` mirrors [`.github/workflows/`](.github/workflows) exactly. The raw commands,
if you want them directly:

```powershell
dotnet test tests/Core.Tests     # Core unit tests
godot --path game                # open the game in the editor (F6 runs the current scene)
```

## The plan (read in order)

1. [`docs/01-game-design.md`](docs/01-game-design.md) — the game design document (what we're building & why it's fun)
2. [`docs/02-technical-architecture.md`](docs/02-technical-architecture.md) — the Core/Engine seam, data flow, key systems
3. [`docs/03-repo-structure.md`](docs/03-repo-structure.md) — folder layout, projects, conventions
4. [`docs/04-testing-strategy.md`](docs/04-testing-strategy.md) — what we test, where, and how (the part you care about)
5. [`docs/05-roadmap.md`](docs/05-roadmap.md) — milestones, vertical-slice-first sequencing, risks
6. [`docs/06-content-bible.md`](docs/06-content-bible.md) — cars, enemies, bosses, weapons, mods, tracks, economy
7. [`docs/07-design-decisions.md`](docs/07-design-decisions.md) — flat ledger of every locked design decision

## Guiding principle

**Prove the fun before building breadth.** The single biggest risk is that ramming cars
isn't fun to do. Milestone 1 exists only to answer that question. Everything else — the map,
the upgrades, four cars, three acts — is worthless if the 10-second moment of slamming a
rival into a barrel doesn't feel great. We build that first, in a grey box, and we don't
move on until it does.
