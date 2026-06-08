# Quickstart: developing on spec-003 (Panel Discovery)

**Phase 1 output for**: [plan.md](./plan.md)

Entry point for picking up spec-003 panel-discovery work. Assumes the repo is
cloned in the canonical bare + worktrees layout, the .NET 10 SDK and the Lean
toolchain are installed, and `dotnet build -c Release` already succeeds on the
branch. Spec-003 baselines on the **shipped** CAN code — the `ICanLinkService`
lifecycle contract, the `IPanelDiscoveryService` facade this feature fills, and
the `Core/Can` domain types — so no sibling-spec setup is required.

---

## Bench setup

1. **A pristine virgin button panel** wired to the PCAN-USB adapter over the
   CAN-H / CAN-L pair, 120 Ω termination at both ends. A bench supply powers the
   panel — **12 V** boards report `fwType = 0x0004`, **24 V** boards report
   `fwType = 0x000F`; both are panels and both are accepted (FR-003 / R7).
2. **No motherboard on the bus.** The supplier-QA scenario has the panel talking
   only to the tester. A motherboard would baptize the panel out of `AAS_STARTUP`
   into the silent `AAS_STAND_BY` state, and a claimed panel does not broadcast.

## Repo setup (canonical bare + worktrees layout)

```powershell
# Work happens in the per-branch worktree, never in main.
cd C:\Users\LucaV\Source\Repos\button-panel-tester\docs-153-spec-003-respec

# Build the solution
dotnet build Stem.ButtonPanelTester.slnx -c Release

# Build the Lean Phase 2 modules
cd lean; lake build; cd ..

# Run the CI test layers (Hardware excluded)
dotnet test tests\ButtonPanelTester.Tests\ButtonPanelTester.Tests.fsproj -c Release
dotnet test tests\ButtonPanelTester.Tests.Windows\ButtonPanelTester.Tests.Windows.fsproj -c Release `
    --filter "Category!=Hardware"
```

> **Speckit tooling on this machine.** `jq` is absent and `python3` is the
> Windows Store stub, so the speckit bash scripts can't parse
> `.specify/feature.json`. Invoke them with the env-var override:
> `SPECIFY_FEATURE_DIRECTORY=specs/003-panel-discovery bash .specify/scripts/bash/setup-tasks.sh --json`.
> See [plan.md](./plan.md) §Tooling note.

## Running the tool against a real panel

```powershell
dotnet run --project src\ButtonPanelTester.GUI\ButtonPanelTester.GUI.fsproj -c Release
```

Expected behaviour on a clean bench, once the CAN status row is Connected:

1. Power on the virgin panel. Within ~6 s (SC-001) a row appears under
   "Panels on bus":

   ```text
   UUID: 0x12AB34CD · 0x56EF78AB · 0x9012BC34
   Variant: virgin
   Last seen: 14:32:07 (just now)
   ```

2. The row's last-seen advances in place on each re-broadcast (~2-6 s) — never a
   duplicate (FR-002 / SC-002).
3. Power off the panel. After 15 s of silence the row is pruned (FR-005).
4. Unplug the PCAN adapter mid-session. The list clears immediately (FR-008 /
   SC-004), independent of the prune timer — driven by
   `ICanLinkService.LinkStateChanged` leaving `Connected`.

Throughout, a bus capture shows **zero** frames originating from the tool
(FR-009 / SC-003).

## File map for spec-003 work

| Task type | Where the code lives |
|---|---|
| Correct / extend a discovery domain type | `src/ButtonPanelTester.Core/Can/` (`WhoIAmFrame.fs` is the codec to correct first) |
| Grow the discovery pipeline | `src/ButtonPanelTester.Services/Can/PanelDiscoveryService.fs` (stub → live) |
| Production receive adapter | `src/ButtonPanelTester.Infrastructure/Can/PcanCanFrameStream.fs` (new) |
| Composition wiring | `src/ButtonPanelTester.GUI/Composition/CompositionRoot.fs` (drop `NoOpCanFrameStream`) |
| Panels-on-bus view | `src/ButtonPanelTester.GUI/Can/PanelsOnBusView.fs` (new) + `App.fs` third slot |
| Lean theorem (discovery) | `lean/Stem/ButtonPanelTester/Phase2/{WhoIAmFrame,PanelObservation,PanelsOnBus,Pruning}.lean` |
| FsCheck property (discovery) | `tests/ButtonPanelTester.Tests/Property/Can/*` |
| WHO_I_AM fixtures | `tests/ButtonPanelTester.Tests/Fixtures/Can/whoIAmFixtures.json` (rebuild from the correct layout) |
| Integration test (discovery) | `tests/ButtonPanelTester.Tests/Integration/Can/{DiscoveryE2E,PruningE2E,LinkLossClearsList}Tests.fs` (new) |
| GUI snapshot test | `tests/ButtonPanelTester.Tests.Windows/Gui/Can/PanelsOnBusViewTests.fs` (new) |
| Hardware E2E | `tests/ButtonPanelTester.Tests.Windows/Integration/Can/Hardware/DiscoveryHardwareTests.fs` (new, `Category=Hardware`) |
| Vendored stack tweak | DON'T — re-vendor instead; consume it through `ICanFrameStream`. |

## Common gotchas

- **Panel powered on, but no row appears** — first suspect the shipped codec
  before Phase A lands: the old `WhoIAmFrame.fs` rejects every real frame
  (`byte[1] = fwType >> 8 = 0x00 ≠ 0x04`). After Phase A, a missing row means the
  panel is claimed (`AAS_STAND_BY`, silent) — re-flash its firmware to clear the
  EEPROM back to virgin (a reset-to-virgin flow is a later feature).
- **A frame is silently dropped** — only one rule drops a frame now: payload
  length ≠ 15 (FR-007). `fwType` no longer gates acceptance, so a 24 V panel
  (`0x000F`) is accepted. A dropped frame never flips the CAN status row to Error.
- **List clears unexpectedly** — the link left `Connected`; FR-008 fired. Check
  the CAN status row first.
- **`FrozenClock` vs wall clock in tests** — drive `LastSeen` and the prune
  reference instant through the injected `IClock` (`FrozenClock` in
  `Tests/Fakes/Wiring.fs`) so timing assertions are deterministic; never read
  `DateTimeOffset.UtcNow` directly in the service.

## Where to go next

- **`/speckit-tasks`** expands [plan.md](./plan.md) Phases A–E into `tasks.md`.
  Phase A (the wire-format correction) is the strict first slice — every later
  behaviour depends on a real frame parsing.
- A later transmit-side feature adds the `Baptize` flow and introduces the
  hardcoded protocol-metadata tables; spec-003 deliberately stays below that
  layer (see [research.md](./research.md) R2).
