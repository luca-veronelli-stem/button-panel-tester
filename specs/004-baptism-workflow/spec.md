# Feature Specification: Baptism Workflow — Claim and Reset Panels on the Bus

**Feature Branch**: `004-baptism-workflow`

**Created**: 2026-06-11

**Status**: Draft

**Epic**: [#212](https://github.com/luca-veronelli-stem/button-panel-tester/issues/212) (milestone v0.4.0)

**Input**: User description: "Baptism workflow: claim a virgin panel on the bus as one of the four BoardVariants, or reset a claimed panel back to virgin, via the three-step auto-address master sequence"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Baptizing a virgin panel as a chosen machine variant (Priority: P1)

A supplier-side QA technician has a pristine virgin panel announcing itself in the Panels-on-bus list. Before any functional test can run, the panel must be told which machine model it belongs to — in firmware terms, *baptized*. The technician selects the panel's row, picks one of the four marketed machine variants (EDEN-XP, OPTIMUS-XP, R-3L XP, EDEN-BS8) from the baptism surface that activates beside the list, and starts the baptism.

The tool then runs the complete auto-address master handshake on the wire: it broadcasts the claim command for the chosen variant, waits for the panel to re-announce itself carrying the new variant identity (capturing the panel's UUID), and completes the claim by assigning the panel its protocol address. Within the baptize budget (6 seconds) the technician sees either a success confirmation — including the explanation that the panel now goes **silent** by design, so its row will age out of the list — or a structured failure that names what went wrong (wait timeout, unexpected variant identity, panel disappeared, link lost, transmission failure) and what to do next.

This is the first feature in the product that transmits on the CAN bus.

**Why this priority**: baptism is the gate to every downstream test feature — an unbaptized panel never emits application traffic (firmware guards transmission until the claim completes). It is also the v0.4.0 headline. Without it the tool can only watch the bus; with it, the tool turns a pristine board into a testable one.

**Independent Test**: with the link Connected and exactly one virgin panel announcing in the list, select it, pick EDEN-XP, press Baptize. Verify the tool reports success within 6 seconds, the panel stops announcing (bus capture shows no further announcements from that UUID), and the row leaves the list within the established pruning window. Reset-to-virgin (User Story 2) restores the panel for the next run.

**Acceptance Scenarios**:

1. **Given** the link is Connected and exactly one panel is announcing (label "virgin"), **When** the technician selects it, chooses a variant, and starts baptism, **Then** the tool performs the full three-step master sequence and reports success within the 6-second budget, naming the variant claimed and the panel UUID.
2. **Given** a baptism just succeeded, **When** the technician looks at the Panels-on-bus list, **Then** the success outcome explains that a claimed panel goes silent by design and that its row will disappear from the list once the pruning timeout elapses — the disappearance is presented as expected behaviour, not a fault.
3. **Given** a baptism is started and the panel never re-announces with the chosen variant within 6 seconds, **Then** the tool reports a structured wait-timeout failure that says the claim may be incomplete, that the panel may still re-announce late, and that re-running Baptize (or Reset) on the still-announcing panel recovers the situation.
4. **Given** a baptism is started and the panel re-announces still carrying an identity other than the chosen variant, **Then** the tool reports an unexpected-variant failure rather than proceeding to the address-assignment step.
5. **Given** a baptism is started and the selected panel's row prunes from the list before the panel ever re-announces (e.g. the board was powered off), **Then** the tool reports a panel-disappeared failure.
6. **Given** zero panels or two-or-more panels are announcing, **When** the technician looks at the baptism surface, **Then** the Baptize action is disabled with the explanation that baptism requires exactly one panel announcing on the bus.
7. **Given** an announcing panel whose identity byte already decodes to a marketing variant (e.g. a half-completed earlier claim), **When** the technician selects it, **Then** Baptize is available — any *announcing* panel is claimable regardless of the identity it currently announces.
8. **Given** the link leaves Connected mid-sequence, **Then** the tool aborts the baptism with a link-lost failure and does not retry on its own.

---

### User Story 2 - Resetting a claimed panel back to virgin (Priority: P2)

A technician needs to re-run validation on a panel that was already claimed — by this tool a minute ago, or by a real machine some time in the past. A claimed panel is **silent**: it never announces itself, so it cannot appear in the Panels-on-bus list. The technician physically attaches the single panel to the bench bus, and uses a **Reset to virgin** action that does not require selecting a list row. After a confirmation step (the action erases the panel's machine identity, and can reach a silent panel the list cannot show), the tool broadcasts the reset command and reports success as soon as the command is confirmed written to the bus — the firmware does not reply synchronously. The panel then re-announces itself as virgin within its announcement cadence (~2–6 s) and reappears in the list, ready to be baptized again.

**Why this priority**: reset-to-virgin is the recovery path that makes every baptism reversible and the bench loop repeatable (baptize → verify → reset → next variant). It is also the only way to bring a previously-claimed panel back onto the bus where the tool can see it. It depends on nothing from User Story 1 and is independently valuable (e.g. recovering panels claimed by a real machine).

**Independent Test**: attach one previously-claimed (silent) panel, confirm the list is empty, invoke Reset to virgin, accept the confirmation. Verify the tool reports the reset as sent, and a row labeled "virgin" appears in the list within 6 seconds.

**Acceptance Scenarios**:

1. **Given** the link is Connected, the list is empty, and one claimed (silent) panel is attached, **When** the technician invokes Reset to virgin and accepts the confirmation step, **Then** the tool broadcasts the reset command, reports success on write completion without waiting for a reply, and a "virgin" row appears in the list within 6 seconds.
2. **Given** the reset confirmation step is shown, **When** the technician declines it, **Then** nothing is transmitted.
3. **Given** exactly one panel is announcing in the list (virgin or otherwise), **When** the technician invokes Reset to virgin, **Then** the action is available and behaves identically — the announcing panel returns to (or stays in) the virgin announcing state.
4. **Given** two or more panels are announcing, **Then** Reset to virgin is disabled with the explanation that the reset broadcast would reach every panel on the bus.
5. **Given** a reset was sent with no panel actually attached, **Then** the success message is honest about what was confirmed: the command was written to the bus; a matching panel, if present, re-announces within ~6 seconds — and the list simply stays empty otherwise.

---

### Edge Cases

- **A second panel starts announcing mid-baptism** (a board was powered on during the sequence): the re-announcement wait is keyed to the selected panel's UUID; an announcement from a different UUID does not satisfy it. If the selected panel's claim completes, baptism succeeds; the guard state (now ≥ 2 announcing) applies to the *next* action.
- **Late re-announcement after a reported wait-timeout**: the claim command may have taken effect even though the address-assignment step never ran. The panel keeps announcing — now carrying the chosen variant's identity — and stays visible and claimable. The timeout message tells the technician exactly this: if the panel reappears announcing the target variant, run Baptize again to complete the claim (or Reset to start over). The tool never silently flips a reported failure into a success.
- **Reset reaching invisible panels**: silent claimed panels cannot be counted by the tool. If a technician violates bench discipline and attaches two claimed panels, a reset broadcast resets both — both then announce as virgin, the list shows two rows, and Baptize is disabled until one is removed. Identity loss on both panels is unavoidable; the confirmation step exists precisely because the list cannot enumerate what a reset will reach.
- **Transmission failure on any step** (adapter rejects the write, link drops at the moment of send): structured failure naming the step that failed; no automatic retry.
- **Baptizing the same physical panel repeatedly across variants** (the bench loop): each cycle starts from a virgin announcing panel and ends with a reset; the tool carries no memory between cycles, so the Nth cycle behaves exactly like the first.
- **The selected row prunes during the confirmation/selection interaction**: the baptism surface deactivates with the same panel-disappeared explanation, not a crash or a stale send.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a baptism surface anchored to the Panels-on-bus list: selecting an announcing panel activates it; it offers the four marketed machine variants (EDEN-XP, OPTIMUS-XP, R-3L XP, EDEN-BS8) and a Reset-to-virgin action.
- **FR-002**: System MUST enable Baptize only when the link is Connected AND exactly one panel is announcing on the bus AND that panel is selected. In every other state the action is disabled with an explanation of the unmet condition.
- **FR-003**: System MUST perform baptism as the complete three-step auto-address master sequence — broadcast the claim for the chosen variant with the reset flag set, wait for the selected panel's re-announcement carrying the chosen variant identity (capturing its UUID), then assign the panel its protocol address — never the single-shot claim that the original briefing described (a firmware no-op that leaves the panel unable to emit application traffic).
- **FR-004**: System MUST validate the re-announcement against both the selected panel's UUID and the chosen variant identity before performing the address-assignment step.
- **FR-005**: System MUST bound the re-announcement wait at 6 seconds and report every baptism outcome as exactly one of: success; wait-timeout; unexpected variant identity; panel disappeared; link lost; transmission failure. Each failure outcome MUST name the step that failed, the panel's likely state, and the recommended next action.
- **FR-006**: System MUST report baptism success upon write-completion of the address-assignment step, and the success outcome MUST explain that the claimed panel goes silent by design and that its list row will age out via the existing pruning behaviour. The Panels-on-bus list semantics (announce, update, prune) are NOT modified by this feature.
- **FR-007**: System MUST surface a follow-up warning if, after a reported baptism success, the same panel UUID is heard announcing again within the list-pruning window — the claim did not take; the panel is still unclaimed and visible.
- **FR-008**: System MUST provide Reset to virgin without requiring a list selection, enabled when the link is Connected AND at most one panel is announcing; with two or more panels announcing it is disabled with an explanation. Reset broadcasts the claim command with the virgin identity and the reset flag set.
- **FR-009**: System MUST require an explicit confirmation step before transmitting a reset, stating that the reset erases a panel's machine identity and reaches every matching panel on the bus, including silent ones the list cannot show. Baptize MUST NOT add a confirmation step beyond the deliberate variant selection.
- **FR-010**: System MUST report reset success on write completion of the broadcast, without waiting for any reply, and MUST set the expectation that a matching panel re-announces as virgin within ~6 seconds.
- **FR-011**: System MUST treat a silent claimed panel as unreachable for claiming: re-baptizing a claimed panel to a different variant is performed as Reset to virgin followed by a normal baptism of the then-announcing panel. The system MUST NOT offer a blind claim aimed at a panel it cannot observe.
- **FR-012**: System MUST write a structured audit record for every baptize and reset attempt — action, chosen variant (for baptize), panel UUID when known, outcome, and timestamps — to the tool's structured log.
- **FR-013**: System MUST NOT persist any per-panel baptism state beyond the audit log: no panel registry, no claim history, no lockout. Every baptism MUST remain reversible by a subsequent reset, indefinitely.
- **FR-014**: System MUST transmit on the bus only the frames of the master sequence itself (claim, address assignment, reset), and only as the direct result of a technician-initiated action while the link is Connected. Discovery remains passive; no other feature traffic is introduced.

### Key Entities

- **BoardVariant**: one of the four marketed machine variants a panel can be claimed as (EDEN-XP, OPTIMUS-XP, R-3L XP, EDEN-BS8), each pairing a marketing name with its firmware machine-identity value. A fixed, built-in set for this feature — there is no fifth variant and no user-defined variant.
- **Baptism attempt**: a single technician-initiated claim: the selected panel's UUID, the chosen BoardVariant, the step reached (claim sent / re-announcement seen / address assigned), and the final outcome (the success or one of the five structured failures of FR-005).
- **Reset attempt**: a single technician-initiated reset broadcast and its outcome (sent / declined at confirmation / transmission failure).
- **Panel observation** *(existing, from the discovery feature)*: the announcing-panel row the baptism surface anchors on; reused as-is, including its pruning semantics.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From pressing Baptize on a virgin announcing panel, the technician receives a definitive outcome — success or a named structured failure — within the 6-second budget, never an indefinite wait, across 100% of attempts.
- **SC-002**: After a reported baptism success, a bus capture shows zero further announcements from the claimed panel's UUID (first silence within one announcement period), and the panel's row leaves the list within the established pruning window — with the silence explained to the technician as expected.
- **SC-003**: A claimed, silent panel is recovered to the visible virgin state via a single reset action: a "virgin" row for it appears within 6 seconds of the reset in at least 95% of attempts (worst-case firmware announcement cadence), and a follow-up announcement is never required from the technician's side.
- **SC-004**: A full bench cycle — baptize to a variant, reset to virgin, repeat across all four variants on the same physical panel — completes with zero residual state in the tool between cycles; the fourth cycle is indistinguishable from the first.
- **SC-005**: With two or more panels announcing, destructive actions (Baptize, Reset) are unreachable in 100% of renders; no multi-panel claim or reset can be initiated through the UI.
- **SC-006**: Every baptize/reset attempt — succeeded, failed, or declined at confirmation — produces exactly one audit record carrying action, variant (where applicable), UUID (where known), and outcome.

## Assumptions

- **Firmware behaviours are as audited and verified.** The claim handler processes the claim command regardless of the panel's current state (a silent claimed panel does process it); the reset flag set with a matching firmware type clears the panel's protocol address and moves it to the announcing state immediately, announcing the *newly written* identity on its 2–6 s cadence; the address-assignment step validates the UUID and returns the panel to its silent claimed state without a reply. Verified 2026-06-11 against the on-disk protocol firmware source (`AutoAddressSlave.c`, matching the audit citations in [`CORRECTIONS.md`](../../docs/Context/bpt-rollout/CORRECTIONS.md) §C1–C2).
- **Direct claimed→claimed re-baptism is wire-possible but deliberately not offered** (FR-011): the firmware accepts a claim in any state, but a silent panel cannot be selected, counted, or guarded by the tool, so policy routes re-baptism through reset-first. This is a UX/safety policy choice, not a firmware limitation.
- **The 6-second budget has near-zero margin at the worst-case announcement cadence.** The post-claim re-announcement delay is the panel's UUID-derived period (2–6 s); a panel at the extreme of that range answers at the edge of the budget. The structured wait-timeout outcome plus the late-re-announcement recovery path (edge case above) keep that tail operable. The budget itself is a settled scope pin and is not revisited here.
- **The virgin identity marker and the four variant identity values are firmware constants**, audited 2026-05-24 and recorded in `CORRECTIONS.md`; the BoardVariant set is built-in for this feature.
- **The tool assigns the protocol address for board number 1 of the chosen variant.** Multi-board topologies (`BoardNumber > 1`) are out of scope; the supplier bench validates single panels.
- **Single-panel bench discipline is partly the operator's responsibility.** The tool enforces panel counts on *announcing* panels only — silent claimed panels are physically indistinguishable from an empty bus. The reset confirmation step (FR-009) is the guard at exactly the point where invisible panels can be affected.
- **A tool-baptized panel binds the tool as its master.** The claim records the sender's address as the panel's motherboard address; when the panel is later installed in a real machine, the machine's motherboard re-runs its own auto-addressing. Baptism by this tool is therefore bench-scoped by construction and does not interfere with field installation.
- **No operator-identity capture exists in the tool yet**; audit records (FR-012) carry action, variant, UUID, outcome, and timestamps. Attributing actions to a named operator joins the session-orchestration feature later on the roadmap.

## Dependencies

- **Panels-on-bus list (existing product capability, spec-003).** Provides the announcing-panel rows the baptism surface anchors on, the panel-count input to the enablement guards (FR-002, FR-008), the UUID and identity decoding of announcements (which step 2 of the sequence consumes), and the pruning behaviour that ages out a claimed panel's row (FR-006). This feature reads and reuses those semantics; it does not alter them.
- **CAN link lifecycle (existing product capability, spec-002).** The Connected state gates every transmit action; leaving Connected mid-sequence is the link-lost failure of FR-005.
- **Panel/protocol firmware behaviour** as audited in [`docs/Context/bpt-rollout/CORRECTIONS.md`](../../docs/Context/bpt-rollout/CORRECTIONS.md) §C1 (announce/silent classification) and §C2 (three-step master sequence; transmission guard on unclaimed panels), re-verified against the on-disk protocol firmware source on 2026-06-11.

## Out of Scope (for this feature)

- Bulk baptism — claiming a sequence of panels in one operation.
- Multi-board topologies: `BoardNumber > 1` variants of any machine type.
- Per-panel baptism history or any persistent panel registry (the audit log of FR-012 is the only record, and it is action-scoped, not panel-scoped).
- Blind claiming of silent panels (see FR-011 — reset-first policy).
- Any change to the Panels-on-bus list semantics: announcement decoding, row lifecycle, pruning timeout.
- Protocol-metadata fetch migration (C5): the built-in command codes and protocol addresses stay; tracked separately in [#156](https://github.com/luca-veronelli-stem/button-panel-tester/issues/156).
- Button, LED, or buzzer testing of the baptized panel — the next features on the roadmap consume baptism's output.
- Operator naming / identity attribution in audit records (joins session orchestration later in the roadmap).
