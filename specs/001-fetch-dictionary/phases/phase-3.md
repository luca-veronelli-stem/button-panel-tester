# Phase 3 — User Story 1: Status row + seed extraction (MVP, P1)

This document scopes the third phase-PR for spec 001 (dictionary fetch and
status display). It is the durable anchor for the Phase 3 work driven by the
`resolve-ticket` (unsupervised) protocol.

## Scope

Ship the MVP user story: a technician launches the tool on a fresh, offline
machine and within 1 s sees a colour-coded status row reading
`Cached · last synced <seed build date>` whose detail affordance discloses
the seed origin and source path. Independent acceptance criterion from
[`../spec.md`](../spec.md) §US1.

Concretely, Phase 3 lands:

- The on-disk JSON cache adapter (atomic temp+rename writes, SHA-256 sidecar).
- The embedded seed asset and its extractor (no-op when the cache file
  already exists).
- The minimal `DictionaryService` implementation covering the offline path
  (`RefreshAsync` is a `notSupported` placeholder until US3 / Phase 5).
- The Microsoft.Extensions.DI composition root with US1-scope bindings
  (no-op `IDictionaryProvider` / `IRegistrationClient` so launch succeeds
  without network).
- The FuncUI `DictionaryStatusRow` view (colour pill + headline + detail
  affordance; refresh button + in-flight UX are deferred to US3 / Phase 5).
- The `App.fs` + `Program.fs` shell that hosts the main window and runs
  `IDictionaryService.InitializeAsync` on window-loaded.
- Four test files: unit (cache), integration (service initialise),
  GUI (status row via `Avalonia.Headless.XUnit`), property
  (`DictionaryServiceTransitions` — Principle II ④ from `plan.md`).

No HTTP adapters, no DPAPI credential store, no registration dialog,
no refresh affordance. Those land in Phases 4 (US2) and 5 (US3).

## Tasks (T029..T039)

Mapped one-to-one to the task list in [`../tasks.md`](../tasks.md) lines
87..108 and to GitHub issues #36..#46:

- T029 — issue [#36](https://github.com/luca-veronelli-stem/button-panel-tester/issues/36):
  `src/ButtonPanelTester.Infrastructure/Persistence/JsonFileDictionaryCache.fs`
  per `contracts/cache-format.md` (atomic write, SHA-256 sidecar,
  `Failed(CacheAbsent | CacheUnreadable, …)` on missing-pair or mismatch,
  skip-write when content hash unchanged).
- T030 — issue [#37](https://github.com/luca-veronelli-stem/button-panel-tester/issues/37):
  `src/ButtonPanelTester.GUI/Assets/dictionary.seed.json` initial seed
  wired as `<EmbeddedResource />` (replaced by `eng/refresh-seed.ps1`
  at release time per T064 / Phase 6).
- T031 — issue [#38](https://github.com/luca-veronelli-stem/button-panel-tester/issues/38):
  `src/ButtonPanelTester.Infrastructure/Persistence/EmbeddedSeedExtractor.fs`
  per `research.md` R4 (reads the manifest resource via
  `Assembly.GetManifestResourceStream`, writes through
  `JsonFileDictionaryCache.WriteAsync`, no-op when cache already exists).
- T032 — issue [#39](https://github.com/luca-veronelli-stem/button-panel-tester/issues/39):
  `src/ButtonPanelTester.Services/Dictionary/DictionaryService.fs` —
  minimal `IDictionaryService` covering the offline path
  (`InitializeAsync` extract-if-missing then read, emits
  `SourceChanged Cached(t, FromEmbeddedSeed | FromLocalFile, None)`;
  `RefreshAsync` placeholder for US3).
- T033 — issue [#40](https://github.com/luca-veronelli-stem/button-panel-tester/issues/40):
  `src/ButtonPanelTester.GUI/Composition/CompositionRoot.fs` —
  Microsoft.Extensions.DI wiring per `contracts/ports.md` §Composition root
  with no-op `IDictionaryProvider` / `IRegistrationClient` so offline
  launch succeeds (real adapters land in T045 / US2 and T053 / US3).
- T034 — issue [#41](https://github.com/luca-veronelli-stem/button-panel-tester/issues/41):
  `src/ButtonPanelTester.GUI/Dictionary/DictionaryStatusRow.fs` —
  FuncUI view: colour-coded indicator pill, `Live · synced HH:MM` or
  `Cached · last synced <date>` headline, click-or-hover detail with
  cache path + `CacheOrigin` label + any `LastFailureReason`.
- T035 — issue [#42](https://github.com/luca-veronelli-stem/button-panel-tester/issues/42):
  `src/ButtonPanelTester.GUI/App.fs` + `src/ButtonPanelTester.GUI/Program.fs` —
  FuncUI app shell hosting the main window with status row docked at top;
  `Program.main` builds the host, starts Avalonia, calls
  `IDictionaryService.InitializeAsync` on window-loaded so the row paints
  populated (FR-004 / SC-001 / SC-002).
- T036 — issue [#43](https://github.com/luca-veronelli-stem/button-panel-tester/issues/43):
  `tests/ButtonPanelTester.Tests.Windows/Unit/JsonFileDictionaryCacheTests.fs` —
  read returns `Failed(CacheAbsent, …)` on missing; `Failed(CacheUnreadable, …)`
  on sidecar mismatch; round-trip preserves `ButtonPanelDictionary` +
  `FetchedAt`; torn-write recovery; `ExtractSeedIfMissingAsync` no-op when
  cache exists.
- T037 — issue [#44](https://github.com/luca-veronelli-stem/button-panel-tester/issues/44):
  `tests/ButtonPanelTester.Tests/Integration/DictionaryServiceInitializeTests.fs` —
  wires `DictionaryService` through `InMemoryDictionaryCache` + `FrozenClock`:
  (a) empty disk + available seed ⇒ `Cached(seedTime, FromEmbeddedSeed, None)`;
  (b) pre-existing cache ⇒ `Cached(t, FromLocalFile, None)`;
  (c) cache integrity failure ⇒ falls back to seed and emits
  `Cached(seedTime, FromEmbeddedSeed, Some CacheUnreadable)` (FR-019).
- T038 — issue [#45](https://github.com/luca-veronelli-stem/button-panel-tester/issues/45):
  `tests/ButtonPanelTester.Tests.Windows/Gui/DictionaryStatusRowTests.fs` —
  `Avalonia.Headless.XUnit` tests driving the FuncUI message loop: orange
  pill + `Cached · last synced …` headline on `Cached(_, FromEmbeddedSeed, None)`;
  detail text contains the literal "from embedded seed"; green pill +
  `Live · synced HH:MM` on `Live`.
- T039 — issue [#46](https://github.com/luca-veronelli-stem/button-panel-tester/issues/46):
  `tests/ButtonPanelTester.Tests/Property/DictionaryServiceTransitionsTests.fs` —
  FsCheck property: starting from any reachable `DictionarySource`, applying
  any `DictionaryFetchResult` lands in another reachable state per the Lean
  spec in T024 + T027 (covers `plan.md` Principle II ④
  "DictionaryServiceTransitions").

## Exit state

- `dotnet build -c Release` green across all six projects, 0 warnings.
- `dotnet test -c Release --no-build` green, ≥ 4 new passing test files
  added on top of the Phase 2 baseline (T036 Unit, T037 Integration,
  T038 GUI headless, T039 Property).
- `cd lean && lake build` green — the four Phase 1 theorems still
  elaborate with no `sorry` and no custom axioms (Constitution
  Principle I gate, no new Lean work in Phase 3).
- `dotnet run --project src/ButtonPanelTester.GUI` on a machine with
  no `%LOCALAPPDATA%\Stem.ButtonPanelTester\dictionary.json` and no
  network connectivity shows the main window with the status row
  populated as `Cached · last synced <seed date>` within 1 s of paint,
  and the detail affordance discloses "from embedded seed" (SC-001,
  SC-002, FR-004).

## Cross-TFM test placement

The two-TFM test split established in T005a/b/c per
[issue #76](https://github.com/luca-veronelli-stem/button-panel-tester/issues/76)
governs Phase 3 test placement:

- `tests/ButtonPanelTester.Tests/` (`net10.0`): T037 (service + in-memory
  cache fakes), T039 (property over Core types).
- `tests/ButtonPanelTester.Tests.Windows/` (`net10.0-windows`): T036
  (`JsonFileDictionaryCache` exercises the real file system through the
  Infrastructure project), T038 (`Avalonia.Headless.XUnit` binds to the
  GUI project).

The path strings in [`../tasks.md`](../tasks.md) reflect this split.
GitHub issues #43 and #45 still carry the pre-#76 paths in their bodies;
the work commits land at the corrected paths and supersede the issue text.

## Protocol customization

The `resolve-ticket` lifecycle for Phase 3 starts at `WorkRequired`. The
spec, plan, tasks, research, data model, contracts, and quickstart artifacts
for spec 001 already exist on `main`, ratified against constitution v1.0.2.
Re-running speckit-specify / speckit-plan / speckit-tasks per phase-PR
would rewrite shared feature artifacts six times and is forbidden.

The reviewer's per-commit consistency check is: the durable diff matches
the named T-task in `../tasks.md` and respects constitution Principles I–VI.

Batched amend policy (inherited from Phase 1): approved titles/bodies are
**not** mechanically amended on each `WorkApproved → WorkRequired`
transition. Worker appends each approved title+body+durable-sha to
`llm/reviews/approved-commits.yml`. Owner applies all approved messages in
one rewrite at `FinalizationRequired`. This keeps the durable-commit graph
stable across the run and contains the message-rewrite blast radius to a
single owner-side operation.

Quality gate `llm/reviews/gate.{ps1,sh}` is reused unchanged from Phase 2.
No new Lean elaboration is added in Phase 3, but `lake build` stays in the
gate so the Principle I check still fires on every handoff.

Commit cadence: one durable commit per T-task. Single feature branch
`feat/001-phase-3`. One PR encompassing all 11 commits, rebase-merged on
completion.
