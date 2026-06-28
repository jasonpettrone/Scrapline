# 04 — Testing Strategy

The architecture exists to make this section possible. Because the **Core** is pure C# with
no engine dependency, the overwhelming majority of game logic is testable the same way you'd
test a Node service: fast, deterministic, in CI, with no special harness.

## The testing pyramid

```
        ▲  few          E2E / golden-run smoke tests
       ╱ ╲               (seeded full run plays start→finish without crashing)
      ╱   ╲
     ╱─────╲  some       INTEGRATION (GdUnit4, in-engine)
    ╱       ╲            physics, AI, hazards, scene wiring, the seam round-trip
   ╱─────────╲  many     UNIT (xUnit, Core, no Godot)
  ╱___________╲          map gen, stat derivation, drafting, economy, save, RNG
```

We deliberately put the **bulk** of tests at the bottom (Core unit tests) because that's
where the bulk of *logic* lives and where tests are cheapest and most reliable. The top of
the pyramid (physics feel) is intentionally thin — that's covered by *playtesting*, not
automated tests.

## Layer 1 — Core unit tests (xUnit) — the workhorse

Run with `dotnet test tests/Core.Tests`. No Godot. Milliseconds. Every push.

What gets tested here (target: high coverage, this is the bulk of the suite):

- **Stat derivation** — `Base + Parts + Mods → Derived`. The single most important test
  surface. Cover: each part tier, mod stacking, stacking order independence (or documented
  order), stat caps/floors, conflicting mods, every car's starting kit.
- **Map generation** — given a seed: correct act structure, every path reaches the boss, no
  orphan/unreachable nodes, node-type distribution within designed bounds, boss is always
  terminal. Seeded → reproducible (assert seed X always yields map Y).
- **Drafting / rewards** — offers respect the car's pool, rarity weighting matches tables,
  no duplicate offers when undesired, elites/bosses upgrade the rarity, empty-pool guards.
- **Economy** — scrap payouts (incl. takedown/style bonuses), shop pricing, repair costs,
  Cores payout curve, can't-afford guards, no negative balances.
- **Run state machine** — node resolution, HP persistence across nodes, applying a
  `RaceResult` advances state correctly, death conditions trigger exactly when intended,
  act transitions.
- **Content validation** — load all content data and assert integrity: no dangling ids,
  every car pool non-empty, costs/stats within sane ranges, every enemy/track referenced by
  the act tables exists. (Doubles as a runtime guard.)
- **Save/load** — round-trip a run + meta save; forward-compat across a version bump;
  corrupt/partial save handled gracefully.
- **RNG** — seeded determinism; same seed + same calls → same sequence; injectable fake RNG
  for forcing specific outcomes in other tests.
- **Architecture guard** — a test asserting the `Core` assembly has **no Godot dependency**,
  so the seam can't silently rot.

### Patterns
- Inject `IRng` everywhere; tests use a deterministic or scripted fake to force outcomes.
- Content loaded from test fixtures *and* from the real content files (catch authoring bugs).
- Prefer table/`[Theory]` tests for stat & economy math.
- `RaceResult` fixtures drive run-state tests without ever running a real race.

## Layer 2 — Integration tests (GdUnit4, in-engine)

Run inside Godot (headless in CI). Slower; run on PR/merge, not every keystroke. These cover
the things that *only* exist once the engine is involved:

- **The seam round-trips.** Build a `RaceConfig` in Core → spawn the race scene → it produces
  a well-formed `RaceResult` the Core can consume. No NaNs, valid placement, fields populated.
- **Physics sanity (ranges, not exact values).** A head-on collision at velocity V produces
  damage within an expected band; a heavier car wins a symmetric ram; a barrel explosion
  applies AoE damage to cars within radius. We assert *bounds and monotonicity*, never exact
  floats — physics isn't deterministic.
- **AI driving doesn't break.** AI completes a lap/track within a time bound; never gets
  permanently stuck on geometry; respects its archetype (a "Sprinter" actually pulls ahead;
  a "Rammer" actually pursues).
- **Hazards & items apply effects.** Item box → grants an item; mine → damages on contact;
  oil slick → reduces grip; shield → blocks one hit.
- **Scene flow.** Menu → Car Select → Map → Race → Reward → Map transitions wire up and the
  `GameDirector` carries the Core run object across them.
- **Destruction invariants (occurrence/ranges, not exact shapes).** A hard enough hit deforms
  the struck zone; an opponent at 0 HP triggers the break-apart; debris count stays capped;
  a repair returns the collision proxy to baseline. Assert *that it happens* and stays within
  bounds — never exact vertex positions; deformation itself is non-deterministic and
  playtested for feel.

### Patterns
- Assert **invariants and ranges**, not exact physics outputs.
- Use small fixed sub-tracks/arenas as test scenes for collision/AI cases.
- Run AI-vs-AI to exercise the race loop without human input.

## Layer 3 — End-to-end smoke / golden run

A tiny number of high-value tests:
- **Seeded full run.** Drive a scripted/AI player through a complete seeded run (all node
  types, a boss) headlessly; assert it completes without crashing and end-state is coherent.
- **Golden master (optional).** Snapshot the generated shape of a seeded run (map + drafts +
  economy, the deterministic parts only) and diff against a stored golden file to catch
  unintended balance/generation drift. Never snapshot physics.

## What we explicitly do NOT automate

- **"Does it feel good?"** Takedown weight, handling, game feel, difficulty balance,
  fun — these are answered by **playtesting**, captured as notes, not asserted in code.
- **Exact physics outputs / pixel-perfect rendering / deformation shapes.** Out of scope by
  design — *that* deformation occurs is integration-tested; how it looks/feels is playtested.

## CI

- `.github/workflows/core-tests.yml` — `dotnet test` on **every push**. Must be green to
  merge. This is the fast feedback loop and the safety net for refactors.
- `.github/workflows/engine-tests.yml` — headless Godot + GdUnit4 on **PR/merge to main**.
  Slower; catches integration regressions before they land.
- `main` is protected: no merge unless Core tests pass.

## Coverage philosophy

Chase coverage where it's cheap and meaningful (the Core — aim high). Don't chase a coverage
number through the Godot layer; there, test **behaviors and invariants** that would
genuinely break the game, and lean on playtesting for the rest.
