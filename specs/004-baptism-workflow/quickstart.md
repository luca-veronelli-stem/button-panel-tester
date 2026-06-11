# Quickstart: Baptism Workflow

**Status**: living — operator/developer walkthrough; update when commands or flows change.
**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

## Developer loop (no hardware)

```powershell
dotnet build -c Release
dotnet test tests/ButtonPanelTester.Tests -c Release            # property + integration (virtual adapters)
dotnet test tests/ButtonPanelTester.Tests.Windows -c Release    # GUI headless (+ hardware suite stays excluded)
lake build   # from lean/ — Phase 3 must be green before touching the F# FSM/codecs
```

The whole baptism pipeline runs on CI without hardware: `InMemoryMasterSequenceTransmitter`
records what would hit the bus, a fake/driven `IWhoIAmObserver` scripts re-announcements, and
`FrozenClock` drives the 6 s deadline deterministically. The integration suites
(`BaptismE2E`, `ResetE2E`, `TimeoutE2E`, `LinkLossAborts`, `AuditRecord`) show the wiring.

## Bench walkthrough (real panel)

Prerequisites: the spec-002/003 bench (PEAK PCAN-USB, 120 Ω termination both ends, 24 V supply —
see [spec-002 quickstart](../002-can-link-lifecycle/quickstart.md) §Bench setup and
[#112](https://github.com/luca-veronelli-stem/button-panel-tester/issues/112)) plus **one virgin
panel** physically on the bus.

### Baptize

1. Connect the link (status `Connected`); the panel appears in Panels-on-bus labeled `virgin`
   within ~6 s.
2. Select the row — the baptism surface activates (it stays disabled unless **exactly one** panel
   announces, FR-002).
3. Pick a variant (EDEN-XP / OPTIMUS-XP / R-3L XP / EDEN-BS8) and press **Baptize**.
4. Within 6 s you get a definitive outcome (SC-001). On success the message explains the panel
   now goes **silent by design** and its row will age out of the list within the 15 s pruning
   window — expected behaviour, not a fault (FR-006).
5. On a wait-timeout: the panel may still re-announce late carrying the target variant — it stays
   visible and claimable; re-run **Baptize** to complete the claim, or **Reset** to start over
   (FR-005). The tool never silently upgrades a failure to success.

### Reset to virgin

1. Attach the claimed (silent) panel — the list stays empty; that is expected (FR-011: silent
   panels are invisible).
2. Press **Reset to virgin** (no row selection needed; disabled if ≥ 2 panels announce, FR-008)
   and accept the confirmation — it exists because the reset broadcast reaches every matching
   panel on the bus, including silent ones the list cannot show (FR-009).
3. The tool reports the reset as sent on write completion (two broadcasts under the hood, one per
   firmware type — 12 V and 24 V). A matching panel re-announces as `virgin` within ~6 s
   (SC-003); with no panel attached the list simply stays empty (acceptance 2.5).

### Full bench cycle (SC-004)

baptize → verify → reset → next variant, all four variants on one physical panel; the tool keeps
zero state between cycles (FR-013) — the fourth cycle is indistinguishable from the first.

## Hardware E2E suite (manual pre-release check)

`BaptismHardwareTests.fs` (`Category=Hardware`, env-gated like the spec-002/003 suites, tracked
under #112 — excluded from default CI by the `Category!=Hardware` filter):

```powershell
$env:BPT_HARDWARE = '1'   # plus the interactive gate if the run needs operator action
dotnet test tests/ButtonPanelTester.Tests.Windows -c Release --filter "Category=Hardware"
```

Covers: claim E2E (success within budget; bus capture shows the claimed UUID goes silent,
SC-002), reset E2E (virgin row back within 6 s, SC-003), full cycle (SC-004). Bench needs one
virgin panel per firmware-type class exercised.

## Troubleshooting

- **Wait-timeout near exactly 6 s, panel then announces the target variant**: expected for
  worst-case UUIDs (~2–3 % of the space announce at ~6.0 s — research R4). Re-run Baptize on the
  still-announcing panel; the second claim's re-announcement completes well inside the budget.
- **Baptize disabled with one panel visibly in the list**: check the link chip is `Connected` and
  the row is actually selected (FR-002 explains the unmet condition in the disabled hint).
- **Reset reports sent but no virgin row appears**: nothing matching was attached (acceptance
  2.5), or the panel is mid-announce-cadence — give it the full ~6 s.
