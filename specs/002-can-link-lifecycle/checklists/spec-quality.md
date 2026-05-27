# Spec quality checklist — spec-002 CAN link lifecycle

**Purpose**: Unit tests for the requirements writing in `spec.md` (post-Phase-B amendment, commit `f04318e`). Each item asks whether the requirements are well-written, complete, unambiguous, and ready for implementation — NOT whether the implementation works.

**Created**: 2026-05-27 (Phase B re-review).

**Scope**: spec.md only. Cross-artefact consistency (against data-model.md, plan.md, tasks.md) is deferred to `/speckit-analyze` once those artefacts land.

**Depth**: Pre-fan-out gate. Items surfaced here either get resolved with a spec amendment before the queue fans out, or get explicitly deferred (with rationale) into data-model.md / plan.md.

**Status legend**:

- `[x]` — resolved by spec amendment (commit `f04318e`).
- `[~]` — deferred to a later artefact (data-model.md / plan.md / quickstart.md), with target named.
- `[ ]` — still open; either accepted gap or pending later review (e.g. `/speckit-analyze` after fan-out).

---

## Requirement Completeness

- [x] CHK001 — Is there a Success Criterion that exercises the click-acknowledge contract from FR-009? [Coverage / Gap, Spec §Success Criteria] — **Resolved**: SC-010 added.
- [x] CHK002 — Is there a Success Criterion that exercises FR-010's exclusive driver access claim? [Coverage / Gap, Spec §Success Criteria] — **Resolved**: SC-011 added.
- [x] CHK003 — Is there a Success Criterion that exercises FR-011's external-contention observability, conditional on vendored-stack capability? [Coverage / Gap, Spec §Success Criteria] — **Resolved**: SC-012 added (conditional MUST mirrors FR-011 caveat).
- [x] CHK004 — Are requirements specified for what happens to an in-flight OpenAsync call when the operator clicks Stop during `Opening` — does the call cancel or run to completion before the FSM lands on `Idle(UserPaused)`? [Edge Case / Gap, Spec §FR-006] — **Resolved**: FR-006 now mandates CancellationToken propagation (per STEM `CANCELLATION` standard); Edge Cases bullet for Stop also notes the cancellation path.
- [x] CHK005 — Are accessibility requirements (keyboard navigation, focus order, screen-reader labels) specified for the row's chip, headline, detail affordance, and three buttons? [Coverage / Gap, Spec §FR-009] — **Resolved**: FR-017 (Accessibility) added.

## Requirement Clarity

- [x] CHK006 — Is "spinner glyph" in FR-009 quantified with a specific symbol or an explicit reference to the design-system convention (DictionaryStatusRow uses `⟳` per `DictionaryStatusRow.fs:151-158`)? [Clarity, Spec §FR-009] — **Resolved**: FR-009 now references the dictionary row precedent and names `⟳` explicitly.
- [x] CHK007 — Is "in-flight call" defined unambiguously for FR-009 — does it mean the driver-level OpenAsync call, the F# async task lifetime, the time from button-click to next FSM emission, or all three? [Ambiguity, Spec §FR-009] — **Resolved**: FR-009 now defines in-flight call per affordance (Reconnect = OpenAsync through next FSM emission; Stop = close-and-cancel through Idle emission; Start = synchronous transition, may be sub-perceptual).
- [x] CHK008 — Is the sub-perceptual case addressed — does FR-009's click-acknowledge cue have a minimum-visibility floor, or is it intentionally bounded by actual call duration (consistent with the truth-to-state principle)? [Clarity / Gap, Spec §FR-009] — **Resolved**: FR-009 Note covers sub-perceptual returns; operator's signal comes from the FSM transition, not the cue itself.
- [x] CHK009 — Is FR-016's parenthetical note about cross-row health prescriptive (operator MUST consider both rows) or descriptive (explanatory aside)? [Clarity, Spec §FR-016] — **Resolved**: Cross-row note dropped entirely. Spec-001's seed-fallback guarantee makes "unusable dictionary" unreachable (see Assumptions). FR-016 is GUI responsiveness only.
- [ ] CHK010 — Is the term "active state" used in FR-006 (`Searching, Opening, Open, Faulted`) defined consistently with the operator-initiatable-transitions table? [Clarity / Consistency, Spec §FR-006 + §Operator-initiatable transitions] — **Open**: consistency check, re-evaluate via `/speckit-analyze` after data-model.md lands.

## Requirement Consistency

- [ ] CHK011 — Do FR-002 (chip-colour fixity) and FR-009 (chip MUST always reflect FSM family per FR-002) overlap without contradiction or unnecessary restatement? [Consistency, Spec §FR-002 + §FR-009] — **Open**: FR-009 now cross-references FR-002 explicitly; re-evaluate via `/speckit-analyze`.
- [ ] CHK012 — Are the operator-initiatable transitions in the table and FR-006/FR-007/FR-008 mutually consistent in source states, target states, and surface buttons (no transitions in one that aren't in the other)? [Consistency, Spec §Operator-initiatable transitions + §FR-006/7/8] — **Open**: visually consistent on read; re-evaluate via `/speckit-analyze` after data-model.md formalises the DU.
- [ ] CHK013 — Are Session 2026-05-25's click-feedback bullet (truth-and-acknowledge) and FR-009's click-acknowledge contract consistent in framing and content? [Consistency, Spec §Clarifications + §FR-009] — **Open**: visually consistent on read; both ground in the dictionary-row precedent. Re-evaluate via `/speckit-analyze`.
- [ ] CHK014 — Is the new Session 2026-05-27 boot-order clarification consistent with FR-015's "Dictionary boot completion does NOT gate the CAN service's first scan" statement? [Consistency, Spec §Clarifications + §FR-015] — **Open**: visually consistent on read; re-evaluate via `/speckit-analyze`.

## Acceptance Criteria Quality

- [x] CHK015 — Can FR-009's "disabled state + spinner glyph for the duration of the in-flight call" be objectively measured at the GUI test boundary (e.g., headless Avalonia.Headless test asserting button.IsEnabled = false during the call)? [Measurability, Spec §FR-009] — **Resolved**: SC-010 is the explicit acceptance criterion (headless GUI test assertion).
- [x] CHK016 — Can FR-010's "exclusive driver-level access" be objectively verified (e.g., a test that opens BPT, then attempts an exclusive open from another process and expects driver-busy)? [Measurability, Spec §FR-010] — **Resolved**: SC-011 is the explicit acceptance criterion (external exclusive open observes busy).
- [x] CHK017 — Is FR-011's conditional MUST verifiable today, or does verification block on inspecting the vendored stack's capability first? [Measurability / Traceability, Spec §FR-011] — **Resolved**: SC-012's conditional MUST mirrors FR-011's caveat (zero-log-entry case is trivially satisfied when the stack is silent).
- [~] CHK018 — Is SC-005's "external exclusive-mode tool such as StemDeviceManager attaching successfully within 2 seconds" verifiable in CI, or only on a manual bench with StemDeviceManager installed? [Measurability, Spec §SC-005] — **Deferred to plan.md**: this is a bench-only test, not a CI gate. plan.md will name SC-005 as bench-verifiable and add a CI-compatible surrogate (e.g., assert adapter handle release via a fake exclusive-mode client) if practical.

## Scenario Coverage

- [~] CHK019 — Are requirements specified for the operator opening the detail affordance while the FSM is mid-transition (e.g., during `Opening`) — does the detail snapshot freeze, follow the FSM, or remain undefined? [Coverage / Gap] — **Deferred to data-model.md**: the detail affordance is a render of the current `CanLinkState`; data-model.md will pin "render follows the latest emission, no snapshot freeze".
- [x] CHK020 — Are requirements specified for FSM behaviour during a host suspend/resume cycle (laptop sleeps, adapter "disappears" from OS, host wakes)? [Coverage / Gap] — **Resolved**: Edge Cases bullet added for host suspend/resume; observed as a normal hot-plug sequence, no special-cased FSM path.
- [x] CHK021 — Are requirements specified for FSM behaviour during a driver-uninstall-while-running scenario (driver was present at app launch, gets removed mid-session)? [Coverage / Gap] — **Resolved**: Edge Cases bullet added for driver uninstall while running; lands on `Faulted(DriverNotInstalled, Some <last adapter>, since)`.

## Edge Case Coverage

- [x] CHK022 — Is the case "vendored-stack signals device-arrived event while BPT is in `Open` against a different adapter" addressed (does BPT ignore the event, log it, or re-enumerate)? [Edge Case / Gap] — **Resolved**: Edge Cases bullet added; BPT ignores for FSM purposes, MAY log at Information level; FR-012's iteration contract applies only during Searching.
- [ ] CHK023 — Is the case "operator clicks Reconnect on `Faulted(_, None, _)` and the resulting `Searching(Polling)` immediately re-encounters the same fault" addressed beyond the `since` semantics of FR-004? [Edge Case] — **Open**: FR-004 case (c) covers `since` semantics across the family round-trip; no further FR needed. Verify via `/speckit-analyze` once data-model.md formalises the transition.

## Non-Functional Requirements

- [~] CHK024 — Are logging requirements quantified beyond level mapping in the Presentation surfaces table — message template shape, named parameters per `LOGGING.md`, BeginScope conventions, correlation fields? [Coverage, Spec §Presentation surfaces / forensic log] — **Deferred to plan.md**: STEM `LOGGING.md` carries the generic conventions; plan.md will pin spec-002-specific log templates (state name, discriminator, since-timestamp as named parameters).
- [ ] CHK025 — Are performance budgets defined beyond SC-001/SC-002's first-paint windows (e.g., per-transition emission latency, log-write latency, observable fan-out latency)? [Coverage / Gap] — **Open / accepted gap**: per-transition latency is unlikely to be perceptually relevant given the FSM's transitions are driver-bound (millisecond-scale). plan.md may pin a worst-case if needed.

## Dependencies & Assumptions

- [x] CHK026 — Are dependencies categorised by criticality (blocking vs forward-looking) — currently the list mixes "PEAK driver installed" (blocking) with "Stem.Communication contention event" (forward-looking) without explicit labelling? [Clarity / Organisation, Spec §Dependencies] — **Resolved**: Dependencies split into Blocking / Forward-looking subsections.
- [ ] CHK027 — Is the assumption "production bench uses direct USB connection" testable or verifiable, or is it a declarative scope-narrowing statement? [Assumption, Spec §Assumptions] — **Open / accepted as declarative**: this is a scope-narrowing assumption that bounds bench complexity. Not testable in code; relies on bench documentation / supplier setup conventions.
- [~] CHK028 — Is the dependency on the vendored stack's hot-plug device-arrived event traceable to an existing impl or test that would catch a regression? [Dependency / Traceability, Spec §Dependencies] — **Deferred to plan.md**: plan.md will name the existing hot-plug acceptance test (or flag the gap) and ensure #111's migration plan inherits it.

## Ambiguities & Conflicts

- [x] CHK029 — Is "spinner glyph" defined consistently between FR-009 (CAN row) and the dictionary row's existing `⟳` (`DictionaryStatusRow.fs:151-158`), or could a future implementer choose a different glyph and still pass the spec? [Ambiguity / Consistency, Spec §FR-009] — **Resolved**: FR-009 explicitly pins the glyph as `⟳` with the file-reference precedent.
- [x] CHK030 — Are the eight Session 2026-05-27 clarifications grouped or sequenced for navigability (currently presented as a flat bullet list under one date heading)? [Clarity / Organisation, Spec §Clarifications] — **Resolved**: Session 2026-05-27 grouped under four sub-headings (FSM shape / Edges and iteration / Framing / Scope refinements).

---

**Resolution summary**: 19 of 30 items resolved by amendment `f04318e`. 5 items deferred to later artefacts (4 to plan.md, 1 to data-model.md). 6 items open — five are cross-artefact consistency checks to be re-evaluated via `/speckit-analyze` once the queue is written; one is an accepted scope-narrowing assumption (CHK027).
