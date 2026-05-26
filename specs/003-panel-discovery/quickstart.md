# Quickstart: developing on spec-003 (Panel Discovery)

**Phase 1 output for**: [plan.md](./plan.md)

Entry point for any developer picking up spec-003 panel-discovery work. Assumes spec-002 lifecycle's quickstart has already been worked through (the repo is cloned, .NET 10 SDK + Lean toolchain are installed, the vendored protocol stack is in place, `dotnet build -c Release` succeeds on `main`). See [`../002-can-link-lifecycle/quickstart.md`](../002-can-link-lifecycle/quickstart.md) for the foundational setup.

---

## Bench setup (discovery-specific)

In addition to spec-002's bench setup:

1. **A pristine virgin button panel** wired to the PCAN-USB adapter via the CAN-H/CAN-L pair, 120 Ω termination at both ends of the bus. A 24 V bench supply powers the panel.
2. **No motherboard on the bus.** The supplier QA scenario has the panel talking only to the tester; a motherboard on the same bus would baptize the panel out of `AAS_STARTUP` and silence it.

## Repo setup

```powershell
# 1. Set up a worktree on the feature branch
cd <repo-root>
git worktree add ..\button-panel-tester-003-discovery feat/003-panel-discovery
cd ..\button-panel-tester-003-discovery

# 2. Vendor stack already in place (shared with spec-002). Build:
dotnet build Stem.ButtonPanelTester.slnx -c Release

# 3. Build the Lean Phase 2 modules
cd lean && lake build Stem.ButtonPanelTester.Phase2 && cd ..

# 4. Run tests
dotnet test tests\ButtonPanelTester.Tests\ButtonPanelTester.Tests.fsproj -c Release
dotnet test tests\ButtonPanelTester.Tests.Windows\ButtonPanelTester.Tests.Windows.fsproj -c Release `
    --filter "Category!=Hardware"
```

## Running the tool against a real panel

```powershell
dotnet run --project src\ButtonPanelTester.GUI\ButtonPanelTester.GUI.fsproj -c Release
```

Expected behaviour on a clean bench (discovery-specific behaviour, after spec-002's lifecycle is Connected):

1. The CAN status row is Connected (per spec-002 lifecycle).
2. Power on the virgin panel. Within ~6 s a row appears under "Panels on bus":

   ```text
   UUID: 0x12AB34CD · 0x56EF78AB · 0x9012BC34
   Variant: virgin
   Last seen: 14:32:07 (just now)
   ```

3. Power off the panel. After 15 s of silence, the row disappears.
4. Unplug the PCAN adapter mid-session. The Panels-on-bus list clears immediately (FR-015' consumer of spec-002 FR-015), regardless of the prune timer.

## File map for spec-003 work

| Task type | Where the code lives |
|---|---|
| Add a new discovery domain type | `src/ButtonPanelTester.Core/Can/` (cohabits with spec-002 lifecycle) |
| Add Lean theorem (discovery) | `lean/Stem/ButtonPanelTester/Phase2/WhoIAmFrame.lean` / `PanelObservation.lean` / `PanelsOnBus.lean` / `Pruning.lean` |
| FsCheck property (discovery) | `tests/ButtonPanelTester.Tests/Property/Can/WhoIAmFrameProperties.fs` / `PanelsOnBusProperties.fs` / etc. |
| Integration test (discovery) | `tests/ButtonPanelTester.Tests/Integration/Can/DiscoveryE2ETests.fs` / `PruningE2ETests.fs` / `LinkLossClearsListTests.fs` |
| GUI snapshot test (discovery) | `tests/ButtonPanelTester.Tests.Windows/Gui/Can/PanelsOnBusViewTests.fs` |
| Vendored stack tweak | DON'T — re-vendor instead. See [`../002-can-link-lifecycle/contracts/vendor-manifest.md`](../002-can-link-lifecycle/contracts/vendor-manifest.md). |
| Hardware E2E (discovery) | `tests/ButtonPanelTester.Tests.Windows/Integration/Can/Hardware/DiscoveryHardwareTests.fs` |

## Common gotchas

- **Panel powered on, but no row appears**: the panel may be in `AAS_STAND_BY` (claimed, silent). spec-004 will add a reset-to-virgin flow; for now, re-flash the panel firmware to clear the EEPROM.
- **`fwType ≠ 0x04` frames silently dropped**: a non-panel STEM device (motherboard, etc.) is on the bus. Spec-003 silently drops these per FR-013 and the wire-format contract — verify the bench really only has the panel.
- **List clears unexpectedly**: spec-002 lifecycle dropped out of Connected; FR-015' is firing. Check the CAN status row first.

## Where to go next

- **Spec-004** will add transmit-side capability (`Baptize` flow: `WHO_ARE_YOU(reset=1)` → `WHO_I_AM` capture → `SET_ADDRESS`). It introduces the hardcoded `KnownStemCommands` / `KnownProtocolAddresses` modules and the corresponding stopgap (see CORRECTIONS.md §C5, scoped per [research.md](./research.md) R2).
- **First review-time question** is usually "did spec-002 lifecycle ship cleanly?" — discovery depends on the lifecycle observable.
