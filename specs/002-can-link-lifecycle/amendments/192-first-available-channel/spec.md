# spec-002 amendment — Open the first available PEAK channel

> **Type:** complementary amendment to the shipped four-family CAN-link lifecycle
> ([`../../spec.md`](../../spec.md)). It does **not** restate that spec; it
> revises two of its Assumptions / Out-of-Scope lines and adds one behaviour
> below the FSM. The shipped `spec.md`/`tasks.md` narrative stays frozen, in
> the same spirit as the Phase 3.5 amendments (`../../tasks.md` §Phase 3.5).
>
> **Issue:** [#192](https://github.com/luca-veronelli-stem/button-panel-tester/issues/192)
> · **Label:** `spec-002` · **Branch:** `feat/192-first-available-channel`
>
> **Salvage provenance:** the *Multi-adapter iteration* row of the parked Phase B
> redesign salvage table (`../../parked-phase-b-redesign/README.md:40`), realised
> on the **four-family types** — explicitly **not** the [#190](https://github.com/luca-veronelli-stem/button-panel-tester/issues/190)
> five-family supersede. This amendment must not unpark #190.

## What changes in spec-002

This amendment **revises** two lines of the shipped spec and leaves everything
else intact:

- **Assumptions — `spec.md:147`** (was: *"exactly one PEAK PCAN-USB adapter …
  Multi-adapter setups are out of scope"*). Revised: the bench **may have more
  than one** PEAK adapter present; the tool opens the **first that initializes
  successfully** from an ordered, bounded candidate set. When only one adapter
  is present, behaviour is unchanged (single `0x51`).
- **Out of Scope — `spec.md:162`** (was: *"Multi-adapter disambiguation: covered
  by the 'first enumerated wins' edge case; richer disambiguation is
  downstream"*). Revised: "first **free** adapter wins" — the scan **skips a
  busy candidate to reach a free one**. Richer per-identity disambiguation
  (a picker UI) remains downstream.

Nothing in the FSM (`Initializing | Connected | Disconnected | Error`), its
Lean theorems, the presentation surfaces, or the Recoverable/Fatal taxonomy
changes. Channel selection lives **below** the FSM.

## Problem

Today the channel is a hardcoded `0x51` (`PCAN_USBBUS1`) in **four** places —
the vendored open/read/write/monitor path plus three F# read-back helpers:

| Surface | Site | Role |
|---|---|---|
| Open / read / write / 1 Hz monitor | `Infrastructure.Protocol/Hardware/PCANManager.cs:41` (`const Channel = 0x51`) | the live link |
| Adapter identification read-back | `Infrastructure/Can/PcanAdapterIdentity.fs:33` | FR-004 detail affordance |
| Status + channel-condition probe | `Infrastructure/Can/PeakStatusTranslation.fs:45` | cold-start busy/absent classifier (#168) |
| Error-text read-back | `Infrastructure/Can/PeakErrorText.fs:26` | technical detail line |

When the first channel is held by another application (the canonical case:
StemDeviceManager holding `0x51`, #150), today's behaviour is *spec-compliant*
("first enumerated wins; report busy") but leaves the technician stuck on
`Recoverable · adapter busy` even though a second, free PEAK adapter is plugged
in. The bench need for two adapters is now real.

## P1 user story

As a **bench technician with more than one PEAK adapter present**, I launch the
tool while another app holds the first channel, and observe the CAN status row
connecting through the first **free** adapter instead of reporting "adapter
busy."

## Functional requirements

- **FR-192-1 — Ordered bounded scan.** Channel selection scans an ordered,
  bounded set of PCAN-USB channels (`0x51 …`) and opens the **first that
  initializes successfully**.
- **FR-192-2 — Skip busy, reach free.** When the first candidate is occupied and
  a later candidate is free, the link reaches `Connected` through the free
  candidate.
- **FR-192-3 — Preserve the cold-start taxonomy.** When **every** candidate is
  unavailable, the existing taxonomy is preserved: any occupied candidate →
  `Error(busy)`; otherwise `Disconnected(NoAdapterPresent)`. This keeps
  #136 / #150 / #168 behaviour.
- **FR-192-4 — Surface the selected channel.** The opened channel is reflected in
  the adapter identification (FR-004 detail affordance) and written to `app.log`
  (#148 logging), so the technician and the forensic log can tell which channel
  was selected. *This is the requirement that forces the selected channel to
  propagate to the read-back helpers — see the design constraint below.*
- **FR-192-5 — Configurable extent, single-adapter default.** The scan extent is
  configurable via `appsettings.json` (a `Can` section), **defaulting to today's
  single-`0x51` behaviour** when only one adapter is present.
- **FR-192-6 — FSM and proofs untouched.** No change to the four-family
  `CanLinkState` FSM or its Lean theorems; build + existing FSM/lifecycle tests
  stay green.

## Central design constraint (the hard part)

FR-192-4 means a *selected* channel of, say, `0x52` must not leave the three
read-back helpers querying `0x51`. The amendment's real work is **threading the
selected channel** from the point of selection to:

1. the live link (`PCANManager` — `const` → constructor parameter), and
2. all three read-back helpers (`PcanAdapterIdentity`, `PeakStatusTranslation`,
   `PeakErrorText`), which `PcanCanLink` invokes.

Two decisions are deferred to `plan.md` (not settled here):

- **Where selection lives** — a probe-and-select step *in/above the port factory*
  vs. *inside `PcanCanLink`* (which already owns the `readCondition` seam and the
  identity/status read-backs, so it already knows when an open failed).
- **How "initializes successfully" is probed** — reuse the existing
  `PeakStatusTranslation.tryReadChannelCondition` per-channel pre-filter
  (cheap, no open) to find candidates, then authoritative `Initialize`; vs.
  attempt-open-and-keep down the list. The acceptance test of record is
  *initialize success*, with the condition probe as the busy-vs-absent
  classifier.

The unit seam is a **fake PCAN driver / channel-probe** exposing per-channel
availability, so the scan, the skip-busy path, and the all-unavailable taxonomy
are exercised without hardware.

## Deliverables

| Artifact | Surface(s) |
|---|---|
| Channel-parameterized `PCANManager` (`const` → ctor param) | `Infrastructure.Protocol/Hardware/PCANManager.cs` |
| Channel-parameterized read-back helpers | `PcanAdapterIdentity.fs`, `PeakStatusTranslation.fs`, `PeakErrorText.fs` |
| Channel-selection step (probe ordered set → first openable) | `Infrastructure/Can/` + composition root |
| `Can` options type + binding | `appsettings.json` (`Can` section) + `CompositionRoot.fs` |
| Selected-channel surfacing | `AdapterIdentification` (FR-004) + `app.log` (#148) |
| Unit tests (fake driver / probe seam): scan, skip-busy, all-unavailable taxonomy, config binding, FSM-preservation regression | `tests/ButtonPanelTester.Tests*` |

No new shipped executable, package, or docs-site surface is introduced, so the
deliverables surface set is source + tests + `appsettings.json` only (the
canonical peer — the existing CAN lifecycle code — ships on exactly these).

## Acceptance criteria (from #192)

- [ ] Scan opens the first channel that initializes (fake-driver unit test).
- [ ] First occupied + later free → reaches `Connected` through the free channel.
- [ ] All candidates unavailable → any occupied → `Error(busy)`; else
      `Disconnected(NoAdapterPresent)` (regression; #136/#150/#168).
- [ ] Selected channel surfaced in adapter identification + `app.log`.
- [ ] Scan extent configurable via `appsettings.json` `Can` section, defaulting
      to single-`0x51` (config-binding test).
- [ ] No FSM / Lean change; existing FSM + lifecycle tests stay green.
- [ ] `./gate.ps1` green on both target frameworks.

## Non-goals

- **Mid-session channel hopping.** Selection happens at open / reconnect time
  only; a channel stolen mid-session does not trigger migration (the vendored
  monitor watches a single channel). Larger, later concern.
- **Multi-adapter disambiguation UI** — no per-identity picker; "first free
  wins" is the whole policy.
- **The #190 five-family re-carve / `AdapterCandidate` model** — this is a
  salvage on the four-family types per the parked README salvage table, not the
  supersede. Must not trip #190's unpark.
- **Forking / rewriting the vendored protocol stack** — parameterize the channel
  only; keep `ICanLink` / `ICommunicationPort` stable so #111 stays a clean swap.
- **Baud-rate changes** — fixed at 250 kbps by the panel firmware.

## Refs

- Ticks the *Multi-adapter iteration* row of
  `../../parked-phase-b-redesign/README.md:40` once landed (the bench need is now
  real). Record under #190's *Future modifications*; the structured
  `AdapterCandidate` model stays parked there.
- Related but separate (#190 *Related work*): #111 (vendored-stack →
  `Stem.Communication` migration), #149 (NotificationCenter /
  `ErrorClassification`).
