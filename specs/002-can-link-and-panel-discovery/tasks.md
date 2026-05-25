---

description: "Task list for feat/002-can-link-and-panel-discovery"
---

# Tasks: CAN Link and Panel Discovery

**Input**: Design documents from `specs/002-can-link-and-panel-discovery/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md).

**Tests**: REQUIRED. The plan's Constitution Check (Principles II + IV) mandates FsCheck property tests, integration tests against virtual adapters (`InMemoryCanLink` + `InMemoryCanFrameStream`), GUI tests via `Avalonia.Headless`, Lean Phase 2 proofs, and a hardware E2E suite gated as `[<Trait("Category", "Hardware")>]` (excluded from default CI, tracked by [#112](https://github.com/luca-veronelli-stem/button-panel-tester/issues/112)).

**Organization**: Tasks group by user story (US1 — CAN status row + reconnect; US2 — panel discovery; US3 — surviving mid-session unplug). Phases 1–2 are shared scaffolding and foundational types. Each user story phase ends with a checkpoint where that story is independently testable per spec.md's "Independent Test" criterion.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel — different `.fsproj` projects or independent files outside the F# compile graph (Lean, JSON fixtures, docs).
- **[Story]**: User story this task serves (US1, US2, US3). Setup/Foundational/Polish carry no story label.
- File paths are absolute from repo root.

## Path Conventions

Archetype A, two-TFM split (see `plan.md` §Project Structure):

- `src/ButtonPanelTester.Core/` — `net10.0` F# domain + ports (extended with `Can/`)
- `src/ButtonPanelTester.Services/` — `net10.0` F# use cases (extended with `Can/`)
- `src/ButtonPanelTester.Infrastructure.Protocol/` — `net10.0-windows` **C#**, NEW PROJECT, vendor copy of `stem-device-manager`
- `src/ButtonPanelTester.Infrastructure/` — `net10.0-windows` F# adapters (extended with `Can/`; references `Infrastructure.Protocol`)
- `src/ButtonPanelTester.GUI/` — `net10.0-windows` F# Avalonia + FuncUI shell (extended with `Can/`)
- `tests/ButtonPanelTester.Tests/` — `net10.0` F# xUnit + FsCheck (extended with `Unit/Can/`, `Property/Can/`, `Integration/Can/`, `Fakes/Can/`, `Fixtures/Can/`)
- `tests/ButtonPanelTester.Tests.Windows/` — `net10.0-windows` F# Avalonia.Headless + Infrastructure tests (extended with `Gui/Can/`, `Integration/Can/Hardware/`)
- `lean/Stem/ButtonPanelTester/Phase2/` — Lean 4 Phase 2 modules

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: scaffold the new C# vendor project, vendor the protocol stack, and extend the existing solution + test partitioning so every later task lands on a compiling solution.

- [ ] T001 Add `Peak.PCANBasic.NET` (version pinned at vendoring time per `stem-device-manager`'s `Directory.Packages.props`) to `Directory.Packages.props` at repo root — transitive dependency of the vendored stack (see [contracts/vendor-manifest.md](./contracts/vendor-manifest.md)).
- [ ] T002 Create `eng/vendor-protocol-stack.ps1` — one-shot PowerShell helper. Parameters: `-StemDeviceManagerPath <path>`, `-CommitSha <SHA>`. Steps: (1) `git -C $StemDeviceManagerPath checkout $CommitSha` (saving prior HEAD); (2) copy the 24 files listed in [research.md](./research.md) R1 from upstream to `src/ButtonPanelTester.Infrastructure.Protocol/` preserving directory structure; (3) generate `VENDOR.md` with the pinned SHA + manifest per [contracts/vendor-manifest.md](./contracts/vendor-manifest.md); (4) compute `VENDOR.sha256` over all vendored files; (5) generate `docs/STOPGAP_VENDORED_PROTOCOL_STACK.md` if absent. Script lives in `eng/`, header comment documents prereqs.
- [ ] T003 Create `src/ButtonPanelTester.Infrastructure.Protocol/ButtonPanelTester.Infrastructure.Protocol.csproj` — TFM `net10.0-windows`, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `RootNamespace` left implicit (vendored files keep their `Stem.DeviceManager.*` namespaces per [contracts/vendor-manifest.md](./contracts/vendor-manifest.md) rule 5), `<PackageReference Include="Peak.PCANBasic.NET" />` (versionless under CPM). No `<Compile>` items yet — file list arrives with T004.
- [ ] T004 Run `eng/vendor-protocol-stack.ps1` against the current `stem-device-manager` `main` HEAD; commit the result as one bisect-safe vendor commit. Output: `src/ButtonPanelTester.Infrastructure.Protocol/{Core/, Services/, Hardware/, VENDOR.md, VENDOR.sha256}` + `docs/STOPGAP_VENDORED_PROTOCOL_STACK.md`. The `csproj` from T003 auto-discovers the `.cs` files via the default SDK glob, so no fsproj edit is needed.
- [ ] T005 Add the one local modification recorded in [research.md](./research.md) "Open follow-ups": extend `src/ButtonPanelTester.Infrastructure.Protocol/Hardware/PCANManager.cs` with `CancellationTokenSource` + `IAsyncDisposable` so the background read/monitor tasks stop cleanly on dispose. Record the diff in `VENDOR.md`'s "Local modifications" table (file + lines + reason + upstream PR URL). Open the upstream PR back to `stem-device-manager` in the same commit (cite the URL in the manifest entry).
- [ ] T006 Update `Stem.ButtonPanelTester.slnx` to register `ButtonPanelTester.Infrastructure.Protocol` between `ButtonPanelTester.Services` and `ButtonPanelTester.Infrastructure`. Final solution order: Core → Services → Infrastructure.Protocol → Infrastructure → GUI → Tests → Tests.Windows.
- [ ] T007 Extend `src/ButtonPanelTester.Infrastructure/ButtonPanelTester.Infrastructure.fsproj` — add `<ProjectReference Include="..\ButtonPanelTester.Infrastructure.Protocol\ButtonPanelTester.Infrastructure.Protocol.csproj" />`. Same-TFM (`net10.0-windows`) on both sides, no NU1201.
- [ ] T008 [P] Create empty folders for test partitioning under `tests/ButtonPanelTester.Tests/` — `Unit/Can/`, `Property/Can/`, `Integration/Can/`, `Fakes/Can/`, `Fixtures/Can/` (one `.gitkeep` per folder so `git add` retains them).
- [ ] T009 [P] Create empty folders for test partitioning under `tests/ButtonPanelTester.Tests.Windows/` — `Gui/Can/`, `Integration/Can/Hardware/` (one `.gitkeep` per folder).
- [ ] T010 [P] Extend `lean/lakefile.toml` — add a second `[[lean_lib]]` entry `name = "Stem.ButtonPanelTester.Phase2"` and extend `defaultTargets` to `["Stem.ButtonPanelTester.Phase1", "Stem.ButtonPanelTester.Phase2"]`. The Phase 1 lib stays unchanged.
- [ ] T011 [P] Add `src/ButtonPanelTester.Infrastructure.Protocol/VENDOR-GUARD.md` — one-line README next to `VENDOR.md` in the vendored C# project pointing at [contracts/vendor-manifest.md](./contracts/vendor-manifest.md) §"Re-vendoring procedure" so a future contributor browsing the vendored project is immediately warned about the freeze + re-vendor discipline. (Earlier draft placed this under `Infrastructure/Can/`, but that folder is only created in T034/US1 — the Protocol project is the right home both ordering-wise and semantically.)

**Checkpoint**: solution restores and builds green; `dotnet test --filter "Category!=Hardware"` still passes; `lake build` builds both Phase 1 and Phase 2 (the latter has no modules yet so the lib target is empty). No behaviour change yet. `VENDOR.sha256` hash check passes (single vendor commit).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: F# Core domain types + ports, virtual fakes, per-type FsCheck properties, the six Lean Phase 2 modules, and fixture data. All three user stories depend on this layer.

**Critical**: no user-story task may start until Phase 2 is complete and `dotnet test --filter "Category!=Hardware"` + `lake build` are both green.

- [ ] T012 [P] Add `src/ButtonPanelTester.Core/Can/CanLinkState.fs` — `DisconnectReason` (`NoAdapterPresent | LinkNotYetOpened | MidSessionUnplug | ReconnectPending`), `ErrorClassification` (`Recoverable of detail: string | Fatal of detail: string`), and `CanLinkState` (`Initializing | Connected of AdapterIdentification * DateTimeOffset | Disconnected of DisconnectReason * DateTimeOffset | Error of ErrorClassification * DateTimeOffset`) per [data-model.md](./data-model.md) §1.1. Forward-references `AdapterIdentification`; declare it as an abstract field carrier here and let T017 define the concrete record (or move `AdapterIdentification` into Core to avoid the forward ref — pick the latter so Core stays self-contained).
- [ ] T013 [P] Add `src/ButtonPanelTester.Core/Can/WhoIAmFrame.fs` — `PanelUuid`, `FwType`, `MachineTypeByte` single-case DUs; `WhoIAmFrame` record; `parse : ReadOnlyMemory<byte> -> WhoIAmFrame option` (rejects length ≠ 15 and `fwType ≠ 0x04` per [contracts/who-i-am-wire-format.md](./contracts/who-i-am-wire-format.md) rules 1+2); `encode : WhoIAmFrame -> byte[]` (15-byte output, big-endian UUID writes via `BinaryPrimitives.WriteUInt32BigEndian`).
- [ ] T014 [P] Add `src/ButtonPanelTester.Core/Can/PanelObservation.fs` — `MarketingVariant` (`EdenXp | OptimusXp | R3LXp | EdenBs8`), `VariantIdentity` (`Marketing of MarketingVariant | Virgin | Unknown of raw: byte`), `decodeVariant : MachineTypeByte -> VariantIdentity` (total, mapping `0x03 / 0x0A / 0x0B / 0x0C` to the four marketing variants, `0xFF` to `Virgin`, else `Unknown raw`), `PanelObservation` record. Depends on T013 for `PanelUuid` + `MachineTypeByte`.
- [ ] T015 [P] Add `src/ButtonPanelTester.Core/Can/PanelsOnBus.fs` — `type PanelsOnBus = Map<PanelUuid, PanelObservation>`; `empty`, `observe : DateTimeOffset -> WhoIAmFrame -> PanelsOnBus -> PanelsOnBus` (insert-or-update keyed by `f.Uuid`, derives `VariantIdentity` via `decodeVariant` from T014), `clear : PanelsOnBus -> PanelsOnBus` (returns `empty`, used by FR-015). Depends on T014.
- [ ] T016 [P] Add `src/ButtonPanelTester.Core/Can/Pruning.fs` — `prune : ttl: TimeSpan -> now: DateTimeOffset -> PanelsOnBus -> PanelsOnBus` (`Map.filter` keeping rows where `now - lastSeen ≤ ttl`). Depends on T015.
- [ ] T017 Add `src/ButtonPanelTester.Core/Can/Ports.fs` — `[<Struct>] RawCanFrame { CanId: uint32; Payload: ReadOnlyMemory<byte>; ReceivedAt: DateTimeOffset }` (per [research.md](./research.md) R7), `AdapterIdentification` record (`{ ChannelName: string; SerialNumber: string; BaudrateBps: int }`), and the two interfaces `ICanLink` (per [contracts/can-link-port.md](./contracts/can-link-port.md)) and `ICanFrameStream` (per [contracts/can-frame-stream-port.md](./contracts/can-frame-stream-port.md)). Depends on T012 (`CanLinkState`). Register `Ports.fs` after the type files in `ButtonPanelTester.Core.fsproj`.
- [ ] T018 Add `src/ButtonPanelTester.Services/Can/ICanLinkService.fs` — `ICanLinkService` interface exposing `CurrentState : CanLinkState`, `PanelsOnBus : PanelsOnBus`, `LinkStateChanged : IObservable<CanLinkState>`, `PanelsOnBusChanged : IObservable<PanelsOnBus>`, `InitializeAsync : CancellationToken -> Task`, `ReconnectAsync : CancellationToken -> Task`. Register in `ButtonPanelTester.Services.fsproj`. Depends on T017.
- [ ] T019 [P] Add `tests/ButtonPanelTester.Tests/Fakes/Can/InMemoryCanLink.fs` — `InMemoryCanLink` implementing `ICanLink` per the virtual-adapter contract in [contracts/can-link-port.md](./contracts/can-link-port.md): constructor takes `seq<CanLinkState * TimeSpan>`; hand-rolled `IObservable<CanLinkState>` subject (`ConcurrentBag<IObserver<_>>` + `OnNext` fan-out per [research.md](./research.md) R4); `OpenAsync` advances the script by one event. Register in `ButtonPanelTester.Tests.fsproj` before any `Property/`, `Integration/`, or `Gui/Can/*` test file.
- [ ] T020 [P] Add `tests/ButtonPanelTester.Tests/Fakes/Can/InMemoryCanFrameStream.fs` — `InMemoryCanFrameStream` implementing `ICanFrameStream` per [contracts/can-frame-stream-port.md](./contracts/can-frame-stream-port.md): constructor takes `seq<RawCanFrame * TimeSpan>`; `Start()` walks the script emitting on a thread-pool worker. Register in `ButtonPanelTester.Tests.fsproj`.
- [ ] T021 [P] Add `tests/ButtonPanelTester.Tests/Fixtures/Can/whoIAmFixtures.json` — 8 bench-captured/synthetic fixtures per [contracts/who-i-am-wire-format.md](./contracts/who-i-am-wire-format.md) §Fixtures table (one virgin + four marketing variants + one unknown + two malformed). Wired into the test project as `<Content CopyToOutputDirectory="PreserveNewest" />`.
- [ ] T022 [P] Add `tests/ButtonPanelTester.Tests/Property/Can/WhoIAmFrameProperties.fs` — FsCheck properties: `WhoIAmFrameRoundtrip` (`parse ∘ encode = Some` for well-formed `WhoIAmFrame`); `WhoIAmFrameRejectsMalformed` (arbitrary `byte[]` not matching the wire layout returns `None`, FR-013 silent drop). Depends on T013.
- [ ] T023 [P] Add `tests/ButtonPanelTester.Tests/Property/Can/VariantDecoderProperties.fs` — FsCheck property: `VariantByteMappingTotal` (for every `byte`, `decodeVariant` produces exactly one of the six branches per FR-009). Depends on T014.
- [ ] T024 [P] Add `tests/ButtonPanelTester.Tests/Property/Can/PanelsOnBusProperties.fs` — FsCheck properties: `PanelsOnBusCoalescing` (`|distinct rows| = |distinct UUIDs|` for any arbitrary frame sequence, FR-008); `PanelsOnBusLastSeenMonotonic` (per-UUID `LastSeen` is non-decreasing across the sequence). Depends on T015.
- [ ] T025 [P] Add `tests/ButtonPanelTester.Tests/Property/Can/PruningProperties.fs` — FsCheck property: `PruningCorrectness` (post-prune membership iff `now - lastSeen ≤ 15s`, FR-011). Depends on T016.
- [ ] T026 [P] Add `tests/ButtonPanelTester.Tests/Property/Can/CanLinkStateTransitionsProperties.fs` — FsCheck property: `CanLinkStateTransitions` (starting from any reachable `CanLinkState`, applying any input event lands in another reachable state per the transition relation mechanised in T027 — `transition_reachability_closed`). Depends on T012.
- [ ] T027 [P] Add `lean/Stem/ButtonPanelTester/Phase2/CanLinkState.lean` — define the `CanLinkState` inductive (closed, mirroring T012) and the `CanLinkTransition` relation that mirrors [data-model.md](./data-model.md) §1.2's Mermaid diagram; prove (a) `theorem state_classification_total`: the five top-level classifications partition the state space, and (b) `theorem transition_reachability_closed`: every transition out of a reachable state lands in a reachable state. T026's FsCheck property is the F# counterpart of (b). No `sorry`, no custom axioms.
- [ ] T028 [P] Add `lean/Stem/ButtonPanelTester/Phase2/WhoIAmFrame.lean` — define `WhoIAmFrame` record + `parse` / `encode`; prove `theorem parse_encode_roundtrip`: `parse (encode f) = some f` for every well-formed `WhoIAmFrame`. Proof: `by rfl` or `by cases <;> simp`.
- [ ] T029 [P] Add `lean/Stem/ButtonPanelTester/Phase2/PanelObservation.lean` — define `VariantIdentity` + `decodeVariant`; prove `theorem variant_decoding_total`: `decodeVariant` is defined on every `UInt8`.
- [ ] T030 [P] Add `lean/Stem/ButtonPanelTester/Phase2/PanelsOnBus.lean` — define `PanelsOnBus = UUID → Option PanelObservation` (function-shaped, no Finmap import) + `observe`; prove `theorem observe_coalesces_by_uuid`: observing two frames with the same UUID produces a single entry whose `lastSeen` is the max of the two timestamps.
- [ ] T031 [P] Add `lean/Stem/ButtonPanelTester/Phase2/Pruning.lean` — define `prune now`; prove `theorem prune_partitions_by_threshold`: post-prune membership iff `now - lastSeen ≤ ttl`.
- [ ] T032 [P] Add `lean/Stem/ButtonPanelTester/Phase2/PassiveObserver.lean` — model the `observe` action with an explicit transmit-trace channel; prove `theorem observe_emits_no_transmit`: the projection of `observe`'s effect onto the transmit-trace alphabet is the empty trace. Mechanises SC-007 + FR-014.
- [ ] T033 Add `tests/ButtonPanelTester.Tests/Unit/Can/WhoIAmFrameFixtureTests.fs` — `[<Fact>]`s reading each fixture from T021 and asserting the expected `parse` outcome (six `Some` with expected `VariantIdentity` per fixture, two `None` for malformed). Lives in `Tests` (`net10.0`) because Core is `net10.0`.

**Checkpoint**: `dotnet build -c Release` green; `dotnet test --filter "Category!=Hardware"` green with ≥ 5 new property suites + 1 new unit suite passing; `lake build` builds both Phase 1 and Phase 2 (six new theorems compile with no `sorry`). Working tree is the design substrate for all three user stories.

---

## Phase 3: User Story 1 — CAN link state at start of shift (Priority: P1) 🎯 MVP

**Goal**: technician launches the tool on a freshly-installed machine with no PEAK adapter present. After dictionary boot completes, within 1 second they see a CAN status row with a `Disconnected` headline naming "no PEAK adapter found". Plugging the adapter in and clicking reconnect flips the headline to `Connected` within 2 seconds.

**Independent Test** (from `spec.md` §US1): launch the tool on a freshly-installed machine with no PEAK adapter present. Verify the CAN status row appears within 1 second of the main window and carries a Disconnected headline with a friendly remediation hint. Plug the adapter in, click reconnect, verify the headline flips to Connected within 2 seconds.

### Implementation for User Story 1

- [ ] T034 [US1] Add `src/ButtonPanelTester.Infrastructure/Can/PcanAdapterIdentity.fs` — helper exposing `tryRead : unit -> AdapterIdentification option` that queries `PCANManager` for `ChannelName` + `SerialNumber` (local-only per FR-004 / Principle V; never serialised). Register in `ButtonPanelTester.Infrastructure.fsproj`.
- [ ] T035 [US1] Add `src/ButtonPanelTester.Infrastructure/Can/PcanCanLink.fs` — `PcanCanLink` implementing `ICanLink` per the production-adapter contract in [contracts/can-link-port.md](./contracts/can-link-port.md): constructor takes the vendored `ICommunicationPort` (a `CanPort` wrapping `PCANManager`) and `ILogger<PcanCanLink>`; `OpenAsync 250000 ct` calls `CanPort.ConnectAsync`; subscribes to vendored `StateChanged` and translates `ConnectionState` → `CanLinkState`; translates PEAK status codes to `Error.Recoverable` / `Error.Fatal` (first observation Recoverable; escalation logic lives in `CanLinkService` per T037); `SemaphoreSlim(1)` serialises Open/Close/Reconnect; `IAsyncDisposable` emits final `Disconnected(ReconnectPending, now)`. Depends on T007 (Infrastructure → Infrastructure.Protocol reference) and T017.
- [ ] T036 [US1] Add `src/ButtonPanelTester.Services/Can/CanLinkService.fs` — `CanLinkService` implementing `ICanLinkService` per [research.md](./research.md) R5 + R8 (lifecycle slice only; observation pipeline lands in US2 at T046): constructor takes `ICanLink` + `IClock` + `ILogger<CanLinkService>`; tracks per-error-cause "consecutive reconnect-failure count" and emits `Error.Recoverable` first time, `Error.Fatal` on repeat after reconnect; hand-rolled `IObservable<CanLinkState>` subject for `LinkStateChanged`. `PanelsOnBus` returns `empty` and `PanelsOnBusChanged` is a no-op observable for now (US2 fills these in). Register in `ButtonPanelTester.Services.fsproj`. Depends on T018.
- [ ] T037 [US1] Extend `src/ButtonPanelTester.GUI/Composition/CompositionRoot.fs` — register `ICanLink = PcanCanLink` (constructed from a `CanPort` wrapping `PCANManager`, both from `Infrastructure.Protocol`), `ICanLinkService = CanLinkService`. Order the registration after `IDictionaryService` so the FR-001 boot order is observable in DI. `ICanFrameStream` is bound to a no-op fake here that emits nothing — replaced by `PcanCanFrameStream` in US2 (T049).
- [ ] T038 [US1] Add `src/ButtonPanelTester.GUI/Can/CanStatusRow.fs` — FuncUI view rendering the three observable parts of `CanLinkState`: a colour-coded chip (green for `Connected`, grey for `Disconnected`, red for `Error`), a headline (e.g. `Connected · PCAN-USB Pro FD (1)`, `Disconnected · no PEAK adapter found`, `Error · Bus-off detected — try reconnect`), and a click-or-hover detail affordance showing `AdapterIdentification` (FR-004) + the last transition reason. Reconnect button visible whenever the state is not `Connected`; click invokes `ICanLinkService.ReconnectAsync` (FR-003). The Recoverable/Fatal sub-classification is rendered in the detail panel per FR-002a; the button text reads "Try reconnect" in Recoverable and "Reconnect (unlikely to help)" in Fatal.
- [ ] T039 [US1] Extend `src/ButtonPanelTester.GUI/App.fs` so the main window hosts a vertical-stack panel: `DictionaryStatusRow` (top, from feat-001), `CanStatusRow` (middle, from T038), with the third slot reserved for `PanelsOnBusView` (filled in US2). On window-loaded, after `IDictionaryService.InitializeAsync` (already wired by feat-001), invoke `ICanLinkService.InitializeAsync` so the CAN link opens only after dictionary boot completes per FR-001 + SC-001.

### Tests for User Story 1

- [ ] T040 [P] [US1] Add `tests/ButtonPanelTester.Tests/Integration/Can/CanLinkServiceLifecycleTests.fs` — wires `CanLinkService` through `InMemoryCanLink` (scripted sequences) + `FrozenClock` (reused from feat-001's `Fakes/Wiring.fs`). Cases: (a) scripted `[Connected adapter t]` → service emits `Connected` and `CurrentState` matches within the SC-001 budget; (b) scripted `[Error.Fatal(driver missing)]` on Open → service emits `Error(Fatal "PEAK driver not installed", t)`; (c) `Disconnected(NoAdapterPresent, t)` ↔ `Connected` ↔ `Disconnected(MidSessionUnplug, t)` round-trip preserves the observable state at each transition.
- [ ] T040b [US1] **Deferred to follow-up issue** — add a boot-order negative test for FR-001 ("CAN adapter MUST be opened only after dictionary boot has completed, not before"). Current state: the ordering is wired in `src/ButtonPanelTester.GUI/App.fs` (the `Opened` handler awaits `IDictionaryService.InitializeAsync` before calling `canLinkService.InitializeAsync`), but no test fails if a future refactor reverses the order. SC-001 measures elapsed time after dictionary populates and cannot catch an inverted boot. Implementation requires either (a) extracting the `Opened` task body into a `Services/BootSequence.fs` function with a unit test that spies on call order, or (b) an Avalonia.Headless test in `Tests.Windows` exercising the live composition root. (a) is preferred — FR-001 is a domain rule, not a GUI concern (Principle III).
- [ ] T041 [P] [US1] Add `tests/ButtonPanelTester.Tests/Integration/Can/RecoverableToFatalEscalationTests.fs` — scripts `Error.Recoverable("PEAK status 0x40000", t1)` → service's `ReconnectAsync` triggers the script to re-emit the same PEAK status code → service surfaces `Error.Fatal("PEAK status 0x40000 persists across reconnect — file bug", t2)`. Reset-on-success: scripts the same Recoverable status, then a successful Connected, then the same status again → second-time-after-success is still Recoverable, NOT Fatal. Mechanises [research.md](./research.md) R8.
- [ ] T042 [P] [US1] Add `tests/ButtonPanelTester.Tests.Windows/Gui/Can/CanStatusRowTests.fs` — `Avalonia.Headless.XUnit` tests driving the FuncUI message loop. Cases: chip is green + headline `Connected · <channel name>` when `LinkStateChanged` emits `Connected adapter`; chip is grey + headline includes "no PEAK adapter found" when `Disconnected NoAdapterPresent`; chip is red + headline contains the recoverable detail when `Error(Recoverable "Bus-off detected — try reconnect")`; Reconnect button visible and click raises the service's `ReconnectAsync` (verified via spy `ICanLinkService`). Lives in `Tests.Windows` (`net10.0-windows`) per #76 — the GUI project is `net10.0-windows`.
- [ ] T043 [P] [US1] Add `tests/ButtonPanelTester.Tests.Windows/Integration/Can/Hardware/PcanLifecycleTests.fs` — `[<Trait("Category", "Hardware")>]` AND `[<Fact(Skip = "...#112")>]` (the trait alone does not exclude on CI at standards v1.9.0; tracked by [#112](https://github.com/luca-veronelli-stem/button-panel-tester/issues/112)). Cases: real PEAK adapter present + `OpenAsync 250000` succeeds within 2 s and surfaces `Connected` with non-empty `AdapterIdentification`; `CloseAsync` followed by `OpenAsync` succeeds. The "physical unplug surfaces `Disconnected(MidSessionUnplug, _)` within 5 s" case is **not** included here — it folds into T061's US3 unplug-cycle hardware test to avoid duplication (T055 also exercises an unplug, scoped to discovery; the three hardware tests partition by user story, not by transition).

**Checkpoint (US1)**: `dotnet run --project src/ButtonPanelTester.GUI` on a machine with no PEAK adapter shows the main window with `DictionaryStatusRow` populated (feat-001) AND the CAN status row populated as `Disconnected · no PEAK adapter found` within 1 s of paint, with a working Reconnect button. Plugging the adapter in + clicking Reconnect flips to `Connected` within 2 s. `dotnet test --filter "Category!=Hardware"` adds ≥ 3 passing test files. US1 is independently demoable.

---

## Phase 4: User Story 2 — Pristine panels announce themselves (Priority: P2)

**Goal**: with the CAN link Connected and a single pristine virgin panel powered on the bus, within 6 seconds a row appears in the Panels-on-bus list carrying the panel's UUID, the label "virgin", and a recent last-seen timestamp. Re-broadcasts update the row in place; pruning removes rows after 15 s of silence.

**Independent Test** (from `spec.md` §US2): with the tool running, the adapter Connected, and the bus empty, power on the single pristine virgin panel on the bench. Verify a single row appears in the Panels-on-bus list within 6 seconds, carrying a UUID, the label "virgin", and a recent last-seen timestamp.

### Implementation for User Story 2

- [ ] T044 [US2] Add `src/ButtonPanelTester.Infrastructure/Can/PcanCanFrameStream.fs` — `PcanCanFrameStream` implementing `ICanFrameStream` per [contracts/can-frame-stream-port.md](./contracts/can-frame-stream-port.md) production-adapter contract: constructor takes the vendored `CanPort` + `ILogger<PcanCanFrameStream>`; subscribes to `PacketReceived`; translates `CANPacketEventArgs` → `RawCanFrame` (CanId from `ArbitrationId`, `Payload` wrapped as `ReadOnlyMemory<byte>` from `Data`, `ReceivedAt` from `Timestamp`); hand-rolled `IObservable<RawCanFrame>` subject. Depends on T017.
- [ ] T045 [US2] Extend `src/ButtonPanelTester.Services/Can/CanLinkService.fs` — add the observation pipeline: constructor now also takes `ICanFrameStream`; on `Connected`, subscribes to `RawFramesReceived`, filters for `CanId = 0x1FFFFFFF AND Payload.Length = 15` (per [contracts/can-frame-stream-port.md](./contracts/can-frame-stream-port.md) §Frame contract), parses via `WhoIAmFrame.parse`, and on `Some f` calls `observe (IClock.UtcNow()) f` on the current `PanelsOnBus`. Hand-rolled `IObservable<PanelsOnBus>` for `PanelsOnBusChanged`. `parse` returning `None` is a silent drop per FR-013 (no log, no state-row Error flip).
- [ ] T046 [US2] Extend `src/ButtonPanelTester.Services/Can/CanLinkService.fs` — add the 1 s pruning timer per [research.md](./research.md) R5: `System.Threading.Timer` tick at 1 s; on each tick calls `prune (TimeSpan.FromSeconds 15.0) (IClock.UtcNow())` and emits the new `PanelsOnBus` through `PanelsOnBusChanged` if it differs. Timer started when state enters `Connected`, stopped on dispose.
- [ ] T047 [US2] Extend `src/ButtonPanelTester.Services/Can/CanLinkService.fs` — wire the FR-015 link-loss clear: on `LinkStateChanged` transitioning from `Connected` to any non-`Connected` state, call `clear` on the current `PanelsOnBus` and emit `empty` through `PanelsOnBusChanged`.
- [ ] T048 [US2] Extend `src/ButtonPanelTester.GUI/Composition/CompositionRoot.fs` — replace the US1 no-op `ICanFrameStream` binding with `PcanCanFrameStream` (constructed from the same `CanPort` instance shared with `PcanCanLink`); update `CanLinkService` registration to inject both ports.
- [ ] T049 [US2] Add `src/ButtonPanelTester.GUI/Can/PanelsOnBusView.fs` — FuncUI view rendering the live `PanelsOnBus` (subscribed to `ICanLinkService.PanelsOnBusChanged` via `Cmd.ofSub`). Each row shows `UUID: <three big-endian uint32 chunks formatted as 0x… · 0x… · 0x…>` (FR-008), `Variant: <marketing name | virgin | unknown>` (FR-009, with raw byte in the detail affordance for the latter two), and `Last seen: HH:MM:SS (just now / N s ago)` (FR-010). Empty-state explainer (FR-012): when list is empty AND `CurrentState = Connected`, render "Bus is up but nothing is announcing itself"; when empty AND not Connected, render "No CAN link".
- [ ] T050 [US2] Extend `src/ButtonPanelTester.GUI/App.fs` — fill the third slot of the vertical stack with `PanelsOnBusView` from T049 so the layout `DictionaryStatusRow / CanStatusRow / PanelsOnBusView` matches [research.md](./research.md) R9.

### Tests for User Story 2

- [ ] T051 [P] [US2] Add `tests/ButtonPanelTester.Tests/Integration/Can/DiscoveryE2ETests.fs` — wires `CanLinkService` through `InMemoryCanLink` (scripted `[Connected]`) + `InMemoryCanFrameStream` (scripted with a virgin-panel `WHO_I_AM` fixture from T021) + `FrozenClock`. Cases: (a) scripted single broadcast → `PanelsOnBusChanged` emits a 1-row map with `VariantIdentity = Virgin`, UUID matching the fixture, `LastSeen = clock.UtcNow()`; (b) same UUID re-broadcast at `t + 3s` → 1-row map with `LastSeen = t + 3s` (coalescing, FR-008); (c) two distinct UUIDs → 2-row map (accidental two-on-bus case from spec.md Assumptions); (d) malformed payload (`Payload.Length = 14`) → no row emitted, no state flip (FR-013); (e) frame with `CanId ≠ 0x1FFFFFFF` → no row emitted.
- [ ] T052 [P] [US2] Add `tests/ButtonPanelTester.Tests/Integration/Can/PruningE2ETests.fs` — wires `CanLinkService` with `FrozenClock`. Cases: (a) advance the clock to `t + 15s` exactly → row still present (boundary case: `≤ ttl` per [data-model.md](./data-model.md) §5.4); (b) advance the clock to `t + 16s` → row pruned within the next 1 s tick → `PanelsOnBusChanged` emits empty map. Asserts no duplicate emissions when nothing changes (idempotent ticks).
- [ ] T053 [P] [US2] Add `tests/ButtonPanelTester.Tests/Integration/Can/LinkLossClearsListTests.fs` — scripts `[Connected; observed virgin panel; Disconnected(MidSessionUnplug)]`. Asserts `PanelsOnBusChanged` emits `empty` immediately on the Connected→Disconnected transition (FR-015), not waiting for the prune timer. Mechanises Invariant #3 with an integration counterpart to the Lean theorem in T032.
- [ ] T054 [P] [US2] Add `tests/ButtonPanelTester.Tests.Windows/Gui/Can/PanelsOnBusViewTests.fs` — `Avalonia.Headless.XUnit`. Cases: empty + `Connected` → empty-state text "Bus is up but nothing is announcing itself" rendered; empty + `Disconnected` → empty-state text "No CAN link"; single virgin-panel row → UUID hex format, "virgin" label, "just now" timestamp; same panel re-emitted with later `LastSeen` → row updates in place (no flicker; same row identity verified by `Visual` reference equality); unknown variant byte → row label "unknown" with the raw byte in the detail affordance. Lives in `Tests.Windows` per #76.
- [ ] T055 [US2] Add `tests/ButtonPanelTester.Tests.Windows/Integration/Can/Hardware/DiscoveryHardwareTests.fs` — `[<Trait("Category", "Hardware")>]` (excluded; #112). Cases: with one pristine virgin panel on the bench, within 6 s `PanelsOnBusChanged` emits a 1-row map with `Virgin`; physically power off the panel + wait 16 s → row is pruned; physically unplug the adapter mid-broadcast → list clears within 5 s (folds in the US3 hardware check).

**Checkpoint (US2)**: `dotnet run` against a real bench with the virgin panel powered on shows the panel in the list within 6 s with `virgin` label; pruning removes it 15 s after power-off. `dotnet test --filter "Category!=Hardware"` adds ≥ 4 passing test files. US2 + US1 are both independently demoable.

---

## Phase 5: User Story 3 — Surviving an adapter unplug mid-session (Priority: P3)

**Goal**: mid-session, the technician bumps the PEAK adapter loose. Within 5 seconds the CAN status row flips to `Disconnected`, the Panels-on-bus list empties, the dictionary status row stays untouched, and re-seating + clicking Reconnect resumes operation.

**Independent Test** (from `spec.md` §US3): with the tool Connected and showing one virgin panel, physically unplug the PEAK adapter. Verify the status row flips to Disconnected within 5 seconds with a "link lost" reason, the Panels-on-bus list empties (no stale rows), and the dictionary status row is unchanged. Re-plug the adapter and click reconnect — verify the link recovers and the panel reappears.

### Implementation for User Story 3

- [ ] T056 [US3] Verify `src/ButtonPanelTester.Infrastructure/Can/PcanCanLink.fs` (T035) translates the vendored `StateChanged` "device removed" path to `Disconnected(MidSessionUnplug, now)` (and not to `Error`). If T035 emitted `Error` for this case, fix it here — `MidSessionUnplug` is the explicit `DisconnectReason` for hot-unplug per [data-model.md](./data-model.md) §1.1 + the spec's edge case "PEAK driver returns an unexpected status code" (which is the Error path) vs. the "the technician unplugs the PEAK adapter" path (which is Disconnected, FR-005).
- [ ] T057 [US3] Verify `src/ButtonPanelTester.GUI/Can/CanStatusRow.fs` (T038) renders the `Disconnected(MidSessionUnplug, _)` headline as `Disconnected · link lost — replug adapter` (distinct from the `NoAdapterPresent` headline per FR-005). If T038 used a generic Disconnected headline, refine it here.

### Tests for User Story 3

- [ ] T058 [P] [US3] Add `tests/ButtonPanelTester.Tests/Integration/Can/MidSessionUnplugTests.fs` — scripts `[Connected adapter; observed virgin panel; Disconnected(MidSessionUnplug)]`. Asserts: (a) `LinkStateChanged` emits `Disconnected(MidSessionUnplug, _)` within the SC-005 budget; (b) `PanelsOnBusChanged` emits `empty` (no stale rows, FR-015 — already integration-tested by T053 but re-asserted here for the US3 acceptance trace).
- [ ] T059 [P] [US3] Add `tests/ButtonPanelTester.Tests/Integration/Can/DictionaryIndependenceTests.fs` — wires `DictionaryService` (from feat-001) + `CanLinkService` (from this spec) sharing the same `FrozenClock` and `Fakes/Wiring.fs` adapters. Scripts a `CanLinkService` Connected → Disconnected(MidSessionUnplug) sequence. Asserts `IDictionaryService.SourceChanged` emits zero events during the CAN-side disconnect (FR-016 + SC-006).
- [ ] T060 [P] [US3] Add `tests/ButtonPanelTester.Tests.Windows/Gui/Can/CanStatusRowMidSessionUnplugTests.fs` — `Avalonia.Headless.XUnit`. Cases: row renders `Disconnected · link lost — replug adapter` when `LinkStateChanged` emits `Disconnected(MidSessionUnplug, _)` (verifies T057); Reconnect button is visible and clickable in this state; dictionary status row from feat-001 is unchanged in the parallel snapshot. Lives in `Tests.Windows` per #76.
- [ ] T061 [US3] Add hardware unplug case to `tests/ButtonPanelTester.Tests.Windows/Integration/Can/Hardware/DiscoveryHardwareTests.fs` (or split into a dedicated `UnplugReplugCycleHardwareTests.fs` if T055 grows unwieldy). `[<Trait("Category", "Hardware")>]`. Cases: Connected + observed panel → physical unplug → `Disconnected(MidSessionUnplug)` within 5 s + list clears; replug + click `ReconnectAsync` → `Connected` within 2 s + panel reappears within 6 s.

**Checkpoint (US3)**: on the bench, unplugging mid-session flips the CAN row to `Disconnected · link lost — replug adapter` within 5 s; list empties; dictionary row is untouched; replug + Reconnect restores observation. `dotnet test --filter "Category!=Hardware"` adds ≥ 3 passing test files. All three user stories are independently demoable.

---

## Phase N: Polish & Cross-Cutting Concerns

**Purpose**: cleanup, docs, audits, and end-to-end validation.

- [ ] T062 [P] Add the feat/002 entry to `CHANGELOG.md` under `[Unreleased]` — one line summarising "Passive CAN observation: status row + Panels-on-bus list + vendored protocol stack".
- [ ] T063 [P] Update `README.md` — link to `specs/002-can-link-and-panel-discovery/quickstart.md`; one-paragraph mention of the CAN status row + Panels-on-bus list as the user-visible surface; one sentence noting the vendored `Infrastructure.Protocol` project + the freeze discipline.
- [ ] T064 [P] Add XML doc comments to every public type and member listed in [data-model.md](./data-model.md) §1–§6 per the COMMENTS standard — `DisconnectReason`, `ErrorClassification`, `CanLinkState`, `PanelUuid`, `FwType`, `MachineTypeByte`, `WhoIAmFrame`, `MarketingVariant`, `VariantIdentity`, `PanelObservation`, `PanelsOnBus`, `RawCanFrame`, `AdapterIdentification`, both port interfaces, `ICanLinkService`.
- [ ] T065 [P] Logging audit per the LOGGING standard — confirm every CAN adapter (`PcanCanLink`, `PcanCanFrameStream`) and `CanLinkService` use `ILogger<T>` via DI, no `Console.WriteLine`, no string-interpolation in log messages (parameterised templates only), no panel `PanelUuid` appears in any log statement above `Debug` (panels are device identifiers, not supplier identity — but conservative defaults aid future scrutiny under Principle V).
- [ ] T066 [P] Async-discipline audit per the CANCELLATION + THREAD_SAFETY standards: every `OpenAsync` / `CloseAsync` / `ReconnectAsync` accepts a `CancellationToken`; the pruning timer respects disposal; the receive-thread → service-thread → UI-thread hop chain is documented inline in `CanLinkService.fs` headers; `PCANManager.cs` extension from T005 ships with `IAsyncDisposable.DisposeAsync` ending the read loop cleanly.
- [ ] T067 [P] Compliance check for Principle V: grep the CAN layer for any field that could carry machine name, OS user, machine identifier, MAC, or SID. Expected zero hits — the CAN wire surface is panel-side `WHO_I_AM` payloads only. Document the audit result in a one-line `# Compliance` note inside `specs/002-can-link-and-panel-discovery/quickstart.md` "Common gotchas" tail.
- [ ] T068 [P] Update `docs/Context/bpt-rollout/CORRECTIONS.md` §C5 with a closing paragraph scoping the hardcoded-protocol-metadata stopgap to spec-003+ (per [research.md](./research.md) R2). One paragraph, non-blocking; documents the discovery without re-litigating the original audit.
- [ ] T069 End-to-end validation: walk `quickstart.md` §"Expected behaviour on a clean bench" §1–§5 on a real bench; verify SC-001 (Connected within 2 s on a present adapter), SC-002 (Disconnected within 1 s when adapter absent), SC-003 (panel within 6 s), SC-005 (Disconnected within 5 s of unplug), SC-008 (Error within 5 s of bus-off). SC-007 verified separately in T070.
- [ ] T070 SC-007 verification with an external bus capture: wire a **second CAN listener onto the same physical bus** as the panel (a second PCAN-USB tee'd into the CAN-H / CAN-L pair, 120 Ω terminated at both physical ends, or any known-good silent CAN sniffer with the same wiring). Sibling PCAN channels are separate physical buses and would always show zero frames — do not use them for this verification. Record a 10-minute trace via PCAN-View (or equivalent) while the tool runs through the full quickstart sequence (boot → Connected → observe panel → unplug → replug → Reconnect → observe again). Confirm the captured trace shows zero frames originating from the tool's CAN ID range. Attach the trace summary to the PR.
- [ ] T071 `cd lean && lake build` — confirm all six Phase 2 theorems compile with no `sorry` and no custom axioms after every preceding task (Constitution Principle I gate). Phase 1 theorems must also still compile.
- [ ] T072 [P] Confirm `VENDOR.sha256` hash check still passes against `src/ButtonPanelTester.Infrastructure.Protocol/` — no silent edits crept into the vendor copy across the spec's lifetime. Document the verification in `VENDOR.md`'s "Last verified" column.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: T001 → T002 → T003 → T004 → T005 → T006 / T007. T008–T011 parallel after T006.
- **Foundational (Phase 2)**: depends on Phase 1 complete (vendored project + slnx update). T012–T016 form an ordered chain (`CanLinkState ← WhoIAmFrame ← PanelObservation ← PanelsOnBus ← Pruning`); T017 depends on T012; T018 depends on T017; T019–T020 depend on T017; T021 standalone; T022–T026 each depend on their corresponding Core file; T027–T032 [P] (independent Lean files); T033 depends on T021 + T013.
- **User Story 1 (Phase 3)**: depends on Phase 2 complete. T034 → T035 → T036 → T037 → T038 → T039; tests T040–T043 parallel after T039.
- **User Story 2 (Phase 4)**: depends on Phase 3 complete (CanLinkService lifecycle scaffold). T044 → T045 → T046 → T047 → T048 → T049 → T050; tests T051–T055 parallel after T050.
- **User Story 3 (Phase 5)**: depends on Phase 4 complete (observation pipeline). T056 → T057; tests T058–T061 parallel after T057.
- **Polish (Phase N)**: depends on all desired user stories. T062–T068 [P]; T069 sequential after the user stories; T070 sequential after T069; T071 + T072 [P].

### Within Each User Story

- Tests REQUIRED (Principle II + IV); each user-story phase ends with property + integration + GUI + hardware test files for that story's surface.
- Ports + service before adapter implementations before composition root before GUI views.
- Hardware tests (`Category=Hardware`) excluded from default CI; tracked by [#112](https://github.com/luca-veronelli-stem/button-panel-tester/issues/112). Open-issue list in `VENDOR.md` is the canonical roster.

### Parallel Opportunities

- Phase 1: T008, T009, T010, T011 — all `[P]`.
- Phase 2: T012, T013, T014, T015, T016 are sequential (chain); T017–T018 sequential; T019, T020, T021, T022, T023, T024, T025, T026 all `[P]` after their preconditions land; T027–T032 all `[P]` (Lean files independent).
- Phase 3: T040, T041, T042, T043 all `[P]` after T039.
- Phase 4: T051, T052, T053, T054 all `[P]` after T050; T055 sequential (hardware).
- Phase 5: T058, T059, T060 all `[P]` after T057; T061 sequential (hardware).
- Different developers can take US1, US2, US3 in parallel once Phase 2 lands — the stories share `CanLinkService` so the staged file edits in Phase 4 (T045 → T047) need rebase discipline, but the test files are fully partitioned.

---

## Parallel Example: Phase 2 Foundational

```text
# After T017 (Ports.fs) lands, launch the property + Lean suites in parallel:
Task: "Add WhoIAmFrameProperties.fs"                                    # T022 [P]
Task: "Add VariantDecoderProperties.fs"                                 # T023 [P]
Task: "Add PanelsOnBusProperties.fs"                                    # T024 [P]
Task: "Add PruningProperties.fs"                                        # T025 [P]
Task: "Add CanLinkStateTransitionsProperties.fs"                        # T026 [P]
Task: "Add Phase2/CanLinkState.lean"                                    # T027 [P]
Task: "Add Phase2/WhoIAmFrame.lean"                                     # T028 [P]
Task: "Add Phase2/PanelObservation.lean"                                # T029 [P]
Task: "Add Phase2/PanelsOnBus.lean"                                     # T030 [P]
Task: "Add Phase2/Pruning.lean"                                         # T031 [P]
Task: "Add Phase2/PassiveObserver.lean"                                 # T032 [P]
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (vendor the C# stack, extend solution).
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories; six Lean theorems + property suites must be green).
3. Complete Phase 3: User Story 1 (CAN status row + reconnect, no panel discovery yet).
4. **STOP and VALIDATE**: walk `spec.md` §US1 "Independent Test" end-to-end. Demo: tool launches → CAN row says `Disconnected · no PEAK adapter found` → plug in → Reconnect → green.
5. Deploy if ready (status-row visibility alone is supplier-valuable confirmation that the tool can talk to its CAN side at all).

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. Add US1 → test independently → demo (MVP).
3. Add US2 → test independently → demo (the actual supplier QA bench scenario; panels appear).
4. Add US3 → test independently → demo (robustness).
5. Polish → ship the spec-002 PR.

### Parallel Team Strategy

With multiple developers (post-Phase 2):

- Developer A: US1 (T034–T043).
- Developer B: US2 implementation (T044–T050) — depends on T036 from A, but the test files (T051–T054) can be drafted from the spec.
- Developer C: hardware setup (#112) + the hardware-suite tests (T043, T055, T061).

US3 is naturally folded into US1 + US2 (T056–T060 are mostly verifications). Polish (T062–T072) parallelises across everyone post-checkpoint.

---

## Notes

- `[P]` tasks = different files, no dependencies.
- `[Story]` label maps task to specific user story for traceability.
- Each user story should be independently completable and testable.
- Verify tests fail before implementing (Principle I+II: Lean spec → test → implementation).
- Commit after each task or logical group (bisect-safe per the global rule).
- Stop at any checkpoint to validate story independently.
- Vendoring (T004) is one atomic commit; future re-vendoring follows [contracts/vendor-manifest.md](./contracts/vendor-manifest.md) §"Re-vendoring procedure" — never fix-in-place.
- Avoid: vague tasks, same-file conflicts in Phase 4 (T044→T047 touch `CanLinkService.fs` in sequence), cross-story dependencies that break independence.
