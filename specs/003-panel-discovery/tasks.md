---
description: "Task list for spec-003 — Panel Discovery via Passive WHO_I_AM Observation"
---

# Tasks: Panel Discovery via Passive WHO_I_AM Observation

**Input**: Design documents from [`specs/003-panel-discovery/`](./)

**Prerequisites**: [plan.md](./plan.md) (task authority — §Implementation phases A–E),
[spec.md](./spec.md) (US1 + FR-001..009 / SC-001..004), [data-model.md](./data-model.md),
[research.md](./research.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md),
[context/firmware-verification-2026-06-05.md](./context/firmware-verification-2026-06-05.md).

**Tests**: REQUIRED. The constitution makes tests non-optional (Principle II — FsCheck is
the primary correctness mechanism for `Core`/`Services`; Principle IV — every test layer
greens on CI, hardware E2E is tagged + excluded). Closed-domain / wire types carry the
mandatory triple: **Lean theorem + FsCheck property + XML-doc citation** (Principles I/II).

**Numbering**: re-baselined fresh from **T001** for this independent re-spec under #153.
The archived `context/previous-003/tasks.md` (pre-correction, pre-#197 service split,
spec-002 T044+ numbering) is **structural reference only** — not the source of these tasks.

**Re-scope (2026-06-09).** Bench validation after Phase C found `WHO_I_AM` is a *segmented
multi-frame* SP_APP message (not a single frame) and the vendored receive loop was never
started — so the CI-green pipeline received nothing on real hardware (plan §Re-scope).
**Phase R (T033–T040)** corrects this and executes **after Phase C, before Phase D** (its
higher task numbers are append order, not execution order). Phases A–C are `[X]` (landed);
two of their tasks are superseded: **T009's raw-frame ingest → re-sourced by T038**, and
**T017's "one CAN-FD frame" note is wrong** — it is multi-frame *classic* CAN reassembled by
the Network Layer (see [contracts/who-i-am-wire-format.md](./contracts/who-i-am-wire-format.md)).

---

## Format

`- [ ] T### [P?] [US1?] **[TAG]** Description with exact file path`

- **[P]** — parallelizable (different files, no dependency on an incomplete task).
- **[US1]** — serves the single user story (see below). Setup, the foundational Phase A,
  and Polish carry **no** story label per the spec-kit convention.
- **[TAG]** — provenance against the shipped tree (#121 foundation, #197 service split):
  - **CORRECT** — shipped-but-wrong; spec-003 fixes it (the WHO_I_AM codec only).
  - **GROW** — shipped stub; spec-003 grows it to live (`PanelDiscoveryService`, #197).
  - **NEW** — does not exist; spec-003 writes it.
  - **EXTEND** — shipped + correct; spec-003 adds a binding/slot without rewriting it.
  - **RE-POINT** — comment-only citation move `002 → 003` (rides with the slice that
    already edits the file — never a standalone "fix comments" commit, per `vertical-commits`).

**Single user story.** spec.md defines exactly one story — **US1 (P1): "Seeing pristine
panels announce themselves"**. Every behavioural task (Phases B–E) serves US1; Phase A is the
foundational wire-format correction that makes US1 reachable at all.

**bisect-safe.** Every task/commit boundary MUST compile and pass tests on its own
(`bisect-safe` + `vertical-commits`). Where the constitution order (Lean → xUnit/FsCheck → F#)
would otherwise leave a RED intermediate commit, the corrected **test and its implementation
land in the same commit** (resolve-ticket discipline). Commit groupings are called out per
phase and in §Dependencies.

---

## Phase 1: Setup

**Purpose**: establish the green baseline this branch builds on, so every later commit can be
checked bisect-safe.

- [ ] T001 Verify the green baseline on `docs/153-spec-003-respec`: `dotnet build Stem.ButtonPanelTester.slnx -c Release`; `dotnet test tests\ButtonPanelTester.Tests\ButtonPanelTester.Tests.fsproj -c Release` and `dotnet test tests\ButtonPanelTester.Tests.Windows\ButtonPanelTester.Tests.Windows.fsproj -c Release --filter "Category!=Hardware"`; `cd lean; lake build; cd ..`. Record the result as the bisect-safe anchor (worktrees baseline discipline). Note: the shipped WHO_I_AM unit/property/Lean suites are green against the **wrong** codec (firmware-verification §Impact) — Phase A turns them red-then-green against the real wire format.

---

## Phase A: Correct the WHO_I_AM wire foundation — FOUNDATIONAL (blocks all behaviour)

**Goal**: re-base the shipped codec onto the firmware-verified wire format
([contracts/who-i-am-wire-format.md](./contracts/who-i-am-wire-format.md)): `[0]` machineType
u8, `[1..2]` `fwType` big-endian `uint16`, `[3/7/11]` three big-endian `uint32` UUID words,
15 bytes, **no padding**, **length-only** reject. The shipped `WhoIAmFrame.fs` (#121) gates on
`byte[1] = fwType>>8 = 0x00 ≠ 0x04` and so rejects **every real frame** — without this slice
no real frame parses and SC-001 is unreachable.

**Mandatory triple (WhoIAmFrame).** Lean `parse_encode_roundtrip` (T002) ↔ FsCheck
`WhoIAmFrameRoundtrip` / `WhoIAmFrameRejectsWrongLength` (T005) ↔ XML-doc citation in
`WhoIAmFrame.fs` (T007). The shipped triples for `VariantDecoder`, `PanelsOnBus` coalescing,
and `Pruning` stay intact — only their citations re-point (T003, T008).

**Constitution order**: Lean → fixtures + FsCheck → F#.
**Commit grouping**: A1 = {T002, T003} (Lean-only, green on `lake build`).
A2 = {T004, T005, T006, T007, T008} land **together** (corrected tests + impl + the Pruning.fs
re-point in one commit — the suite is RED with the corrected tests until T007 lands).

- [X] T002 **[CORRECT]** Re-state `lean/Stem/ButtonPanelTester/Phase2/WhoIAmFrame.lean`: drop the `fwType = 4` precondition so `parse_encode_roundtrip` holds for **every** `WhoIAmFrame` (length — the only rejection axis — has no record-level analogue, so `parse` becomes total `some`); model `fwType` as the wire `Nat` with no guard; fully refresh the module-doc to spec-003 (not path-only): citation `data-model.md §2.3` → `§1.3`, the `(FR-013 silent drop)` ref → `FR-007`, the stale "well-formed / `fwType = 0x04` is the only path through `parse`" prose → the corrected total/length-only statement, and the intra-spec cross-refs (this module / F# surface / FsCheck / fixtures / "one vertical PR-B commit") → the owning spec-003 tasks T002 / T007 / T005 / T006. Keep the proof `sorry`-free with axioms ⊆ {`propext`, `Classical.choice`, `Quot.sound`}; `cd lean; lake build` green. (Constitution I; FR-007)
- [X] T003 [P] **[RE-POINT]** Re-point the three discovery-foundation Lean citations `specs/002-can-link-and-panel-discovery/data-model.md` → `specs/003-panel-discovery/data-model.md` (comment-only — **not path-only**: also refresh each header's stale spec-002 *semantic* refs so it resolves against spec-003): `Phase2/PanelObservation.lean` §3.2→§2 and `(FR-009)`→`(FR-003)`, `Phase2/PanelsOnBus.lean` §5.4→§4 and `FR-008`→`FR-002`, `Phase2/Pruning.lean` §5.4→§4 and `FR-011`→`FR-005`. Leave the #121-era authoring stamps (`T029`/`T030`/`T031`, the forward F#/FsCheck task refs, "one vertical PR-B commit") as historical provenance — these modules shipped under #121; do not re-attribute them to spec-003 numbers. Rides in the A1 commit; `lake build` still green.
- [X] T004 **[CORRECT]** Rebuild `tests/ButtonPanelTester.Tests/Fixtures/Can/whoIAmFixtures.json` to the corrected 15-byte layout per contract §Fixtures: `virgin_panel_12v` (machineType `0xFF`, fwType `0x0004`) anchored to a **real bench capture**; `eden_xp`(`0x03`) / `optimus_xp`(`0x0A`) / `r3l_xp`(`0x0B`) / `eden_bs8`(`0x0C`) at fwType `0x0004`; `virgin_panel_24v` (`0xFF`, fwType `0x000F`); `unknown_toplift_a` (`0x08` → `Unknown 0x08uy`); keep `malformed_too_short_14b` (14 bytes); **remove `malformed_wrong_fwtype`** (fwType is no longer a rejection axis). Update the `$schema-description`. (FR-001/003/007; SC-001 anchor)
  - **`virgin_panel_12v` MUST be a verbatim real capture — it is load-bearing, not a nice-to-have.** The `Category=Hardware` E2E (T024) is dormant by default (env-gated — it runs only on a bench with `BPT_HARDWARE=1`, never on CI; see #142), so this CI-runnable parse of real bytes in `WhoIAmFrameFixtureTests` is the *only* wire-format regression guard that runs on every PR. A reconstructed payload would re-create the #121 false-green (synthetic fixtures that agree with a wrong codec). Capture the bytes off the PEAK wire and paste them verbatim.
- [X] T005 **[CORRECT]** Correct `tests/ButtonPanelTester.Tests/Property/Can/WhoIAmFrameProperties.fs`: `WhoIAmFrameRoundtrip` generates an arbitrary `fwType: uint16` (drop the fixed `0x04uy`) — round-trip now holds for all frames; **keep** `WhoIAmFrameRejectsWrongLength` (length-only, FR-007); **delete** `WhoIAmFrameRejectsMalformedFwType` (no fwType reject). Rewrite the XML doc to drop the "well-formed = fwType 0x04" language and cite the corrected contract §Parse contract + re-stated Lean `parse_encode_roundtrip`. (Constitution II; FR-007)
- [X] T006 **[CORRECT]** Update `tests/ButtonPanelTester.Tests/Unit/Can/WhoIAmFrameFixtureTests.fs` to the rebuilt fixtures: rename the fixture `[<Fact>]`s to the new set; add a `virgin_panel_24v` case (asserts fwType `0x000F` round-trips) and an `unknown_toplift_a` case (asserts `Unknown 0x08uy`); **remove** the `malformed_wrong_fwtype` fact; widen the in-memory `Fixture.ExpectedFwType` to `uint16` (`GetUInt16`). (FR-003/007)
- [X] T007 **[CORRECT]** Correct `src/ButtonPanelTester.Core/Can/WhoIAmFrame.fs`: `FwType of byte` → `FwType of uint16`; `parse` reads `fwType` via `BinaryPrimitives.ReadUInt16BigEndian(span.Slice(1,2))`, UUID words at offsets `3/7/11`, accepts any `fwType` (**length-only** reject — drop the `fwType <> 0x04uy` gate and the `buttonPanelFwType` binding); `encode` writes 15 bytes with **no** padding (`[14]` = UUID2 low byte). Rewrite the type/parse/encode XML docs to the corrected layout (fwType uint16 @1-2, UUID @3/7/11, no padding, length-only reject) citing the re-stated `parse_encode_roundtrip` + contract. Lands in the **same commit** as T004–T006 so the suite is GREEN (bisect-safe). (FR-001/002/003/007; enables SC-001)
- [X] T008 [P] **[RE-POINT]** Re-point `src/ButtonPanelTester.Core/Can/Pruning.fs` citation `specs/002-can-link-and-panel-discovery/data-model.md §5.2` → `specs/003-panel-discovery/data-model.md §4` and refresh the stale `FR-011`→`FR-005` ref in the same comment block (comment-only; leave the `(T031)` historical authoring stamp). Rides in the A2 commit.

**Checkpoint A**: every real WHO_I_AM frame parses; `lake build` + the corrected FsCheck/fixture suites are green; the discovery foundation's citations point at spec-003. SC-001 is now reachable.

---

## Phase B: Discovery pipeline (User Story 1) — GROW the stub service to live

**Goal**: grow `PanelDiscoveryService` from the #197 parameterless stub (empty map +
never-firing observable) into the live ingest → parse → observe → prune → clear pipeline over
the shipped ports. Service-level behaviours (end-to-end wiring, timing, link-loss) are
**example-based integration tests** over the virtual adapter + `FrozenClock` — the plan's
one-line rationale: they assert wiring/timing across threads, not a pure-function law.

**Threading**: a single `mutable PanelsOnBus` guarded by a private `obj()` lock, **never held
across an await** (stem-fp-discipline §8); three input threads (read / 1 s timer / link-emission).
**Commit grouping**: B1 = {T009, T010, T011, T012}; B2 = {T013, T014}; B3 = {T015, T016} —
each a vertical slice landing impl + its integration test together.

- [X] T009 [US1] **[GROW]** Grow the ingest path in `src/ButtonPanelTester.Services/Can/PanelDiscoveryService.fs`: replace the parameterless ctor with `(frameStream: ICanFrameStream, link: ICanLinkService, clock: IClock)`; hold one `mutable PanelsOnBus` behind a private `obj()` lock; subscribe to `frameStream.RawFramesReceived`; on each frame, when `link.CurrentState` is `Connected` **and** `CanId = 0x1FFFFFFFu` **and** `Payload.Length = 15`, `WhoIAmFrame.parse` it and on `Some f` apply `PanelsOnBus.observe (clock.UtcNow()) f`, then publish; `None` and not-`Connected` are silent drops (FR-007). Make `DiscoveryObservable.SubjectFanOut` actually publish on each recompute (the stub's `OnNext` is never called) and give `Subscribe` a real unsubscribe — the shipped no-op `ConcurrentBag` remove leaks, so move to a lock-guarded observer list whose `Dispose` truly detaches (research R5 caveat); `member _.PanelsOnBus` returns the live map. Because the 3-arg ctor breaks the only existing call site, **B1 also rewires the `IPanelDiscoveryService` registration in `src/ButtonPanelTester.GUI/Composition/CompositionRoot.fs`** to resolve the already-bound `ICanFrameStream` / `ICanLinkService` / `IClock` (keeping the `NoOpCanFrameStream` placeholder — the live PEAK-stream swap is Phase C / T018) so the whole-solution Release build stays green (bisect-safe). (FR-001/002/004)
- [X] T010 [US1] **[NEW]** Add `tests/ButtonPanelTester.Tests/Integration/Can/DiscoveryE2ETests.fs` over `InMemoryCanFrameStream` + `InMemoryCanLink` + `FrozenClock`: (a) single broadcast while Connected → 1-row map with UUID + decoded variant; (b) same UUID re-broadcast → 1 row, `LastSeen` advanced in place, no duplicate (FR-002/SC-002); (c) two distinct UUIDs → 2 rows; (d) `Payload.Length = 14` → no row, and the link stays `Connected` (no Error flip — FR-007 both clauses); (e) `CanId ≠ 0x1FFFFFFF` → no row; (f) frame while not Connected → no row; (g) a frame at `t0` surfaces a row within the SC-001 ≤6 s window under `FrozenClock`. The `Connected` state is supplied by a **real `CanLinkService` wrapping `InMemoryCanLink`** (R6 — discovery binds to the `ICanLinkService` capability, not a mock); case (b) advances `FrozenClock` between two re-broadcasts, so T010 extends `InMemoryCanFrameStream` with a synchronous `member _.Emit(frame)` (deterministic per-frame emission — `Start`'s timed walk cannot advance the frozen clock mid-sequence). Lands with T009. (FR-001/002/004/007; SC-001/SC-002)
- [X] T011 [P] [US1] **[RE-POINT]** Re-point `src/ButtonPanelTester.Core/Can/Ports.fs` `RawCanFrame` citation `specs/002-can-link-and-panel-discovery/contracts/can-frame-stream-port.md` → `specs/003-panel-discovery/contracts/can-frame-stream-port.md` and fix the stale research-section ref `research.md R7`→`R3` in the same comment (the allocation-free-`[<Struct>]` rationale is R3 in spec-003 research; R7 is now `fwType`-informational) (comment-only). Also fix the one `ICanFrameStream` doc line the slice makes stale in the same file: B1 moves the frame filter into `PanelDiscoveryService`, so `happens in the service layer (`CanLinkService`)` → `(`PanelDiscoveryService`)` (the `can-frame-stream-port.md` contract already attributes the filter there). Leave the `ICanLink` lifecycle block (spec-002-lifecycle-owned) and its remaining provenance untouched (deferred to the Polish citation sweep). Rides in the B1 commit.
- [X] T012 [P] [US1] **[RE-POINT]** Re-point the subject-pattern citation in `src/ButtonPanelTester.Services/Can/PanelDiscoveryService.fs` `specs/002-can-link-lifecycle/research.md R4` → `specs/003-panel-discovery/research.md R5` (the hand-rolled hot-observable decision is now documented in spec-003 R5; plan §Citation re-pointing borderline → resolved to re-point). Rides in the B1 commit.
- [X] T013 [US1] **[GROW]** Add the prune timer to `PanelDiscoveryService`: one `System.Threading.Timer` created + started in the ctor, ticking every 1 s; each tick applies `Pruning.prune (TimeSpan.FromSeconds 15.0) (clock.UtcNow())` to the held map under `panelsLock` and publishes **only when the row count changed** (idle-render suppression, backed by `prune_idempotent`; publish fires OUTSIDE the lock, same discipline as `onFrame`). The timer runs unconditionally — a tick over the empty map is a no-op, so it self-quiesces when no panels are present; the literal "start on `Connected`" gate is dropped (it would need B3's `LinkStateChanged` subscription, and B3's clear empties the map on disconnect anyway). The service now implements `IDisposable` (stops + disposes the timer and disposes the held `_frameSubscription`). Since a real `Timer` runs on wall-clock time and cannot be stepped by `FrozenClock`, expose a public `member _.RunPruneTick()` (the exact body the timer invokes) so the E2E test drives ticks deterministically. Making the service `IDisposable` promotes FS0760 to a hard error under the repo-wide `TreatWarningsAsErrors`, so this slice also prepends `new` at the 8 existing bare-construction sites — `CompositionRoot.fs` (1) + `DiscoveryE2ETests.fs` (7), keyword-only, no `use`/dispose. (FR-005; research R4)
- [X] T014 [US1] **[NEW]** Add `tests/ButtonPanelTester.Tests/Integration/Can/PruningE2ETests.fs`, driving prune via `svc.RunPruneTick()` (not the wall-clock timer) over `FrozenClock` and reusing the B1 pattern (real `CanLinkService` wrapping `InMemoryCanLink` + `InMemoryCanFrameStream.Emit`) to seat a row: `t + 15s` exactly → `RunPruneTick` keeps the row (kept-iff-`≤ttl`); `t + 16s` → `RunPruneTick` prunes it and publishes **exactly once**; a `RunPruneTick` with nothing expiring emits **no** duplicate `PanelsOnBusChanged`. Dispose the service at the end (exercises `Dispose`). Lands with T013. (FR-005)
- [X] T015 [US1] **[GROW]** Wire the FR-008 link-loss clear in `PanelDiscoveryService`: subscribe to `link.LinkStateChanged` (held like `_frameSubscription`, disposed in the extended `Dispose`); on any emission whose new state is **not** `Connected`, apply `PanelsOnBus.clear` under `panelsLock` and publish the empty snapshot **outside** the lock, **publish-on-change** (a clear over an already-empty map stays silent — so only the genuine `Connected`→non-`Connected` transition emits, since rows exist only while `Connected`). The empty fires immediately off the link event, independent of the prune timer (list empties on disconnect, not after a TTL). No ctor/construction change (the service is already `IDisposable` from B2), so no FS0760 ripple. (FR-008; SC-004)
- [X] T016 [US1] **[NEW]** Add `tests/ButtonPanelTester.Tests/Integration/Can/LinkLossClearsListTests.fs`, over a real `CanLinkService` wrapping a scripted `InMemoryCanLink` `[Connected; Disconnected(MidSessionUnplug)]` — construct the discovery service **before** driving the link (so it catches the disconnect) and advance to the second scripted state with a **second `InitializeAsync`** (re-`Open` dequeues the next state without `ReconnectAsync`'s synthesized `ReconnectPending`, so the clear fires on the scripted `MidSessionUnplug`): (1) `Connected` → observe a panel → `Disconnected` emits `empty` **exactly once** on the transition, not after the prune TTL; (2) a disconnect with an already-empty list emits **no** publish (publish-on-change). Lands with T015. (FR-008; SC-004)

**Checkpoint B**: on the virtual adapter + `FrozenClock`, a frame produces a row, re-broadcasts coalesce in place, rows prune at 15 s, and link-loss clears immediately — the whole pipeline greens on CI with no hardware.

---

## Phase C: Production adapter + composition (User Story 1)

**Goal**: write the production `ICanFrameStream` adapter and wire the live service into the GUI
host, replacing the `NoOpCanFrameStream` placeholder and the stub ctor.

- [X] T017 [US1] **[NEW]** Add `src/ButtonPanelTester.Infrastructure/Can/PcanCanFrameStream.fs` (`net10.0-windows`) implementing `ICanFrameStream` per [contracts/can-frame-stream-port.md](./contracts/can-frame-stream-port.md) §Production: ctor takes the **new `CanPortShare` holder** (`Infrastructure/Can/CanPortShare.fs`, also this slice) + `IClock` + `ILogger<PcanCanFrameStream>` — NOT the raw port: the shared-host design keeps `PcanCanLink`'s lazy build (its `portFactory` becomes `share.GetOrBuild`), and `PcanCanFrameStream` attaches to the built port's `PacketReceived` via `share.OnBuilt` (deferred — no eager PEAK build at composition). Translate each `RawPacket` → `RawCanFrame`: `CanId` = `ReadUInt32LittleEndian(payload[0..4])` (`CanPort` carries the arbitration ID as a 4-byte LE in-band prefix), `Payload` = a zero-copy `ReadOnlyMemory<byte>` over `payload[4..]` (the CAN data; **re-scope correction: the original "15-byte WHO_I_AM is one CAN-FD frame" note is WRONG — it is multi-frame *classic* CAN reassembled by the Network Layer; see the Re-scope banner + Phase R + the wire contract**), `ReceivedAt` = `RawPacket.Timestamp` (→ UTC `DateTimeOffset`) falling back to `clock.UtcNow()`; allocate nothing per frame beyond the struct; hand-rolled `IObservable<RawCanFrame>` (gated-list, real unsubscribe); `IDisposable` detaches the handler. (FR-001 receive side; FR-009 — receive-only, no send surface)
- [X] T018 [US1] **[EXTEND]** Extend `src/ButtonPanelTester.GUI/Composition/CompositionRoot.fs`: register the `CanPortShare` singleton (its `GetOrBuild` becomes `PcanCanLink`'s `portFactory`, so one PEAK handle serves both lifecycle + receive — resolving the PR-C "single consumer of `ICommunicationPort`" comment); bind `ICanFrameStream → new PcanCanFrameStream(share, clock, logger)` (`new` per FS0760 — it is `IDisposable`); delete the private `NoOpCanFrameStream`. The `IPanelDiscoveryService` registration is **already** the 3-arg `PanelDiscoveryService(frameStream, canLinkService, clock)` form from B1/T009, so T018 only redirects which `ICanFrameStream` the graph resolves (NoOp → Pcan). Preserve the lazy-PCANManager construction (no eager P/Invoke before `OpenAsync`). Add a CI composition smoke (`tests/ButtonPanelTester.Tests.Windows/Integration/Can/CompositionRootCanTests.fs`): `CompositionRoot.configure` → `BuildServiceProvider` → resolve `ICanFrameStream` / `ICanLink` / `IPanelDiscoveryService` and assert the resolved `ICanFrameStream` is `PcanCanFrameStream` (not the `NoOp`) — proving the wiring with **no hardware** (the lazy `CanPortShare` build means resolution never P/Invokes `pcanbasic.dll`); dispose the provider via `DisposeAsync` (`PcanCanLink` is `IAsyncDisposable`). RED = the pre-rewire `Assert.IsType<PcanCanFrameStream>` failing against the `NoOp` binding. The real PEAK frame-flow is the bench / Phase-E proof (runnable now — hardware on hand). (FR-001/008/009)
- [X] T019 [P] [US1] **[NEW]** Add `tests/ButtonPanelTester.Tests.Windows/Unit/Can/PcanCanFrameStreamTests.fs` covering the `RawPacket` → `RawCanFrame` translation (LE-arbId decode, data-slice payload, timestamp/clock fallback). The vendored `PacketReceived` **is** raisable without hardware: drive a file-private fake `ICommunicationPort` (the shipped `FakeCommunicationPort` pattern — `Event<EventHandler<RawPacket>, RawPacket>` + `[<CLIEvent>]` + a `RaisePacket` member) through the `CanPortShare`, raise a synthetic WHO_I_AM packet, assert the translated `RawCanFrame`. No hardware — the live-boundary gap is the real PEAK frame-flow + sharing, which Phase E (T024) proves. (FR-001 receive side)

**Checkpoint C**: the GUI host binds the real PEAK receive adapter and the live discovery service; the app composes + launches (on a host without the PEAK driver it still launches — the link surfaces `Error`, the discovery list stays empty).

---

## Phase R: Receive-path re-scope (User Story 1) — segmented WHO_I_AM transport

**Goal**: the 2026-06-09 bench re-scope (plan §Re-scope). `WHO_I_AM` is a *segmented*
multi-frame SP_APP message, not a single frame, and the vendored receive loop was never
started — so the CI-green pipeline produced nothing on real hardware. Fix the read loop,
reassemble the fragments, and re-source discovery onto the reassembled feed. **Executes after
Phase C and before Phase D** (this section's position = execution order; T033+ is append order).

**Commit grouping**: R1 = {T033, T034}; R2 = {T035, T036, T037}; R3 = {T038, T039, T040} — each a
vertical slice landing impl + its tests together.

- [X] T033 **[CORRECT]** Activate the receive loop in the vendored stack. In `src/ButtonPanelTester.Infrastructure.Protocol/Hardware/IPcanDriver.cs` expose `void StartReading();`; in `CanPort.ConnectAsync` (`Hardware/CanPort.cs`), once the driver reports `Connected`, call `_driver.StartReading()`. Make `PCANManager.StartReading` **idempotent** (the monitor's reconnect branches already call it and it spawns `_readTask = Task.Run(...)` unconditionally — guard so a second call does not start a second read loop). Root cause (bench 2026-06-09): `Initialize` sets `IsConnected` but never starts reading on a clean open, and the monitor only calls `StartReading` on a reconnect, so `PacketReceived` never fires and nothing is received. Reading is spec-003's domain (#151 split); folds in closed #203. (FR-001 receive side; prerequisite for SC-001 on hardware)
- [X] T034 **[NEW]** Add a CI regression for T033 (no hardware): drive `CanPort` over a fake `IPcanDriver` (the shipped fake-driver / `FakeCommunicationPort` pattern) whose `StartReading` increments a counter; assert `ConnectAsync` (driver reporting connected) calls `StartReading` **exactly once**, and a subsequent reconnect does not start a second read loop. Lands with T033. (FR-001 receive side)
- [X] T035 [US1] **[NEW]** Add the reassembled-WHO_I_AM port `IWhoIAmObserver` (`WhoIAmObserved : IObservable<WhoIAmFrame>`) in **`src/ButtonPanelTester.Core/Can/`** (Constitution III — ports live in `Core`; pairs with the existing `ICanFrameStream` + `WhoIAmFrame` there). It is the input `PanelDiscoveryService` consumes instead of the raw `ICanFrameStream`; its only production adapter is the R2 reassembler (Infrastructure), with a trivial in-memory fake for tests. (FR-001)
- [X] T036 [US1] **[NEW]** Add the WHO_I_AM reassembly adapter in `src/ButtonPanelTester.Infrastructure/Can/` implementing `IWhoIAmObserver` over the raw `ICanFrameStream.RawFramesReceived`: per frame, when `CanId = 0x1FFFFFFFu`, feed `Payload` to `Services.Protocol.PacketReassembler.Accept`; on a complete reassembled packet, check command bytes `[7..8] = 0x00,0x24` (`SP_APP_CMD_AA_WHO_I_AM`), extract the application payload `packet[9 .. len-2]` (reuse `PacketDecoder`'s `ApplicationPayloadStart = 9` / `CrcTailLength = 2` — name them locally, cite the contract), `WhoIAmFrame.parse` it, emit on `Some f`. Every drop axis (wrong id / incomplete / wrong command / bad length) is a silent non-event (FR-007). Reuse the hand-rolled `SubjectFanOut` fan-out. Do **not** use the dictionary-driven `PacketDecoder`. Per [contracts/who-i-am-wire-format.md](./contracts/who-i-am-wire-format.md) §Transport + §Reassembled SP_APP application packet. (FR-001/002/003/007)
- [X] T037 [US1] **[NEW]** Add transport fixtures + adapter unit tests for T036 (no hardware): `whoiam_5frame_virgin_12v` = the verbatim real 5-frame split from the bench trace (contract §Fixtures); `whoiam_missing_fragment` (incomplete → no emission); `whoiam_wrong_command` (reassembles but `cmd ≠ 0x0024` → dropped); `whoiam_wrong_length` (reassembles to a payload length ≠ 15 → `parse` returns `None` → dropped — **absorbs old `DiscoveryE2ETests` case (d)**); `nonbroadcast_id` (id ≠ 0x1FFFFFFF → ignored — **absorbs old case (e)**); `interleaved_packetids` (two concurrent `PacketId`s reassemble independently). Drive the adapter via `InMemoryCanFrameStream.Emit`; assert the emitted `WhoIAmFrame` equals the §1 `virgin_panel_12v` parse on the happy path, and that **every** drop axis is a silent non-event — **no emission, no exception** (the adapter has no CAN-status surface, so a drop structurally cannot flip the link to Error: FR-007). Lands with T035/T036. (FR-001/007)
- [X] T038 [US1] **[GROW]** Re-source `PanelDiscoveryService` (`src/ButtonPanelTester.Services/Can/PanelDiscoveryService.fs`): ctor takes `IWhoIAmObserver` in place of `ICanFrameStream`; subscribe to `WhoIAmObserved`; the on-observed handler **drops the inline `CanId = 0x1FFFFFFF ∧ Payload.Length = 15 ∧ parse` filter** (the adapter owns reassembly/command/parse) and, when `link.CurrentState = Connected`, applies `PanelsOnBus.observe (clock.UtcNow()) f` + publishes (not-Connected = ignored). Coalesce / prune (B2) / link-loss-clear (B3) logic is **unchanged**. Supersedes T009's raw-frame ingest. (FR-001/002/004)
- [X] T039 [US1] **[EXTEND]** Rewire `src/ButtonPanelTester.GUI/Composition/CompositionRoot.fs`: register the reassembly adapter as `IWhoIAmObserver` (consuming the already-bound `ICanFrameStream`); resolve `IPanelDiscoveryService` from `IWhoIAmObserver` + `ICanLinkService` + `IClock` (was `ICanFrameStream` + …). `PcanCanFrameStream` / `CanPortShare` (C1/C2) bindings unchanged — the adapter layers on top. Extend the C2 composition smoke (`CompositionRootCanTests`) to also resolve `IWhoIAmObserver`. Lands with T038. (FR-001)
- [X] T040 [US1] **[CORRECT]** Rework the B-phase integration-test setups (`DiscoveryE2ETests`, `PruningE2ETests`, `LinkLossClearsListTests`) for the re-sourced ctor: either drive the **real** reassembly adapter with `InMemoryCanFrameStream` emitting multi-frame WHO_I_AM sequences (preferred — exercises reassembly end-to-end), or inject a trivial fake `IWhoIAmObserver`. The coalesce/prune/clear **assertions** are unchanged. **Relocate** old `DiscoveryE2ETests` cases (d) (`Payload.Length = 14`) and (e) (`CanId ≠ 0x1FFFFFFF`) into the adapter test (T037) — post-re-source the service never sees those frames (the adapter filters them), so keeping the assertions at the service layer is vacuous; do not merely re-plumb them. **Add** one integration case over a real `CanLinkService`: a malformed/incomplete multi-frame sequence while `Connected` produces no row **and** the link stays `Connected` (FR-007 no-Error-flip, end-to-end — the assertion that needs the link service, which the adapter unit test cannot make). Lands with T038. (FR-002/004/005/007/008)

**Checkpoint R**: with the read loop running and the reassembly adapter in place, a WHO_I_AM (synthetic multi-frame on CI, real on the bench) produces a discovery row — the receive path is correct end-to-end, validated against the real bench trace.

---

## Phase D: GUI (User Story 1)

**Goal**: render the live Panels-on-bus list in the third vertical slot, with the FR-006
empty-state explainer.

- [X] T020 [US1] **[NEW]** Add `src/ButtonPanelTester.GUI/Can/PanelsOnBusView.fs`: a FuncUI view over `PanelsOnBus` — one row per panel showing the UUID (hex triple), the decoded `VariantIdentity` label (marketing name / "virgin" / "unknown"), the **raw variant byte via a detail affordance** for Virgin/Unknown (FR-003), and the last-seen timestamp (FR-004); an empty-state explainer that distinguishes "the link is down" from "link up, nothing announcing" off `ICanLinkService.CurrentState` (FR-006). (FR-003/004/006)
- [X] T021 [US1] **[EXTEND]** Extend `src/ButtonPanelTester.GUI/App.fs`: resolve `IPanelDiscoveryService` in `MainWindow`; fill the third slot of the vertical stack (`DictionaryStatusRow / CanStatusRow / PanelsOnBusView`) with `PanelsOnBusView`; subscribe to `IPanelDiscoveryService.PanelsOnBusChanged` (and reuse the existing `LinkStateChanged` for the empty-state) via `Observable.subscribe` marshalled onto `Dispatcher.UIThread` — the same `Cmd.ofSub`-style pattern already used for `SourceChanged`/`LinkStateChanged`; hold the latest `PanelsOnBus` in a `mutable` cell and `renderCombined ()`. (FR-006/008; SC-004)
- [X] T022 [P] [US1] **[NEW]** Add `tests/ButtonPanelTester.Tests.Windows/Gui/Can/PanelsOnBusViewTests.fs` (`Avalonia.Headless.XUnit`): empty + Connected → "link up, nothing announcing" text; empty + not-Connected → "link is down" text (FR-006); one virgin row → UUID hex + "virgin" + last-seen; re-broadcast → row updates in place, no duplicate; unknown variant byte → "unknown" label with the raw byte in the detail affordance (FR-003). Lands with T020/T021. (FR-003/004/006)

**Checkpoint D**: the Panels-on-bus row renders live; the empty state distinguishes the two cases; Headless snapshot tests green.

---

## Phase E: Hardware E2E (User Story 1) — the live-boundary proof for the #121 codec

**Goal**: prove a **real** virgin panel appears within 6 s with **zero** tool-originated
frames — the proof synthetic fixtures + Headless tests cannot give. The #121 bug survived
precisely because synthetic fixtures + unit/Headless tests stayed green while the real PEAK
wire path was broken (live-boundary-smoke). Shape this E2E so a wire-format regression fails
loudly at the CAN boundary.

> **Dormancy caveat (from #142).** This suite is **env-gated and dormant by default** — it runs
> only on a bench with `BPT_HARDWARE=1`, never on CI. An unconditional `[<Fact(Skip = "…")>]`
> is worse still: xunit short-circuits at attribute-binding before any filter/probe, so the
> test never runs *anywhere* and "coverage becomes folklore" (#142's words — the same false-green
> pattern as #121). So Phase E is the periodic *live re-confirmation*, **not** the everyday
> regression guard — that guard is T004's CI-runnable real-capture fixture. Use the shipped
> env-gated attributes only; never a bare `Skip` literal.

- [ ] T023 [US1] **[NEW]** Establish the **live** CAN-hardware bench-suite tracking issue for the discovery E2E. History: **#112** was a deliberate cross-spec *living tracker* ("add the discovery suite here rather than opening a per-spec issue") but closed COMPLETED 2026-06-03 with its spec-003 checkbox unticked; **#116** (the issue it deferred the discovery suite to) is `CLOSED/NOT_PLANNED` — the old plan was abandoned for the #153 re-spec; **#142** (`[<HardwareFact>]` infra) is also closed. So the "add it to #112" path is dead and no live hardware tracker exists. Decide one: **reopen #112** (restore the living tracker — most faithful to its design), track the suite under the live re-spec issue **#153**'s bench-validation follow-up, or open a fresh `chore(test): bench config for spec-003 Category=Hardware E2E`. A `[<Trait("Category","Hardware")>]` test MUST carry a **live** tracking-issue link (Constitution IV — "no untagged skip, no exclusion without a linked tracking issue").
- [ ] T024 [US1] **[NEW]** Add `tests/ButtonPanelTester.Tests.Windows/Integration/Can/Hardware/DiscoveryHardwareTests.fs`, every case `[<Trait("Category","Hardware")>]` (excluded by the default `Category!=Hardware` CI filter) and linked to the T023 issue: `[<HardwareFact>]` (env-gated `BPT_HARDWARE=1`, unattended) — with a real virgin panel powered on the bus, assert `PanelsOnBusChanged` emits a 1-row map decoding to `Virgin` within 6 s (SC-001) **and** a bus capture shows zero frames originating from the tool (FR-009/SC-003); `[<ManualHardwareFact>]` (env-gated `BPT_HARDWARE_INTERACTIVE=1`, attended) — operator powers the panel off, assert the row prunes within ~16 s (FR-005). The assertions read the **parsed UUID + variant off the reassembled real wire message** (the full read-loop → reassemble → discovery chain, Phase R), so a codec **or transport** regression fails here even when the synthetic suite is green; this E2E is seeded by the throwaway `PanelDiscoverySmoke.fs`. **Never** use a bare `[<Fact(Skip = "…")>]` literal (#142: unconditional `Skip` makes the case dormant everywhere); the shipped `[<HardwareFact>]` / `[<ManualHardwareFact>]` + the `Category=Hardware` trait are the only gating mechanism. (SC-001/SC-003; FR-005/009)

**Checkpoint E**: on a bench rig (`BPT_HARDWARE=1`), a real virgin panel appears within 6 s with zero tool-originated frames; the wire-format boundary is proven against live firmware, closing the #121 false-green gap. Per the Validation Gate, the feature is **not "Done"** until this bench artifact exists.

---

## Phase F: Polish & cross-cutting concerns

- [ ] T025 [P] **[NEW]** XML-doc the new public surfaces per COMMENTS / stem-fp-discipline §10: `PcanCanFrameStream`, `PanelsOnBusView`; confirm the corrected `WhoIAmFrame` / `FwType` docs (T007) cite the re-stated Lean theorem + corrected contract (Lean-citation format: path + `parse_encode_roundtrip` + task number).
- [ ] T026 [P] **[NEW]** Logging audit per LOGGING / stem-logging over `PcanCanFrameStream` and the discovery branches of `PanelDiscoveryService`: typed `ILogger<T>`, template messages with named params (no string interpolation), exception-as-first-arg, no `Console.WriteLine` / `Debug.WriteLine` on the production path.
- [ ] T027 [P] Principle V compliance grep over the discovery path: confirm **zero** OS-user / machine-name / SID / MAC fields cross to STEM-controlled storage — the discovery wire surface is panel-side `WHO_I_AM` payloads in volatile UI memory only (research R8). Expected zero hits.
- [ ] T028 [P] FR-009 / SC-003 zero-transmit audit: grep the discovery path (`PcanCanFrameStream`, the R2 reassembly adapter, `PanelDiscoveryService`) for any CAN send/write call; confirm the `ICanFrameStream` and `IWhoIAmObserver` ports have no send surface and nothing on the discovery path transmits.
- [ ] T029 [P] `cd lean; lake build` — confirm the four Phase-2 discovery theorems compile with no `sorry`; `#print axioms parse_encode_roundtrip` (and the three unchanged theorems) shows only standard axioms.
- [ ] T030 [P] Add a `CHANGELOG.md` `[Unreleased]` entry: "Passive CAN panel discovery — Panels-on-bus list (spec-003)."
- [ ] T031 [P] Update `README.md`: link `specs/003-panel-discovery/quickstart.md` and add a one-paragraph mention of the Panels-on-bus list.
- [ ] T032 quickstart.md bench validation: confirm SC-001 (panel ≤6 s) and SC-002 (no duplicate rows on re-broadcast) on a real bench — the operator/bench follow-up that gates the "Done" claim (live-boundary-smoke Validation Gate; pairs with T024).

---

## Dependencies & Execution Order

### Phase order (strict)

`Setup (T001)` → **`Phase A` (foundational — blocks everything)** → `Phase B` → `Phase C` →
**`Phase R` (receive-path re-scope)** → `Phase D` → `Phase E` → `Polish`. Phase A is
non-negotiably first: no real frame parses without it. Phase R depends on Phase C (it layers
on the C1/C2 receive graph) and gates real-hardware discovery (R1 read loop + R2 reassembly +
R3 re-source); Phase D depends on Phase R's re-sourced service; Phase E proves the full chain.

### Commit groupings (bisect-safe; test + impl land together)

- **A1** = {T002, T003} — Lean only; `lake build` green.
- **A2** = {T004, T005, T006, T007, T008} — corrected fixtures + FsCheck + fixture tests + `WhoIAmFrame.fs` + `Pruning.fs` re-point, **one commit** (RED until T007).
- **B1** = {T009, T010, T011, T012} · **B2** = {T013, T014} · **B3** = {T015, T016}.
- **C1** = {T017, T019} (PcanCanFrameStream + CanPortShare + the no-hardware translation test; `NoOpCanFrameStream` stays bound → bisect-safe) · **C2** = {T018} (composition flip: register `CanPortShare`, rewire both adapters, drop `NoOpCanFrameStream`, `new` FS0760 fix; + a CI composition smoke resolving the graph hardware-free, RED vs `NoOp`).
- **R1** = {T033, T034} (read-loop activation + fake-driver regression) · **R2** = {T035, T036, T037} (`IWhoIAmObserver` port + reassembly adapter + transport fixtures) · **R3** = {T038, T039, T040} (re-source `PanelDiscoveryService` + composition rewire + B-test rework).
- **D** = {T020, T021, T022} (T022 [P]) · **E** = T023 then T024 · **Polish** = T025–T032 (mostly [P]).

### Within-phase notes

- **Phase A carries a one-time bench-hardware dependency.** T004's `virgin_panel_12v` fixture MUST be a verbatim real PEAK-wire capture — the only PR-time wire-format guard while the hardware E2E (T024) is dormant. Capturing it needs bench access *before* Phase A can close, even though the bench tracker (T023) is not established until Phase E. Schedule the capture as a Phase-A input; never substitute a reconstructed payload (it re-creates the #121 false-green).
- `PanelDiscoveryService.fs` is edited by T009/T012 (B1), T013 (B2), T015 (B3) — sequential edits on one file; keep the slices ordered to avoid rebase pain. B1 additionally makes a minimal registration touch in `CompositionRoot.fs` (3-arg ctor rewire, `NoOpCanFrameStream` retained) and a one-line synchronous `Emit` extension to the shipped `InMemoryCanFrameStream` fake; the full NoOp→Pcan composition swap is T018 (Phase C).
- Phase B depends on Phase A's corrected `WhoIAmFrame.parse`. Phase C depends on Phase B's live service ctor. **Phase R depends on Phase C** (it layers the reassembly adapter on the C1/C2 graph and starts the read loop); R1 → R2 → R3 in order (R3 re-sources the service onto R2's port). **Phase D depends on Phase R** (binds the re-sourced service). Phase E depends on Phases C+R+D (real adapter + reassembly + render) for a bench run.

### Parallel opportunities

- T003 / T008 / T011 / T012 — comment-only re-points, independent files (each still commits *with* its riding slice, not standalone).
- The three integration test files (T010, T014, T016) are mutually independent once their pipeline pieces exist.
- T019 (Windows unit test) and T022 (Headless snapshot) are independent files.
- Polish T025–T031 are mutually parallel; T032 is the bench follow-up (not [P]).

---

## FR / SC → task coverage matrix

*(The downstream `/speckit-analyze` coverage pass keys off this — every FR and SC maps to at
least one implementing task and one test.)*

| Requirement | Implementing task(s) | Test(s) | Shipped proof preserved |
|---|---|---|---|
| **FR-001** listen while Connected, present each panel | T007, T009, T017, T033, T036, T038, T020, T021 | T010, T034, T037, T024 | — |
| **FR-002** UUID coalesce, no duplicates | T009 (`observe`) | T010(b), T022 | Lean `observe_coalesces_by_uuid` + FsCheck `PanelsOnBusCoalescing` (re-pointed T003) |
| **FR-003** decode variant; raw byte via detail | T004, T006, T020 | T022 | Lean `variant_decoding_total` + FsCheck `VariantByteMappingTotal` (re-pointed T003) |
| **FR-004** last-seen, update in place | T009, T020, T021 | T010(b), T022 | — |
| **FR-005** prune after 15 s | T013 | T014, T024 | Lean `prune_partitions_by_threshold` / `prune_idempotent` (re-pointed T003/T008) |
| **FR-006** empty-state explainer (down vs silent) | T020, T021 | T022 | — |
| **FR-007** silent drop malformed; no Error flip | T002, T005, T007, T009, T036 | T005, T006, T010(d), T037 | — |
| **FR-008** clear on Connected→¬Connected | T015, T021 | T016, T022 | — |
| **FR-009** transmit zero frames | T017, T036, T028 | T024 (bus capture) | receive-only `ICanFrameStream` / `IWhoIAmObserver` (no send surface) |
| **SC-001** pristine panel visible ≤ 6 s | T007, T009, T013, T033, T036, T038, T024 | T010(g), T037, T024 | — |
| **SC-002** in-place coalesce 100%, no dup | T009 | T010(b), T022, T032 | FsCheck `PanelsOnBusCoalescing` |
| **SC-003** zero frames across session | T017, T036, T028 | T024 (capture) | — |
| **SC-004** list empty by next render after leaving Connected | T015, T021 | T016, T022 | — |

---

## Implementation Strategy

### MVP = Phase A → Phase B → Phase C → Phase R → Phase D (CI-provable US1)

1. **Phase A** — correct the wire foundation (the strict prerequisite).
2. **Phase B** — grow the pipeline; **STOP and validate** on the virtual adapter + `FrozenClock` (Checkpoint B greens US1's logic on CI without hardware).
3. **Phase C + R + D** — wire the real PEAK adapter (C); start the read loop, reassemble the segmented WHO_I_AM, and re-source the service (R); render the row (D). The app demos US1 end-to-end on a bench.
4. **Phase E** — the live-boundary bench proof gates the *Done* claim (CI-green closes the code slices; the bench closes the feature — live-boundary-smoke Validation Gate).

### Discipline

- **bisect-safe / vertical-commits**: every commit compiles + passes tests; corrected test rides with its impl (Phase A2, each Phase B slice). Conventional Commits with a `Tasks: T###` trailer linking back here.
- **Constitution order** (I): Lean → xUnit/FsCheck → F# for the closed/wire type (WhoIAmFrame, Phase A).
- **Mandatory triple** (I/II): WhoIAmFrame carries Lean `parse_encode_roundtrip` + FsCheck round-trip/length-reject + XML-doc citation; the shipped `VariantDecoder` / `PanelsOnBus` / `Pruning` triples are preserved (citations re-pointed only).
- **Gate** (`./gate.ps1`): during `/speckit-implement` extend the gate (build + the two test projects `Category!=Hardware` + `lake build` + a focused discovery `--filter`) in its own `chore: extend gate.ps1` commit; the Phase E bench run is named as the operator follow-up in the PR body, not run in CI. *(Out of scope for this task-generation run — gate.ps1 is unchanged here.)*

---

## Notes

- This tasks.md is the breakdown only — no implementation in this run.
- `[P]` = different files, no dependency on an incomplete task. `[US1]` = the single user story (Phases B–E). Phase A is foundational (no label); Setup + Polish carry no label.
- Provenance tags reflect the shipped tree: only `WhoIAmFrame.fs` (+ its Lean/fixtures/properties/fixture-tests) is **CORRECT**; `PanelDiscoveryService` is **GROW**; the adapter/view/tests are **NEW**; foundation citations are **RE-POINT**. Lifecycle-owned `specs/002-can-link-lifecycle/…` citations (e.g. `CanStatusRow.fs`, `ICanLinkService.fs`, `CanLinkState.lean`, `PcanCanLink.fs`) stay — only the discovery-foundation `002-can-link-and-panel-discovery/…` citations move to 003.
- Next: `/speckit-analyze` (cross-artifact consistency) before `/speckit-implement`.
