# Quickstart: developing on spec-002 (CAN Link Lifecycle)

**Phase 1 output for**: [plan.md](./plan.md)

Entry point for any developer picking up spec-002 lifecycle work. For panel-discovery onboarding, see [`specs/003-panel-discovery/quickstart.md`](../003-panel-discovery/quickstart.md). Assumes spec-001's quickstart has already been worked through (the repo is cloned, .NET 10 SDK + Lean toolchain are installed, `dotnet build -c Release` succeeds on `main`).

---

## Bench setup (one-time)

1. **PEAK PCAN-USB adapter** (or PCAN-USB Pro FD) plugged into a USB port on the dev machine.
2. **Peak.PCANBasic driver** installed — download from peak-system.com, accept the OEM key on install, reboot. The vendored stack's `PCANManager.Initialize` returns a "driver not installed" status code if this step is skipped (surfaces as `Error.Fatal` in the status row).
3. **For lifecycle work specifically:** a panel is NOT required. The lifecycle slice covers Connected/Disconnected/Error transitions regardless of whether anything is announcing on the bus.

## Repo setup

```powershell
# 1. Set up a worktree on the feature branch (the main repo stays on `main`).
cd <repo-root>
git worktree add ..\button-panel-tester-002-lifecycle feat/002-can-link-lifecycle
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

Expected behaviour on a clean bench (lifecycle slice only):

1. The main window appears with the dictionary status row from feat-001 (`Live` or `Cached` chip).
2. Within ~2 s, the CAN status row beneath it flips from `Initializing` to `Connected` (chip turns green; detail reads e.g. `PCAN-USB Pro FD (1) · 250 kbps`).
3. Unplug the PCAN adapter. Within ~5 s the CAN status row flips to `Disconnected · link lost — replug adapter` (chip turns grey) and the dictionary status row stays untouched (FR-016).
4. Replug the adapter, click `Reconnect`. The link comes back up.

(For panel-on-bus behaviour after the link is up, see spec-003's quickstart.)

## Running the hardware E2E suite (manual pre-release check)

The hardware cases are gated behind the `BPT_HARDWARE` environment variable (the `[<HardwareFact>]` attribute, #142). Set it to `1`, then run the `Category=Hardware` filter:

```powershell
$env:BPT_HARDWARE = "1"
dotnet test tests\ButtonPanelTester.Tests.Windows\ButtonPanelTester.Tests.Windows.fsproj -c Release `
    --filter "Category=Hardware"
```

`BPT_HARDWARE=1` is a **promise, not a probe** — set it only when a PEAK PCAN-USB adapter is actually plugged in and the driver is installed. The non-interactive cases *fail* (not skip) if the variable is set but no adapter responds. With the variable unset, every hardware case skips — which is also why CI (where it is never set) stays green even though the suite ships in the test project. The interactive unplug/replug cases stay skipped regardless; they prompt the operator and are run by hand.

These tests are **excluded from CI** by Principle IV — via both the `Category!=Hardware` filter in the standards reusable workflow and the `BPT_HARDWARE` gate. The Hardware-Test-Setup tracking issue (#112) documents the bench config required.

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

- **`PCAN status 0x40010` on Open**: PCAN driver thinks the adapter is in use by another process. Close any PCAN-View or competing apps.
- **`PCAN status 0x80000` on Open**: bus-off detected immediately — usually a wiring or termination problem on the bench (check the 120 Ω termination at both ends).
- **Status row stays `Initializing` past 5 s**: the dictionary boot from feat-001 has not completed yet; check the dictionary status row first. The CAN link only opens after dictionary boot per FR-001.

## Where to go next

- **Spec-003 panel discovery** carries the WHO_I_AM observation pipeline + Panels-on-bus list. See [`specs/003-panel-discovery/quickstart.md`](../003-panel-discovery/quickstart.md).
- **Lean Phase 2** can be opened in VS Code with the lean4 extension pointed at `lean/lean-toolchain`. The `lean4` skill documents the workflow (proof-state inspection, `lean_goal` / `lean_local_search`).
