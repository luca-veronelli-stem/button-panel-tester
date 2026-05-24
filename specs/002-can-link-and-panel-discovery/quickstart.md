# Quickstart: developing on spec-002 (CAN Link and Panel Discovery)

**Phase 1 output for**: [plan.md](./plan.md)

Entry point for any developer picking up spec-002 work. Assumes spec-001's quickstart has already been worked through (the repo is cloned, .NET 10 SDK + Lean toolchain are installed, `dotnet build -c Release` succeeds on `main`).

---

## Bench setup (one-time)

1. **PEAK PCAN-USB adapter** (or PCAN-USB Pro FD) plugged into a USB port on the dev machine.
2. **Peak.PCANBasic driver** installed — download from peak-system.com, accept the OEM key on install, reboot. The vendored stack's `PCANManager.Initialize` returns a "driver not installed" status code if this step is skipped (surfaces as `Error.Fatal` in the status row).
3. **A pristine virgin button panel** wired to the PCAN-USB adapter via the CAN-H/CAN-L pair, 120 Ω termination at both ends of the bus. A 24 V bench supply powers the panel.
4. **No motherboard on the bus.** The supplier QA scenario has the panel talking only to the tester; a motherboard on the same bus would baptize the panel out of `AAS_STARTUP` and silence it.

## Repo setup

```powershell
# 1. Fetch / verify the worktree
cd C:\Users\veron\Source\Repos\Stem
# (worktree already exists at .\button-panel-tester-002-can-link)
cd .\button-panel-tester-002-can-link

# 2. Vendor the protocol stack (one-time per stem-device-manager SHA)
.\eng\vendor-protocol-stack.ps1 -StemDeviceManagerPath ..\stem-device-manager -CommitSha <SHA>
#   Generates src/ButtonPanelTester.Infrastructure.Protocol/ + VENDOR.md + VENDOR.sha256
#   Also generates docs/STOPGAP_VENDORED_PROTOCOL_STACK.md if absent.

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

## Running the tool against a real panel

```powershell
dotnet run --project src\ButtonPanelTester.GUI\ButtonPanelTester.GUI.fsproj -c Release
```

Expected behaviour on a clean bench:

1. The main window appears with the dictionary status row from feat-001 (`Live` or `Cached` chip).
2. Within ~2 s, the CAN status row beneath it flips from `Initializing` to `Connected` (chip turns green; detail reads e.g. `PCAN-USB Pro FD (1) · 250 kbps`).
3. Power on the virgin panel. Within ~6 s a row appears under "Panels on bus":

   ```text
   UUID: 0x12AB34CD · 0x56EF78AB · 0x9012BC34
   Variant: virgin
   Last seen: 14:32:07 (just now)
   ```

4. Unplug the PCAN adapter. Within ~5 s the CAN status row flips to `Disconnected` (chip turns grey), the Panels-on-bus list empties (FR-015), and the dictionary status row stays untouched (FR-016).
5. Replug the adapter, click `Reconnect`. The link comes back up; the virgin panel reappears within the next ~6 s broadcast cycle.

## Running the hardware E2E suite (manual pre-release check)

```powershell
dotnet test tests\ButtonPanelTester.Tests.Windows\ButtonPanelTester.Tests.Windows.fsproj -c Release `
    --filter "Category=Hardware"
```

These tests are **excluded from CI** by Principle IV and are gated as a manual pre-release check. The Hardware-Test-Setup tracking issue documents the bench config required (specific adapter model, specific panel firmware version).

## File map for spec-002 work

| Task type | Where the code lives |
|---|---|
| Add a new CAN domain type | `src/ButtonPanelTester.Core/Can/` |
| Add Lean theorem | `lean/Stem/ButtonPanelTester/Phase2/<Name>.lean` |
| FsCheck property | `tests/ButtonPanelTester.Tests/Property/Can/<Name>Properties.fs` |
| Integration test | `tests/ButtonPanelTester.Tests/Integration/Can/` |
| GUI snapshot test | `tests/ButtonPanelTester.Tests.Windows/Gui/Can/` |
| Vendored stack tweak | DON'T — re-vendor instead. See [contracts/vendor-manifest.md](./contracts/vendor-manifest.md) §"Re-vendoring procedure". |
| Hardware E2E | `tests/ButtonPanelTester.Tests.Windows/Integration/Can/Hardware/` (`Category=Hardware`) |

## Common gotchas

- **`PCAN status 0x40010` on Open**: PCAN driver thinks the adapter is in use by another process. Close any PCAN-View or competing apps.
- **`PCAN status 0x80000` on Open**: bus-off detected immediately — usually a wiring or termination problem on the bench (check the 120 Ω termination at both ends).
- **Panel powered on, but no row appears**: the panel may be in `AAS_STAND_BY` (claimed, silent). spec-003 will add a reset-to-virgin flow; for now, re-flash the panel firmware to clear the EEPROM.
- **`fwType ≠ 0x04` frames silently dropped**: a non-panel STEM device (motherboard, etc.) is on the bus. Spec-002 silently drops these per FR-013 and the wire-format contract — verify the bench really only has the panel.
- **Status row stays `Initializing` past 5 s**: the dictionary boot from feat-001 has not completed yet; check the dictionary status row first. The CAN link only opens after dictionary boot per FR-001.

## Where to go next

- **Spec-003** will add transmit-side capability (`Baptize` flow: `WHO_ARE_YOU(reset=1)` → `WHO_I_AM` capture → `SET_ADDRESS`). It introduces the hardcoded `KnownStemCommands` / `KnownProtocolAddresses` modules and the corresponding stopgap (see CORRECTIONS.md §C5, scoped per spec-002 [research.md](./research.md) R2).
- **First review-time question** is usually "did we re-vendor since the last upstream fix?" — check the pinned SHA in `VENDOR.md` against the latest `stem-device-manager` `main`.
- **Lean Phase 2** can be opened in VS Code with the lean4 extension pointed at `lean/lean-toolchain`. The `lean4` skill documents the workflow (proof-state inspection, `lean_goal` / `lean_local_search`).
