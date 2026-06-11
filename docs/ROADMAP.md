# Roadmap

Living skeleton of where ButtonPanelTester goes next. **Only the next numbered spec is
committed; everything below it is provisional** and re-evaluated at each release cut,
alongside the CHANGELOG.

The full planning material for each upcoming spec — the `/speckit.specify` input
paragraph, locked design decisions, open questions for `/speckit.clarify`, the scope
cliff, and the hardware-risk story it retires — lives in the frozen pre-implementation
briefing at [`docs/Context/bpt-rollout/03-roadmap.md`](./Context/bpt-rollout/03-roadmap.md),
as corrected by the firmware audit in
[`CORRECTIONS.md`](./Context/bpt-rollout/CORRECTIONS.md). This file is the live ordering,
numbering, and status; the briefing is the reference text. When they disagree, this file
wins on *order and scope*, the briefing + corrections win on *technical content*.

> **Numbering drift.** The briefing's "spec-002 (CAN link and panel discovery)" was split
> via [#151](https://github.com/luca-veronelli-stem/button-panel-tester/issues/151) into
> the shipped spec-002 (lifecycle) and spec-003 (passive discovery). Every briefing item
> after it therefore shifts by one: briefing §spec-003 → spec-004 here, and so on.

## Shipped

| Release | Spec | Capability |
|---|---|---|
| [v0.1.0](../CHANGELOG.md) | [spec-001](../specs/001-fetch-dictionary/spec.md) | Dictionary fetch, status row, registration ceremony, manual refresh |
| [v0.2.0](../CHANGELOG.md) | [spec-002](../specs/002-can-link-lifecycle/spec.md) | CAN-link lifecycle: status row, four-family FSM, PEAK adapter realities |
| [v0.3.0](../CHANGELOG.md) | [spec-003](../specs/003-panel-discovery/spec.md) | Passive panel discovery: Panels-on-bus list from `WHO_I_AM` broadcasts |

## Next — v0.4.0: Baptism workflow (spec-004)

Briefing [§spec-003](./Context/bpt-rollout/03-roadmap.md#spec-003--baptism-workflow),
corrected by **C2**: baptism is the three-step master sequence
`WHO_ARE_YOU(reset=1)` → `WHO_I_AM` (capture UUID) → `SET_ADDRESS(UUID, sp_address)` —
the briefing's single-shot `reset=0` send is a silent no-op on real firmware. Claim a
virgin panel as one of the four BoardVariants, or reset a claimed panel back to virgin,
with single-panel-on-bus enforcement.

Mind **C1** at spec time: a *claimed* panel goes silent (`AAS_STAND_BY`), so it drops off
the Panels-on-bus list after baptism and only reappears via reset-to-virgin. The spec
must define what the discovery list shows for the panel just baptized.

Ride-alongs on the [v0.4.0 milestone](https://github.com/luca-veronelli-stem/button-panel-tester/milestone/7):
log polish ([#207](https://github.com/luca-veronelli-stem/button-panel-tester/issues/207),
[#208](https://github.com/luca-veronelli-stem/button-panel-tester/issues/208)) and the
bench config for the `Category=Hardware` E2E suite
([#112](https://github.com/luca-veronelli-stem/button-panel-tester/issues/112)) — baptism
is the first spec that transmits on the bus, exactly when a real-iron suite starts paying
for itself.

## Then (provisional order)

- **spec-005 — Button-press test (input side)** — briefing
  [§spec-004](./Context/bpt-rollout/03-roadmap.md#spec-004--button-press-test-input-side),
  corrected by **C3**: OPTIMUS-XP's active set is panel positions `{1, 2, 4, 5}` =
  `{DOWN, P1, P3, MEM}`, and the prompt UI needs a decal-name vs firmware-name clarify
  (an OPTIMUS-XP technician reads *Light / Suspension / All-Up / Stop*, not
  *DOWN / P1 / P3 / MEM*). The **C5 protocol-metadata fetch migration** first materially
  affects behaviour here; it is hosted in
  [#156](https://github.com/luca-veronelli-stem/button-panel-tester/issues/156) with a
  timing guard — pull it out standalone if the spec-001 supersede hasn't unparked by the
  time this spec starts.
- **spec-006 — LED and buzzer test (output side)** — briefing
  [§spec-005](./Context/bpt-rollout/03-roadmap.md#spec-005--led-and-buzzer-test-output-side).
  Open audit carried forward: OPTIMUS-XP `HasBuzzer` is still TBD (C3 note).
- **spec-007 — Session orchestration, verdict, persistence, report** — briefing
  [§spec-006](./Context/bpt-rollout/03-roadmap.md#spec-006--session-orchestration-verdict-persistence-report).
- **spec-008 — Robustness, recovery, forensic logging** — briefing
  [§spec-007](./Context/bpt-rollout/03-roadmap.md#spec-007--robustness-recovery-forensic-logging).
  Also where the vendored stack's known carried-over limitations (no RX CRC validation,
  no chunk-reassembly timeout) come due (C4).

## v1.0 — definition of done

Unchanged from the briefing's
["After 007"](./Context/bpt-rollout/03-roadmap.md#after-007--what-the-tool-looks-like)
picture (renumbered: after spec-008). A bench-deployable Avalonia application that, on a
Windows machine with a PEAK PCAN-USB adapter:

- boots, fetches its dictionary, shows the dictionary + CAN status rows;
- lets the technician name an operator, pick a panel from the bus, baptize it if virgin,
  and run a full input + output test session;
- produces a per-panel JSON session record and a printable HTML report;
- survives USB unplugs and bus silence without losing in-progress data;
- leaves a per-session CAN frame trace for offline forensics.

Beyond v1.0 (bulk mode, cloud upload, firmware update, multi-panel topologies, batch
reports): deliberately unplanned — decided after real bench use.

## Debt ledger (not release-bound)

- [#111](https://github.com/luca-veronelli-stem/button-panel-tester/issues/111) — vendored
  protocol stack removal. Blocked on upstream `stem-device-manager` Phase 5 /
  `Stem.Communication` NuGet; the tester migrates only after the device manager does.
- [#156](https://github.com/luca-veronelli-stem/button-panel-tester/issues/156) — spec-001
  supersede carrier (status-row redesign γ, Re-Register recovery semantics, C5 fetch
  migration). Park condition ("after spec-003 ships the viable product") **met as of
  v0.3.0**; unparks via the speckit supersede protocol when scheduled.
- [#190](https://github.com/luca-veronelli-stem/button-panel-tester/issues/190) — spec-002
  supersede carrier (five-family FSM re-carve, parked Phase B). Same protocol as #156.
- [#149](https://github.com/luca-veronelli-stem/button-panel-tester/issues/149) — app-wide
  NotificationCenter + ErrorClassification split. Natural candidate alongside the
  session-orchestration UX (spec-007/008).

## Maintenance protocol

- **At each release cut:** move the shipped spec to the table above, re-evaluate the
  provisional order, create the next release's GitHub milestone, and slot ride-along debt.
- **One GitHub milestone per minor release**; issues join it as they are opened.
- **One epic per spec, created only when work starts** (via `new-ticket` → `resolve-epic`).
  No pre-created epics for future specs — they rot.
- Spec planning material stays in the frozen briefing + corrections; this file never
  duplicates it, only points at it.
