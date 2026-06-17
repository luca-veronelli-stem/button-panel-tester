# Plan: Baptism Confirmation-Model Rework (F1 + F6 + guided recovery)

**Parent feature**: [../spec.md](../spec.md) · **Epic**: [#212](https://github.com/luca-veronelli-stem/button-panel-tester/issues/212) (v0.4.0)
**Date**: 2026-06-17 · **Design**: [data-model.md](./data-model.md) · **Tasks**: [tasks.md](./tasks.md)

A focused re-plan of **only** the confirmation slice of spec-004, against the corrected FRs
(spec Clarifications 2026-06-17). The wire foundation (#213), TX port (#214), enablement/reset
(#216), and GUI shell (#217) are shipped and stay; this rework changes the **success-detection
core** in the FSM/Lean (#213/#215) and the service (#215), plus the outcome rendering (#217). It is
sequenced **before** the bench gate [#218](https://github.com/luca-veronelli-stem/button-panel-tester/issues/218),
which re-bases its hardware E2E on the corrected criterion.

## Summary

Three coupled corrections, one root (the tool inferred the outcome from the wrong signals):

1. **F1** — `AwaitingAnnounce` treats a virgin (`0xFF`) re-announce as keep-waiting, not
   `UnexpectedVariant` (FR-004). Removes the retry-hammering that creates the F6 state.
2. **F6** — baptize `Succeeded` only on **confirmed adoption**: the SET_ADDRESS `0x25` ACK **and**
   confirmed broadcast-silence, via a new `AwaitingAdoption` FSM phase; never on write-completion
   (FR-006). A still-announcing panel after the assign → the new deterministic `ClaimNotAdopted`
   outcome (FR-006a). FR-007's watch folds into the success gate + a residual backstop.
3. **Recovery** — `ClaimNotAdopted` guides Reset-to-virgin → re-baptize (FR-015), proven by a
   `Category=Hardware` E2E (SC-007), landed with the bench gate #218.

## Architecture decisions (the real choices; everything else follows the shipped patterns)

- **D1 — `0x25` ACK observation is a new RX consumption, not a TX-port change.** The
  `IMasterSequenceTransmitter` port stays fire-and-forget (contract unchanged). The `0x25` ACK is
  an application-layer frame the firmware addresses to the tool's srid; the service must observe it
  on the RX CAN stream. **Decision**: add a minimal `ISetAddressAckObserver`-style Core port (RX,
  mirrors the shipped `IWhoIAmObserver` shape) + an Infrastructure adapter that filters the RX
  frame stream for the `0x80|0x25` ACK addressed to the tool, + an in-memory fake for CI. This is
  the one genuinely new boundary; it earns a port (Constitution III) and a virtual adapter.
  *Alternative rejected*: widening `IWhoIAmObserver` to carry ACKs — conflates two RX concerns and
  drifts spec-003's frozen contract.
- **D2 — silence is the authoritative signal; the ACK is the fast positive.** Per the firmware,
  silence ⟺ adoption. The success gate requires **both** (ACK observed ∧ silence held one announce
  period) per Luca's clarify answer — the strict reading. The one tunable: a "silence held but ACK
  never observed" tick currently routes to `ClaimNotAdopted` (safe, never a false success; a dropped
  ACK on an actually-adopted panel costs one harmless re-baptize). Flagged here so the implementer
  and review can revisit if dropped ACKs prove common on the bench; the spec edge case documents it.
- **D3 — `adoptionBudget` (~one announce period) is a new bounded wait** anchored at SET_ADDRESS
  write-completion, sequential after the 6 s announce wait. The 6 s announce budget itself is the
  settled pin and is not touched. `FrozenClock`-driven in tests (the shipped `RunPruneTick`-style
  hook); no wall-clock sleeps.

## Constitution re-check (Principles I–VI)

- **I (Lean-first, NON-NEGOTIABLE)** — the FSM changes land in `Phase3/BaptismSequence.lean` first:
  new `AwaitingAdoption` state, `claimNotAdopted` outcome, `setAddressAcked` event; restated
  `baptize_progress`, extended `baptize_outcome_total`, new `virgin_keeps_waiting` /
  `no_success_without_adoption`. No `sorry`, axioms ⊆ {propext, Classical.choice, Quot.sound}.
- **II (property-first)** — FsCheck mirrors each: `BaptismSucceedsIffConfirmedAdoption` (replaces
  the write-completion mirror), `VirginAnnounceKeepsWaiting`, `ClaimNotAdoptedWhenStillAnnouncing`,
  `BaptismOutcomeTotal` extended to seven. Properties drive the **real** service over fakes.
- **III (ports + virtual adapter)** — D1's new RX ACK port gets a virtual adapter for CI; the
  Hardware E2E (SC-007) is the live-boundary proof (#218/#112), never a substitute for the unit.
- **IV (CI greens the stack; hardware explicit)** — all CI layers extended; the recovery E2E is
  `Category=Hardware`, env-gated, on #112, excluded from default CI. **CI-green = code-complete;
  the bench run is the done line.** (live-boundary-smoke: CI cannot prove a real panel went silent.)
- **V (identity hashing)** — no identity-bearing data on this path (panel UUIDs are device ids; no
  operator identity). Unchanged.
- **VI (stopgap)** — none new. The `0x25`/`0x23` command codes already extend the hardcoded
  protocol-metadata set under #156; no new waiver.

**Result: PASS.** Complexity Tracking empty.

## Consumed surfaces

| Surface | This rework |
|---|---|
| `IWhoIAmObserver` / `WhoIAmFrame` (spec-003) | **consumed** — silence = absence of announces for the selected uuid; the new `AwaitingAdoption` phase reads it |
| `ICanFrameStream` (RX, spec-003) | **consumed (new)** — the `0x25` ACK adapter filters it (D1); RX path otherwise untouched |
| `IMasterSequenceTransmitter` (#214) | **consumed, unchanged** — fire-and-forget; contract corrected in wording only |
| `IPanelDiscoveryService`, `ICanLinkService`, `IClock` | **consumed** — as shipped |
| `BoardVariant`, codecs, enablement, reset, audit | **unchanged** — out of this slice |

## Affected files (rework child)

```text
lean/Stem/ButtonPanelTester/Phase3/BaptismSequence.lean   AMEND  FSM state/outcome/event + theorems
src/ButtonPanelTester.Core/Can/Baptism.fs                 AMEND  BaptismState/Event/Outcome + step (F1/F6)
src/ButtonPanelTester.Core/Can/Ports.fs                   EXTEND + RX 0x25-ACK observer port (D1)
src/ButtonPanelTester.Services/Can/BaptismService.fs      AMEND  drive AwaitingAdoption; observe ACK + silence; backstop
src/ButtonPanelTester.Infrastructure/Can/*AckObserver.fs  NEW    RX adapter filtering 0x80|0x25 (D1)
src/ButtonPanelTester.GUI/Can/BaptismView.fs              AMEND  render ClaimNotAdopted + recovery guidance (FR-015)
src/ButtonPanelTester.GUI/Composition/CompositionRoot.fs  EXTEND wire the ACK observer
tests/…/Property/Can/BaptismSequenceProperties.fs         AMEND  confirmed-adoption / virgin-keeps-waiting / not-adopted
tests/…/Fakes/Can/InMemory*AckObserver.fs                 NEW    virtual ACK source
tests/…/Integration/Can/{Baptism,Timeout}E2ETests.fs      AMEND  adoption-confirm, ClaimNotAdopted, F1-virgin
tests/…/Gui/Can/BaptismViewTests.fs                       AMEND  ClaimNotAdopted + recovery rendering
tests/…/Integration/Can/Hardware/BaptismHardwareTests.fs  AMEND  recovery E2E (SC-007) — lands with #218
```

## Phases (bisect-safe vertical slices; Lean → FsCheck → F#)

- **R1 — Lean.** Amend `BaptismSequence.lean`: `AwaitingAdoption`, `claimNotAdopted`,
  `setAddressAcked`; F1 branch; restate `baptize_progress`; extend `baptize_outcome_total`; add
  `virgin_keeps_waiting`, `no_success_without_adoption`. `lake build` green, sorry-free. (one commit)
- **R2 — Core FSM.** Amend `Baptism.fs` to mirror R1 (state/event/outcome + `step`), XML-doc
  citations updated; FsCheck properties land with it (confirmed-adoption, virgin-keeps-waiting,
  not-adopted, totality×7). (one commit)
- **R3 — ACK RX port + adapter (D1).** Core port + Infrastructure adapter (frame-synthesis-style
  test against a fake `ICommunicationPort`) + in-memory fake + composition wiring. (one commit)
- **R4 — Service.** Drive `AwaitingAdoption` over the ACK observer + silence; `adoptionBudget`
  (`FrozenClock`); fold FR-007 into the gate + residual backstop; audit records the new outcome +
  step. Integration suites amended (adoption-confirm happy path, `ClaimNotAdopted` via
  still-announcing and via no-ACK, F1 virgin-keeps-waiting, no-flip-after-terminal). (one commit)
- **R5 — GUI.** `BaptismView` renders `ClaimNotAdopted` with the Reset → re-baptize recovery
  guidance (FR-015); Headless tests. (one commit)
- **R6 — Hardware E2E hook (lands with #218).** Recovery E2E `[<HardwareFact>]` (SC-007): induce a
  not-adopted state, recover via Reset → re-baptize; add the checklist entry to #112. Not in the
  rework child's CI scope — sequenced into the #218 bench gate.

Each commit compiles + greens its tests (`bisect-safe` + `vertical-commits`); test rides with impl
where the constitution order would otherwise leave a red intermediate. Conventional Commits with a
`Tasks: R#`-style trailer back to [tasks.md](./tasks.md). `gate.ps1` copied fresh at implement time.

## Child PR boundary (for `new-ticket` / `resolve-ticket`)

One child folds F1 + F6 + recovery-rendering (R1–R5) — they share the FSM root and bisect as one
vertical PR. R6 (hardware recovery E2E) belongs to **#218**, not the rework child (it needs the
bench rig). If R3 (the ACK RX port) inflates the diff, it is the one clean split point into a
prerequisite PR; default is one child unless the diff argues otherwise.

## Status

*Created 2026-06-17 (re-plan of the confirmation slice). Spec amended in place; this plan + tasks +
data-model delta live in `confirmation-rework/`. Next: `/speckit-analyze` (cross-artifact), then
file the child under #212 (before #218).*
