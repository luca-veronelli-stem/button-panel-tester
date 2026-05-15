# Phase 2 — Foundational (Blocking Prerequisites)

This document scopes the second phase-PR for spec 001 (dictionary fetch and
status display). It is the durable anchor for the Phase 2 work driven by the
`resolve-ticket` (unsupervised) protocol.

## Scope

Domain substrate. F# types, ports, in-memory test adapters, FsCheck property
tests for the closed-enum invariants, and the four Lean Phase 1 theorems
that mechanise the cross-layer guarantees claimed in `plan.md` §Constitution
Check Principle I.

No HTTP adapters, no DPAPI, no GUI behaviour. Those land in Phases 3–5 (one
per user story). Phase 2 must finish before **any** user-story task starts.

## Tasks (T010..T028)

Mapped one-to-one to the task list in [`../tasks.md`](../tasks.md) lines
57..83 and to GitHub issues #17..#35:

- T010 → issue [#17](https://github.com/luca-veronelli-stem/button-panel-tester/issues/17):
  `src/ButtonPanelTester.Core/Dictionary/ButtonPanelDictionary.fs`.
- T011 → issue [#18](https://github.com/luca-veronelli-stem/button-panel-tester/issues/18):
  `src/ButtonPanelTester.Core/Dictionary/ContentHash.fs`.
- T012 → issue [#19](https://github.com/luca-veronelli-stem/button-panel-tester/issues/19):
  `src/ButtonPanelTester.Core/Dictionary/DictionarySource.fs`.
- T013 → issue [#20](https://github.com/luca-veronelli-stem/button-panel-tester/issues/20):
  `src/ButtonPanelTester.Core/Dictionary/FetchFailureReason.fs`
  (eight cases incl. `CacheAbsent`, `CacheUnreadable`; precedes T012 in fsproj).
- T014 → issue [#21](https://github.com/luca-veronelli-stem/button-panel-tester/issues/21):
  `src/ButtonPanelTester.Core/Dictionary/DictionaryFetchResult.fs`.
- T015 → issue [#22](https://github.com/luca-veronelli-stem/button-panel-tester/issues/22):
  `src/ButtonPanelTester.Core/Dictionary/RegistrationTypes.fs`.
- T016 → issue [#23](https://github.com/luca-veronelli-stem/button-panel-tester/issues/23):
  `src/ButtonPanelTester.Core/Dictionary/Ports.fs` (five port interfaces).
- T017 → issue [#24](https://github.com/luca-veronelli-stem/button-panel-tester/issues/24):
  `src/ButtonPanelTester.Services/Dictionary/IDictionaryService.fs`.
- T018 → issue [#25](https://github.com/luca-veronelli-stem/button-panel-tester/issues/25):
  `src/ButtonPanelTester.Infrastructure/Clock.fs` (`SystemClock`).
- T019 → issue [#26](https://github.com/luca-veronelli-stem/button-panel-tester/issues/26):
  `tests/ButtonPanelTester.Tests/Fakes/Wiring.fs` (five in-memory adapters).
- T020 → issue [#27](https://github.com/luca-veronelli-stem/button-panel-tester/issues/27):
  `tests/ButtonPanelTester.Tests/Fixtures/DictionaryResolvedDto.json`.
- T021 → issue [#28](https://github.com/luca-veronelli-stem/button-panel-tester/issues/28):
  `tests/ButtonPanelTester.Tests/Property/DictionarySerializationTests.fs`.
- T022 → issue [#29](https://github.com/luca-veronelli-stem/button-panel-tester/issues/29):
  `tests/ButtonPanelTester.Tests/Property/ContentHashTests.fs`.
- T023 → issue [#30](https://github.com/luca-veronelli-stem/button-panel-tester/issues/30):
  `tests/ButtonPanelTester.Tests/Property/FetchFailureReasonClosureTests.fs`.
- T024 → issue [#31](https://github.com/luca-veronelli-stem/button-panel-tester/issues/31):
  `lean/Stem/ButtonPanelTester/Phase1/DictionarySource.lean` —
  `source_data_preserved`.
- T025 → issue [#32](https://github.com/luca-veronelli-stem/button-panel-tester/issues/32):
  `lean/Stem/ButtonPanelTester/Phase1/FetchFailureReason.lean` —
  `failure_reason_exhaustion`.
- T026 → issue [#33](https://github.com/luca-veronelli-stem/button-panel-tester/issues/33):
  `lean/Stem/ButtonPanelTester/Phase1/DictionaryProvider.lean` —
  `provider_success_xor_failed`.
- T027 → issue [#34](https://github.com/luca-veronelli-stem/button-panel-tester/issues/34):
  `lean/Stem/ButtonPanelTester/Phase1/CacheConsistency.lean` —
  `cache_memory_equal_post_first_success` (mechanises FR-010).
- T028 → issue [#35](https://github.com/luca-veronelli-stem/button-panel-tester/issues/35):
  delete `tests/ButtonPanelTester.Tests/PlaceholderTests.fs` + its
  `<Compile>` entry; delete `src/ButtonPanelTester.Core/Placeholder.fs` +
  its `<Compile>` entry. Atomic cleanup — T010 leaves both alive for
  bisect-safety (`PlaceholderTests.fs` references `Placeholder.markerVersion`).

## Exit state

- `dotnet build -c Release` green across all six projects.
- `dotnet test` green with **≥ 3 passing property suites** (T021–T023); no
  `PlaceholderTests` remaining (T028 removed it).
- `cd lean && lake build` green — four theorems compile with **no `sorry`**
  and **no custom axioms** (Constitution Principle I gate).
- No behaviour change visible from the GUI shell. `DictionaryService`
  orchestration lands in T032/Phase 3, not here.

## Protocol customization

The `resolve-ticket` lifecycle for Phase 2 starts at `WorkRequired`. The
spec, plan, tasks, research, data model, contracts, and quickstart
artifacts for spec 001 already exist on `main`, ratified against
constitution v1.0.2. Re-running speckit-specify / speckit-plan /
speckit-tasks per phase-PR rewrites shared feature artifacts and is
forbidden.

**Reviewer's per-commit consistency check** (same as Phase 1): the durable
diff matches the named T-task in `../tasks.md` and respects Constitution
Principles I–VI.

**Batched amend policy** (inherited from Phase 1): approved
titles/bodies are **not** mechanically amended on each `WorkApproved →
WorkRequired` transition. Worker appends each approved
title+body+durable-sha to `llm/reviews/approved-commits.yml`. Owner
applies all approved messages in one rewrite at `FinalizationRequired`.
This keeps the durable-commit graph stable across the run and contains
the message-rewrite blast radius to a single owner-side operation.

**Within-phase ordering**:
- F# `<Compile>` order: `FetchFailureReason.fs` (T013) precedes
  `DictionarySource.fs` (T012) inside `ButtonPanelTester.Core.fsproj`
  despite the lower T-number on T012 — `DictionarySource` carries an
  `option<FetchFailureReason>` field.
- Lean ⇄ F# enum width parity: T013 (F# eight-case DU) and T025 (Lean
  inductive) must agree on case names. The reviewer cross-checks both
  files at T025 review.
- Tests landing after producers: T019 (fakes) and T021–T023 (property
  tests) follow T010–T016. No TDD-first ordering imposed on Phase 2
  per `tasks.md` §"Within story dependencies".

**Commit cadence**: one durable commit per T-task. Single feature branch
`feat/001-phase-2`. One PR encompassing all 19 commits, rebase-merged on
completion. Lean build is **not** gated on every commit — `lake build`
is slow; the four Lean tasks (T024–T027) carry it themselves. CI runs it
on PR and at the Phase-2 checkpoint.
