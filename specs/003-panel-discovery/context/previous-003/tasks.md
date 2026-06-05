---

description: "Task list for feat/003-panel-discovery"
---

# Tasks: Panel Discovery via Passive WHO_I_AM Observation

**Input**: Design documents from `specs/003-panel-discovery/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md). **Phases 1–2 of [`../002-can-link-lifecycle/tasks.md`](../002-can-link-lifecycle/tasks.md) must be complete** (shared foundation already shipped via PR-A [#120](https://github.com/luca-veronelli-stem/button-panel-tester/pull/120) and PR-B [#121](https://github.com/luca-veronelli-stem/button-panel-tester/pull/121)).

**Tests**: REQUIRED. Per spec-002's Constitution Check (Principles II + IV), inherited unchanged.

**Numbering**: tasks below are renumbered from the historical T044–T055 (spec-002 Phase 4 + the discovery slice of Phase N) to start at T001 for this spec. Historical numbers preserved in parentheses for traceability with PR descriptions and issue bodies that already cite them.

---

## Phase 4 (this spec's Phase 1): User Story — Pristine panels announce themselves

**Goal**: with the CAN link Connected (spec-002) and a single pristine virgin panel powered on the bus, within 6 seconds a row appears in the Panels-on-bus list carrying the panel's UUID, the label "virgin", and a recent last-seen timestamp. Re-broadcasts update the row in place; pruning removes rows after 15 s of silence.

**Independent Test** (from `spec.md` §US): with the tool running, the adapter Connected, and the bus empty, power on the single pristine virgin panel on the bench. Verify a single row appears in the Panels-on-bus list within 6 seconds.

### Implementation

- [ ] T001 (was T044) Add `src/ButtonPanelTester.Infrastructure/Can/PcanCanFrameStream.fs` — `PcanCanFrameStream` implementing `ICanFrameStream` per [contracts/can-frame-stream-port.md](./contracts/can-frame-stream-port.md): constructor takes the vendored `CanPort` + `ILogger<PcanCanFrameStream>`; subscribes to `PacketReceived`; translates `CANPacketEventArgs` → `RawCanFrame` (CanId from `ArbitrationId`, `Payload` wrapped as `ReadOnlyMemory<byte>` from `Data`, `ReceivedAt` from `Timestamp`); hand-rolled `IObservable<RawCanFrame>` subject.
- [ ] T002 (was T045) Extend `src/ButtonPanelTester.Services/Can/CanLinkService.fs` — add the observation pipeline: constructor now also takes `ICanFrameStream`; on `Connected`, subscribes to `RawFramesReceived`, filters for `CanId = 0x1FFFFFFF AND Payload.Length = 15`, parses via `WhoIAmFrame.parse`, and on `Some f` calls `observe (IClock.UtcNow()) f` on the current `PanelsOnBus`. Hand-rolled `IObservable<PanelsOnBus>` for `PanelsOnBusChanged`. `parse` returning `None` is a silent drop per FR-013.
- [ ] T003 (was T046) Extend `src/ButtonPanelTester.Services/Can/CanLinkService.fs` — add the 1 s pruning timer per [`../002-can-link-lifecycle/research.md`](../002-can-link-lifecycle/research.md) R5: `System.Threading.Timer` tick at 1 s; on each tick calls `prune (TimeSpan.FromSeconds 15.0) (IClock.UtcNow())` and emits the new `PanelsOnBus` through `PanelsOnBusChanged` if it differs. Timer started when state enters `Connected`, stopped on dispose.
- [ ] T004 (was T047) Extend `src/ButtonPanelTester.Services/Can/CanLinkService.fs` — wire the FR-015' link-loss clear (consumer of spec-002 FR-015): on `LinkStateChanged` transitioning from `Connected` to any non-`Connected` state, call `clear` on the current `PanelsOnBus` and emit `empty` through `PanelsOnBusChanged`.
- [ ] T005 (was T048) Extend `src/ButtonPanelTester.GUI/Composition/CompositionRoot.fs` — replace the spec-002 no-op `ICanFrameStream` binding with `PcanCanFrameStream` (constructed from the same `CanPort` instance shared with `PcanCanLink`); update `CanLinkService` registration to inject both ports.
- [ ] T006 (was T049) Add `src/ButtonPanelTester.GUI/Can/PanelsOnBusView.fs` — FuncUI view rendering the live `PanelsOnBus`. Each row shows UUID hex format, decoded variant label, last-seen timestamp. Empty-state explainer (FR-012): when list is empty AND `CurrentState = Connected`, render "Bus is up but nothing is announcing itself"; when empty AND not Connected, render "No CAN link".
- [ ] T007 (was T050) Extend `src/ButtonPanelTester.GUI/App.fs` — fill the third slot of the vertical stack with `PanelsOnBusView` so the layout `DictionaryStatusRow / CanStatusRow / PanelsOnBusView` matches [`../002-can-link-lifecycle/research.md`](../002-can-link-lifecycle/research.md) R9.

### Tests

- [ ] T008 (was T051) [P] Add `tests/ButtonPanelTester.Tests/Integration/Can/DiscoveryE2ETests.fs` — `CanLinkService` through `InMemoryCanLink` + `InMemoryCanFrameStream` + `FrozenClock`. Cases: (a) scripted single broadcast → 1-row map; (b) same UUID re-broadcast → 1-row map with updated `LastSeen` (FR-008); (c) two distinct UUIDs → 2-row map; (d) malformed payload (`Payload.Length = 14`) → no row (FR-013); (e) frame with `CanId ≠ 0x1FFFFFFF` → no row.
- [ ] T009 (was T052) [P] Add `tests/ButtonPanelTester.Tests/Integration/Can/PruningE2ETests.fs` — boundary cases for the 15 s prune (FR-011): `t + 15s` exactly → row still present; `t + 16s` → row pruned within the next tick. No duplicate emissions when nothing changes.
- [ ] T010 (was T053) [P] Add `tests/ButtonPanelTester.Tests/Integration/Can/LinkLossClearsListTests.fs` — `[Connected; observed panel; Disconnected(MidSessionUnplug)]`. Asserts `PanelsOnBusChanged` emits `empty` immediately on the Connected→Disconnected transition (FR-015'), not waiting for the prune timer.
- [ ] T011 (was T054) [P] Add `tests/ButtonPanelTester.Tests.Windows/Gui/Can/PanelsOnBusViewTests.fs` — `Avalonia.Headless.XUnit`. Cases: empty + `Connected` → empty-state text "Bus is up but nothing is announcing itself"; empty + `Disconnected` → "No CAN link"; single virgin-panel row → UUID hex format, "virgin" label, "just now" timestamp; re-broadcast → row updates in place; unknown variant byte → "unknown" label with raw byte in detail affordance.
- [ ] T012 (was T055) Add `tests/ButtonPanelTester.Tests.Windows/Integration/Can/Hardware/DiscoveryHardwareTests.fs` — `[<Trait("Category", "Hardware")>]` (excluded; #112). Cases: with one pristine virgin panel, within 6 s `PanelsOnBusChanged` emits a 1-row map with `Virgin`; physically power off + wait 16 s → row is pruned.

**Checkpoint**: `dotnet run` against a real bench with the virgin panel powered on shows the panel in the list within 6 s; pruning removes it 15 s after power-off. `dotnet test --filter "Category!=Hardware"` adds ≥ 4 passing test files. Spec-003's user story is independently demoable.

---

## Phase N (this spec): Polish

**Purpose**: discovery-side polish. Lifecycle polish lives in [`../002-can-link-lifecycle/tasks.md`](../002-can-link-lifecycle/tasks.md).

- [ ] T013 [P] Add the feat/003 entry to `CHANGELOG.md` under `[Unreleased]` — one line summarising "Passive CAN panel discovery: Panels-on-bus list".
- [ ] T014 [P] Update `README.md` — link to `specs/003-panel-discovery/quickstart.md`; one-paragraph mention of the Panels-on-bus list.
- [ ] T015 [P] Add XML doc comments to every discovery public type — `PanelUuid`, `FwType`, `MachineTypeByte`, `WhoIAmFrame`, `MarketingVariant`, `VariantIdentity`, `PanelObservation`, `PanelsOnBus`, `RawCanFrame`, `ICanFrameStream`.
- [ ] T016 [P] Logging audit for `PcanCanFrameStream` and the discovery branches of `CanLinkService` per the LOGGING standard.
- [ ] T017 [P] Compliance check for Principle V: grep the discovery branch for any field that could carry machine name, OS user, machine identifier, MAC, or SID. Expected zero hits — the discovery wire surface is panel-side `WHO_I_AM` payloads only.
- [ ] T018 [P] End-to-end validation: verify SC-003 (panel within 6 s), SC-004 (no duplicate rows) on a real bench.
- [ ] T019 [P] `cd lean && lake build` — confirm discovery Phase 2 theorems compile with no `sorry` and no custom axioms.

---

## Dependencies & Execution Order

- **Phase 4**: T001 → T002 → T003 → T004 → T005 → T006 → T007 (sequential through `CanLinkService.fs` edits to avoid rebase pain). Tests T008–T011 parallel after T007; T012 sequential (hardware).
- **Polish**: T013–T019 all `[P]`.

## Notes

- Spec-003's implementation slice depends on spec-002 Phase 3.5 [#136](https://github.com/luca-veronelli-stem/button-panel-tester/issues/136) landing first — it widens `Disconnected` arity and reshapes the FR-015' consumer pattern.
- `CanLinkService.fs` is shared with spec-002. T002–T004 are sequential edits on the same file; small enough to not rebase-pain but discipline matters.
- See `../002-can-link-lifecycle/tasks.md` for the historical T001–T033 numbering of the shared foundation tasks.
