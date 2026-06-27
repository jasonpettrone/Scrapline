# 03 — Repo Structure & Conventions

## Layout

```
scrapline/
├─ README.md
├─ .gitignore
├─ .editorconfig                 # formatting/style, enforced in CI
├─ Directory.Build.props         # shared C# settings (lang version, nullable, analyzers)
├─ Scrapline.sln                 # ties all C# projects together
│
├─ docs/                         # the plan (this folder)
│
├─ game/                         # THE GODOT PROJECT (project.godot lives here)
│  ├─ project.godot
│  ├─ Game.csproj                # Godot-generated C# project; references src/Core
│  ├─ scenes/                    # .tscn scene files
│  │  ├─ menu/
│  │  ├─ map/
│  │  ├─ race/
│  │  └─ reward/
│  ├─ scripts/                   # C# node scripts (the Godot-facing layer)
│  │  ├─ GameDirector.cs         # autoload; owns the Core Run, drives scene flow
│  │  ├─ race/                   # CarController, RaceScene, hazards, items, AI
│  │  ├─ map/                    # map rendering & node selection
│  │  └─ ui/
│  ├─ content/                   # gameplay data (cars, mods, enemies, tracks, economy)
│  ├─ assets/                    # placeholder art/audio (sprites, sfx)
│  └─ resources/                 # Godot .tres resources (presentation bindings)
│
├─ src/
│  └─ Core/                      # pure C# class library — NO Godot reference
│     ├─ Core.csproj
│     ├─ Run/
│     ├─ Map/
│     ├─ Cars/
│     ├─ Upgrades/
│     ├─ Drafting/
│     ├─ Economy/
│     ├─ Content/
│     ├─ Save/
│     ├─ Rng/
│     └─ Contracts/              # RaceConfig, RaceResult, shared DTOs
│
├─ tests/
│  ├─ Core.Tests/                # xUnit — references Core only, runs without Godot
│  │  └─ Core.Tests.csproj
│  └─ Game.Tests/                # GdUnit4 in-engine integration tests
│
├─ tools/                        # content-authoring & build helper scripts
│
└─ .github/
   └─ workflows/
      ├─ core-tests.yml          # dotnet test on every push (fast)
      └─ engine-tests.yml        # headless Godot + GdUnit4 (slower, on PR/merge)
```

### Why the Godot project is in `game/` (not repo root)

Godot's C# tooling expects `project.godot` and its `.csproj` together. Putting the whole
Godot project under `game/` keeps the root clean and lets the **solution** reference both
`game/Game.csproj` and `src/Core/Core.csproj` without entangling them. `Game.csproj`
references `Core.csproj`; `Core` references nothing engine-related. This is what makes
`dotnet test tests/Core.Tests` work with no Godot install.

## Project reference graph

```
Scrapline.sln
 ├─ src/Core/Core.csproj           (no project refs; no Godot)
 ├─ game/Game.csproj               ──► Core.csproj   (+ Godot SDK)
 ├─ tests/Core.Tests/*.csproj      ──► Core.csproj   (+ xUnit)
 └─ tests/Game.Tests/*.csproj      ──► Game + Core   (+ GdUnit4)   [in-engine]
```

## Conventions

- **Namespaces:** `Scrapline.Core.*` for the library, `Scrapline.Game.*` for Godot scripts.
- **Nullable reference types: ON** project-wide (`<Nullable>enable</Nullable>`). Catches a
  whole class of bugs at compile time — set once in `Directory.Build.props`.
- **Analyzers + warnings-as-errors** in CI builds (lenient locally). Roslyn analyzers +
  `.editorconfig` enforce style so reviews aren't about formatting.
- **One public type per file**, file named after the type.
- **Content ids are strings/enums in one registry**, never magic literals sprinkled around.
- **No `System.Random` in gameplay logic** — always go through the injected `IRng` so tests
  control the seed.
- **Commit hygiene:** conventional-ish messages (`feat:`, `fix:`, `test:`, `chore:`),
  small focused commits. Branch per feature; `main` stays green (CI passing).

## Naming

- Repo / working title: **scrapline** (lowercase for the folder & Steam app id slug).
- Solution/assembly: `Scrapline`.
- Final game name is a marketing decision for later; keep the codename stable until then.
