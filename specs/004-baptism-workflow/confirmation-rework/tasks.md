---
description: "Task list for the spec-004 baptism confirmation-model rework (F1 + F6 + guided recovery)"
---

# Tasks: Baptism Confirmation-Model Rework (F1 + F6)

**Input**: [plan.md](./plan.md) (task authority — §Phases R1–R6), [data-model.md](./data-model.md)
(corrected FSM/outcomes/events/theorems), [../spec.md](../spec.md) (Clarifications 2026-06-17;
FR-004/005/006/006a/007/010/015, SC-001/002/007).

**Tests**: REQUIRED (Constitution II/IV). The FSM, outcome DU, and event DU are closed-domain →
mandatory triple: **Lean theorem + FsCheck property + XML-doc citation** (stem-fp §3).

**Numbering**: `RW##`, fresh for this rework, to not collide with the shipped `../tasks.md`
(T001–T046, frozen trace of #213–#217).

**bisect-safe.** Every commit compiles + greens its tests on its own (`bisect-safe` +
`vertical-commits`). Lean lands ahead of F# inside each theorem-bearing slice (RW01 before RW02);
where the constitution order would leave a red intermediate, the test rides with its impl.
Conventional Commits with a `Tasks: RW##` trailer. `gate.ps1` copied fresh at implement time and
extended (build + both test projects `Category!=Hardware` + `lake build` + a focused baptism
`--filter`) in its own `chore: extend gate.ps1` commit.

**Scope pin.** One child PR folds RW01–RW06 (F1 + F6 + recovery rendering). RW07–RW08 (hardware
recovery E2E + bench validation) are **#218's** — they need the bench rig. If RW03 (ACK RX port)
inflates the diff it is the one clean split point (plan §Child PR boundary).

**Bisect-safety & execution order (orchestrator re-slice, 2026-06-17).** The naive RW01→RW06 order
is NOT bisect-safe: the Core DUs are wildcard-free, so adding `ClaimNotAdopted`/`AwaitingAdoption`
breaks the exhaustive matches in `BaptismLogging.fs` + `BaptismView.fs` (compile error), and the
`step` flip (`assigning`-write → `AwaitingAdoption`, not `Succeeded`) breaks the existing service
integration suites (`BaptismE2ETests`, `PostSuccessWarningTests`, `TimeoutE2ETests`) which assert
success-on-write-completion — and the service cannot reach the new `Succeeded` until the `0x25` ACK
observer (RW03) is wired in and consumed. So the **commit order** is re-cut (task IDs unchanged):

1. **RW01** — Lean FSM. *(done — commit `a749518`→stamped)*
2. **RW03** — the `0x25` ACK RX plumbing (port + production adapter + InMemory fake + composition +
   adapter unit tests). **ADDITIVE: nothing consumes it yet**, so it lands green BEFORE the FSM flip.
3. **RW02 + RW04 (ONE combined commit)** — the confirmed-adoption behavioral flip. Core types + `step`
   + `BaptismSequenceProperties` + `BaptismService` driving `AwaitingAdoption` (subscribing the RW03
   ACK observer + adoption-deadline ticks) + the three integration suites updated + the **minimal
   compile-arms** in `BaptismLogging.fs` (outcome/state → audit string — the real RW05 projection) and
   `BaptismView.fs` (state in-progress predicate = real; a compile-safe `ClaimNotAdopted` outcome arm =
   bridge, refined in RW06). Green ONLY as one commit (the step flip + its service + its tests are
   inseparable). Trailer `Tasks: RW02, RW04`.
4. **RW05** — `BaptismLogging` audit TEST for the new outcome + the `adoption confirmed` step-reached
   value (the source arm already lands in step 3).
5. **RW06** — the full FR-015 guided-recovery rendering + `BaptismViewTests` Headless (refines the
   compile-arm from step 3).

`BaptismGuidance.recoveryText` already has a `_ -> None` wildcard, so it does not break on the new
outcome; only `BaptismLogging` + `BaptismView` need compile-arms in step 3.

---

## Phase R1: Lean FSM (the corrected spec) — FOUNDATIONAL

**Commit group**: RW01 = {T} Lean-only, `lake build` green.

- [X] RW01 **[AMEND]** `lean/Stem/ButtonPanelTester/Phase3/BaptismSequence.lean`: add the
      `awaitingAdoption (deadline) (ackSeen)` state, the `claimNotAdopted` outcome, and the
      `setAddressAcked` event (data-model §4.1/§4.2/§4.3). Rewrite `step`: (a) `awaitingAnnounce`
      branches `Marketing chosen → assigning` / `Virgin → stay` (F1) / `Marketing other | Unknown _
      → unexpectedVariant`; (b) `assigning` write-complete → `awaitingAdoption` (not `succeeded`);
      (c) `awaitingAdoption` — `setAddressAcked` sets `ackSeen`; selected-uuid `announcementHeard`
      → `claimNotAdopted`; foreign uuid no-op; `tick ≥ deadline` → `succeeded` iff `ackSeen` else
      `claimNotAdopted`; link-loss → `linkLost`. Restate **`baptize_progress`** (succeeded iff
      claim writes complete + matching chosen-variant announce in budget + assign writes complete +
      `setAddressAcked` + silence held to the adoption deadline). Extend **`baptize_outcome_total`**
      to seven outcomes (extend `closingSchedule` for `awaitingAdoption`). Keep
      **`no_assignment_without_match`**. Add **`virgin_keeps_waiting`** and
      **`no_success_without_adoption`**. `sorry`-free; axioms ⊆ {`propext`, `Classical.choice`,
      `Quot.sound`}. (Constitution I; FR-004/006/006a)

**Checkpoint R1**: the corrected FSM is mechanised; `lake build` green; no false-success path exists
(succeeded requires ack + silence).

---

## Phase R2: Core FSM mirror + properties (User Story 1)

**Commit group**: RW02 = {T} (Core `Baptism.fs` + FsCheck, one commit — mirror lands with its tests).

- [ ] RW02 **[AMEND]** `src/ButtonPanelTester.Core/Can/Baptism.fs`: mirror RW01 exactly (stem-fp
      §10 — same names/case order): add `AwaitingAdoption` state, `ClaimNotAdopted` outcome,
      `SetAddressAcked` event; rewrite the pure `step` per data-model §4.1; update XML-doc citations
      to the RW01 theorems. **Same commit** — add/extend
      `tests/ButtonPanelTester.Tests/Property/Can/BaptismSequenceProperties.fs`:
      `BaptismSucceedsIffConfirmedAdoption` (replaces the write-completion mirror —
      `baptize_progress`), `VirginAnnounceKeepsWaiting` (F1), `ClaimNotAdoptedWhenStillAnnouncing`
      (FR-006a), `BaptismOutcomeTotal` extended to seven, `NoSuccessWithoutAdoption`. Foreign-uuid
      no-op property retained. (Constitution I/II; stem-fp §3 update protocol; FR-004/006/006a)

**Checkpoint R2**: Core FSM and Lean agree on the corrected model; properties green; the closed-DU
triple is intact for the new state/outcome/event.

---

## Phase R3: `0x25` ACK RX observation (D1) — new boundary

**Commit group**: RW03 = {T,T,T} port + adapter + fake + wiring (one commit; ports compile green,
exercised from R4).

- [X] RW03a **[EXTEND]** `src/ButtonPanelTester.Core/Can/Ports.fs`: add a minimal RX port for the
      SET_ADDRESS ACK — e.g. `ISetAddressAckObserver` with an event/observable carrying the ACK
      observation (mirror the shipped `IWhoIAmObserver` shape). XML doc: it surfaces the
      application-layer `0x25` ACK addressed to the tool, an adoption fast-positive (the TX port
      stays fire-and-forget; D1). (Constitution III)
- [X] RW03b **[NEW]** `src/ButtonPanelTester.Infrastructure/Can/SetAddressAckObserver.fs`
      (`net10.0-windows` if PEAK-bound, else neutral): filter the RX frame stream
      (`ICanFrameStream`/reassembly) for the `0x80|0x25` ACK addressed to the tool's srid; surface
      it on the port. Unit-test it against a fake `ICommunicationPort` replaying a captured/fixture
      ACK frame (spec-003 Phase-C frame-synthesis precedent) — assert the genuine `02 80 25` is
      surfaced and a `02 80 23` / foreign frame is not.
- [X] RW03c **[NEW]** `tests/ButtonPanelTester.Tests/Fakes/Can/InMemorySetAddressAckObserver.fs`:
      scriptable virtual ACK source (raise-on-demand) for CI; **[EXTEND]**
      `CompositionRoot.fs` to register the production adapter and extend `CompositionRootCanTests`
      to resolve it, hardware-free. (Constitution III/IV)

**Checkpoint R3**: the ACK is observable on an RX port with a virtual adapter for CI; nothing in the
success path consumes it yet.

---

## Phase R4: Service drives AwaitingAdoption (User Story 1)

**Commit group**: RW04 = {T} service + integration suites (one commit). RW05 = {T} audit.

- [ ] RW04 **[AMEND]** `src/ButtonPanelTester.Services/Can/BaptismService.fs` (+ `IBaptismService`):
      consume `ISetAddressAckObserver` (RW03) and `IWhoIAmObserver`; after the assign write
      completes, enter `AwaitingAdoption` with `adoptionBudget` (`IClock`/`FrozenClock`, no
      wall-clock sleep); reach `Succeeded` only on `ackSeen ∧ silence-held-to-deadline`; emit
      `ClaimNotAdopted` on a selected-uuid re-announce or a no-ACK deadline. **Fold FR-007** into the
      gate; keep the residual post-success re-announce **backstop warning** (volatile, FR-013;
      cancelled by a new attempt / link loss). Serialize transitions under the existing lock (never
      across await; stem-async §8). **Same commit** — amend
      `tests/ButtonPanelTester.Tests/Integration/Can/BaptismE2ETests.fs` +
      `TimeoutE2ETests.fs`: (a) happy path now needs the scripted `0x25` ACK **and** confirmed
      silence before `Succeeded`; (b) assign written + panel keeps announcing → `ClaimNotAdopted`,
      never `Succeeded` (F6); (c) assign written + ACK seen but no silence (a re-announce) →
      `ClaimNotAdopted`; (d) silence held but no ACK by the deadline → `ClaimNotAdopted` (D2 strict);
      (e) F1 — a virgin re-announce during `AwaitingAnnounce` keeps waiting, a later chosen-variant
      announce still succeeds; (f) link-loss in `AwaitingAdoption` → `LinkLost`. (FR-004/006/006a/007;
      SC-001/002)
- [ ] RW05 **[AMEND]** `BaptismLogging.fs` + `BaptismLoggingTests.fs`: the audit record covers the
      new `ClaimNotAdopted` outcome and the `adoption confirmed` step-reached value; still exactly
      one record per attempt across all seven outcomes (SC-006). (FR-012)

**Checkpoint R4**: on the virtual adapters + `FrozenClock`, a scripted baptism reaches all seven
outcomes deterministically; no write-completion success path remains; Lean + FsCheck agree. US1
CI-provable below the GUI under the corrected criterion.

---

## Phase R5: GUI — ClaimNotAdopted + recovery (User Stories 1)

**Commit group**: RW06 = {T} view + Headless tests (one commit).

- [ ] RW06 **[AMEND]** `src/ButtonPanelTester.GUI/Can/BaptismView.fs`: render the `ClaimNotAdopted`
      outcome with the **guided recovery** (FR-015) — state the claim did not take (deterministic,
      not "likely") and direct the operator to Reset-to-virgin then re-baptize, using the existing
      Reset/Baptize affordances; keep the success rendering's silence explainer (now the confirmed
      signal); surface the residual FR-007 backstop warning if raised. Wire any new
      `ISetAddressAckObserver` subscription onto `Dispatcher.UIThread` if the GUI shows ACK progress
      (optional). **Same commit** — extend
      `tests/ButtonPanelTester.Tests.Windows/Gui/Can/BaptismViewTests.fs`: `ClaimNotAdopted` renders
      the recovery guidance; success rendering only appears after confirmed adoption is signalled;
      the backstop warning renders when raised. (FR-006a/007/015; SC-007 GUI side)

**Checkpoint R5**: both flows operable in the GUI under the corrected criterion; **CI-green =
code-complete** (ValidationPending — the bench proof is RW08).

---

## Phase R6: Hardware recovery E2E — the Validation Gate (lands with #218)

> Not in the rework child's CI scope. Sequenced into the bench gate
> [#218](https://github.com/luca-veronelli-stem/button-panel-tester/issues/218), which also
> re-bases its existing claim/reset E2E on the corrected criterion (confirmed adoption, not
> write-completion).

- [ ] RW07 **[AMEND]** `tests/ButtonPanelTester.Tests.Windows/Integration/Can/Hardware/BaptismHardwareTests.fs`:
      re-base the claim E2E on confirmed adoption (assert the `0x25` ACK + broadcast-silence, SC-002,
      not write-completion); add a **recovery E2E** `[<HardwareFact>]` (SC-007) — induce a
      not-adopted state, recover via Reset → re-baptize to a confirmed adoption; assert F1 no longer
      fires on a real post-reset virgin re-announce. `Category=Hardware`, env-gated (never bare
      `Skip`). Add the checklist entries to #112. (SC-001/002/007; FR-006a/015)
- [ ] RW08 Bench validation per [../quickstart.md](../quickstart.md): run on the rig, record results
      as the operator follow-up that gates the **Done** claim (verification discipline). Update
      `quickstart.md` (living) only if commands/flows drifted.

**Checkpoint R6 (= #218)**: a real not-adopted panel is detected (never falsely succeeded) and
recovered; the F1 race no longer fires on real silicon. The confirmation model is **Done**.

---

## FR / SC → task coverage (rework slice)

| Requirement | Task(s) | Test(s) |
|---|---|---|
| **FR-004** virgin = keep-waiting; unexpected only on non-virgin | RW01, RW02 | RW02 (`VirginAnnounceKeepsWaiting`), RW04(e) |
| **FR-006** success = confirmed adoption (ACK + silence) | RW01, RW02, RW04 | RW02 (`BaptismSucceedsIffConfirmedAdoption`), RW04(a), RW07 |
| **FR-006a** deterministic ClaimNotAdopted | RW01, RW02, RW04 | RW02 (`ClaimNotAdopted…`), RW04(b)(c)(d), RW06 |
| **FR-007** fold-in + residual backstop | RW04 | RW04, RW06 |
| **FR-012** one audit record incl. new outcome | RW05 | RW05 (SC-006) |
| **FR-015** guided recovery + hardware E2E | RW06, RW07 | RW06 (GUI), RW07 (`HardwareFact`) |
| **SC-001** definitive outcome ≤ combined budget | RW01, RW04 | RW04, RW07 |
| **SC-002** confirmed silence before success | RW04, RW06 | RW04(a), RW07 (capture) |
| **SC-007** detect-and-recover, never false success | RW04, RW06, RW07 | RW04(b)(c)(d), RW07 |
| **D1** ACK observable on an RX port + virtual adapter | RW03a/b/c | RW03b (adapter), RW03c (fake/composition) |

## Notes

- This file is the breakdown only — no implementation in this run (it feeds the child ticket).
- The closed-DU triple update protocol (stem-fp §3) is enforced by RW01+RW02 landing together in
  spirit (Lean ahead, then the F# mirror + FsCheck in the next commit) — adding `ClaimNotAdopted` /
  `AwaitingAdoption` without all three artifacts is a slice failure.
- Next: `/speckit-analyze` (cross-artifact consistency vs the amended spec), then file the child
  under #212 (sequenced before #218).
