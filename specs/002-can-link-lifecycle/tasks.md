---

description: "Task list for feat/002-can-link-lifecycle"
---

# Tasks: CAN Link Lifecycle

**Input**: Design documents from `specs/002-can-link-lifecycle/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md).

**Scope note (#151, 2026-05-26)**: panel-discovery work (former Phase 4, T044–T055) moved to [`specs/003-panel-discovery/tasks.md`](../003-panel-discovery/tasks.md). This task list covers lifecycle Phases 1–3 (shared/lifecycle foundation), 3.5 (lifecycle fix queue), 5 (US3 mid-session unplug), and the lifecycle half of Phase N polish. Phases 1–2 task IDs (T001–T033) are preserved as-is for traceability; the panel-discovery subset of these tasks is the same physical work and is also referenced from spec-003's task list (the foundation cohabits one F# project, one Lean lib).

**Tests**: REQUIRED. The plan's Constitution Check (Principles II + IV) mandates FsCheck property tests, integration tests against virtual adapters (`InMemoryCanLink`), GUI tests via `Avalonia.Headless`, Lean Phase 2 proofs, and a hardware E2E suite gated as `[<Trait("Category", "Hardware")>]` (excluded from default CI, tracked by [#112](https://github.com/luca-veronelli-stem/button-panel-tester/issues/112)).

**Organization**: Tasks group by user story (US1 — CAN status row + reconnect; US3 — surviving mid-session unplug). Phases 1–2 are shared scaffolding and foundational types (shared with spec-003). Each user story phase ends with a checkpoint where that story is independently testable per spec.md's "Independent Test" criterion.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel — different `.fsproj` projects or independent files outside the F# compile graph (Lean, JSON fixtures, docs).
- **[Story]**: User story this task serves (US1, US3). Setup/Foundational/Polish carry no story label.
- File paths are absolute from repo root.

## Path Conventions

Archetype A, two-TFM split (see `plan.md` §Project Structure):

- `src/ButtonPanelTester.Core/` — `net10.0` F# domain + ports (extended with `Can/`)
- `src/ButtonPanelTester.Services/` — `net10.0` F# use cases (extended with `Can/`)
- `src/ButtonPanelTester.Infrastructure.Protocol/` — `net10.0-windows` **C#**, vendor copy (shared with spec-003)
- `src/ButtonPanelTester.Infrastructure/` — `net10.0-windows` F# adapters (extended with `Can/`)
- `src/ButtonPanelTester.GUI/` — `net10.0-windows` F# Avalonia + FuncUI shell (extended with `Can/`)
- `tests/ButtonPanelTester.Tests/` — `net10.0` F# xUnit + FsCheck
- `tests/ButtonPanelTester.Tests.Windows/` — `net10.0-windows` F# Avalonia.Headless + Infrastructure tests
- `lean/Stem/ButtonPanelTester/Phase2/` — Lean 4 Phase 2 modules (shared with spec-003)

---

## Phase 1: Setup (Shared Infrastructure — with spec-003)

**Purpose**: scaffold the new C# vendor project, vendor the protocol stack, and extend the existing solution + test partitioning so every later task lands on a compiling solution.

- [X] T001 Add `Peak.PCANBasic.NET` to `Directory.Packages.props` — transitive dependency of the vendored stack.
- [X] T002 Create `eng/vendor-protocol-stack.ps1` — one-shot vendoring helper.
- [X] T003 Create `src/ButtonPanelTester.Infrastructure.Protocol/ButtonPanelTester.Infrastructure.Protocol.csproj`.
- [X] T004 Run `eng/vendor-protocol-stack.ps1`; commit the result as one bisect-safe vendor commit.
- [X] T005 Add the local modification to `PCANManager.cs` (CancellationTokenSource + IAsyncDisposable) and record in `VENDOR.md`.
- [X] T006 Update `Stem.ButtonPanelTester.slnx` to register `ButtonPanelTester.Infrastructure.Protocol`.
- [X] T007 Extend `ButtonPanelTester.Infrastructure.fsproj` — add ProjectReference to `Infrastructure.Protocol`.
- [X] T008 [P] Create empty folders for test partitioning under `tests/ButtonPanelTester.Tests/`.
- [X] T009 [P] Create empty folders for test partitioning under `tests/ButtonPanelTester.Tests.Windows/`.
- [X] T010 [P] Extend `lean/lakefile.toml` — add `[[lean_lib]]` for `Stem.ButtonPanelTester.Phase2`.
- [X] T011 [P] Add `src/ButtonPanelTester.Infrastructure/Can/VENDOR-GUARD.md` README pointing at the vendor-manifest discipline.

**Checkpoint**: solution restores and builds green; `dotnet test --filter "Category!=Hardware"` still passes; `lake build` builds both Phase 1 and Phase 2 (the latter has no modules yet so the lib target is empty). No behaviour change yet. `VENDOR.sha256` hash check passes (single vendor commit).

---

## Phase 2: Foundational (Blocking Prerequisites — shared with spec-003)

**Purpose**: F# Core domain types + ports, virtual fakes, per-type FsCheck properties, the six Lean Phase 2 modules, and fixture data. All three user stories depend on this layer.

**Critical**: no user-story task may start until Phase 2 is complete and `dotnet test --filter "Category!=Hardware"` + `lake build` are both green.

Tasks marked **(spec-003 also)** are shared foundation: the file lives in this repo's source tree once but is logically owned by both specs (one Core/Services project, one Lean lib). Task IDs preserved for historical traceability.

- [X] T012 [P] Add `src/ButtonPanelTester.Core/Can/CanLinkState.fs` — `DisconnectReason`, `ErrorClassification`, `CanLinkState`, `AdapterIdentification` per [data-model.md](./data-model.md) §1.1 + §2.1.
- [X] T013 [P] **(spec-003 also)** Add `src/ButtonPanelTester.Core/Can/WhoIAmFrame.fs`.
- [X] T014 [P] **(spec-003 also)** Add `src/ButtonPanelTester.Core/Can/PanelObservation.fs`.
- [X] T015 [P] **(spec-003 also)** Add `src/ButtonPanelTester.Core/Can/PanelsOnBus.fs`.
- [X] T016 [P] **(spec-003 also)** Add `src/ButtonPanelTester.Core/Can/Pruning.fs`.
- [X] T017 Add `src/ButtonPanelTester.Core/Can/Ports.fs` — `RawCanFrame` (struct, spec-003), `ICanLink` (this spec), `ICanFrameStream` (spec-003).
- [X] T018 Add `src/ButtonPanelTester.Services/Can/ICanLinkService.fs` — interface covering lifecycle + discovery surface.
- [X] T019 [P] Add `tests/ButtonPanelTester.Tests/Fakes/Can/InMemoryCanLink.fs` (lifecycle virtual adapter).
- [X] T020 [P] **(spec-003 also)** Add `tests/ButtonPanelTester.Tests/Fakes/Can/InMemoryCanFrameStream.fs`.
- [X] T021 [P] **(spec-003 also)** Add `tests/ButtonPanelTester.Tests/Fixtures/Can/whoIAmFixtures.json`.
- [X] T022 [P] **(spec-003 also)** Add `WhoIAmFrameProperties.fs` FsCheck properties.
- [X] T023 [P] **(spec-003 also)** Add `VariantDecoderProperties.fs` FsCheck properties.
- [X] T024 [P] **(spec-003 also)** Add `PanelsOnBusProperties.fs` FsCheck properties.
- [X] T025 [P] **(spec-003 also)** Add `PruningProperties.fs` FsCheck properties.
- [X] T026 [P] Add `CanLinkStateTransitionsProperties.fs` FsCheck property (lifecycle).
- [X] T027 [P] Add `lean/Stem/ButtonPanelTester/Phase2/CanLinkState.lean` — lifecycle theorems (`state_classification_total`, `transition_reachability_closed`).
- [X] T028 [P] **(spec-003 also)** Add `Phase2/WhoIAmFrame.lean` — `parse_encode_roundtrip`.
- [X] T029 [P] **(spec-003 also)** Add `Phase2/PanelObservation.lean` — `variant_decoding_total`.
- [X] T030 [P] **(spec-003 also)** Add `Phase2/PanelsOnBus.lean` — `observe_coalesces_by_uuid`.
- [X] T031 [P] **(spec-003 also)** Add `Phase2/Pruning.lean` — `prune_partitions_by_threshold`.
- [X] T032 [P] Add `Phase2/PassiveObserver.lean` — `observe_emits_no_transmit` (lifecycle, mechanises SC-007 + FR-014).
- [X] T033 **(spec-003 also)** Add `tests/ButtonPanelTester.Tests/Unit/Can/WhoIAmFrameFixtureTests.fs`.

**Checkpoint**: `dotnet build -c Release` green; `dotnet test --filter "Category!=Hardware"` green with ≥ 5 new property suites + 1 new unit suite passing; `lake build` builds both Phase 1 and Phase 2 (six new theorems compile with no `sorry`). Working tree is the design substrate for all user stories.

---

## Phase 3: User Story 1 — CAN link state at start of shift (Priority: P1) 🎯 MVP

**Goal**: technician launches the tool on a freshly-installed machine with no PEAK adapter present. After dictionary boot completes, within 1 second they see a CAN status row with a `Disconnected` headline naming "no PEAK adapter found". Plugging the adapter in and clicking reconnect flips the headline to `Connected` within 2 seconds.

**Independent Test** (from `spec.md` §US1): launch the tool on a freshly-installed machine with no PEAK adapter present. Verify the CAN status row appears within 1 second of the main window and carries a Disconnected headline with a friendly remediation hint. Plug the adapter in, click reconnect, verify the headline flips to Connected within 2 seconds.

### Implementation for User Story 1

- [X] T034 [US1] Add `src/ButtonPanelTester.Infrastructure/Can/PcanAdapterIdentity.fs` — `tryRead` helper for `AdapterIdentification`.
- [X] T035 [US1] Add `src/ButtonPanelTester.Infrastructure/Can/PcanCanLink.fs` — `PcanCanLink` implementing `ICanLink`.
- [X] T036 [US1] Add `src/ButtonPanelTester.Services/Can/CanLinkService.fs` — lifecycle slice (US2 observation pipeline lands later via spec-003).
- [X] T037 [US1] Extend `src/ButtonPanelTester.GUI/Composition/CompositionRoot.fs` — register `ICanLink = PcanCanLink`, `ICanLinkService = CanLinkService`. `ICanFrameStream` bound to no-op (replaced by spec-003).
- [X] T038 [US1] Add `src/ButtonPanelTester.GUI/Can/CanStatusRow.fs` — chip + reconnect button + detail affordance.
- [X] T039 [US1] Extend `src/ButtonPanelTester.GUI/App.fs` so the main window hosts the vertical-stack panel (third slot reserved for spec-003's PanelsOnBusView).

### Tests for User Story 1

- [X] T040 [P] [US1] Add `tests/ButtonPanelTester.Tests/Integration/Can/CanLinkServiceLifecycleTests.fs`.
- [X] T040b [US1] Boot-order negative test for FR-001 → shipped via PR [#133](https://github.com/luca-veronelli-stem/button-panel-tester/pull/133).
- [X] T041 [P] [US1] Add `tests/ButtonPanelTester.Tests/Integration/Can/RecoverableToFatalEscalationTests.fs`.
- [X] T042 [P] [US1] Add `tests/ButtonPanelTester.Tests.Windows/Gui/Can/CanStatusRowTests.fs`.
- [X] T043 [P] [US1] Add `tests/ButtonPanelTester.Tests.Windows/Integration/Can/Hardware/PcanLifecycleTests.fs`.

**Checkpoint (US1)**: `dotnet run --project src/ButtonPanelTester.GUI` on a machine with no PEAK adapter shows the main window with `DictionaryStatusRow` populated (feat-001) AND the CAN status row populated as `Disconnected · no PEAK adapter found` within 1 s of paint, with a working Reconnect button. Plugging the adapter in + clicking Reconnect flips to `Connected` within 2 s. `dotnet test --filter "Category!=Hardware"` adds ≥ 3 passing test files. US1 is independently demoable.

---

## Phase 3.5: Post-PR-C Amendments (lifecycle fix queue)

**Purpose**: bench-feedback amendments that surfaced after PR-C [#122](https://github.com/luca-veronelli-stem/button-panel-tester/pull/122) shipped to `main`. Each is a one-PR slice and a vertical commit (TDD: RED on a regression test, GREEN on the implementation, REFACTOR if needed). All amendments below are **lifecycle-only**; panel-discovery amendments (if any arise after spec-003 work starts) live in spec-003's tasks.md.

- [X] T-amend-1 (FR-001, was T040b deferral) — extract `BootSequence.fs` with call-order spy test. PR [#133](https://github.com/luca-veronelli-stem/button-panel-tester/pull/133).
- [X] T-amend-2 (FR-001 / cold-start hang) — `CanPort` ctor IsConnected snapshot drop. PR [#134](https://github.com/luca-veronelli-stem/button-panel-tester/pull/134).
- [X] T-amend-3 (FR-004 / unexpected PEAK status) — `PeakErrorText.fs`. PR [#135](https://github.com/luca-veronelli-stem/button-panel-tester/pull/135).
- [X] T-amend-4 (FR-002b sticky `since`) — escalation tracker stores `(string * DateTimeOffset) option`. PR [#138](https://github.com/luca-veronelli-stem/button-panel-tester/pull/138).
- [X] T-amend-5 (FR-003 click-feedback contract) — `ReconnectAsync` synthesises `Disconnected(ReconnectPending, _)`. PR [#141](https://github.com/luca-veronelli-stem/button-panel-tester/pull/141).
- [X] T-amend-6 (FR-003 visibility table) — `CanStatusRow.shouldShowReconnectButton`. PR [#145](https://github.com/luca-veronelli-stem/button-panel-tester/pull/145).
- [X] T-amend-7 (FR-002a severity in headline) — render Recoverable/Fatal prefix in Error chip headline. PR [#147](https://github.com/luca-veronelli-stem/button-panel-tester/pull/147).
- [ ] T-amend-8 (Edge case cold-start poll-exhaust) — reclassify cold-start poll-exhaust from `Error(Recoverable, _)` to `Disconnected(NoAdapterPresent, detail)`, widening the `Disconnected` arity. Cascades into `lean/Stem/ButtonPanelTester/Phase2/CanLinkState.lean` (re-parametrise the `Disconnected` constructor and re-prove `state_classification_total` + `transition_reachability_closed`); FR-005 wording carries the same parametrisation. Issue [#136](https://github.com/luca-veronelli-stem/button-panel-tester/issues/136).
- [ ] T-amend-9 [P] (Phase 3 hardware suite gate) — env-gated `[<HardwareFact>]` xUnit attribute. Issue [#142](https://github.com/luca-veronelli-stem/button-panel-tester/issues/142).

**Carry-overs (tracked, NOT gating Phase 5):**

- [ ] T-amend-10 [P] — hot-plug auto-reconnect regression test (depends on [#111](https://github.com/luca-veronelli-stem/button-panel-tester/issues/111)). Issue [#132](https://github.com/luca-veronelli-stem/button-panel-tester/issues/132).
- [ ] T-amend-11 [P] — GUI tooltip test asserting `CanStatusRow` detail renders the `since` / opened timestamp. Issue [#140](https://github.com/luca-veronelli-stem/button-panel-tester/issues/140).
- [ ] T-amend-12 (C4 from PR-C audit) — render a driver-download remediation link inside the `Error · Fatal · "PEAK PCANBasic native DLL not found …"` chip. Issue [#143](https://github.com/luca-veronelli-stem/button-panel-tester/issues/143).

**Checkpoint (Phase 3.5)**: open amendments above are closed; `dotnet test --filter "Category!=Hardware"` green; `lake build` re-greens (T-amend-8 invalidates Phase 2 proofs that must be re-proven). At this point Phase 5 (US3) may start.

---

## Phase 5: User Story 3 — Surviving an adapter unplug mid-session (Priority: P3)

**Goal**: mid-session, the technician bumps the PEAK adapter loose. Within 5 seconds the CAN status row flips to `Disconnected`, the dictionary status row stays untouched, and re-seating + clicking Reconnect resumes operation. Downstream consumers (spec-003's Panels-on-bus list) react via `LinkStateChanged` independently.

**Independent Test** (from `spec.md` §US3): with the tool Connected, physically unplug the PEAK adapter. Verify the status row flips to Disconnected within 5 seconds with a "link lost" reason and the dictionary status row is unchanged. Re-plug the adapter and click reconnect — verify the link recovers.

### Implementation for User Story 3

- [ ] T056 [US3] Verify `src/ButtonPanelTester.Infrastructure/Can/PcanCanLink.fs` (T035) translates the vendored `StateChanged` "device removed" path to `Disconnected(MidSessionUnplug, now)` (and not to `Error`). If T035 emitted `Error` for this case, fix it here.
- [ ] T057 [US3] Verify `src/ButtonPanelTester.GUI/Can/CanStatusRow.fs` (T038) renders the `Disconnected(MidSessionUnplug, _)` headline as `Disconnected · link lost — replug adapter` (distinct from the `NoAdapterPresent` headline per FR-005). If T038 used a generic Disconnected headline, refine it here.

### Tests for User Story 3

- [ ] T058 [P] [US3] Add `tests/ButtonPanelTester.Tests/Integration/Can/MidSessionUnplugTests.fs` — scripts `[Connected adapter; Disconnected(MidSessionUnplug)]`. Asserts `LinkStateChanged` emits `Disconnected(MidSessionUnplug, _)` within the SC-005 budget. (Spec-003's list-clear assertion lives in spec-003's tests.)
- [ ] T059 [P] [US3] Add `tests/ButtonPanelTester.Tests/Integration/Can/DictionaryIndependenceTests.fs` — `IDictionaryService.SourceChanged` emits zero events during the CAN-side disconnect (FR-016 + SC-006).
- [ ] T060 [P] [US3] Add `tests/ButtonPanelTester.Tests.Windows/Gui/Can/CanStatusRowMidSessionUnplugTests.fs` — row renders `Disconnected · link lost — replug adapter`; Reconnect button visible; dictionary status row unchanged.
- [ ] T061 [US3] Add hardware unplug case to `tests/ButtonPanelTester.Tests.Windows/Integration/Can/Hardware/PcanLifecycleTests.fs` (or split into a dedicated `UnplugReplugCycleHardwareTests.fs`). `[<Trait("Category", "Hardware")>]`. Cases: Connected → physical unplug → `Disconnected(MidSessionUnplug)` within 5 s; replug + click `ReconnectAsync` → `Connected` within 2 s.

**Checkpoint (US3)**: on the bench, unplugging mid-session flips the CAN row to `Disconnected · link lost — replug adapter` within 5 s; dictionary row is untouched; replug + Reconnect restores the link. `dotnet test --filter "Category!=Hardware"` adds ≥ 3 passing test files. Lifecycle US1 + US3 are both independently demoable.

---

## Phase N: Polish & Cross-Cutting Concerns (lifecycle slice)

**Purpose**: cleanup, docs, audits, and end-to-end validation for the lifecycle. Discovery-side polish lives in spec-003's tasks.md.

- [ ] T062 [P] Add the feat/002 entry to `CHANGELOG.md` under `[Unreleased]` — one line summarising "CAN link lifecycle: status row + vendored protocol stack".
- [ ] T063 [P] Update `README.md` — link to `specs/002-can-link-lifecycle/quickstart.md`; mention the CAN status row + vendored `Infrastructure.Protocol` project.
- [ ] T064 [P] Add XML doc comments to every public type and member listed in [data-model.md](./data-model.md) §1–§2 per the COMMENTS standard — `DisconnectReason`, `ErrorClassification`, `CanLinkState`, `AdapterIdentification`, `ICanLink`, `ICanLinkService`.
- [ ] T065 [P] Logging audit per LOGGING standard for `PcanCanLink` / `PeakErrorText` / `CanLinkService` lifecycle paths.
- [ ] T066 [P] Async-discipline audit per CANCELLATION + THREAD_SAFETY standards for lifecycle paths.
- [ ] T067 [P] Compliance check for Principle V on the lifecycle path: grep for OS user / machine name / MAC / SID — expected zero hits. Document in `quickstart.md` "Common gotchas" tail.
- [ ] T068 [P] Update `docs/Context/bpt-rollout/CORRECTIONS.md` §C5 with a closing paragraph scoping the hardcoded-protocol-metadata stopgap to spec-003+ (per [research.md](./research.md) decisions migrated to spec-003).
- [ ] T069 End-to-end validation: walk `quickstart.md` §"Expected behaviour on a clean bench" steps 1, 2, 4, 5 on a real bench; verify SC-001, SC-002, SC-005, SC-008. (Step 3 — panel observation — is spec-003's validation.) SC-007 verified separately in T070.
- [ ] T070 SC-007 verification with an external bus capture. Confirm zero frames originating from the tool. Attach the trace summary to the PR.
- [ ] T071 `cd lean && lake build` — confirm lifecycle Phase 2 theorems compile with no `sorry` and no custom axioms.
- [ ] T072 [P] Confirm `VENDOR.sha256` hash check still passes — no silent edits crept into the vendor copy.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: T001 → T002 → T003 → T004 → T005 → T006 / T007. T008–T011 parallel after T006.
- **Foundational (Phase 2)**: depends on Phase 1 complete. T012–T016 form an ordered chain; T017 depends on T012; T018 depends on T017; T019–T020 depend on T017; T021 standalone; T022–T026 each depend on their corresponding Core file; T027–T032 [P] (independent Lean files); T033 depends on T021 + T013.
- **User Story 1 (Phase 3)**: depends on Phase 2 complete. T034 → T035 → T036 → T037 → T038 → T039; tests T040–T043 parallel after T039.
- **User Story 3 (Phase 5)**: depends on Phase 3 complete. T056 → T057; tests T058–T061 parallel after T057.
- **Polish (Phase N)**: depends on lifecycle user stories. T062–T068 [P]; T069 sequential after the user stories; T070 sequential after T069; T071 + T072 [P].

### Within Each User Story

- Tests REQUIRED (Principle II + IV); each user-story phase ends with property + integration + GUI + hardware test files for that story's surface.
- Ports + service before adapter implementations before composition root before GUI views.
- Hardware tests (`Category=Hardware`) excluded from default CI; tracked by [#112](https://github.com/luca-veronelli-stem/button-panel-tester/issues/112).

### Parallel Opportunities

- Phase 1: T008, T009, T010, T011 — all `[P]`.
- Phase 2: T012–T016 sequential (chain); T017–T018 sequential; T019–T026 [P] after their preconditions; T027–T032 [P] (Lean independent).
- Phase 3: T040, T041, T042, T043 all `[P]` after T039.
- Phase 5: T058, T059, T060 all `[P]` after T057; T061 sequential (hardware).

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (vendor the C# stack, extend solution).
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories; six Lean theorems + property suites must be green).
3. Complete Phase 3: User Story 1 (CAN status row + reconnect).
4. **STOP and VALIDATE**: walk `spec.md` §US1 "Independent Test" end-to-end.
5. Deploy if ready (status-row visibility alone is supplier-valuable confirmation that the tool can talk to its CAN side at all).

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. Add US1 → test independently → demo (MVP).
3. Add US3 → test independently → demo (robustness).
4. Polish → ship the spec-002 PR.

Panel-discovery (US2 / former Phase 4) and spec-003 polish ship under [`specs/003-panel-discovery/`](../003-panel-discovery/).

---

## Notes

- `[P]` tasks = different files, no dependencies.
- `[Story]` label maps task to specific user story for traceability.
- Each user story should be independently completable and testable.
- Verify tests fail before implementing (Principle I+II: Lean spec → test → implementation).
- Commit after each task or logical group (bisect-safe per the global rule).
- Stop at any checkpoint to validate story independently.
- Vendoring (T004) is one atomic commit; future re-vendoring follows [contracts/vendor-manifest.md](./contracts/vendor-manifest.md) §"Re-vendoring procedure" — never fix-in-place.
