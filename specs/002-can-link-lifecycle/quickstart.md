# Quickstart: developing on spec-002 (CAN Link Lifecycle)

**Phase 1 output for**: [plan.md](./plan.md)

Entry point for any developer picking up spec-002 lifecycle work. For panel-discovery onboarding, see [`specs/003-panel-discovery/quickstart.md`](../003-panel-discovery/quickstart.md). Assumes spec-001's quickstart has already been worked through (the repo is cloned, .NET 10 SDK + Lean toolchain are installed, `dotnet build -c Release` succeeds on `main`).

---

## Bench setup (one-time)

1. **PEAK PCAN-USB adapter** (or PCAN-USB Pro FD) plugged into a USB port on the dev machine.
2. **Peak.PCANBasic driver** installed — download from peak-system.com, accept the OEM key on install, reboot. The vendored stack's `PCANManager.Initialize` returns a "driver not installed" status code if this step is skipped (surfaces as `Faulted(DriverNotInstalled, None, _)` in the status row, chip red).
3. **For lifecycle work specifically:** a panel is NOT required. The lifecycle slice covers the five-family FSM transitions (`Idle | Searching | Opening | Open | Faulted` per [data-model.md](./data-model.md) §1.1) regardless of whether anything is announcing on the bus.

## Repo setup

```powershell
# 1. Set up a worktree on the feature branch (the main repo stays on `main`).
cd <repo-root>
git worktree add ..\button-panel-tester-002-lifecycle docs/002-lifecycle-spec-refresh
cd ..\button-panel-tester-002-lifecycle

# 2. Vendor the protocol stack (one-time per stem-device-manager SHA; shared with spec-003)
.\eng\vendor-protocol-stack.ps1 -StemDeviceManagerPath ..\stem-device-manager -CommitSha <SHA>

# 3. Build the .NET stack
dotnet build Stem.ButtonPanelTester.slnx -c Release

# 4. Build the Lean Phase 2 modules
cd lean
lake build Stem.ButtonPanelTester.Phase2
cd ..

# 5. Run tests (no hardware needed)
dotnet test tests\ButtonPanelTester.Tests\ButtonPanelTester.Tests.fsproj -c Release
dotnet test tests\ButtonPanelTester.Tests.Windows\ButtonPanelTester.Tests.Windows.fsproj -c Release `
    --filter "Category!=Hardware"
```

## Running the tool against the adapter

```powershell
dotnet run --project src\ButtonPanelTester.GUI\ButtonPanelTester.GUI.fsproj -c Release
```

Expected behaviour on a clean bench (lifecycle slice only). The CAN status row starts in `Searching(Polling, _)` at app launch independently of dictionary boot ([research.md](./research.md) R10); the FSM walks the bench-reality paths below.

1. The main window appears with the dictionary status row from feat-001 (chip carries sync-subsystem truth per spec-001 [#156](https://github.com/luca-veronelli-stem/button-panel-tester/issues/156) Option γ; copy-health rendered as separate decoration via origin marker + stale-seed glyph).
2. The CAN status row appears immediately beneath it in `Searching · scanning for a PEAK adapter…` and walks the FSM as the bench dictates (scenarios §1–§8 below). The two rows populate in parallel — no boot gate between them.

### Bench walkthrough (FSM scenarios)

Each scenario is a Success Criterion ([spec.md](./spec.md) §Success Criteria) verifiable by eye on the bench. Edges are mechanised in [data-model.md](./data-model.md) §1.2 (Mermaid).

1. **Launch with no PEAK adapter present** (SC-002). Within 1 second the row settles on `Searching · no PEAK adapter found — plug in the adapter`, chip grey.
2. **Plug in the adapter** ([research.md](./research.md) R7 hot-plug edge; [spec.md](./spec.md) User Story 1 Acceptance #2). Within 5 seconds (and without a click) the row transits through `Opening · contacting <adapter>` (chip grey) and lands on `Open · <adapter identification>` (chip green). The vendored-stack device-arrived event is the fast path — typical recovery is ≤ 1 s; the 5-second periodic poll ([plan.md](./plan.md) §Searching retry policy) is the safety net if the event misses. (SC-001's 2-second budget applies only to the launch-time case where the adapter is already plugged in before the app starts; the empty-host plug-in path is User Story 1 Acceptance #2.)
3. **Unplug mid-session** (SC-003). Within 5 seconds the row flips to `Searching · waiting for adapter to come back`, chip grey. The dictionary status row is unchanged (FR-015, SC-006).
4. **Re-seat the adapter without clicking anything** (SC-004 + hot-plug auto-recovery). Within 5 seconds the row recovers to `Open · <adapter identification>`, chip green.
5. **Click Stop while in `Open`** (FR-006, SC-005). Within 2 seconds the row goes to `Idle · paused by user — click Start to resume`, chip grey, and the adapter handle is released. Verifiable bench-side by launching StemDeviceManager and watching it attach successfully to the same adapter (this is the bench-only acceptance — the CI surrogate `StopReleasesAdapterHandle` covers the boundary call sequence; see [plan.md](./plan.md) §CHK018). If Stop lands while the FSM is in `Opening`, the in-flight `OpenAsync` is cancelled via `CancellationToken` propagation and the row lands in `Idle(UserPaused, now)` within ≤ 250 ms on a normal-load workstation ([plan.md](./plan.md) §FR-006 cancellation budget).
6. **Click Start while in `Idle(UserPaused, _)`** (FR-007). The row transitions to `Searching · scanning for a PEAK adapter…` and the FSM resumes the scan loop.
7. **Trigger bus-off** (SC-008). The row transitions to `Faulted · bus-off — try Reconnect`, chip red, within 5 seconds of the underlying event. Click **Reconnect** — the button becomes disabled and shows the `⟳` glyph for the duration of the in-flight `OpenAsync` (SC-010 click-acknowledge cue), the chip transits truthfully through `Opening · contacting <adapter>` (grey), and lands on `Open` (green) if the fault cleared or back on `Faulted · bus-off — try Reconnect` (red) with a refreshed `since` if the same fault re-fires.
8. **Try opening an exclusively-held adapter** (FR-010 / SC-011). Launch StemDeviceManager (or any exclusive-mode PCAN client) against the only PEAK adapter, then click **Start** in BPT from `Idle(UserPaused, _)`. BPT enumerates the adapter, attempts `Open`, the driver returns busy, and the row lands on `Searching · no available adapter (1 found, busy) — release the other tool's link or attach a second adapter`, chip grey.

**Hot-plug acceptance traceability**: scenarios §2 and §4 above are bench-verified for now. The explicit Phase 2 / Phase 3 regression test that asserts the `Open → Searching(Polling) → Opening → Open` round-trip without operator input is gap-noted by [#132](https://github.com/luca-veronelli-stem/button-panel-tester/issues/132) and lands as Phase C `T213` (`HotPlugRecoveryAfterUnplug`, `Category=Hardware`); until then the manual walkthrough above is the gate ([plan.md](./plan.md) §CHK028).

(For panel-on-bus behaviour after the link is up, see spec-003's quickstart.)

## Running the hardware E2E suite (manual pre-release check)

```powershell
dotnet test tests\ButtonPanelTester.Tests.Windows\ButtonPanelTester.Tests.Windows.fsproj -c Release `
    --filter "Category=Hardware"
```

These tests are **excluded from CI** by Principle IV and are gated as a manual pre-release check. The Hardware-Test-Setup tracking issue documents the bench config required.

## File map for spec-002 lifecycle work

| Task type | Where the code lives |
|---|---|
| Add a new CAN lifecycle type | `src/ButtonPanelTester.Core/Can/CanLinkState.fs` |
| Add Lean theorem (lifecycle) | `lean/Stem/ButtonPanelTester/Phase2/CanLinkState.lean` or `PassiveObserver.lean` |
| FsCheck property (lifecycle) | `tests/ButtonPanelTester.Tests/Property/Can/CanLinkStateTransitionsProperties.fs` |
| Integration test (lifecycle) | `tests/ButtonPanelTester.Tests/Integration/Can/CanLinkServiceLifecycleTests.fs` (and friends) |
| GUI snapshot test (lifecycle) | `tests/ButtonPanelTester.Tests.Windows/Gui/Can/CanStatusRowTests.fs` |
| Vendored stack tweak | DON'T — re-vendor instead. See [contracts/vendor-manifest.md](./contracts/vendor-manifest.md) §"Re-vendoring procedure". |
| Hardware E2E (lifecycle) | `tests/ButtonPanelTester.Tests.Windows/Integration/Can/Hardware/PcanLifecycleTests.fs` (`Category=Hardware`) |

## Common gotchas

- **`PCAN status 0x40010` on Open**: PCAN driver thinks the adapter is in use by another process. Close any PCAN-View or competing exclusive-mode apps; the FSM should then iterate to the next enumerated candidate (FR-012) or land on `Searching(NoCandidateAvailable count, _)` if every enumerated adapter is busy.
- **`PCAN status 0x80000` on Open**: bus-off detected immediately — usually a wiring or termination problem on the bench (check the 120 Ω termination at both ends). Surfaces as `Faulted(BusOff, Some <adapter>, _)`.
- **Unrecognised PEAK status code in the headline**: the FSM lands on `Faulted(UnexpectedAdapterStatus code, Some <adapter>, _)` and the detail string carries the raw status code verbatim ([spec.md](./spec.md) §Edge Cases). File the code in a bug report; the PEAK driver may have introduced a new status in a recent release.
- **Row stuck in `Searching(Polling, _)` past the 5-second poll cadence on hot-plug**: the vendored stack's device-arrived event may have been dropped or coalesced. Wait one full poll cycle (≤ 5 s per [plan.md](./plan.md) §Searching retry policy); if the row still doesn't move after re-seating, the adapter handle may be wedged at the OS level — try unplugging and waiting a few seconds before re-seating.

## Where to go next

- **Spec-003 panel discovery** carries the WHO_I_AM observation pipeline + Panels-on-bus list. See [`specs/003-panel-discovery/quickstart.md`](../003-panel-discovery/quickstart.md).
- **Lean Phase 2** can be opened in VS Code with the lean4 extension pointed at `lean/lean-toolchain`. The `lean4` skill documents the workflow (proof-state inspection, `lean_goal` / `lean_local_search`).
