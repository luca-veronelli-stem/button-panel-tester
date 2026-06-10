# Tasks — #204 instrument PanelDiscoveryService with structured logging

> Lean ticket. The GitHub issue [#204](https://github.com/luca-veronelli-stem/button-panel-tester/issues/204)
> **is the spec** (P1 story, acceptance criteria, non-goals all live there). This file exists only to give
> each bisect-safe slice a `Tasks:` trailer referent and a scannable completion state. No `spec.md` /
> `plan.md` / `research.md` were generated — disproportionate for a logging-only follow-up.

Archetype **A** (`.stem-standard.json`), standard `v1.15.0`, `docs/Standards/LOGGING.md` pinned `v1.2.0`:
logger is **required**, no `?.`; templates + named params only (CA2254 clean); no `Console`/`Debug.WriteLine`.

## Design decisions (orchestrator-owned)

- `PanelDiscoveryService` gains a 4th ctor param `logger: ILogger<PanelDiscoveryService>` (mirrors
  `CanLinkService`, which takes its logger last). Threaded through **every** construction site in the same
  commit (bisect-safe): `CompositionRoot.fs`, `DiscoveryE2ETests.fs`, `PruningE2ETests.fs`,
  `LinkLossClearsListTests.fs`, `DiscoveryHardwareTests.fs`.
- **New panel** (UUID not already in the map) → `Information`. **Coalescing re-broadcast** (UUID already
  present) → `Debug` (NOT Information). The new-vs-rebroadcast distinction is computed under the existing
  `panelsLock` (`Map.containsKey f.Uuid` *before* `PanelsOnBus.observe`); the log fires **outside** the lock,
  matching the existing publish-outside-lock discipline.
- **Prune** (row count changed) → `Debug`. **FR-008 link-loss clear** (non-empty list cleared) → `Debug`.
- UUID rendered as the canonical hex triple `%08X-%08X-%08X` (same as the GUI's `uuidText`), passed as a
  single `{Uuid}` named param. `PanelUuid` is a three-word value, so the issue's `{Uuid:X8}` is illustrative.
  Services must NOT depend on the GUI's renderer (wrong layer direction) — render inline.
- No behavior change: publish-on-change, publish-outside-lock, silent-drop-while-not-Connected, prune TTL
  (15 s), and the GUI are all untouched. Logging is purely additive.

## Tasks

- [X] **T001** — `feat`: structured domain-event logging in `PanelDiscoveryService`
  - RED: a capturing-`ILogger` test (new shared `Fakes/RecordingLogger.fs`) that (a) a first WHO_I_AM for a
    new UUID emits exactly one `Information` entry, and (b) a second WHO_I_AM for the **same** UUID emits **no
    further** `Information` entry. Fails before the impl (no `logger` param / no log calls).
  - GREEN: add the required `ILogger<PanelDiscoveryService>` param + the four log calls (appeared = Information,
    re-observed = Debug, prune = Debug, link-loss clear = Debug); thread the param through all five
    construction sites listed above. Solution builds, all tests green.
  - Folds RED+GREEN into ONE bisect-safe commit.

- [X] **T002** — `feat`: per-drop-axis `Trace` logging in `WhoIAmReassemblyObserver`
  - RED: a capturing-`ILogger` test asserting a `Trace` entry (with a reason) on each drop axis
    (wrong id / incomplete reassembly / wrong command / wrong length).
  - GREEN: add the `Trace` log calls at each drop point. Trace is off by default (min level Information) so no
    bench spam. Solution builds, all tests green.
  - Folds RED+GREEN into ONE bisect-safe commit.

- [ ] **T003** — `docs` (orchestrator): `CHANGELOG.md` `[Unreleased]` gains a discovery-logging line so it
  rides into v0.3.0. (Docs commit — no `Tasks:` trailer required by the gate.)

## Out of scope (issue non-goals)

No new log sink/provider; no `[LoggerMessage]` source-gen; no `BeginScope`/correlation IDs; no discovery
behavior / codec / pruning-timing / GUI change; no retro-logging of other subsystems.
