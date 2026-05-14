# Phase 2 — Foundational (Blocking Prerequisites)

This document scopes the second phase-PR for spec 001 (dictionary fetch and
status display). It is the durable anchor for the Phase 2 work driven by the
`resolve-ticket-supervised` protocol.

## Scope

Domain substrate. F# types, ports, in-memory test adapters, FsCheck property
tests for the closed-enum invariants, and the four Lean Phase 1 theorems that
mechanise the cross-layer guarantees claimed in `plan.md` §Constitution Check
Principle I.

No HTTP adapters, no DPAPI, no GUI. Those land in Phases 3–5 (one per user
story). Phase 2 must finish before **any** user-story task starts.

## Tasks (T010..T028)

Mapped one-to-one to the task list in [`../tasks.md`](../tasks.md) lines
57..83 and to GitHub issues #17..#35:

- T010 — issue [#17](https://github.com/luca-veronelli-stem/button-panel-tester/issues/17):
  add `src/ButtonPanelTester.Core/Dictionary/ButtonPanelDictionary.fs`
  (`Variable`, `PanelType`, `ButtonPanelDictionary`); remove `Placeholder.fs`.
- T011 — issue [#18](https://github.com/luca-veronelli-stem/button-panel-tester/issues/18):
  add `src/ButtonPanelTester.Core/Dictionary/ContentHash.fs`
  (`compute : byte[] -> string`, lowercase-hex SHA-256).
- T012 — issue [#19](https://github.com/luca-veronelli-stem/button-panel-tester/issues/19):
  add `src/ButtonPanelTester.Core/Dictionary/DictionarySource.fs`
  (`CacheOrigin`, `DictionarySource`).
- T013 — issue [#20](https://github.com/luca-veronelli-stem/button-panel-tester/issues/20):
  add `src/ButtonPanelTester.Core/Dictionary/FetchFailureReason.fs`
  (eight closed cases, including `CacheAbsent` and `CacheUnreadable`).
  Positioned **before** `DictionarySource.fs` in fsproj `<Compile>` order.
- T014 — issue [#21](https://github.com/luca-veronelli-stem/button-panel-tester/issues/21):
  add `src/ButtonPanelTester.Core/Dictionary/DictionaryFetchResult.fs`
  (`Success | Failed` DU).
- T015 — issue [#22](https://github.com/luca-veronelli-stem/button-panel-tester/issues/22):
  add `src/ButtonPanelTester.Core/Dictionary/RegistrationTypes.fs`
  (`BootstrapToken`, `InstallationCredential`, `RegistrationError`).
- T016 — issue [#23](https://github.com/luca-veronelli-stem/button-panel-tester/issues/23):
  add `src/ButtonPanelTester.Core/Dictionary/Ports.fs` (five port interfaces).
- T017 — issue [#24](https://github.com/luca-veronelli-stem/button-panel-tester/issues/24):
  add `src/ButtonPanelTester.Services/Dictionary/IDictionaryService.fs`
  (`DictionaryStateUpdate`, `IDictionaryService`).
- T018 — issue [#25](https://github.com/luca-veronelli-stem/button-panel-tester/issues/25):
  add `src/ButtonPanelTester.Infrastructure/Clock.fs` (`SystemClock`).
- T019 — issue [#26](https://github.com/luca-veronelli-stem/button-panel-tester/issues/26):
  add `tests/ButtonPanelTester.Tests/Fakes/Wiring.fs`
  (five in-memory adapters).
- T020 — issue [#27](https://github.com/luca-veronelli-stem/button-panel-tester/issues/27):
  add `tests/ButtonPanelTester.Tests/Fixtures/DictionaryResolvedDto.json`
  sample payload.
- T021 — issue [#28](https://github.com/luca-veronelli-stem/button-panel-tester/issues/28):
  add `tests/ButtonPanelTester.Tests/Property/DictionarySerializationTests.fs`
  (FsCheck round-trip property).
- T022 — issue [#29](https://github.com/luca-veronelli-stem/button-panel-tester/issues/29):
  add `tests/ButtonPanelTester.Tests/Property/ContentHashTests.fs`
  (FsCheck hash properties).
- T023 — issue [#30](https://github.com/luca-veronelli-stem/button-panel-tester/issues/30):
  add `tests/ButtonPanelTester.Tests/Property/FetchFailureReasonClosureTests.fs`
  (FsCheck exhaustion property).
- T024 — issue [#31](https://github.com/luca-veronelli-stem/button-panel-tester/issues/31):
  add `lean/Stem/ButtonPanelTester/Phase1/DictionarySource.lean`;
  prove `source_data_preserved`.
- T025 — issue [#32](https://github.com/luca-veronelli-stem/button-panel-tester/issues/32):
  add `lean/Stem/ButtonPanelTester/Phase1/FetchFailureReason.lean`;
  prove `failure_reason_exhaustion`.
- T026 — issue [#33](https://github.com/luca-veronelli-stem/button-panel-tester/issues/33):
  add `lean/Stem/ButtonPanelTester/Phase1/DictionaryProvider.lean`;
  prove `provider_success_xor_failed`.
- T027 — issue [#34](https://github.com/luca-veronelli-stem/button-panel-tester/issues/34):
  add `lean/Stem/ButtonPanelTester/Phase1/CacheConsistency.lean`;
  prove `cache_memory_equal_post_first_success` (mechanises FR-010).
- T028 — issue [#35](https://github.com/luca-veronelli-stem/button-panel-tester/issues/35):
  delete `tests/ButtonPanelTester.Tests/PlaceholderTests.fs` and its
  `<Compile>` entry.

## Exit state

- `dotnet build -c Release` green across all six projects.
- `dotnet test` green with **≥ 3 passing property suites** (T021, T022, T023)
  and no `PlaceholderTests` remaining (T028 removed it).
- `cd lean && lake build` green — four theorems compile with **no `sorry`**
  and **no custom axioms** (Constitution Principle I gate).
- No behaviour change visible from the GUI shell (still no
  `DictionaryService` orchestration; that lands in T032/Phase 3).

## Protocol customization

The `resolve-ticket` lifecycle for Phase 2 starts at `WorkRequired`. The
spec, plan, tasks, research, data model, contracts, and quickstart artifacts
for spec 001 already exist on `main`, ratified against constitution v1.0.2.
Re-running speckit-specify / speckit-plan / speckit-tasks per phase-PR
rewrites shared feature artifacts and is forbidden.

The reviewer's per-commit consistency check is the same as Phase 1: the
durable diff matches the named T-task in `../tasks.md` and respects
constitution Principles I–VI.

### Within-phase ordering notes

- **F# `<Compile>` order**: `FetchFailureReason.fs` (T013) precedes
  `DictionarySource.fs` (T012) inside `ButtonPanelTester.Core.fsproj`
  despite the lower T-number on T012 — `DictionarySource` carries an
  `option<FetchFailureReason>` field. Both commits must build green
  individually per `bisect-safe.md`; one acceptable approach is to land
  T013 first as a self-contained file, then T012 referencing it.
- **Lean ⇄ F# enum width parity**: T013 (F# eight-case DU) and T025
  (Lean inductive) must agree on case names. If either changes, the
  other follows in the same commit or the next commit; never leave a
  mismatch on the branch tip.
- **Tests landing before producers**: T019 (in-memory fakes) and
  T021–T023 (property tests) depend on T010–T016 being present. The
  property tests are written **after** the types they exercise — there
  is no TDD-first ordering imposed on Phase 2 per `tasks.md` §"Within
  story dependencies".

### Commit cadence

One commit per T-task. Single feature branch `feat/001-phase-2`. One PR
encompassing all 19 commits, rebase-merged on completion. The PR body
lists `Closes #17, Closes #18, …, Closes #35` so the supervisor protocol's
finalization step closes the entire phase atomically.

Lean build is intentionally **not** gated on every commit — `lake build`
is slow and the four Lean tasks (T024–T027) carry it themselves. CI runs
it on PR and at the Phase-2 checkpoint. Locally prefer
`lean_diagnostic_messages` via the lean-lsp MCP for incremental feedback.
