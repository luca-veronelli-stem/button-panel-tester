---
description: "Task list for spec-004 — Baptism Workflow: Claim and Reset Panels on the Bus"
---

# Tasks: Baptism Workflow — Claim and Reset Panels on the Bus

**Input**: Design documents from [`specs/004-baptism-workflow/`](./)

**Prerequisites**: [plan.md](./plan.md) (task authority — §Implementation phases A–F),
[spec.md](./spec.md) (US1/US2 + FR-001..014 / SC-001..006), [data-model.md](./data-model.md),
[research.md](./research.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md),
[checklists/protocol.md](./checklists/protocol.md).

**Tests**: REQUIRED. The constitution makes tests non-optional (Principle II — FsCheck is the
primary correctness mechanism for `Core`/`Services`; Principle IV — every test layer greens on
CI, hardware E2E is tagged + excluded). Closed-domain / wire types carry the mandatory triple:
**Lean theorem + FsCheck property + XML-doc citation** (Principles I/II).

**Numbering**: fresh from **T001** — per-feature numbering, spec-003 precedent.

---

## Format

`- [ ] T### [P?] [US#?] **[TAG]** Description with exact file path`

- **[P]** — parallelizable (different files, no dependency on an incomplete task).
- **[US1]** — baptize a virgin panel (P1); **[US2]** — reset a claimed panel to virgin (P2).
  Setup, the foundational Phases A–B, and Polish carry **no** story label per the spec-kit
  convention. **Deviation, noted explicitly**: the enablement slice (T027–T028) rides in
  Phase D per plan §Implementation phases but serves **both** stories (FR-002 gates US1's
  surface, FR-008 gates US2's), so it carries no story label; Phase F's bench tasks likewise
  span both stories.
- **[TAG]** — provenance against the shipped tree:
  - **NEW** — does not exist; spec-004 writes it.
  - **EXTEND** — shipped + correct; spec-004 adds a field/binding/slot without rewriting it
    (the two spec-003-owned code extensions are documented in [data-model.md](./data-model.md)
    §3 and plan §Consumed surfaces — spec-003's *documents* stay frozen).

**bisect-safe.** Every task/commit boundary MUST compile and pass tests on its own
(`bisect-safe` + `vertical-commits`). Where the constitution order (Lean → FsCheck/xUnit → F#)
would otherwise leave a RED intermediate commit, the **test and its implementation land in the
same commit** (resolve-ticket discipline). Lean lands **ahead of F# inside every slice that has
theorems** (A1 before A2/A3, C1 before C3/C4, D1 before D2/D3). Commit groupings are called out
per phase and in §Dependencies.

**F# compile order.** New `Core` files must be inserted into `ButtonPanelTester.Core.fsproj`
in dependency order — noted inline per task (F# forward references are compile errors).

---

## Phase 1: Setup

**Purpose**: establish the green baseline this branch builds on, so every later commit can be
checked bisect-safe.

- [ ] T001 Verify the green baseline on `004-baptism-workflow`: `dotnet build
      Stem.ButtonPanelTester.slnx -c Release`; `dotnet test
      tests\ButtonPanelTester.Tests\ButtonPanelTester.Tests.fsproj -c Release`; `dotnet test
      tests\ButtonPanelTester.Tests.Windows\ButtonPanelTester.Tests.Windows.fsproj -c Release
      --filter "Category!=Hardware"`; `cd lean; lake build; cd ..`. Record the result as the
      bisect-safe anchor (worktrees baseline discipline; the branch is docs-only so far —
      baseline was green at worktree creation, re-verify before the first code commit).

---

## Phase A: Wire foundation — FOUNDATIONAL (blocks all behaviour; serves US1 + US2)

**Goal**: the two TX codecs the master sequence transmits, firmware-verified per
[contracts/master-sequence-wire-format.md](./contracts/master-sequence-wire-format.md):
WHO_ARE_YOU (4 B: machineType u8, fwType u16 BE, reset byte) and SET_ADDRESS (16 B: 12 UUID
bytes byte-echoed + SP_Address u32 BE), plus the `BoardVariant` encode inverse and the
SP_Address formula (R1/R3). Without this slice nothing can be transmitted correctly.

**Mandatory triples.** `WhoAreYouFrame`: Lean `parse_encode_roundtrip`/`encode_length` (T002) ↔
FsCheck `WhoAreYouFrameRoundtrip`/`WhoAreYouFrameRejectsWrongLength` (T008) ↔ XML-doc citation
(T006). `BoardVariant` encode inverse: Lean `encode_decode_inverse` (T002) ↔ FsCheck
`VariantEncodeDecodeInverse` (T008) ↔ XML-doc citation (T005). `SetAddressFrame`: Lean
`parse_encode_roundtrip`/`encode_length` (T003) ↔ FsCheck round-trip + echo properties (T011) ↔
XML-doc citation (T009).

**TX fixtures vs spec-003's real-capture rule.** Spec-003's RX fixture had to be a verbatim
bench capture because *parse* must accept real bytes a wrong codec would synthetically agree
with. These are **TX** fixtures: the normative target is the slave's *parser*, verified against
the on-disk firmware source (`AutoAddressSlave.c`, contract §Verified) — the fixtures encode
the contract bytes, and the **live-boundary proof that real panels accept them is Phase F**
(live-boundary-smoke Validation Gate). No tool-TX capture exists yet to paste.

**Constitution order**: Lean → fixtures + FsCheck → F#.
**Commit grouping**: A1 = {T002, T003, T004} (Lean-only, green on `lake build`).
A2 = {T005, T006, T007, T008} (WHO_ARE_YOU vertical: impl + fixtures + tests, one commit).
A3 = {T009, T010, T011} (SET_ADDRESS vertical: impl + fixtures + tests, one commit).

- [X] T002 **[NEW]** Add `lean/Stem/ButtonPanelTester/Phase3/WhoAreYouFrame.lean`: model the
      4-byte codec per [data-model.md](./data-model.md) §2.1 (machineType, fwType, reset);
      prove `parse_encode_roundtrip` and `encode_length`; model `encodeVariant`
      (variant → identity byte, §1) and prove `encode_decode_inverse` against the shipped
      Phase 2 decoder model (the partial inverse of `variant_decoding_total`). `sorry`-free,
      axioms ⊆ {`propext`, `Classical.choice`, `Quot.sound`}. (Constitution I; FR-003/FR-008)
- [X] T003 [P] **[NEW]** Add `lean/Stem/ButtonPanelTester/Phase3/SetAddressFrame.lean`: model
      the 16-byte codec per [data-model.md](./data-model.md) §2.2 (3 × uint32 UUID words +
      spAddress); prove `parse_encode_roundtrip` and `encode_length`. State the round-trip in
      the byte direction too (`encode (parse b) = b` — total on 16-byte inputs), which **is**
      the contract's byte-echo invariant: encoding the UUID parsed from a WHO_I_AM reproduces
      the announced bytes verbatim (contract §SET_ADDRESS, R1). (Constitution I; FR-003/FR-004)
- [X] T004 **[NEW]** Add the umbrella `lean/Stem/ButtonPanelTester/Phase3.lean` (mirroring
      `Phase2.lean` — imports the Phase3 modules as they land) and register the new lib in
      `lean/lakefile.toml`: `[[lean_lib]] name = "Stem.ButtonPanelTester.Phase3"` + append to
      `defaultTargets`. `cd lean; lake build` green. Rides in the A1 commit.
- [X] T005 **[NEW]** Add `src/ButtonPanelTester.Core/Can/BoardVariant.fs`: the encode inverse
      `encode : MarketingVariant -> byte` over the **shipped** `MarketingVariant` DU
      (`EdenXp → 0x03uy | OptimusXp → 0x0Auy | R3LXp → 0x0Buy | EdenBs8 → 0x0Cuy`, total by
      construction) plus the virgin marker constant `0xFFuy` (reset target **only** — never a
      BoardVariant, the picker never offers it; data-model §1). XML doc cites
      `Phase3/WhoAreYouFrame.lean` `encode_decode_inverse` (T002) + data-model §1. Insert in
      `ButtonPanelTester.Core.fsproj` **after** `PanelObservation.fs` (uses
      `MarketingVariant`). (FR-001; FR-008)
- [X] T006 **[NEW]** Add `src/ButtonPanelTester.Core/Can/WhoAreYouFrame.fs`: record
      (`MachineType: byte`, `FwType: uint16`, `Reset: bool`); `encode : WhoAreYouFrame ->
      byte[]` writing 4 B (`[0]` machineType, `[1..2]` fwType **big-endian** via
      `BinaryPrimitives.WriteUInt16BigEndian`, `[3]` `0x01`/`0x00`); `parse : ReadOnlySpan ->
      WhoAreYouFrame option` (length-only reject, house codec style; non-zero `[3]` = reset
      set). XML docs cite the contract §WHO_ARE_YOU + Lean `parse_encode_roundtrip` (T002).
      Insert after `BoardVariant.fs`. (FR-003/FR-008; contract §WHO_ARE_YOU)
- [X] T007 **[NEW]** Add `tests/ButtonPanelTester.Tests/Fixtures/Can/masterSequenceFixtures.json`
      (WHO_ARE_YOU entries; T010 extends with SET_ADDRESS): `claim_eden_xp_12v`
      (`03 00 04 01`), `claim_optimus_xp_12v` (`0A 00 04 01`), `claim_r3l_xp_12v`
      (`0B 00 04 01`), `claim_eden_bs8_12v` (`0C 00 04 01`), `claim_eden_xp_24v`
      (`03 00 0F 01`), `reset_12v` (`FF 00 04 01`), `reset_24v` (`FF 00 0F 01`),
      `malformed_too_short_3b`. Bytes are the contract's normative TX payloads
      (firmware-parser-verified, see Phase A preamble). (FR-003/FR-008)
- [X] T008 **[NEW]** Add `tests/ButtonPanelTester.Tests/Property/Can/WhoAreYouFrameProperties.fs`
      (`WhoAreYouFrameRoundtrip` over arbitrary machineType/fwType/reset;
      `WhoAreYouFrameRejectsWrongLength` — length-only),
      `tests/ButtonPanelTester.Tests/Property/Can/BoardVariantProperties.fs`
      (`VariantEncodeDecodeInverse`: `decode (encode v) = Marketing v`, mirrors
      `encode_decode_inverse`), and `tests/ButtonPanelTester.Tests/Unit/Can/MasterSequenceFixtureTests.fs`
      WHO_ARE_YOU facts: for each T007 fixture assert `encode frame = fixture bytes` (TX
      direction) and the parse round-trip; `malformed_too_short_3b` → `None`. Lands with
      T005–T007 (A2 commit). (Constitution II; FR-003/FR-008)
- [X] T009 **[NEW]** Add `src/ButtonPanelTester.Core/Can/SetAddressFrame.fs`: record
      (`Uuid: PanelUuid`, `SpAddress: uint32`); `encode` writing 16 B (UUID words at
      `[0..11]` in the same convention `WhoIAmFrame.parse` reads them — the byte-echo
      invariant; spAddress `[12..15]` **big-endian**); `parse` (length-only reject). Plus the
      pure SP_Address formula `spAddress (network: byte) (machineType: byte) (fwType: uint16)
      (boardNumber: byte) : uint32 = network<<<24 ||| machineType<<<16 ||| (fwType &&&
      0x3FFus)<<<6 ||| (boardNumber &&& 0x3Fuy)` (R3 — this feature always calls
      `spAddress 0uy variantByte announcedFwType 1uy`, spec assumption: board 1). XML docs
      cite contract §SET_ADDRESS (byte-echo normative) + Lean theorems (T003). Insert after
      `WhoAreYouFrame.fs` (needs `PanelUuid`). (FR-003/FR-004; contract §SET_ADDRESS)
- [X] T010 **[NEW]** Extend `masterSequenceFixtures.json` with SET_ADDRESS entries:
      `set_address_eden_xp_12v_board1` — a fixed UUID triple + spAddress `00 03 01 01`
      (the R3 worked example: EDEN-XP/12 V/board 1 → `0x00030101` = the shipped
      `DeviceVariantConfig` "Keyboard 1" constant), one entry with a distinct UUID/variant,
      `malformed_too_short_15b`. Rides in the A3 commit. (FR-003)
- [X] T011 **[NEW]** Add `tests/ButtonPanelTester.Tests/Property/Can/SetAddressFrameProperties.fs`:
      `SetAddressFrameRoundtrip` (both directions — frame-level and `encode (parse b) = b` on
      16-byte inputs), `SetAddressFrameRejectsWrongLength`, and
      `SetAddressEchoesAnnouncedUuidBytes` — generate a valid 15-byte WHO_I_AM payload, parse
      it with the **shipped** `WhoIAmFrame.parse`, encode a `SetAddressFrame` from the parsed
      UUID, assert bytes `[0..11]` equal the WHO_I_AM payload bytes `[3..14]` verbatim
      (contract's normative invariant; mirrors T003). Extend
      `Unit/Can/MasterSequenceFixtureTests.fs` with the SET_ADDRESS fixture facts + a
      `spAddress` worked-example fact (`spAddress 0uy 0x03uy 0x0004us 1uy = 0x00030101u`).
      Lands with T009–T010 (A3 commit). (Constitution II; FR-003/FR-004)

**Checkpoint A**: both TX codecs round-trip under Lean + FsCheck, the fixtures pin the contract
bytes, the SP_Address formula reproduces the shipped constant. Nothing transmits yet.

---

## Phase B: TX port + adapters — FOUNDATIONAL (the product's first transmit boundary)

**Goal**: the single TX entry point (Constitution III): `IMasterSequenceTransmitter` port in
Core, the recording fake for CI, the production adapter over the vendored protocol stack
(#111 waiver, consumed not modified), composition wiring. Contract:
[contracts/master-sequence-transmitter-port.md](./contracts/master-sequence-transmitter-port.md).

**Commit grouping**: B1 = {T012, T013} (port + fake; compiles green, exercised from Phase C
on). B2 = {T014, T015} (production adapter + its frame-synthesis tests, one commit).
B3 = {T016} (composition wiring + smoke extension).

- [ ] T012 **[EXTEND]** Extend `src/ButtonPanelTester.Core/Can/Ports.fs` with
      `IMasterSequenceTransmitter` exactly per the port contract: `SendWhoAreYouAsync
      (machineType, fwType, reset, ct) : Task` and `SendSetAddressAsync (uuid, spAddress, ct)
      : Task`; XML docs carry the contract's semantics verbatim — write-completion contract
      (completed = written to the bus, **not** acted on), fault ⇒ service maps to
      `TransmissionFailure`, no retry, no queuing, cancellation surfaces as
      `OperationCanceledException` (never a transmission failure), FR-014 whitelist note.
      `Ports.fs` already sits after the frame modules in the fsproj (uses `PanelUuid`).
      (Constitution III; FR-014)
- [ ] T013 **[NEW]** Add `tests/ButtonPanelTester.Tests/Fakes/Can/InMemoryMasterSequenceTransmitter.fs`:
      records every send in order (`Sent : (command × fields × timestamp) list` per the
      contract's adapter table — enough to assert payload fields and ordering); scriptable
      per-call fault injection (for every `TransmissionFailure` path in Phases C/D); honors
      `ct` cooperatively. Lands with T012 (B1 commit). (Constitution III/IV)
- [ ] T014 **[NEW]** Add `src/ButtonPanelTester.Infrastructure/Can/ProtocolMasterSequenceTransmitter.fs`
      (`net10.0-windows`): synthesize the two built-in `Command` records (`0x00:0x23`
      WHO_ARE_YOU, `0x00:0x25` SET_ADDRESS — extending the existing hardcoded
      protocol-metadata set, fetch migration is #156/out of scope); encode app payloads via
      the Core codecs (T006/T009); delegate packet build / CRC16 / chunking / NetInfo framing /
      port write to the vendored `IProtocolService.SendCommandAsync`
      (`ProtocolService.cs:87-114`, R1 — no new framing code) over the shared `CanPort`
      obtained through `CanPortShare` at send time (no eager PEAK build at composition; a send
      with no built port faults, which is the `TransmissionFailure` path — FR-014 gates sends
      on `Connected` anyway). Adapter exceptions propagate per the port contract (mapping is
      the service's job). (Constitution III; FR-003/FR-014; R1/R5)
- [ ] T015 **[NEW]** Add `tests/ButtonPanelTester.Tests.Windows/Unit/Can/ProtocolMasterSequenceTransmitterTests.fs`:
      drive the adapter through `CanPortShare` over a file-private fake `ICommunicationPort`
      capturing the exact wire frames (the shipped `FakeCommunicationPort` pattern — spec-003
      Phase-C precedent): `SendWhoAreYouAsync` → **3** CAN frames on broadcast arbId
      `0x1FFFFFFF`, `SendSetAddressAsync` → **5** frames (contract §Transport); reassemble the
      captured chunks and assert the embedded app payload equals the matching
      `masterSequenceFixtures.json` bytes; a port write fault propagates as the task's
      exception. Lands with T014 (B2 commit). (FR-003/FR-014; contract §Transport)
- [ ] T016 **[EXTEND]** Extend `src/ButtonPanelTester.GUI/Composition/CompositionRoot.fs`:
      construct the vendored `IProtocolService` over the shared `CanPortShare` (same port
      instance the RX stream taps — contract adapter table) and register
      `IMasterSequenceTransmitter → ProtocolMasterSequenceTransmitter`; preserve the lazy
      PEAK construction (no P/Invoke before `OpenAsync`). Extend
      `tests/ButtonPanelTester.Tests.Windows/Integration/Can/CompositionRootCanTests.fs` to
      resolve `IMasterSequenceTransmitter` and assert it is the production adapter,
      hardware-free (lazy share — spec-003 T018 precedent). (Constitution III/IV)

**Checkpoint B**: the TX boundary exists end-to-end — port, recording fake, production adapter
whose synthesized frames are asserted byte-exact against the fixtures, composition resolves it.
Still nothing user-reachable transmits.

---

## Phase C: Baptism state machine (User Story 1)

**Goal**: the attempt FSM (`Idle → ClaimSent → AwaitingAnnounce → Assigning → terminal`, six
outcomes — data-model §4) as a pure Core transition function driven by `BaptismService` over
the existing observables (R6), plus the FR-007 post-success watch and the baptize audit
records. The 6 s window is anchored at **claim-write completion** (entry to
`AwaitingAnnounce`) — the data-model §4.1 pin resolving checklist CHK010.

**Mandatory triple (BaptismSequence).** Lean `baptize_progress` / `baptize_outcome_total` /
`no_assignment_without_match` (T017) ↔ FsCheck `BaptismOutcomeTotal` /
`BaptismSucceedsIffMatchingAnnouncement` / `ForeignUuidNeverSatisfiesWait` (T020) +
`NoSetAddressWithoutMatch` on recorded sends (T022) ↔ XML-doc citations (T019).

**Commit grouping**: C1 = {T017} (Lean-only). C2 = {T018} (additive `FwType`, suite green).
C3 = {T019, T020} (pure FSM + its properties, one commit). C4 = {T021, T022, T023, T024}
(service + composition + the three integration suites, one commit). C5 = {T025} (FR-007
watch + test). C6 = {T026} (audit + test).

- [ ] T017 [US1] **[NEW]** Add `lean/Stem/ButtonPanelTester/Phase3/BaptismSequence.lean`:
      states, events, and transition relation exactly per data-model §4.1/§4.3 (foreign-UUID
      announcements never transition; terminal states absorb all events; deadline =
      `AwaitingAnnounce` entry + 6 s). Prove the three theorems of data-model §8:
      `baptize_progress` (run reaches `Succeeded` **iff** a WHO_I_AM matching selected UUID ∧
      chosen variant is observed within the budget and both writes complete),
      `baptize_outcome_total` (every run terminates in exactly one of the six FR-005
      outcomes), `no_assignment_without_match` (the SET_ADDRESS action is unreachable without
      a validated match — FR-004). Extend the `Phase3.lean` umbrella; `lake build` green,
      `sorry`-free, standard axioms only. (Constitution I; FR-003/004/005)
- [ ] T018 [US1] **[EXTEND]** Extend `src/ButtonPanelTester.Core/Can/PanelObservation.fs` with
      the additive `FwType: uint16` field (R2 — the claim must echo the selected panel's
      announced fwType; data-model §3), carried through from the already-parsed
      `WhoIAmFrame` at the `PanelsOnBus.observe` construction site
      (`src/ButtonPanelTester.Core/Can/PanelsOnBus.fs`); latest announcement wins (same as
      every field under coalescing). Compile-driven sweep of record literals the new field
      breaks (`Property/Can/PanelsOnBusProperties.fs`, `Property/Can/PruningProperties.fs`,
      view/integration tests as the compiler reports). **Semantics untouched**: UUID keying,
      coalesce, prune, clear unchanged — the Phase 2 Lean models key on uuid/lastSeen and are
      unaffected (no Lean change); the shipped discovery suites stay green as the regression
      proof (FR-006 "list semantics NOT modified"). (FR-006; R2; data-model §3)
- [ ] T019 [US1] **[NEW]** Add `src/ButtonPanelTester.Core/Can/Baptism.fs`: the FSM types —
      `BaptismState`, `BaptismEvent` (`AnnouncementHeard | Tick | PanelsChanged | LinkChanged
      | WriteCompleted | WriteFaulted`, data-model §4.3), `BaptismOutcome` (the exact six-case
      DU of §4.2 incl. `UnexpectedVariant of announced: VariantIdentity` and
      `TransmissionFailure of step: SequenceStep`), `SequenceStep = ClaimStep | AssignStep` —
      plus the **pure** transition function over §4.1 (returns next state + the action to
      perform, so `no_assignment_without_match` has a code-level analogue): terminal-state
      idempotence; foreign-UUID `AnnouncementHeard` is a no-op; deadline computed from the
      `AwaitingAnnounce` entry instant + 6 s (CHK010 pin); uuid-match + variant-mismatch →
      `Failed_UnexpectedVariant`; selected uuid pruned → `Failed_PanelDisappeared`; link
      leaves `Connected` in any non-terminal state → `Failed_LinkLost`. XML docs cite
      `Phase3/BaptismSequence.lean` (T017) + data-model §4. Insert after `SetAddressFrame.fs`
      + `CanLinkState.fs` in the fsproj. (FR-003/004/005; stem-fp closed-DU triple)
- [ ] T020 [US1] **[NEW]** Add `tests/ButtonPanelTester.Tests/Property/Can/BaptismSequenceProperties.fs`
      (custom `Arbitrary` for scripted announcement/link/tick event sequences, stem-fp §9):
      `BaptismOutcomeTotal` (any event sequence long enough to pass the deadline terminates
      in exactly one of the six outcomes), `BaptismSucceedsIffMatchingAnnouncement` (mirrors
      `baptize_progress` — succeeds iff a matching announcement arrives within budget and
      both writes complete), `ForeignUuidNeverSatisfiesWait` (announcements from any other
      UUID never advance `AwaitingAnnounce` — spec edge case). Lands with T019 (C3 commit).
      (Constitution II; FR-004/005)
- [ ] T021 [US1] **[NEW]** Add `src/ButtonPanelTester.Services/Can/IBaptismService.fs` +
      `BaptismService.fs` (house pattern: interface file beside the service, spec-003
      precedent): drive the pure FSM over the consumed surfaces — ctor
      `(transmitter: IMasterSequenceTransmitter, whoIAm: IWhoIAmObserver, discovery:
      IPanelDiscoveryService, link: ICanLinkService, clock: IClock, logger:
      ILogger<BaptismService>)`. Baptize operation: guards re-checked at entry; send
      `WHO_ARE_YOU(variantByte, selectedPanel.FwType, reset=true)` — **echoing the announced
      fwType** (R2/CHK007); on validated match compute `spAddress 0uy variantByte
      announcedFwType 1uy` and send SET_ADDRESS; surface the outcome + state changes as an
      observable the GUI subscribes to. Events arrive from observer threads and the UI
      thread: serialize transitions under a private lock **never held across an await**, with
      terminal-state idempotence (data-model §4.3; stem-async-discipline / stem-fp §8). The
      6 s deadline is `IClock`-based with a deterministic test hook (the
      `RunPruneTick`-style precedent — no wall-clock sleeps in tests). At most one attempt
      runs at a time (the surface is modal while running — data-model §4, CHK013 pin); no
      automatic retry on any failure. Register `IBaptismService` in
      `src/ButtonPanelTester.GUI/Composition/CompositionRoot.fs` and extend
      `CompositionRootCanTests` to resolve it (hardware-free). (FR-002..006; R6)
- [ ] T022 [US1] **[NEW]** Add `tests/ButtonPanelTester.Tests/Integration/Can/BaptismE2ETests.fs`
      over `InMemoryMasterSequenceTransmitter` + `InMemoryWhoIAmObserver` + the discovery
      service + a real `CanLinkService` wrapping `InMemoryCanLink` + `FrozenClock` (spec-003
      integration pattern): (a) happy path — claim recorded with the **panel's announced
      fwType**, matching re-announcement, SET_ADDRESS recorded with byte-echoed UUID +
      computed spAddress, outcome `Succeeded`; (b) uuid match + wrong variant →
      `UnexpectedVariant`, **no** SET_ADDRESS recorded (FR-004); (c) selected uuid pruned
      before any match → `PanelDisappeared`; (d) scripted fault on claim →
      `TransmissionFailure ClaimStep`, on assign → `TransmissionFailure AssignStep`, no
      retry; (e) `NoSetAddressWithoutMatch` — FsCheck over scripted sequences asserting the
      fake's recorded sends never contain a SET_ADDRESS without a prior validated match
      (plan-named property, adapter-agnostic). Lands with T021 (C4 commit).
      (FR-003/004/005; SC-001)
- [ ] T023 [P] [US1] **[NEW]** Add `tests/ButtonPanelTester.Tests/Integration/Can/TimeoutE2ETests.fs`
      (`FrozenClock`-driven): advance to just under the deadline → still waiting; cross the
      deadline (claim-write completion + 6 s, CHK010) → `WaitTimeout` whose structured
      outcome carries the recovery guidance verbatim from FR-005/clarification 4 (claim may
      be incomplete; panel may re-announce late with the target variant; re-run Baptize or
      Reset); a **matching announcement after the reported timeout does NOT flip the outcome**
      (never-flip rule, spec edge case); foreign-UUID announcements before the deadline never
      satisfy the wait. Rides in the C4 commit. (FR-005; SC-001)
- [ ] T024 [P] [US1] **[NEW]** Add `tests/ButtonPanelTester.Tests/Integration/Can/LinkLossAbortsTests.fs`:
      the link leaving `Connected` in **each** non-terminal state — `ClaimSent`,
      `AwaitingAnnounce`, `Assigning` — yields `LinkLost`, transmits nothing further, and
      never retries (CHK015 coverage: link-loss complete across every sequence step,
      acceptance 1.8). Rides in the C4 commit. (FR-005)
- [ ] T025 [US1] **[NEW]** Implement the FR-007 post-success watch in `BaptismService.fs` +
      add `tests/ButtonPanelTester.Tests/Integration/Can/PostSuccessWarningTests.fs`: after
      `Succeeded`, watch `IWhoIAmObserver` for the claimed UUID for one pruning window (15 s,
      spec-003 constant); heard → raise the claim-did-not-take warning (panel still
      unclaimed and visible); a new attempt or link loss cancels the watch; window expiry is
      silent. Volatile in-memory only — no persistence (FR-013). Tests drive the window via
      `FrozenClock` + the deterministic tick hook. (FR-007; data-model §4.4)
- [ ] T026 [US1] **[NEW]** Add `src/ButtonPanelTester.Services/Can/BaptismLogging.fs` + 
      `tests/ButtonPanelTester.Tests/Unit/Can/BaptismLoggingTests.fs`: exactly one structured
      audit record per baptize attempt via `ILogger<BaptismService>` (house pattern —
      `CanLinkLogging.fs` precedent, R8): fields `Action`/`Variant`/`PanelUuid`/`Outcome`/
      `StepReached`/`StartedAt`/`CompletedAt` (ISO-8601 via `IClock`) per data-model §7;
      template messages + named params (stem-logging — no string interpolation). Tests use
      the shipped `RecordingLogger`: exactly one record per attempt across all six outcomes;
      **no operator-identity field by design** (clarification 5, Principle V). Wire the
      emission into `BaptismService` at terminal transition. (FR-012/FR-013; SC-006)

**Checkpoint C**: on the virtual adapters + `FrozenClock`, a scripted baptism reaches all six
outcomes deterministically; Lean + FsCheck agree on totality/progress/no-assignment; every
attempt logs exactly one audit record. US1 is CI-provable end-to-end below the GUI.

---

## Phase D: Reset path + enablement guards (User Story 2 + the FR-002 guard for US1)

**Goal**: the pure enablement predicates both stories' surfaces render (FR-002/FR-008 — in
Phase D per plan §Implementation phases; the cross-story role is the Format-section deviation),
then the reset flow: confirmation-gated dual-fwType broadcast, success on write completion,
audit incl. declined attempts.

**Mandatory triple (Enablement).** Lean `baptize_enabled_iff` / `reset_enabled_iff` (T027) ↔
FsCheck `EnablementGuards` (T028) ↔ XML-doc citations (T028).

**Commit grouping**: D1 = {T027} (Lean-only). D2 = {T028} (predicates + properties, one
commit). D3 = {T029, T030} (reset flow + its integration tests, one commit). D4 = {T031}
(reset audit + tests).

- [ ] T027 **[NEW]** Add `lean/Stem/ButtonPanelTester/Phase3/Enablement.lean`: prove
      `baptize_enabled_iff` (enabled ⇔ link `Connected` ∧ exactly one panel announcing ∧ that
      panel selected — FR-002) and `reset_enabled_iff` (enabled ⇔ `Connected` ∧ at most one
      announcing — FR-008); the count ranges over **announcing** panels only (silent panels
      are invisible by construction — spec assumption, CHK019). Extend the `Phase3.lean`
      umbrella; `lake build` green. (Constitution I; FR-002/FR-008)
- [ ] T028 **[EXTEND]** Extend `src/ButtonPanelTester.Core/Can/Baptism.fs` with the
      enablement surface (data-model §6): `type Enablement = Enabled | Disabled of
      explanation: string`; `baptizeEnablement : CanLinkState -> int -> PanelUuid option ->
      Enablement`; `resetEnablement : CanLinkState -> int -> Enablement` — `Disabled` always
      carries the unmet-condition explanation (one per failed conjunct: link down / zero
      announcing / two-or-more announcing / none selected; reset's two-or-more explanation
      states the broadcast reaches every panel). Add
      `tests/ButtonPanelTester.Tests/Property/Can/EnablementProperties.fs`:
      `EnablementGuards` — iff-properties mirroring both T027 theorems over arbitrary
      `(CanLinkState, count, selection)` (the SC-005 basis), plus "disabled ⇒ explanation is
      non-empty and names the unmet condition". XML docs cite `Phase3/Enablement.lean`. One
      commit (D2). *(No story label — serves FR-002/US1 and FR-008/US2; see §Format.)*
      (FR-002/FR-008; SC-005)
- [ ] T029 [US2] **[NEW]** Implement the reset flow: extend
      `src/ButtonPanelTester.Core/Can/Baptism.fs` with `ResetOutcome = Sent | Declined |
      ResetLinkLost | ResetTransmissionFailure` (data-model §5) and
      `src/ButtonPanelTester.Services/Can/BaptismService.fs` (+ `IBaptismService.fs`) with
      the reset operation behind a **confirmation seam**: the caller supplies the
      confirmation result (the GUI dialog is Phase E); declined → nothing transmitted
      (`Declined`); confirmed → broadcast `WHO_ARE_YOU(0xFF, 0x0004, reset=1)` then
      `WHO_ARE_YOU(0xFF, 0x000F, reset=1)` **awaited sequentially as one technician action**
      (R2 — once per known fwType class; each only matches its hardware class), `Sent` when
      **all** writes complete (FR-010 — no reply ever comes, write completion is the success
      signal); a fault on either write → `ResetTransmissionFailure`; link not `Connected` /
      lost mid-broadcast → `ResetLinkLost`. No announcement wait, no retry. (FR-008/009/010;
      R2)
- [ ] T030 [US2] **[NEW]** Add `tests/ButtonPanelTester.Tests/Integration/Can/ResetE2ETests.fs`
      over the fake transmitter + real `CanLinkService` wrapping `InMemoryCanLink`:
      (a) confirmed → exactly **two** recorded WHO_ARE_YOU broadcasts, in order, payloads
      `FF 00 04 01` then `FF 00 0F 01` (the T007 fixtures), outcome `Sent`; (b) declined →
      **zero** recorded sends (FR-009 / acceptance 2.2 at the service seam — plan Phase D);
      (c) scripted fault on the first or second write → `ResetTransmissionFailure`, no
      retry, no further sends; (d) link not Connected / drops mid-pair → `ResetLinkLost`.
      Lands with T029 (D3 commit). (FR-008/009/010; SC-003 logic side)
- [ ] T031 [US2] **[NEW]** Extend `BaptismLogging.fs` + `BaptismLoggingTests.fs` with the
      reset audit records: one record per reset attempt **including declined-at-confirmation**
      (SC-006, clarification 5) — `Action = "Reset"`, no variant, no uuid (broadcast — uuid
      unknown, data-model §7), outcome, step reached (confirmation / broadcast), timestamps.
      Wire emission into the reset operation. (FR-012; SC-006)

**Checkpoint D**: enablement is theorem + property-backed (SC-005's basis); a scripted reset
records the exact dual-fwType byte pairs, declines transmit nothing, and every attempt —
including declines — logs one audit record. US2 is CI-provable below the GUI.

---

## Phase E: GUI (User Stories 1 + 2)

**Goal**: the baptism surface anchored to the Panels-on-bus list (FR-001): row selection, the
variant picker + Baptize, Reset + confirmation dialog, structured outcome/explanation
rendering. All service-backed; the GUI renders enablement and outcomes, it decides nothing.

**Commit grouping**: E1 = {T032, T033} (selection slice). E2 = {T034, T035} (baptize surface
slice). E3 = {T036, T037} (reset surface slice). Each lands impl + Headless tests together.

- [ ] T032 [US1] **[EXTEND]** Extend `src/ButtonPanelTester.GUI/Can/PanelsOnBusView.fs` with
      the row-selection affordance (spec-004-owned addition; row rendering and empty states
      untouched — plan §Consumed surfaces) and `src/ButtonPanelTester.GUI/App.fs` with the
      selection state (`PanelUuid option`, cleared when the selected row leaves the map —
      the "selected row prunes during interaction" edge case: the baptism surface deactivates
      with the panel-disappeared explanation, never a stale send). Selection feeds
      `baptizeEnablement` (T028). (FR-001/FR-002; spec edge case)
- [ ] T033 [US1] **[NEW]** Extend `tests/ButtonPanelTester.Tests.Windows/Gui/Can/PanelsOnBusViewTests.fs`:
      selecting a row renders the selected state; the selected row pruning from the map
      clears the selection and deactivates the surface with the panel-disappeared
      explanation. Lands with T032 (E1 commit). (FR-002; spec edge case)
- [ ] T034 [US1] **[NEW]** Add `src/ButtonPanelTester.GUI/Can/BaptismView.fs` + wire the
      surface slot in `App.fs` (subscribe to `IBaptismService` state/outcome changes
      marshalled onto `Dispatcher.UIThread` — spec-003 T021 pattern): variant picker offering
      **exactly the four** marketed variants (never the virgin marker — data-model §1);
      Baptize button rendering `baptizeEnablement` (disabled state shows the unmet-condition
      explanation, FR-002); **no confirmation step beyond the deliberate variant pick**
      (FR-009); surface **modal while an attempt runs** (data-model §4, CHK013 pin); outcome
      rendering — success names variant + UUID and explains the panel now goes **silent by
      design** so its row ages out via pruning (FR-006, acceptance 1.2); each of the five
      failures names the step that failed, the panel's likely state, and the recommended next
      action (FR-005), incl. the wait-timeout recovery guidance (clarification 4); the FR-007
      claim-did-not-take warning surfaces when raised. (FR-001/002/005/006/007/009)
- [ ] T035 [US1] **[NEW]** Add `tests/ButtonPanelTester.Tests.Windows/Gui/Can/BaptismViewTests.fs`
      (`Avalonia.Headless.XUnit`): the FR-002 enable matrix over (link, announcing count,
      selection) — disabled at zero and at ≥ 2 announcing with the explanation rendered
      (SC-005, acceptance 1.6), enabled for **any** announcing selected panel regardless of
      its current identity (acceptance 1.7); picking a variant + Baptize invokes exactly one
      attempt with the picked variant; controls modal during a running attempt; success
      rendering carries the silence explainer; each failure rendering names step / likely
      state / next action; FR-007 warning renders; no confirmation dialog appears on Baptize.
      Lands with T034 (E2 commit). (FR-002/005/006/007/009; SC-005)
- [ ] T036 [US2] **[EXTEND]** Extend `BaptismView.fs` with the Reset-to-virgin surface:
      button requiring **no** list selection, rendering `resetEnablement` (disabled at ≥ 2
      announcing with the broadcast-reaches-every-panel explanation, FR-008); the explicit
      confirmation dialog with the FR-009 wording — the reset erases a panel's machine
      identity and reaches **every matching panel on the bus, including silent ones the list
      cannot show**; on confirm invoke the service reset (T029 seam); honest success message
      per FR-010/acceptance 2.5 — command(s) written to the bus; a matching panel, if
      present, re-announces as virgin within ~6 s; otherwise the list simply stays empty.
      (FR-008/009/010)
- [ ] T037 [US2] **[NEW]** Extend `BaptismViewTests.fs`: the FR-008 enable matrix (enabled at
      zero and exactly one announcing — acceptance 2.3; disabled at ≥ 2 with explanation —
      acceptance 2.4, SC-005); Reset shows the confirmation dialog with the FR-009 wording;
      confirming invokes the service reset; **declining invokes nothing** (zero recorded
      sends on the wired fake transmitter — acceptance 2.2); the honest success rendering
      (acceptance 2.5). Lands with T036 (E3 commit). (FR-008/009/010; SC-005)

**Checkpoint E**: the full baptize and reset flows are operable in the GUI against the virtual
adapters; the Headless suites pin the enable matrices, the confirmation flow, and every
outcome rendering. **CI-green here = code-complete** — not Done (Validation Gate, Phase F).

---

## Phase F: Hardware E2E + bench validation — the Validation Gate (US1 + US2)

**Goal**: the live-boundary proof CI cannot give (live-boundary-smoke): real panels accept the
synthesized TX frames and behave as the firmware audit says. **CI-green is code-complete; the
bench E2E is the done line** (spec-003 ValidationPending discipline). Tracked under the living
bench tracker [#112](https://github.com/luca-veronelli-stem/button-panel-tester/issues/112) —
this feature adds its hooks there and does not expand #112's scope (plan Constitution Check
IV). Bench needs one virgin panel per fwType class exercised.

- [ ] T038 **[NEW]** Add `tests/ButtonPanelTester.Tests.Windows/Integration/Can/Hardware/BaptismHardwareTests.fs`,
      every case `[<Trait("Category","Hardware")>]` (excluded by the default
      `Category!=Hardware` CI filter) + the shipped env-gated attributes — **never** a bare
      `[<Fact(Skip=…)>]` (#142 lesson): `[<HardwareFact>]` (`BPT_HARDWARE=1`) **claim E2E** —
      one virgin panel on the bus: select, baptize, definitive outcome within 6 s (SC-001);
      bus capture shows zero further announcements from the claimed UUID (first silence
      within one announcement period, SC-002) and **only master-sequence frames** originated
      by the tool (FR-014); `[<HardwareFact>]` **reset E2E** — silent claimed panel attached,
      reset, a virgin row appears within 6 s (SC-003); `[<ManualHardwareFact>]`
      (`BPT_HARDWARE_INTERACTIVE=1`) **full bench cycle** — baptize → verify → reset across
      all four variants on one physical panel, zero residual tool state between cycles
      (SC-004, FR-013). Add the baptism suite's checklist entry to #112 (claim E2E, reset
      E2E, silence verification — the hooks plan §IV names). (SC-001..004; FR-013/FR-014)
- [ ] T039 Bench validation per [quickstart.md](./quickstart.md) §Bench walkthrough +
      §Hardware E2E: run the suite on the rig (`BPT_HARDWARE=1`, plus the interactive gate
      for the cycle case), record the results in the PR/issue as the operator follow-up that
      gates the **Done** claim (Validation Gate; verification discipline — evidence before
      claims). quickstart.md is **living** — update it only if commands/flows drifted during
      implementation. (SC-001..004)

**Checkpoint F**: a real virgin panel is claimed and silenced, a real silent panel is
recovered to virgin, the four-variant cycle holds — the TX wire format and the firmware
semantics are proven at the live boundary. The feature is **Done**.

---

## Phase G: Polish & cross-cutting concerns

- [ ] T040 [P] XML-doc audit of the new public surfaces per COMMENTS / stem-fp §10:
      `BoardVariant`, `WhoAreYouFrame`, `SetAddressFrame`, `Baptism` (types + predicates),
      `IMasterSequenceTransmitter`, `ProtocolMasterSequenceTransmitter`,
      `InMemoryMasterSequenceTransmitter`, `IBaptismService`/`BaptismService`,
      `BaptismLogging`, `BaptismView` — Lean citations in the house format (module path +
      theorem name + authoring task), contract paths current.
- [ ] T041 [P] Logging audit per LOGGING / stem-logging over the baptism path
      (`BaptismService`, `BaptismLogging`, `ProtocolMasterSequenceTransmitter`): typed
      `ILogger<T>`, template messages with named params (no string interpolation),
      exception-as-first-arg, no `Console.WriteLine` / `Debug.WriteLine` on production paths.
- [ ] T042 [P] Principle V + FR-013 compliance audit over the baptism path: zero OS-user /
      machine-name / SID / MAC fields anywhere (panel UUIDs are device hardware identifiers —
      plan §V; audit records carry no operator identity, clarification 5); no per-panel
      persistence beyond the structured log — no registry, no claim history, no lockout
      (FR-013). Expected zero hits.
- [ ] T043 [P] FR-014 TX whitelist audit (CHK027 at code level): grep the production tree for
      CAN send/write surfaces — `IMasterSequenceTransmitter` is the **only** TX port, its
      production adapter sends **only** the two command codes (`0x00:0x23` claim/reset,
      `0x00:0x25` assignment), every send sits behind a technician-initiated service
      operation gated on `Connected`; the RX ports (`ICanFrameStream`, `IWhoIAmObserver`)
      remain send-free; discovery still transmits nothing (spec-003 FR-009 preserved).
- [ ] T044 [P] `cd lean; lake build` — the ten Phase 3 theorems of data-model §8
      (`parse_encode_roundtrip` + `encode_length` per codec module, `encode_decode_inverse`,
      `baptize_progress`, `baptize_outcome_total`, `no_assignment_without_match`,
      `baptize_enabled_iff`, `reset_enabled_iff`) compile with no `sorry`;
      `#print axioms` on each shows only {`propext`, `Classical.choice`, `Quot.sound`}.
- [ ] T045 [P] Add a `CHANGELOG.md` `[Unreleased]` entry: "Baptism workflow — claim a virgin
      panel as a marketed variant and reset claimed panels to virgin via the auto-address
      master sequence; first CAN-transmit feature (spec-004)."
- [ ] T046 [P] Update `README.md`: link `specs/004-baptism-workflow/quickstart.md` and add a
      one-paragraph mention of the baptism workflow beside the Panels-on-bus list mention.

---

## Dependencies & Execution Order

### Phase order (strict)

`Setup (T001)` → **`Phase A`** → **`Phase B`** → `Phase C` → `Phase D` → `Phase E` →
`Phase F` → `Polish`. A and B are foundational (both stories transmit). C needs A's codecs +
B's port (the service sends), and C2's `FwType` before C4 (the claim echoes it). D needs B
(reset sends) and C's service file (D3 extends it); D1–D2 (enablement) additionally gate
**Phase E's** enable matrices. E needs C+D; F needs E (it drives the GUI-complete product).

### Commit groupings (bisect-safe; test + impl land together)

- **A1** = {T002, T003, T004} — Lean only; `lake build` green.
- **A2** = {T005, T006, T007, T008} — WHO_ARE_YOU vertical (+ BoardVariant), one commit.
- **A3** = {T009, T010, T011} — SET_ADDRESS vertical, one commit.
- **B1** = {T012, T013} · **B2** = {T014, T015} · **B3** = {T016}.
- **C1** = {T017} · **C2** = {T018} · **C3** = {T019, T020} · **C4** = {T021, T022, T023,
  T024} · **C5** = {T025} · **C6** = {T026}.
- **D1** = {T027} · **D2** = {T028} · **D3** = {T029, T030} · **D4** = {T031}.
- **E1** = {T032, T033} · **E2** = {T034, T035} · **E3** = {T036, T037}.
- **F1** = {T038} · T039 is the bench follow-up (no commit unless quickstart drifted).
- **Polish** = T040–T046 (audits commit only what they fix; T045/T046 are docs commits).

### Within-phase notes

- `Baptism.fs` is edited by T019 (C3), T028 (D2), T029 (D3); `BaptismService.fs` by T021
  (C4), T025 (C5), T029 (D3); `BaptismLogging.fs` by T026 (C6), T031 (D4); `BaptismView.fs`
  by T034 (E2), T036 (E3) — sequential edits on shared files; keep the slices ordered to
  avoid rebase pain.
- `masterSequenceFixtures.json` is created by T007 (A2) and extended by T010 (A3) — A2
  before A3.
- T018 (C2) is independent of C1/C3 but MUST precede T021/T022 (the service reads
  `PanelObservation.FwType`).
- Lean-ahead inside slices: T002–T004 before any A2/A3 F#; T017 before T019; T027 before
  T028 (constitution Principle I order).

### Parallel opportunities

- T003 ∥ T002 (different Lean modules); T023 / T024 are independent test files inside C4.
- A2 and A3 touch disjoint code files but share the fixtures JSON — keep the commits ordered.
- E2 and E3 share `BaptismView.fs`/`BaptismViewTests.fs` — sequential.
- Polish T040–T046 are mutually parallel; T039 is the bench follow-up (not [P]).

---

## FR / SC → task coverage matrix

*(The downstream `/speckit-analyze` coverage pass keys off this — every FR and SC maps to at
least one implementing task and one test.)*

| Requirement | Implementing task(s) | Test(s) |
|---|---|---|
| **FR-001** baptism surface: anchor, four variants, reset action | T005, T032, T034, T036 | T033, T035, T037 |
| **FR-002** baptize enablement conjunction + explanations | T027, T028, T032, T034 | T028 (`EnablementGuards`), T033, T035 |
| **FR-003** complete three-step master sequence | T002–T011 (codecs), T012–T016 (TX), T021 | T008, T011, T015, T022(a) |
| **FR-004** validate UUID ∧ variant before assignment | T017 (`no_assignment_without_match`), T019, T021 | T020, T022(b)(e), T023 |
| **FR-005** 6 s bound; exactly six outcomes; failure content | T017, T019, T021, T034 | T020, T022, T023, T024, T035 |
| **FR-006** success on assign write-completion + silence explainer; list semantics untouched | T021, T018 (additive only), T034 | T022(a), T035; shipped discovery suites stay green (T018) |
| **FR-007** post-success claim-did-not-take warning | T025, T034 | T025, T035 |
| **FR-008** reset enablement + virgin/reset broadcast | T027, T028, T029, T036 | T028, T030, T037 |
| **FR-009** reset confirmation; no baptize confirmation | T029 (seam), T036, T034 | T030(b), T037, T035 |
| **FR-010** reset success on write completion + ~6 s expectation | T029, T036 | T030, T037 |
| **FR-011** no blind claim — reset-first policy | structural: T032/T034 (claim path requires an announcing selected row; no other claim path exists) | T035 (enable matrix: no claim without announcing selection) |
| **FR-012** one structured audit record per attempt | T026, T031 | T026, T031 (SC-006) |
| **FR-013** no per-panel persistence; indefinitely reversible | T021/T025 (volatile state only), T042 | T038 (SC-004 cycle) |
| **FR-014** TX whitelist; technician-initiated; Connected-gated | T012 (port shape), T014, T043 | T015, T022(e), T038 (bus capture) |
| **SC-001** definitive outcome ≤ 6 s, 100 % | T019, T021 | T023, T038 |
| **SC-002** claimed UUID silent; row ages out; explained | T034 (explainer) | T038 (capture), T035 |
| **SC-003** reset → virgin row ≤ 6 s (≥ 95 %) | T029 | T030 (logic), T038 (bench) |
| **SC-004** four-variant cycle, zero residual state | T021/T029 (FR-013 design) | T038 (`ManualHardwareFact` cycle) |
| **SC-005** destructive actions unreachable with ≥ 2 panels | T028, T034, T036 | T028 (`EnablementGuards`), T035, T037 |
| **SC-006** exactly one audit record incl. declined | T026, T031 | T026, T031 |

---

## Implementation Strategy

### MVP = US1 CI-provable, then US2, then the bench gate

1. **Phases A + B** — the wire foundation and the TX boundary (foundational, no story value
   alone but everything transmits through them).
2. **Phase C** — US1's logic greens on CI (Checkpoint C): the **MVP cut for review** is
   reachable here below the GUI.
3. **Phase D** — enablement guards (gating both stories' surfaces) + US2's logic greens.
4. **Phase E** — both surfaces operable; **CI-green = code-complete** (ValidationPending).
5. **Phase F** — the bench proof **is the done line** (live-boundary-smoke Validation Gate):
   the #121 lesson says CI-green TX synthesis means nothing until a real panel acts on it.

### Discipline

- **bisect-safe / vertical-commits**: every commit compiles + passes tests; test rides with
  its impl (every A2/A3/C3/C4/D3/E* slice). Conventional Commits with a `Tasks: T###` trailer
  linking back here.
- **Constitution order** (I): Lean → FsCheck/xUnit → F# inside every theorem-bearing slice
  (A1→A2/A3, C1→C3/C4, D1→D2/D3).
- **Mandatory triples** (I/II): `WhoAreYouFrame`, `SetAddressFrame`, `BoardVariant` inverse,
  `BaptismSequence` FSM, `Enablement` — each carries Lean theorem + FsCheck property +
  XML-doc citation. The reset flow is linear (no FSM): its guards are the `Enablement`
  theorems and its wire bytes the codec theorems; `ResetOutcome` is rendered, not transitioned.
- **Gate** (`./gate.ps1`): at `/speckit-implement` time copy the template fresh (llm-settings#70
  fix), extend it (build + both test projects `Category!=Hardware` + `lake build` + a focused
  baptism `--filter`) in its own `chore: extend gate.ps1` commit. *(Out of scope for this
  task-generation run.)*

---

## Notes

- This tasks.md is the breakdown only — no implementation in this run.
- `[P]` = different files, no dependency on an incomplete task. Phases A/B are foundational
  (no story label); the D2 enablement slice and Phase F span both stories (see §Format).
- The three checklist pins this breakdown encodes: **CHK007** → the claim echoes the panel's
  announced fwType (T018/T021, R2); **CHK010** → the 6 s window starts at claim-write
  completion (T017/T019/T023, data-model §4.1); **CHK013** → the surface is modal while an
  attempt runs (T021/T034/T035, data-model §4).
- Next: `/speckit-analyze` (cross-artifact consistency) before `/speckit-implement`.
