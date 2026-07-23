---
description: "Task list for spec-005 — Button-Press Test (Input Side): prompt, observe, score each active button"
---

# Tasks: Button-Press Test (Input Side)

**Input**: Design documents from [`specs/005-button-press-test/`](./)

**Prerequisites**: [plan.md](./plan.md) (task authority — §Implementation phases A–G),
[spec.md](./spec.md) (US1/US2/US3 + FR-001..016 / SC-001..008), [data-model.md](./data-model.md)
(parser, press-edge detector, schema, FSM, enablement), [research.md](./research.md) (R1–R10 —
firmware-pinned wire facts + polarity), [contracts/](./contracts/)
([button-state-wire-format](./contracts/button-state-wire-format.md),
[button-state-observer-port](./contracts/button-state-observer-port.md)),
[quickstart.md](./quickstart.md).

**Tests**: REQUIRED. The constitution makes tests non-optional (Principle II — FsCheck is the
primary correctness mechanism for `Core`/`Services`; Principle IV — every test layer greens on CI,
hardware E2E is tagged + excluded). Closed-domain / wire types carry the **mandatory triple**:
**Lean theorem + FsCheck property + XML-doc citation** (Principles I/II; `stem-fp-discipline` §3).
FsCheck-first; example-based tests carry a one-line rationale (the wire **fixture** documents a
concrete protocol fixture — Principle II's sanctioned exception; the **integration** tests assert
wiring/timing across threads, where no pure law is expressible).

**Numbering**: fresh from **T001** — per-feature numbering (spec-003/004 precedent).

---

## Format

`- [ ] T### [P?] [US#?] **[TAG]** Description with exact file path`

- **[P]** — parallelizable (different files, no dependency on an incomplete task).
- **[US1]** — verify every active button on a baptized panel (P1); **[US2]** — recover from a
  missed/wrong press without restarting (P2); **[US3]** — re-run + cover other variants (P3).
  Setup, the foundational Phases **A/B/C**, the Hardware gate (Phase G, spans all stories), and
  Polish carry **no** story label per the spec-kit convention. **Deviation, noted explicitly**:
  the FSM core (Phase D) is labelled **[US1]** because it is the test engine the MVP rides, but it
  encodes US2's recovery transitions (Unexpected/Missed/Retry/Skip) and US3's re-run in the same
  pure `step`; the per-story *surfacing* is split by label in Phases E/F. Enablement (T021–T022)
  gates US1's surface (FR-001) and US3's unavailable path (AC-3), so it carries no story label.
- **[TAG]** — provenance against the shipped tree:
  - **NEW** — does not exist; spec-005 writes it.
  - **EXTEND** — shipped + correct; spec-005 adds a member/binding/slot without rewriting it (the
    consumed spec-002/003/004 surfaces stay frozen — plan §Consumed surfaces).

**bisect-safe.** Every task/commit boundary MUST compile and pass tests on its own (`bisect-safe` +
`vertical-commits`). Where the constitution order (Lean → xUnit/FsCheck → F#) would otherwise leave
a RED intermediate commit, the **test and its implementation land in the same commit**
(resolve-ticket discipline). Lean lands **ahead of F# inside every slice that has theorems** (A1
before A2/A3, B1 before B2, D1 before D2, E1 before E2). Commit groupings are called out per phase
and in §Dependencies.

**F# compile order.** New `Core` files insert **between `Can/Baptism.fs` and `Can/Ports.fs`** in
`ButtonPanelTester.Core.fsproj`, in dependency order `ButtonStateFrame.fs → KeyStateBitmap.fs →
ButtonSchema.fs → ButtonPressTest.fs` (the new `Can/Ports.fs` member references `ButtonStateFrame`,
so the frame file MUST precede `Ports.fs`; `ButtonPressTest.fs` reuses the `Enablement` DU defined
in `Baptism.fs`, so it MUST follow it). New `Services` files insert **after `Can/BaptismService.fs`**
in dependency order `IButtonPressTestService.fs → ButtonPressTestLogging.fs → ButtonPressTestService.fs`
(mirroring the `IBaptismService/BaptismLogging/BaptismService` triplet). F# forward references are
compile errors.

**Epic decomposition.** This breakdown is epic-sized (plan §Status) — it does **not** fit one
bisect-safe PR. Implementation decomposes into **one ordered resolve-ticket child PR per phase
A–G**, filed after this `tasks.md` (parent epic + children, `resolve-epic`). Phase boundaries are
the PR boundaries; each is independently green.

---

## Phase 1: Setup

**Purpose**: establish the green baseline this branch builds on, so every later commit is
checkable bisect-safe.

- [ ] T001 Verify the green baseline on `005-button-press-test`: `dotnet build
      Stem.ButtonPanelTester.slnx -c Release`; `dotnet test
      tests\ButtonPanelTester.Tests\ButtonPanelTester.Tests.fsproj -c Release`; `dotnet test
      tests\ButtonPanelTester.Tests.Windows\ButtonPanelTester.Tests.Windows.fsproj -c Release
      --filter "Category!=Hardware"`; `cd lean; lake build; cd ..`. Record the result as the
      bisect-safe anchor (worktrees baseline discipline; the branch is docs-only so far — baseline
      was green at worktree creation, re-verify before the first code commit).

---

## Phase A: Wire foundation — FOUNDATIONAL (blocks all behaviour; serves US1)

**Goal**: the two pure wire types nothing downstream can run without — the `VAR_WRITE`
button-frame **codec** (`ButtonStateFrame`, command `0x00:0x02` + address `0x80NN`, R1) and the
**press-edge detector** (`pressEdges` + `PressedBit`, the polarity-bearing type — pressed = bit
`0`, detect the active bit `1 → 0`, R2), firmware-verified per
[contracts/button-state-wire-format.md](./contracts/button-state-wire-format.md).

**Mandatory triples.** `ButtonStateFrame`: Lean `parse_encode_roundtrip` / `encode_length` (T002) ↔
FsCheck `ButtonStateFrameRoundtrip` / `ButtonStateFrameRejectsWrongLength` (T007) ↔ XML-doc
citation (T005). Press-edge detector: Lean `press_edge_iff_high_to_low` / `inactive_bits_ignored`
(T003) ↔ FsCheck `PressEdgeDetectsHighToLow` / `InactiveBitsIgnored` (T009) ↔ XML-doc citation
(T008).

**RX fixture (real-capture rule).** `buttonStateFixtures.json` (T006) is an **RX** fixture — like
spec-003's WHO_I_AM capture, the normative target is the tool's *parser*, so the bytes are the
contract's `[0x00,0x02,0x80,var_low,bitmap]` payloads verified against the on-disk firmware source
(`UserMain.c:429–449`); the live-boundary proof that a real panel emits them is **Phase G**
(live-boundary-smoke Validation Gate).

**Constitution order**: Lean → F# codec + fixtures + FsCheck → F# detector + FsCheck.
**Commit grouping**: **A1** = {T002, T003, T004} (Lean-only, green on `lake build`).
**A2** = {T005, T006, T007} (codec vertical: impl + fixture + tests, one commit).
**A3** = {T008, T009} (detector vertical: impl + tests, one commit).

- [X] T002 **[NEW]** Add `lean/Stem/ButtonPanelTester/Phase4/ButtonStateFrame.lean`: model the
      `VAR_WRITE` button-frame codec per [data-model.md](./data-model.md) §1 (variable address
      `0x80NN`, key-state byte); prove `parse_encode_roundtrip` and `encode_length`. `sorry`-free,
      axioms ⊆ {`propext`, `Classical.choice`, `Quot.sound`}. (Constitution I; FR-006; R1)
- [X] T003 [P] **[NEW]** Add `lean/Stem/ButtonPanelTester/Phase4/KeyStateBitmap.lean`: model the
      masked bitmap + press-edge detector per [data-model.md](./data-model.md) §2; prove
      `press_edge_iff_high_to_low` (an active bit is a press **iff** it was `1` and is now `0` —
      pressed = `0` per firmware R2) and `inactive_bits_ignored` (bits outside the active mask never
      appear in the edge set — FR-014). `sorry`-free, standard axioms only. (Constitution I;
      FR-006/FR-014; R2)
- [X] T004 **[NEW]** Add the umbrella `lean/Stem/ButtonPanelTester/Phase4.lean` (mirroring
      `Phase3.lean` — imports the Phase4 modules as they land) and register the new lib in
      `lean/lakefile.toml`: `[[lean_lib]] name = "Stem.ButtonPanelTester.Phase4"` + append to
      `defaultTargets`. `cd lean; lake build` green. Rides in the A1 commit. (Constitution I)
- [X] T005 **[NEW]** Add `src/ButtonPanelTester.Core/Can/ButtonStateFrame.fs`: the single-case
      wrappers `VariableAddress of uint16` and `KeyStateBitmap of byte` (data-model §1 — prevent
      primitive confusion; `KeyStateBitmap` is the raw wire byte, `0` = pressed bit), the
      `ButtonStateFrame = { Address; Bitmap }` record, `parse : ReadOnlyMemory<byte> ->
      ButtonStateFrame option` (length-only reject, mirror `WhoIAmFrame.parse`) and `encode`. XML
      docs cite [contracts/button-state-wire-format.md](./contracts/button-state-wire-format.md) +
      Lean `parse_encode_roundtrip` (T002). Insert in `ButtonPanelTester.Core.fsproj` **after
      `Can/Baptism.fs`** (before `Can/Ports.fs`). (FR-006; R1; contract §App-layer payload)
- [X] T006 **[NEW]** Add `tests/ButtonPanelTester.Tests/Fixtures/Can/buttonStateFixtures.json`:
      `idle_all_released` (`00 02 80 00 FF` — no buttons pressed, all-active-bits-`1`),
      `optimus_light_pressed` (DOWN/bit1 cleared from idle), `optimus_suspension_pressed`
      (P1/bit2 cleared), a two-bit transition frame, `virgin_sentinel` (address `0x80FE` — MUST NOT
      be a result), `malformed_too_short`. Bytes are the contract's normative payloads
      (firmware-source-verified, see Phase A preamble). (FR-006; FR-014; R1)
- [X] T007 **[NEW]** Add `tests/ButtonPanelTester.Tests/Property/Can/ButtonStateFrameProperties.fs`
      (`ButtonStateFrameRoundtrip` over arbitrary address/bitmap; `ButtonStateFrameRejectsWrongLength`
      — length-only) and `tests/ButtonPanelTester.Tests/Unit/Can/ButtonStateFrameFixtureTests.fs`:
      for each T006 fixture assert `encode frame = fixture bytes` and the parse round-trip;
      `virgin_sentinel` parses to a frame whose address is the virgin marker (the **observer**, not
      the parser, drops it — T015); `malformed_too_short` → `None`. Lands with T005–T006 (A2
      commit). (Constitution II; FR-006)
- [X] T008 **[NEW]** Add `src/ButtonPanelTester.Core/Can/KeyStateBitmap.fs`: `[<Literal>] let
      PressedBit = 0uy` (data-model §2 — firmware press clears the bit, `UserMain.c:1369,:978`) and
      the pure `pressEdges : activeMask: byte -> prior: KeyStateBitmap -> next: KeyStateBitmap ->
      Set<int>` returning the active-masked bit positions that went `1 → 0`. XML docs cite
      [contracts/button-state-wire-format.md](./contracts/button-state-wire-format.md) §R2 + Lean
      `press_edge_iff_high_to_low` (T003), noting `PressedBit` is the **one-line-flip** point for a
      bench surprise. Insert **after `ButtonStateFrame.fs`** (uses the `KeyStateBitmap` type).
      (FR-006/FR-014; R2)
- [X] T009 **[NEW]** Add `tests/ButtonPanelTester.Tests/Property/Can/KeyStateBitmapProperties.fs`:
      `PressEdgeDetectsHighToLow` (mirrors `press_edge_iff_high_to_low` — a button is reported iff
      its active bit was `1` in `prior` and `0` in `next`), `InactiveBitsIgnored` (mirrors
      `inactive_bits_ignored` — bits outside `activeMask` never appear), and a baseline property
      (a single frame against itself yields the empty set — no absolute byte is read as press-state,
      the held/bouncing edge cases). Lands with T008 (A3 commit). (Constitution II; FR-006/FR-014)

**Checkpoint A**: the codec round-trips and the detector reports press edges under Lean + FsCheck;
the fixture pins the contract bytes; `PressedBit` is a single named constant. Nothing observes the
bus yet.

---

## Phase B: Variant schema — FOUNDATIONAL (the FSM keys off it; serves US1 + US3)

**Goal**: the per-variant `ButtonSchema` table (active mask + ordered active buttons + decal/firmware
labels + `Provisional` flag, FR-016) — OPTIMUS-XP authoritative, the other three provisional (R3/R4).
`Active` is the canonical firmware order filtered by `ActiveMask`; the FSM invariant
`test_visits_active_only` rests on this.

**Mandatory triple.** `FirmwareButton` (closed 8-case DU) / schema: Lean `canonical_order_total`
(T010) ↔ FsCheck `SchemaActiveOnlyInOrder` (T012) ↔ XML-doc citation (T011).

**Constitution order**: Lean → F# + FsCheck. **Commit grouping**: **B1** = {T010} (Lean-only).
**B2** = {T011, T012} (schema + properties, one commit).

- [X] T010 **[NEW]** Add `lean/Stem/ButtonPanelTester/Phase4/ButtonSchema.lean`: model the fixed
      firmware button order `[UP;DOWN;P1;P2;P3;MEM;STOP;LIGHT]` and the active-only ordered schema
      per [data-model.md](./data-model.md) §3; prove `canonical_order_total` (the active list is the
      canonical firmware order filtered by the active mask — total, order-preserving, no inactive
      bit). Extend the `Phase4.lean` umbrella; `lake build` green, `sorry`-free, standard axioms
      only. (Constitution I; FR-016; supports `test_visits_active_only`)
- [X] T011 **[NEW]** Add `src/ButtonPanelTester.Core/Can/ButtonSchema.fs`: the closed DU
      `FirmwareButton = UP | DOWN | P1 | P2 | P3 | MEM | STOP | LIGHT` (canonical = declaration
      order, R3), `ActiveButton = { Button; Bit; Decal }`, `ButtonSchema = { Variant: MarketingVariant;
      ActiveMask: byte; Active: ActiveButton list; Provisional: bool }`, and the four-variant table
      (data-model §3): OPTIMUS-XP `0x36` = {DOWN→Light, P1→Suspension, P3→Up, MEM→Down},
      `Provisional = false` (authoritative); EDEN-XP / R-3L XP / EDEN-BS8 all-8 from the legacy
      enums, `Provisional = true`. `Active` is computed as the canonical order filtered by
      `ActiveMask`. XML docs cite Lean `canonical_order_total` (T010) + [research.md](./research.md)
      R3/R4. Insert **after `KeyStateBitmap.fs`** (uses `MarketingVariant`). (FR-004/FR-016; R3/R4)
- [X] T012 **[NEW]** Add `tests/ButtonPanelTester.Tests/Property/Can/ButtonSchemaProperties.fs`:
      `SchemaActiveOnlyInOrder` (mirrors `canonical_order_total` — for every variant, `Active` is
      exactly the canonical firmware order filtered by `ActiveMask`: order preserved, every entry's
      bit set in the mask, no inactive bit present), plus an OPTIMUS-XP exactness fact (the four
      decals are `Light, Suspension, Up, Down` in order — SC-006 / the §C3 correction) and
      "every non-OPTIMUS row carries `Provisional = true`" (FR-016). Lands with T011 (B2 commit).
      (Constitution II; FR-016; SC-006)

**Checkpoint B**: every variant's active-button list is theorem + property-backed; OPTIMUS-XP's
decals are pinned exactly; the provisional flag is set on the three unverified rows. The schema is
ready for the FSM to walk.

---

## Phase C: Observation port + adapters — FOUNDATIONAL (the new RX seam; serves US1)

**Goal**: the new RX observation seam over the existing CAN boundary (Constitution III) —
`IButtonStateObserver` port in Core, the deterministic in-memory fake for CI, the production adapter
over `ICanFrameStream` + the reused `PacketReassembler` (hardcoding command `0x00:0x02` + the
button-state address set inline, mirroring `WhoIAmReassemblyObserver` `0x0024` — R6, **no new
stopgap**), composition wiring. Contract:
[contracts/button-state-observer-port.md](./contracts/button-state-observer-port.md).

**Commit grouping**: **C1** = {T013, T014} (port + fake; compiles green, exercised from Phase E on).
**C2** = {T015, T016} (production adapter + its frame-synthesis tests, one commit). **C3** = {T017}
(composition wiring + smoke extension).

- [X] T013 **[EXTEND]** Extend `src/ButtonPanelTester.Core/Can/Ports.fs` with `IButtonStateObserver`
      exactly per the port contract: `abstract member ButtonStateObserved : IObservable<ButtonStateFrame>`
      (hot, fan-out; late subscribers do not replay). XML docs carry the contract's semantics — emits
      one `ButtonStateFrame` per accepted `VAR_WRITE` on a recognised button-state address; the virgin
      sentinel `0x80FE` and non-button addresses are dropped; **edge detection is the consumer's job**
      (the observer is stateless w.r.t. press/release). Add directly after `IWhoIAmObserver`; `Ports.fs`
      already follows `ButtonStateFrame.fs` in the fsproj (T005 ordering). (Constitution III; R5)
- [X] T014 **[NEW]** Add `tests/ButtonPanelTester.Tests/Fakes/Can/InMemoryButtonStateObserver.fs`:
      `Emit(frame)` pushes synchronously to subscribers — the deterministic test driver (mirror
      `InMemoryWhoIAmObserver`). Insert after `Fakes/Can/InMemoryMasterSequenceTransmitter.fs` in
      the test fsproj. Lands with T013 (C1 commit). (Constitution III/IV)
- [X] T015 **[NEW]** Add `src/ButtonPanelTester.Infrastructure/Can/ButtonStateReassemblyObserver.fs`
      (`net10.0-windows`): subscribe `ICanFrameStream.RawFramesReceived`, feed a reused
      `PacketReassembler`, filter command `0x00:0x02` + the button-state address set
      (`{0x8000, 0x803E}`; drop the `0x80FE` virgin sentinel and non-button addresses inline — R6,
      extending the inherited hardcoded-metadata set, **no new bypass**), call `ButtonStateFrame.parse`,
      republish via `SubjectFanOut<ButtonStateFrame>` (thread-safe, callback not held under the lock —
      spec-002/003 precedent). Mirror `WhoIAmReassemblyObserver.fs`. (Constitution III/VI; R5/R6)
- [X] T016 **[NEW]** Add `tests/ButtonPanelTester.Tests.Windows/Unit/Can/ButtonStateReassemblyObserverTests.fs`:
      drive raw chunks of a `VAR_WRITE` button frame through a file-private fake `ICanFrameStream`
      (the shipped `WhoIAmReassemblyObserverTests` pattern), assert the observer emits the matching
      `ButtonStateFrame`; a `0x80FE` virgin frame and a non-button address (`0x0024` WHO_I_AM) are
      **dropped**; a wrong command is dropped. Lands with T015 (C2 commit). (Constitution IV; R5/R6)
- [X] T017 **[EXTEND]** Extend `src/ButtonPanelTester.GUI/Composition/CompositionRoot.fs`: register
      `IButtonStateObserver → ButtonStateReassemblyObserver` as a singleton over the existing shared
      `ICanFrameStream` + a `PacketReassembler` (same RX path the WHO_I_AM observer taps). Extend
      `tests/ButtonPanelTester.Tests.Windows/Integration/Can/CompositionRootCanTests.fs` to resolve
      `IButtonStateObserver` and assert it is the production adapter, hardware-free (lazy share —
      spec-003 precedent). (Constitution III/IV)

**Checkpoint C**: the RX seam exists end-to-end — port, deterministic fake, production adapter whose
reassembled frames are asserted against a fake stream, composition resolves it. No FSM consumes it
yet.

---

## Phase D: Button-press-test FSM (User Story 1)

**Goal**: the genuinely-new core (R7) — the session FSM (`Idle → Prompting(index, deadline, results)`
→ terminal `Completed | Interrupted`) as a pure `step : ButtonSchema -> State -> Event ->
State × Action` over the events from the existing observables, plus `ButtonOutcome` (closed DU) and
`allActivePassed`. The seven preservation theorems live here; US2's recovery transitions
(Unexpected/Missed/Retry/Skip) and US3's re-run are encoded in the same pure `step` and surfaced by
story in Phases E/F.

**Mandatory triple (ButtonPressTest FSM).** Lean `test_visits_active_only`, `result_vector_length`,
`test_outcome_total`, `pass_requires_press_edge`, `skip_never_pass`, `interrupt_excludes_all_passed`,
`terminal_absorbs` (T018) ↔ FsCheck `TestVisitsActiveOnly`, `ResultVectorLength`, `TestOutcomeTotal`,
`PassRequiresPressEdge`, `SkipNeverPass`, `InterruptExcludesAllPassed`, `TerminalAbsorbs` (T020) ↔
XML-doc citations (T019).

**Constitution order**: Lean → F# + FsCheck. **Commit grouping**: **D1** = {T018} (Lean-only).
**D2** = {T019, T020} (pure FSM + its properties, one commit).

- [X] T018 [US1] **[NEW]** Add `lean/Stem/ButtonPanelTester/Phase4/ButtonPressTest.lean`: states,
      events (`PressEdge | Tick | Retry | Skip | LinkChanged | PanelPresence`), the per-button
      `ButtonOutcome` (`Pending | Pass | Missed | Skipped`), `InterruptReason` (`LinkLost | PanelLost`),
      and the transition relation exactly per [data-model.md](./data-model.md) §4. Prove the seven
      theorems of [research.md](./research.md) R9: `test_visits_active_only` (prompts exactly the active
      buttons in canonical order — FR-002), `result_vector_length` (final results length = active count
      — FR-011), `test_outcome_total` (every run terminates in exactly one terminal — totality),
      `pass_requires_press_edge` (no `Pass` without an in-window matching press-edge — FR-006),
      `skip_never_pass` (a `Skip` records `Skipped`, never `Pass` — FR-009),
      `interrupt_excludes_all_passed` (no `Interrupted` run reports all-passed — FR-013),
      `terminal_absorbs` (a late press after `Missed`/terminal does not change a recorded outcome —
      never-flip). Extend the `Phase4.lean` umbrella; `lake build` green, `sorry`-free, standard axioms
      only. (Constitution I; FR-002/006/009/011/013)
- [X] T019 [US1] **[NEW]** Add `src/ButtonPanelTester.Core/Can/ButtonPressTest.fs`: the FSM types —
      `ButtonOutcome` (the closed DU of data-model §4), `InterruptReason`, `ButtonPressTestState`
      (`Idle | Prompting of index * deadline * results | Completed of results | Interrupted of reason *
      partial`), `TestEvent`, `TestAction` (`NoAction | RecordUnexpected of bit | AdvancePrompt of
      nextIndex | FinishCompleted | Halt of InterruptReason`) — plus the **pure** `step : ButtonSchema
      -> ButtonPressTestState -> TestEvent -> ButtonPressTestState * TestAction` over data-model §4
      (matching press-edge within the window → `Pass` + `AdvancePrompt`; a non-matching active press →
      `RecordUnexpected`, no advance; a press for an inactive position → `NoAction` (FR-014);
      `Tick ≥ deadline` → `Missed`; `Retry` re-arms with a fresh deadline; `Skip` → `Skipped` + advance;
      `LinkChanged false` / `PanelPresence false` → `Halt`; terminal states absorb) and
      `allActivePassed (results) = results |> Array.forall ((=) Pass)`. XML docs cite Lean
      `Phase4/ButtonPressTest.lean` (T018) + data-model §4. Insert **after `ButtonSchema.fs`** (uses
      `ButtonSchema`) and after `Baptism.fs` (the `Enablement` DU it reuses in Phase E). (FR-002..014;
      stem-fp closed-DU triple)
- [X] T020 [US1] **[NEW]** Add `tests/ButtonPanelTester.Tests/Property/Can/ButtonPressTestProperties.fs`
      (custom `Arbitrary` for scripted event sequences, `stem-fp-discipline` §9): the seven FsCheck
      properties mirroring T018 — `TestVisitsActiveOnly`, `ResultVectorLength`, `TestOutcomeTotal`,
      `PassRequiresPressEdge`, `SkipNeverPass`, `InterruptExcludesAllPassed`, `TerminalAbsorbs`. Lands
      with T019 (D2 commit). (Constitution II; FR-002/006/009/011/013)

**Checkpoint D**: the FSM is theorem + property-backed for totality, press-edge scoring,
skip-never-pass, interrupt-excludes-all-passed, active-only visiting, and never-flip. US1's engine is
CI-provable below the service.

---

## Phase E: Service + enablement + integration (User Stories 1 + 2 + 3)

**Goal**: `ButtonPressTestService` drives the pure FSM over the consumed surfaces (RX-only — no
transmitter): runs `pressEdges` across consecutive observed frames, arms a per-button 10 s deadline
via `IClock`, routes link/panel-presence loss to `Interrupted`, exposes Retry/Skip/Re-run, and emits
the forensic log (R8/R10). The enablement predicate (FR-001) gates the surface. Integration tests
prove timing, Unexpected-not-counted, Retry/Skip, link/panel loss, re-run, and log emission on the
real service graph with manual fakes.

**Mandatory triple (Enablement).** Lean `test_enabled_iff` (T021) ↔ FsCheck `TestEnablementGuards`
(T022) ↔ XML-doc citation (T022).

**Commit grouping**: **E1** = {T021} (Lean-only). **E2** = {T022} (enablement predicate + properties,
one commit). **E3** = {T023} (service + composition). **E4** = {T024, T025, T026, T027, T028}
(integration suites). **E5** = {T029} (forensic logging + test).

- [X] T021 **[NEW]** Add `lean/Stem/ButtonPanelTester/Phase4/Enablement.lean`: prove `test_enabled_iff`
      (the test is enabled **iff** the CAN link is `Connected` ∧ a panel is selected and baptized ∧ that
      panel is observable on the bus — FR-001), priority-ordered (link → selected-baptized → observable).
      Extend the `Phase4.lean` umbrella; `lake build` green. (Constitution I; FR-001)
- [X] T022 **[EXTEND]** Extend `src/ButtonPanelTester.Core/Can/ButtonPressTest.fs` with the enablement
      surface (data-model §6, reusing the `Enablement = Enabled | Disabled of explanation` DU from
      `Baptism.fs`): `testEnablement : CanLinkState -> selectedBaptized: bool -> observable: bool ->
      Enablement` — `Disabled` always carries the unmet-condition explanation (link not Connected / no
      baptized panel selected / panel not observable), priority-ordered (mirror `baptizeEnablement`). Add
      `tests/ButtonPanelTester.Tests/Property/Can/ButtonPressTestEnablementProperties.fs`:
      `TestEnablementGuards` — the iff-property mirroring `test_enabled_iff` over arbitrary
      `(CanLinkState, selectedBaptized, observable)`, plus "disabled ⇒ explanation non-empty and names
      the unmet condition" (the SC-008 basis). XML docs cite `Phase4/Enablement.lean` (T021). One commit
      (E2). *(No story label — serves FR-001/US1 surface and AC-3/US3 unavailable path; see §Format.)*
      (FR-001; SC-008)
- [X] T023 [US1] **[NEW]** Add `src/ButtonPanelTester.Services/Can/IButtonPressTestService.fs` +
      `ButtonPressTestService.fs` (house pattern — interface file beside the service): drive the pure
      `step` over the consumed surfaces — ctor `(buttons: IButtonStateObserver, discovery:
      IPanelDiscoveryService, link: ICanLinkService, clock: IClock, logger:
      ILogger<ButtonPressTestService>)`. Subscribe `ButtonStateObserved`, run `pressEdges` across
      consecutive frames (baseline seeded from the first frame, R2) → `PressEdge` events; the per-button
      10 s deadline (`research`-config constant, **not** UI) via `IClock` + a deterministic tick hook
      (the `BaptismService` `FrozenClock` precedent — no wall-clock sleeps in tests); reactive link-state
      guard (`LinkStateChanged` non-Connected → `Halt LinkLost`) and panel-presence guard via discovery
      (selected panel pruned → `Halt PanelLost`); Start / Retry / Skip / Re-run operations; surface the
      FSM state + result grid as an observable the GUI subscribes to. Events arrive from observer threads
      and the UI thread — serialize transitions under a private `lock` **never held across an await**,
      with terminal-state idempotence (`stem-async-discipline` / `stem-fp-discipline` §8). Single-attempt,
      no auto-retry (Retry is technician-driven). Register `IButtonPressTestService` in
      `CompositionRoot.fs` and extend `CompositionRootCanTests` to resolve it (hardware-free). (FR-002/003/
      005/006/007/009/010/013; R7/R8)
- [X] T024 [US1] **[NEW]** Add `tests/ButtonPanelTester.Tests/Integration/Can/ButtonPressTestE2ETests.fs`
      over `InMemoryButtonStateObserver` + the discovery service + a real `CanLinkService` wrapping
      `InMemoryCanLink` + `FrozenClock` (spec-003/004 integration pattern): the happy path — a panel
      baptized OPTIMUS-XP, the four active buttons pressed in order (emit idle→pressed frames), each
      scored `Pass` within the window and the prompt advancing (`Light → Suspension → Up → Down`), the
      final grid showing four `Pass` and `allActivePassed = true` (SC-001/SC-002 logic side). Lands in
      the E4 commit. (FR-002/006/010/011; SC-001/SC-002)
- [X] T025 [P] [US2] **[NEW]** Add `tests/ButtonPanelTester.Tests/Integration/Can/ButtonPressTimeoutTests.fs`
      (`FrozenClock`-driven): advance to just under the 10 s deadline → still prompting; cross the
      deadline → `Missed` with Retry/Skip offered (SC-003, within ~1 s of the window elapsing); a
      **matching press after the reported `Missed` does NOT flip the outcome** (the `terminal_absorbs` /
      never-flip rule). Rides in the E4 commit. (FR-005/FR-007; SC-003)
- [X] T026 [P] [US2] **[NEW]** Add `tests/ButtonPanelTester.Tests/Integration/Can/ButtonPressRecoveryTests.fs`:
      a wrong **active** button while X is prompted → recorded `Unexpected` (visible in the log, **not**
      counted as X's result), prompt for X stays active (FR-008 / SC-004 logic side); `Retry` re-arms the
      same button with a fresh countdown (FR-009); `Skip` records `Skipped` (**≠ Pass**) and advances
      (FR-009); a held button registers once and a bouncing repeat scores once (edge cases). Rides in the
      E4 commit. (FR-008/FR-009/FR-010; SC-004)
- [X] T027 [P] [US1] **[NEW]** Add `tests/ButtonPanelTester.Tests/Integration/Can/ButtonPressInterruptionTests.fs`:
      the link leaving `Connected` mid-prompt → `Interrupted LinkLost`, never `allActivePassed`; the
      selected panel disappearing from discovery mid-prompt → `Interrupted PanelLost`; a press for an
      **inactive** position (outside the variant mask) is ignored, never a prompted-button result
      (FR-014). Rides in the E4 commit. (FR-013/FR-014; SC-005)
- [X] T028 [P] [US3] **[NEW]** Add `tests/ButtonPanelTester.Tests/Integration/Can/ButtonPressRerunTests.fs`:
      after a completed run, Re-run clears all prior per-button results and starts a fresh sequence
      (SC-007); a panel baptized as a full-set provisional variant (EDEN-XP) drives all-eight prompts with
      that variant's labels (the prompted set/labels track the schema — US3 AC-2); the enablement guard
      reports `Disabled` (unavailable) when no panel is selected or the selected panel is not baptized
      (US3 AC-3 / FR-001, the service-seam projection of SC-008). Rides in the E4 commit. (FR-001/FR-003/
      FR-016; SC-007/SC-008)
- [X] T029 [US1] **[NEW]** Add `src/ButtonPanelTester.Services/Can/ButtonPressTestLogging.fs` +
      `tests/ButtonPanelTester.Tests/Unit/Can/ButtonPressTestLoggingTests.fs`: structured forensic records
      via `ILogger<ButtonPressTestService>` template messages + named parameters (`stem-logging`,
      archetype-A required — **no** string interpolation, **no** `Console.WriteLine`): each prompt, each
      observed press (expected and unexpected), each score, timeout, retry, skip — with timestamps and the
      observed bit (R10). Level discipline: prompt/score = Information, Unexpected/Missed/Interrupted =
      Warning, exception as first arg; correlate a run with `BeginScope`. Tests use the shipped
      `RecordingLogger`: a scripted run emits the expected record sequence; **no operator-identity field by
      design** (Principle V). Wire the emission into `ButtonPressTestService`. Insert
      `ButtonPressTestLogging.fs` after `BaptismLogging.fs`-equivalent position (between
      `IButtonPressTestService.fs` and `ButtonPressTestService.fs`). Lands as the E5 commit. (FR-012;
      Principle V; R10)

**Checkpoint E**: on the virtual adapters + `FrozenClock`, a scripted run reaches Pass / Missed /
Skipped / Unexpected / Interrupted deterministically; enablement is theorem + property-backed (SC-008's
basis); re-run clears; every event logs a forensic record with no operator identity. US1/US2/US3 are
CI-provable end-to-end below the GUI.

---

## Phase F: GUI (User Stories 1 + 2 + 3)

**Goal**: the button-press test surface — a **pure-render** `ButtonPressTestView` of FSM state +
result grid + countdown + Retry/Skip + the disabled/unavailable hint (BaptismView is pure-render; host
`App.fs` owns Msg/update and subscribes the service). **Functional layout only** — the visual-hierarchy
design is a deferred late-train spec (out of scope). All service-backed; the GUI renders, it decides
nothing.

**Commit grouping**: **F1** = {T030, T031} (US1 surface slice). **F2** = {T032, T033} (US2 recovery
controls slice). **F3** = {T034, T035} (US3 re-run / variant / unavailable slice). Each lands impl +
Headless tests together.

- [X] T030 [US1] **[NEW]** Add `src/ButtonPanelTester.GUI/Can/ButtonPressTestView.fs` + wire the surface
      slot in `src/ButtonPanelTester.GUI/App.fs` (subscribe `IButtonPressTestService` state changes
      marshalled onto `Dispatcher.UIThread` — spec-003/004 pattern): render the current prompt by **decal
      label** (FR-004; firmware name as secondary diagnostic detail) with a per-button countdown (FR-005);
      the per-button result grid (decal + outcome) and the aggregate "all active passed" indicator
      (FR-011), positive only when every active button scored `Pass`. (FR-004/005/011)
- [X] T031 [US1] **[NEW]** Add `tests/ButtonPanelTester.Tests.Windows/Gui/Can/ButtonPressTestViewTests.fs`
      (`Avalonia.Headless.XUnit`): for an OPTIMUS-XP run the prompt renders the decal ("Light") with the
      countdown; the result grid renders the four active rows in canonical order; the all-active-passed
      indicator is positive only when all four are `Pass`. Lands with T030 (F1 commit). (FR-004/005/011;
      SC-006)
- [X] T032 [US2] **[EXTEND]** Extend `ButtonPressTestView.fs` (+ `App.fs` Msg/update) with the
      recovery controls: per-button **Retry** (re-arm) and **Skip** (record Skipped + advance) offered on
      a `Missed`/in-flight button; surface an observed `Unexpected` press transiently (operator status /
      log echo) without advancing the prompt. (FR-008/FR-009)
- [X] T033 [US2] **[EXTEND]** Extend `ButtonPressTestViewTests.fs`: a timed-out button renders Retry/Skip;
      Retry invokes the service re-arm and re-shows the countdown; Skip records Skipped and advances; an
      Unexpected press surfaces without advancing. Lands with T032 (F2 commit). (FR-008/FR-009)
- [X] T034 [US3] **[EXTEND]** Extend `ButtonPressTestView.fs` (+ `App.fs`) with: a **Re-run** control
      that clears the grid and restarts the sequence (FR-003); variant-adaptive prompts/labels driven by
      the selected panel's `ButtonSchema`, with a **provisional** badge wherever a non-OPTIMUS label is
      shown (FR-016); the **unavailable** state rendering `testEnablement`'s explanation when the panel is
      not baptized or the link is not Connected (FR-001). (FR-001/FR-003/FR-016)
- [X] T035 [US3] **[EXTEND]** Extend `ButtonPressTestViewTests.fs`: Re-run clears the prior grid (SC-007);
      a provisional variant renders the provisional badge (FR-016); the enable matrix — the surface is
      unavailable with the explanation when not baptized / link not Connected, and never prompts on an
      unbaptized panel (SC-008). Lands with T034 (F3 commit). (FR-001/FR-003/FR-016; SC-007/SC-008)

**Checkpoint F**: the full prompt / score / grid flow plus Retry/Skip recovery, re-run, variant
adaptation, and the unavailable state are operable in the GUI against the virtual adapters; the Headless
suites pin the decal prompts, the result grid, the recovery controls, the enable matrix, and the
provisional badge. **CI-green here = code-complete** — not Done (Validation Gate, Phase G).

---

## Phase G: Hardware E2E + bench validation — the Validation Gate (US1 + US2 + US3)

**Goal**: the live-boundary proof CI cannot give (`live-boundary-smoke`) — a real OPTIMUS-XP panel
emits the `VAR_WRITE` frames the parser expects and the press-edge polarity is the **press** edge
(`1 → 0`), not release. **CI-green is code-complete; the bench E2E is the done line** (spec-003/004
ValidationPending discipline). Tracked under the new bench-validation tracking issue
[#253](https://github.com/luca-veronelli-stem/button-panel-tester/issues/253)
(filed alongside this `tasks.md`; mirrors #237's role for baptism SC-004 — the prior bench tracker #112
is closed). Bench needs one baptized OPTIMUS-XP panel on the rig.

- [X] T036 **[NEW]** Add `tests/ButtonPanelTester.Tests.Windows/Integration/Can/Hardware/ButtonPressTestHardwareTests.fs`,
      every case `[<Trait("Category","Hardware")>]` (excluded by the default `Category!=Hardware` CI
      filter) + the shipped env-gated attributes (`Fixtures/HardwareFactAttribute.fs`) — **never** a bare
      `[<Fact(Skip=…)>]` (#142 lesson): `[<HardwareFact>]` (`BPT_HARDWARE=1`) **full OPTIMUS-XP run** —
      select the baptized panel, run the sequence, each prompted button scored `Pass` within ~1 s
      (SC-001/SC-002), a not-pressed button `Missed` at ~10 s (SC-003), a wrong press `Unexpected` in the
      log without advancing (SC-004), the adapter unplugged mid-run → distinct link-lost, never
      all-active-passed (SC-005); `[<HardwareFact>]` **R2 polarity confirmation** — assert scoring fires
      on the **press** (bit `1 → 0`), not release (if a real panel scores on release, flip `PressedBit` and
      re-run — do not redesign; quickstart §Polarity confirmation); `[<ManualHardwareFact>]`
      (`BPT_HARDWARE_INTERACTIVE=1`) **interactive full run** — the operator-driven press-each-button cycle.
      Add the suite's checklist hooks to the bench tracking issue (full run, Missed timing, Unexpected,
      link-loss, polarity). (SC-001..006; FR-006/007/008/013)
- [ ] T037 Bench validation per [quickstart.md](./quickstart.md) §Bench walkthrough: run the suite on the
      rig (`BPT_HARDWARE=1`, plus the interactive gate), confirm the **press-edge polarity** (scoring on
      press, not release), and record the results in the PR/issue as the operator follow-up that gates the
      **Done** claim (Validation Gate; verification discipline — evidence before claims). Only after this
      passes is OPTIMUS-XP declared bench-validated; the other three variants stay provisional until their
      hardware reaches the bench. quickstart.md is **living** — update it only if commands/flows drifted.
      (SC-001..006)

**Checkpoint G**: a real OPTIMUS-XP panel is walked through its four active buttons, scoring on the
press edge with the firmware-pinned polarity confirmed; Missed/Unexpected/link-loss behave as specified.
The input-side wire format and firmware semantics are proven at the live boundary. The feature is
**Done**.

---

## Phase H: Polish & cross-cutting concerns

*(Distributes into the phase PRs that produce the audited surface; the final docs tasks ride with the
Phase F/G child. Listed here for the `/speckit-analyze` coverage pass.)*

- [X] T038 [P] XML-doc audit of the new public surfaces per COMMENTS / `stem-fp-discipline` §10:
      `ButtonStateFrame`, `KeyStateBitmap` (+ `PressedBit`), `ButtonSchema`, `ButtonPressTest` (types +
      `step` + `testEnablement`), `IButtonStateObserver`, `ButtonStateReassemblyObserver`,
      `InMemoryButtonStateObserver`, `IButtonPressTestService`/`ButtonPressTestService`,
      `ButtonPressTestLogging`, `ButtonPressTestView` — Lean citations in the house format (module path +
      theorem name + authoring task), contract paths current.
- [X] T039 [P] Logging audit per LOGGING / `stem-logging` over the button-press path
      (`ButtonPressTestService`, `ButtonPressTestLogging`, `ButtonStateReassemblyObserver`): typed
      `ILogger<T>`, template messages with named params (no string interpolation), exception-as-first-arg,
      no `Console.WriteLine` / `Debug.WriteLine` on production paths.
- [X] T040 [P] Principle V + FR-015 compliance audit over the button-press path: zero OS-user /
      machine-name / SID / MAC fields anywhere (panel UUIDs are device hardware identifiers — plan §V; the
      forensic log carries no operator identity); no result persistence beyond the in-session view — the
      test retains nothing about the panel (FR-015). Expected zero hits.
- [X] T041 [P] `cd lean; lake build` — the Phase 4 theorems (`parse_encode_roundtrip` + `encode_length`,
      `press_edge_iff_high_to_low`, `inactive_bits_ignored`, `canonical_order_total`,
      `test_visits_active_only`, `result_vector_length`, `test_outcome_total`, `pass_requires_press_edge`,
      `skip_never_pass`, `interrupt_excludes_all_passed`, `terminal_absorbs`, `test_enabled_iff`) compile
      with no `sorry`; `#print axioms` on each shows only {`propext`, `Classical.choice`, `Quot.sound`}.
- [X] T042 [P] Add a `CHANGELOG.md` `[Unreleased]` entry: "Button-press test (input side) — prompt a
      technician through each active button on a baptized panel, observe the CAN button-state frame, and
      score Pass / Missed / Unexpected / Skipped with a per-button grid; first input-side test (spec-005)."
- [X] T043 [P] Update `README.md`: link `specs/005-button-press-test/quickstart.md` and add a
      one-paragraph mention of the button-press test beside the baptism workflow mention; confirm
      `quickstart.md` §Bench walkthrough is current as the per-case run-sheet.

---

## Dependencies & Execution Order

### Phase order (strict)

`Setup (T001)` → **`Phase A`** → **`Phase B`** → **`Phase C`** → `Phase D` → `Phase E` → `Phase F` →
`Phase G` → `Polish`. A, B, C are foundational (the FSM and service can't run without the codec,
detector, schema, and observer). D needs A (the detector feeds `PressEdge`) + B (the FSM walks the
schema). E needs C (the service consumes `IButtonStateObserver`) + D (it drives `step`) + its own
Lean enablement (E1 before E2). F needs E (it subscribes the service). G needs F (it drives the
GUI-complete product).

### Commit groupings (bisect-safe; test + impl land together)

- **A1** = {T002, T003, T004} — Lean only; `lake build` green.
- **A2** = {T005, T006, T007} — codec vertical (frame + fixture + tests), one commit.
- **A3** = {T008, T009} — detector vertical (`PressedBit` + `pressEdges` + tests), one commit.
- **B1** = {T010} (Lean-only) · **B2** = {T011, T012} (schema + properties).
- **C1** = {T013, T014} · **C2** = {T015, T016} · **C3** = {T017}.
- **D1** = {T018} (Lean-only) · **D2** = {T019, T020} (FSM + properties).
- **E1** = {T021} (Lean-only) · **E2** = {T022} (enablement + properties) · **E3** = {T023} (service +
  composition) · **E4** = {T024, T025, T026, T027, T028} (integration suites) · **E5** = {T029}
  (forensic logging).
- **F1** = {T030, T031} · **F2** = {T032, T033} · **F3** = {T034, T035}.
- **G1** = {T036} · T037 is the bench follow-up (no commit unless quickstart drifted).
- **Polish** = T038–T043 (audits commit only what they fix; T042/T043 are docs commits).

### Within-phase notes

- `ButtonStateFrame.fs` (T005) defines the `KeyStateBitmap` **type**; `KeyStateBitmap.fs` (T008)
  defines `PressedBit` + the `pressEdges` **detector** over it — T005 before T008 in the fsproj.
- `ButtonPressTest.fs` is created by T019 (D2) and **extended** by T022 (E2, enablement surface) —
  D2 before E2; it reuses `Baptism.fs`'s `Enablement` DU, so it sits after `Baptism.fs` in the fsproj.
- `Ports.fs` (T013) references `ButtonStateFrame`, so T005 (the frame file) must be inserted before
  `Ports.fs` in the fsproj (it already is — see §Format compile order).
- `ButtonPressTestService.fs` is created by T023 (E3) and its emission wired by T029 (E5) — E3 before E5.
- `ButtonPressTestView.fs` is created by T030 (F1) and extended by T032 (F2) + T034 (F3) — keep the
  slices ordered to avoid rebase pain. `App.fs` is touched by T030/T032/T034 likewise.
- Lean-ahead inside slices: T002–T004 before any A2/A3 F#; T010 before T011; T018 before T019; T021
  before T022 (Constitution Principle I order).

### Parallel opportunities

- T003 ∥ T002 (different Lean modules). T025 / T026 / T027 / T028 are independent integration test
  files inside E4 (different files, all over the same fakes) — parallelizable.
- T038–T041 audits are mutually parallel; T042/T043 are docs. T037 is the bench follow-up (not [P]).
- Across phases: each phase is a separate child PR — sequential by the strict phase order above, not
  parallel (shared `Core/Can` + `App.fs` files serialize the slices).

---

## FR / SC → task coverage matrix

*(The downstream `/speckit-analyze` coverage pass keys off this — every FR and SC maps to at least one
implementing task and one test.)*

| Requirement | Implementing task(s) | Test(s) |
|---|---|---|
| **FR-001** test offered only for a baptized, observable panel on a Connected link; else unavailable + reason | T021, T022, T034; T046, T047 (I); T050 (J); T055, T056 (K) | T022 (`TestEnablementGuards`), T028, T035; T046/T047 reworked suites; T050 slow-cadence case; T056 destination-addressed stream cases |
| **FR-002** start; present active buttons one at a time in canonical-filtered order | T018, T019, T023 | T020 (`TestVisitsActiveOnly`), T024 |
| **FR-003** re-run end-to-end, clearing prior results | T019, T023, T034 | T028, T035 (SC-007) |
| **FR-004** decal label primary; firmware name secondary | T011, T030 | T012, T031 (SC-006) |
| **FR-005** per-button countdown, default 10 s | T023, T030 | T025, T031 |
| **FR-006** Pass on first scoring transition within window (armed → press edge; unarmed → first release, #293) | T008, T018, T019, T023; T051, T052 (J) | T009 (`PressEdgeDetectsHighToLow`), T020 (`PassRequiresPressEdge`), T024; T052 cold-boot case + FsCheck |
| **FR-007** Missed on timeout | T018, T019, T023 | T020, T025 (SC-003) |
| **FR-008** Unexpected logged-not-counted, no advance | T019, T023, T032 | T026, T033 (SC-004) |
| **FR-009** Retry / Skip; Skipped ≠ Pass | T018, T019, T023, T032 | T020 (`SkipNeverPass`), T026, T033 |
| **FR-010** advance on Pass | T019, T023 | T024, T026 |
| **FR-011** per-button grid + "all active passed" only when all Pass | T018, T019, T030 | T020 (`ResultVectorLength`), T024, T031 |
| **FR-012** forensic record per prompt/press/score/timeout/retry/skip | T029 | T029 |
| **FR-013** link/panel loss → distinct interruption; never all-passed | T018, T019, T023; T046 (I); T050 (J) | T020 (`InterruptExcludesAllPassed`), T027 (SC-005); T050 slow-cadence case |
| **FR-014** inactive-mask bits never a result | T003, T008, T019; T051, T052 (J — arming is active-masked too) | T009 (`InactiveBitsIgnored`), T027; T052 |
| **FR-015** retain nothing after the test (no persistence) | T023 (volatile state only), T040 | T040 (audit) |
| **FR-016** per-variant mask + decal labels; provisional flag surfaced | T010, T011, T034 | T012 (`SchemaActiveOnlyInOrder`), T035 |
| **SC-001** full four-button OPTIMUS run, all Pass, all-active-passed | T019, T023; T052 (J) | T024, T036; T053 (cold-panel precondition) |
| **SC-002** Pass within ~1 s of the press (unarmed: of the release, #293) | T023; T052 (J) | T024, T036; T052 |
| **SC-003** Missed within the window of the prompt | T023 | T025, T036 |
| **SC-004** wrong press never scores/advances; visible in log | T019, T023 | T026, T036 |
| **SC-005** interruption never reports all-passed; link-loss fast, panel-loss ≤ ~20 s (#293) | T019, T023; T046 (I); T050 (J) | T027, T036; T050 |
| **SC-006** OPTIMUS decals match the panel (Light/Suspension/Up/Down) | T011, T030 | T012, T031, T036 |
| **SC-007** re-run clears all prior results | T019, T023, T034 | T028, T035 |
| **SC-008** unavailable + reason when not baptized / link not Connected | T021, T022, T034; T046, T047 (I); T050 (J); T056 (K) | T022, T028, T035; T050; T056 |

---

## Implementation Strategy

### MVP = US1 CI-provable, then US2/US3, then the bench gate

1. **Phases A + B + C** — the wire foundation, the variant schema, and the RX observation seam
   (foundational; no story value alone but everything observes/scores through them).
2. **Phase D** — US1's FSM engine greens on CI (Checkpoint D): the seven preservation theorems +
   property mirrors. The **MVP cut for review** is reachable just above here (Phase E core).
3. **Phase E** — the service drives the FSM; US1 happy path + US2 recovery + US3 re-run green on the
   virtual adapters; enablement is theorem-backed (SC-008 basis).
4. **Phase F** — the surface is operable; **CI-green = code-complete** (ValidationPending).
5. **Phase G** — the bench proof **is the done line** (`live-boundary-smoke` Validation Gate): a
   CI-green parser/detector means nothing until a real panel emits frames the tool scores on the
   **press** edge with the firmware-pinned polarity confirmed.

### Discipline

- **bisect-safe / vertical-commits**: every commit compiles + passes tests; test rides with its impl
  (every A2/A3/B2/C1-C2/D2/E2-E5/F* slice). Conventional Commits with a `Tasks: T###` trailer linking
  back here.
- **Constitution order** (I): Lean → FsCheck/xUnit → F# inside every theorem-bearing slice (A1→A2/A3,
  B1→B2, D1→D2, E1→E2).
- **Mandatory triples** (I/II): `ButtonStateFrame`, the press-edge detector, `ButtonSchema`/`FirmwareButton`,
  the `ButtonPressTest` FSM, `Enablement` — each carries Lean theorem + FsCheck property + XML-doc
  citation. `ButtonResultRow` (data-model §5) is a pure presentation projection — rendered, not
  transitioned, so it carries no theorem of its own.
- **Gate** (`./gate.ps1`): at `/speckit-implement` time, per child PR, copy the template fresh and extend
  it (build + both test projects `Category!=Hardware` + `lake build` + a focused button-press `--filter`)
  in its own `chore: extend gate.ps1` commit. *(Out of scope for this task-generation run.)*
- **Stopgap discipline** (VI): the inline command/address hardcode (T015) **extends** the inherited
  stopgap (no new bypass — R6); the §C5 protocol-metadata **fetch** migration is a separate standalone
  ticket (decoupled from the parked #156), explicitly out of scope.

---

## Notes

- This `tasks.md` is the breakdown only — no implementation in this run.
- `[P]` = different files, no dependency on an incomplete task. Phases A/B/C are foundational (no story
  label); Phase G spans all three stories; enablement (E2) serves US1's surface and US3's unavailable
  path (see §Format).
- Two supporting issues are filed alongside this breakdown: the **bench-validation tracking issue**
  (#253, Phase G hooks) and the standalone **§C5 protocol-metadata fetch** ticket (#254, the
  inline-stopgap migration, decoupled from the parked #156).
- Next: the parent epic + one ordered child PR per phase A–G (`resolve-epic`), then `/speckit-analyze`
  (cross-artifact consistency) before `/speckit-implement`.

---

## Phase I: Observability re-key (corrective — fix #270, 2026-06-24)

**Why**: bench validation (#253) found the test's observability/selection mis-keyed to WHO_I_AM
discovery. A baptized panel is silent on WHO_I_AM (`AAS_STAND_BY`; `CORRECTIONS.md` §C1) and instead
heartbeats its button-state on a **directed CAN ID** whose machineType byte (bits 23–16) is the variant
(OPTIMUS `0x000A0441`, Eden-XP `0x00030141`, R-3L `0x000B0481`). The shipped
`ButtonStateReassemblyObserver` filtered **broadcast `0x1FFFFFFF` only** (`:66`, ignores non-broadcast
at `:120`), so it observed nothing from a real panel — **Phase C is in scope, not just E/F/G**. See
spec.md §Clarifications (Session 2026-06-24). Tool-side only; no firmware change.

**Design (Luca-signed-off 2026-06-24)**: match-any-non-broadcast + variant-from-ID (no baptism
plumbing); auto-target the single heartbeating baptized panel (drop `IPanelDiscoveryService` from the
button-press path); observability/panel-loss off button-state frame recency with **configurable
thresholds** (provisional defaults: observable window 2 s, panel-lost 3 s — bench-confirmed like the
press-edge polarity; the ~12 s in the original note was CAN id `0x00000008`, a different message).

> **Superseded in part by Phase J (#293, 2026-07-20):** the 2 s / 3 s defaults were calibrated
> against the heartbeat's post-boot fast ramp; the idle steady state of a cold panel is
> `TEMPO_CAN_LENTO` ≈ 12.5 s, so the thresholds are recalibrated to 15 s / 20 s (firmware-derived,
> T050). And the "~12 s was a different message" parenthetical is itself corrected: that figure
> **was** the button-state slow branch, mis-attributed to the tool's SRID. And superseded in part
> by **Phase K (#296)**: the observer's accept rule moves from the arbitration ID to the payload
> **senderId** (the arbitration ID is the destination — the baptizing master's address). The
> auto-target and recency model stand.

**Commit grouping (bisect-safe)**: **I1** = {T044} (Lean-only). **I2** = {T045} (observation type +
observer rework — one vertical commit: port + observer + fake + tests + minimal service adaptation).
**I3** = {T046} (service presence/observability re-key + discovery drop). **I4** = {T047} (GUI). **I5** =
{T048} (hardware E2E). **I6** = {T049} (contracts/data-model/CHANGELOG docs).

- [x] T044 **[NEW]** Add `lean/Stem/ButtonPanelTester/Phase4/ButtonStateObservation.lean`: model the
      directed-CAN-ID → machineType extraction + variant decode; prove `machine_type_at_bits_23_16`
      (machineType = `(id >>> 16) &&& 0xFF`) and `non_marketing_ids_rejected` (broadcast `0x1FFFFFFF`
      → `0xFF` and the tool SRID `0x00000008` → `0x00` decode to non-marketing → not accepted). Extend
      the `Phase4.lean` umbrella; `lake build` green, `sorry`-free, standard axioms only.
      (Constitution I; FR-001)
- [x] T045 **[NEW/EXTEND]** The observation-carries-variant + directed-ID observer vertical, ONE commit:
      (a) add `src/ButtonPanelTester.Core/Can/ButtonStateObservation.fs` — `ButtonStateObservation =
      { Frame: ButtonStateFrame; Variant: MarketingVariant }` + `variantOfDirectedId : uint32 ->
      VariantIdentity` reusing `PanelObservation.VariantDecoder.decode` on `(CanId >>> 16) &&& 0xFF`;
      XML doc cites `Phase4/ButtonStateObservation.lean` (T044). (b) change `IButtonStateObserver`
      (`Core/Can/Ports.fs`) to `ButtonStateObserved : IObservable<ButtonStateObservation>`. (c) rework
      `src/ButtonPanelTester.Infrastructure/Can/ButtonStateReassemblyObserver.fs`: per-source-CanId
      reassembly; accept a completed packet **iff** `(CanId>>>16)&0xFF` decodes to a `Marketing` variant
      (drops broadcast/virgin/SRID inline), keep the cmd `0x0002` + addr `{0x8000,0x803E}` filter, emit
      `ButtonStateObservation`. (d) update the fake `InMemoryButtonStateObserver.fs` + the observer
      Windows tests (drive directed-ID frames, assert variant; broadcast/SRID rejected) + a FsCheck
      property mirroring T044. (e) minimal `ButtonPressTestService` subscription adaptation so it still
      compiles (`.Frame.Bitmap`). Mandatory triple on the new wire fact. (FR-001; Constitution I/II/III)
- [x] T046 **[EXTEND]** Re-key `src/ButtonPanelTester.Services/Can/ButtonPressTestService.fs`: presence
      guard + observability off **button-state recency** (track last-frame time via `IClock`; no frame
      for `panelLostThreshold` during a run → `Halt PanelLost`; a frame within `observableWindow` →
      observable), **drop the `IPanelDiscoveryService` ctor dependency** from the button-press path, take
      the variant from the observation. Add the threshold config constants (provisional 2 s / 3 s,
      XML-doc noted bench-confirmed). Update the composition registration + `CompositionRootCanTests`.
      Rework the integration tests that drove PanelLost from discovery pruning
      (`ButtonPressInterruptionTests`, `ButtonPressRerunTests`, `ButtonPressTestE2ETests`) to the recency
      model + variant-from-stream, `FrozenClock`-driven. (FR-001/FR-013; SC-005/SC-008)
- [x] T047 **[EXTEND]** Re-key `src/ButtonPanelTester.GUI/App.fs` (~lines 627–656): compute
      `testObservable` / `testSelectedBaptized` / variant from the button-state observation stream
      (recency + the observation's `Variant`), not `lastPanelsOnBus`; auto-target the single heartbeating
      panel (no UUID selection). Update `ButtonPressTestView` enablement wiring as needed and the Headless
      tests (`Gui/Can/ButtonPressTestViewTests.fs`) for the stream-driven enable matrix. (FR-001; SC-008)
- [x] T048 **[EXTEND]** Rework `tests/.../Hardware/ButtonPressTestHardwareTests.fs`: drop the
      `waitForOptimusXpUuid` WHO_I_AM precondition; wait up to ~2 s for the first button-state
      observation and assert its variant is OPTIMUS-XP; then run the existing button-press cases. Update
      the #253 bench checklist hooks (observability = heartbeat arrival; thresholds confirmed on the rig).
      (SC-001..006; FR-001/FR-013)
- [x] T049 **[EXTEND]** Update `specs/005-button-press-test/contracts/button-state-observer-port.md` and
      `button-state-wire-format.md` for the directed-ID match rule + the `ButtonStateObservation`
      envelope; update `data-model.md` (observation + thresholds); add a `CHANGELOG.md` `[Unreleased]`
      line ("Re-key button-press observability to the button-state heartbeat (directed CAN ID), not
      WHO_I_AM discovery — fixes the bench-surfaced defect").

**Checkpoint I**: the observer catches a real baptized panel's directed-ID button-state, derives the
variant from the CAN ID, and the service/GUI/test key observability + panel-loss off heartbeat recency
with bench-tunable thresholds — no dependency on WHO_I_AM discovery. Re-run the #253 bench to validate
(the Done line).

---

## Phase J — dual-rate heartbeat correction (child I, #293)

Second corrective phase, from the 2026-07-20 firmware + trace re-read (no bench run). Phase I re-keyed
observability onto the button-state heartbeat correctly, but calibrated its recency thresholds against
the post-boot *ramp* cadence rather than the idle steady state, and the cold-panel latch means a
button's first press is never transmitted at all. See `spec.md` §Clarifications Session 2026-07-20,
`research.md` R1 (dual-rate table), `data-model.md` §6a/§6b, `plan.md` §Amendment 2026-07-20.

Issue #293's AC-5 (artifact corrections: spec/research/data-model/contract/plan) is already landed in
this branch's amendment commits — no task backs it by design.

**Commit grouping (bisect-safe)**: **J1** = {T050} (thresholds — one vertical commit: constants +
tests). **J2** = {T051} (Lean-only, mirrors I1 — always green). **J3** = {T052} (arming rule — F#
vertical: FsCheck property + detector + service threading + tests; depends on J2). **J4** = {T053}
(hardware suite recalibration). **J5** = {T054} (docs; orchestrator-owned). Parallelizable: J1, J4,
and the J2→J3 chain are pairwise independent (disjoint files); J5 last.

- [x] T050 **[EXTEND]** Retune the recency thresholds in
      `src/ButtonPanelTester.Core/Can/ButtonPressTest.fs`: `observableWindow` 2 s → **15 s**,
      `panelLostThreshold` 3 s → **20 s**. Both must exceed `TEMPO_CAN_LENTO` ≈ 12.5 s, the cadence of a
      cold never-touched panel. Rewrite both XML docs to cite `UserMain.c:1013–1020` and the measured
      186.7 ms / 12.5 s rates, and drop the stale "derived from the same ~182 ms refresh" /
      "to be confirmed on the rig" wording (the rates are firmware-derived, not bench-provisional).
      **RED**: extend `tests/ButtonPanelTester.Tests/Integration/Can/ButtonPressInterruptionTests.fs`
      with a case driving the heartbeat at the **slow** cadence (frames 12.5 s apart on `FrozenClock`)
      and asserting the run stays live — fails at 3 s, passes at 20 s. **GREEN**: the constant change
      plus any threshold-dependent assertions in `ButtonPressRerunTests` / `ButtonPressTestE2ETests` /
      `Gui/Can/ButtonPressTestViewTests.fs` updated to the new values. RED and GREEN fold into the one
      commit: the new case is written and observed failing first, then the constants change.
      (FR-001/FR-013; SC-005/SC-008)
- [x] T051 **[LEAN]** Extend `lean/Stem/ButtonPanelTester/Phase4/KeyStateBitmap.lean` with the arming
      model (Lean-only commit, mirrors I1): an `armed` predicate (position observed with bit value `1`
      in some earlier bitmap) and a `scored` predicate over (armed, prior, next). Theorems:
      `armed_scores_on_press_edge` (an armed position scores iff `pressEdges` reports it — the existing
      `press_edge_iff_high_to_low` semantics are preserved), `unarmed_scores_on_first_release` (an
      unarmed position scores exactly once, on its first `0 → 1` transition, and that transition arms
      it), `arming_monotonic` (armed never reverts). Update the file header doc: it claims to mechanise
      data-model §2 verbatim; it now also mechanises §6b. No `sorry`; axioms ⊆ the constitution set.
      (FR-006/FR-014; SC-001/SC-002; Constitution I)
- [x] T052 **[EXTEND]** Implement the arming rule in
      `src/ButtonPanelTester.Core/Can/KeyStateBitmap.fs` per T051's theorems: `pressEdges` unchanged;
      add the armed-set threading and a `scoredPositions` (armed → press edge; unarmed → first release,
      which also arms). Thread the armed state through
      `src/ButtonPanelTester.Services/Can/ButtonPressTestService.fs` alongside `prior`; `PressedBit`
      stays `0uy`. XML docs cite the T051 theorems. **RED**: a Core unit test driving the cold-boot
      sequence (`0x00` baseline → press emits no frame → release frame) currently yields no score for
      the prompted button; the new test asserts it scores on the release. **GREEN**: the arming rule +
      an FsCheck property mirroring `unarmed_scores_on_first_release`/`arming_monotonic` (custom
      `Arbitrary` over bitmap sequences) + existing detector/service tests still green. Folded into one
      commit, RED observed first. Depends on T051. (FR-006/FR-014; SC-001/SC-002)
- [x] T053 **[EXTEND]** Recalibrate the hardware suite for the slow branch:
      `tests/ButtonPanelTester.Tests.Windows/Integration/Can/Hardware/ButtonPressTestHardwareTests.fs`
      `heartbeatTimeout` 2 s → **15 s** (the shipped wait encodes the misread ~182 ms cadence and fails
      ~84 % of the time against a cold panel — the very scenario #293 fixes), with the XML doc rewritten
      to the dual-rate fact (first observation within ≈ 12.5 s + slack) and the diagnostic messages'
      "verify a baptized panel is heartbeating" wording kept. Review the other budget constants against
      the dual-rate table (`fullRunTimeout`, `missTimeout`, `pressTimeout` are press-activity-driven —
      once the first prompt is answered the panel is in the ≈ 188 ms branch — so they stand; state that
      in the commit body). Update the #253 bench-checklist hooks: observability = heartbeat arrival
      within 15 s on a cold panel; thresholds are firmware-derived (drop T048's "confirmed on the rig"
      hook). Compile-checked locally (`Category=Hardware` is CI-excluded); the bench run itself stays
      #253. (SC-001..006; FR-001/FR-013)
- [x] T054 **[DOCS]** Orchestrator-owned docs sweep: (a) `CHANGELOG.md` `[Unreleased]` entry for the
      dual-rate correction (thresholds recalibrated above `TEMPO_CAN_LENTO`; first press after power-up
      now scored via the unarmed release rule). (b) `quickstart.md` refresh: drop the stale "Select the
      baptized panel" step (auto-target since #270), note a cold panel can take ≈ 12.5 s to first
      surface as observable, and describe first-press scoring on release for a cold panel; the polarity
      section stands. (c) `docs/Context/bpt-rollout/03-roadmap.md` spec-007 section: annotate the "5 s
      heartbeat timeout / any frame counts" sketch with the dual-rate constraint so spec-007 doesn't
      re-commit this defect at session level. (d) Verify the FR/SC matrix rows updated in this
      amendment commit stay accurate as J1–J4 land.

**Checkpoint J**: a cold, never-touched baptized panel stays continuously observable at its ≈ 12.5 s
heartbeat, a run started against it is not killed by `PanelLost`, the first press of every button
scores (on its release), and the hardware suite's own preconditions tolerate the slow branch. Then
re-run the #253 bench — still the Done line.

---

## Phase K — destination addressing: variant from the senderId (child J, #296)

Third corrective phase, from the 2026-07-23 bench sanity capture (`bench-logs/pcan/test1.trc`): a
tool-baptized panel heartbeats on the tool's own SRID `0x00000008` (the arbitration ID is the
DESTINATION — the baptizing master's stored address), so the #270 arbitration-ID accept rule drops
every frame. The variant lives in the packet **senderId** (bits 23–16). See `spec.md`
§Clarifications Session 2026-07-23, `research.md` R1 destination-addressing addendum, both #296
contract sections, `plan.md` §Amendment 2026-07-23.

Issue #296's AC-5 artifact corrections landed in this branch's amendment commits — no task backs
them by design (the residual AC-5 surfaces are T056(a)'s Ports.fs doc and T057's sweep).

**Commit grouping (bisect-safe)**: **K1** = {T055} (Lean-only, mirrors I1/J2 — always green).
**K2** = {T056} (observer re-key — one vertical commit: Core envelope helper + Infrastructure
observer + fake + FsCheck + observer tests; depends on K1). **K3** = {T057} (docs;
orchestrator-owned). Strictly serial: K1 → K2 → K3.

- [x] T055 **[LEAN]** Extend `lean/Stem/ButtonPanelTester/Phase4/ButtonStateObservation.lean`
      (Lean-only commit): model the completed-packet accept rule over (cmd, addr, senderId) —
      `accept ↔ cmd = 0x0002 ∧ addr ∈ recognised ∧ (senderId >>> 16) &&& 0xFF decodes Marketing`.
      Theorems: `variant_from_sender_id` (the bits-23-16 extraction applied to the senderId word —
      reuse/instantiate the T044 `machine_type_at_bits_23_16` lemma), `who_i_am_rejected_on_cmd`
      (cmd `0x0024` never accepted regardless of senderId), `virgin_sentinel_rejected` (`0x80FE`
      never accepted), `arbitration_id_irrelevant` (model the accept rule over a packet shape that INCLUDES the
      arbitration id, so the theorem — acceptance and variant invariant under changing it — is
      non-vacuous). The existing `accepted` def + `optimus_directed_id_accepted` docs claim to BE
      the observer's accept predicate: re-document them as the machineType-word decode predicate
      the new packet-level rule composes with (statements stay true; do not delete). Header doc:
      mechanises wire-format §Destination addressing (#296). No `sorry`; axioms ⊆ the
      constitution set.
      (FR-001; Constitution I)
- [x] T056 **[EXTEND]** Re-key the observer per T055: (a)
      `src/ButtonPanelTester.Core/Can/ButtonStateObservation.fs` — add `variantOfSenderId` (the same
      bits-23-16 extraction, applied to the senderId word); XML docs cite the T055 theorems +
      wire-format #296. Re-key the stale directed-id XML doc on `IButtonStateObserver` in
      `src/ButtonPanelTester.Core/Can/Ports.fs` (doc-only) in the same slice. (b)
      `src/ButtonPanelTester.Infrastructure/Can/ButtonStateReassemblyObserver.fs` — remove the
      arbitration-ID variant gate; reassemble per source arbitration ID as today; on a completed
      packet accept iff cmd `0x0002` + recognised addr + senderId machineType decodes Marketing
      (extract the senderId from the reassembled packet bytes 1-4, big-endian, mirroring
      `PacketDecoder.ReadSenderIdBigEndian` — the observer hand-indexes the merged array and does
      NOT use the dictionary-driven `PacketDecoder`; keep it that way); emit the observation with
      the senderId-derived variant. Commit-body note: without the arbitration-ID pre-filter the
      per-id `PacketReassembler` map allocates an entry per id seen on the bus — negligible on a
      bench bus. (c) `tests/.../Fakes/Can/InMemoryButtonStateObserver.fs` (doc-comment re-key only — the fake's
      surface is post-reassembly, no ids exist there) + the
      observer Windows tests: drive a destination-addressed stream — arb. ID `0x00000008`,
      senderId `0x000A0101` (the test1.trc shape) → accepted as OptimusXp; machine-destination
      stream (arb. `0x000A0441`, same senderId) → accepted; WHO_I_AM broadcast → rejected on cmd;
      `0x80FE` → rejected on addr. (d) FsCheck property mirroring `variant_from_sender_id` +
      `arbitration_id_irrelevant` (generate arbitrary arbitration ids; acceptance/variant must not
      change). **RED**: the test1.trc-shaped stream currently yields zero observations; new test
      asserts one OptimusXp observation — observed failing first. **GREEN**: the re-key + full
      observer suite + `./gate.ps1`. Folded into one commit, RED first. Depends on T055.
      (FR-001; SC-008)
- [ ] T057 **[DOCS]** Orchestrator-owned docs sweep: CHANGELOG `[Unreleased]` — add the #296
      entry AND amend the still-unreleased #270 entry, which asserts dropping `0x00000008`, the
      very id the tool now listens on; quickstart bench-walkthrough note (the heartbeat arrives on
      the tool SRID `0x00000008` — what to expect in PCAN-View); hardware-suite directed-id
      references — doc comments AND the three `Assert.Fail`/prompt diagnostic strings
      (`ButtonPressTestHardwareTests.fs:282/333/394`; string-only, no assertion change); stale
      directed-id comments in `App.fs` (:492/:667/:818) and the `observableWindow` XML doc
      (`ButtonPressTest.fs:196-198`) (comment-only); research.md R5's pre-#270
      `IObservable<ButtonStateFrame>` envelope drift; memory + #253 body already updated
      orchestrator-side.

**Checkpoint K**: a panel baptized by THIS tool is observed — the GUI enables off its heartbeat on
`0x00000008` and the hardware suite's first-observation wait passes on the rig. Then re-run the
#253 bench — still the Done line.
