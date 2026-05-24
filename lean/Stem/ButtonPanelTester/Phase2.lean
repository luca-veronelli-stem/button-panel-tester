-- Umbrella module for the Phase 2 formal track of spec 002 (CAN link and
-- panel discovery).
--
-- Sub-modules will land starting at task T027 of
-- `specs/002-can-link-and-panel-discovery/tasks.md`:
--
--   * Phase2.CanLinkState       — T027
--   * Phase2.WhoIAmFrame        — T028
--   * Phase2.PanelObservation   — T029
--   * Phase2.PanelsOnBus        — T030
--   * Phase2.Pruning            — T031
--   * Phase2.PassiveObserver    — T032
--
-- This commit (PR-A, T010) only registers the empty `[[lean_lib]]` in
-- `lakefile.toml` so `lake build` exercises the Phase 2 target shape
-- before any modules exist. The `import`s are added module-by-module as
-- each commit lands the corresponding theorem.

namespace Stem.ButtonPanelTester.Phase2
end Stem.ButtonPanelTester.Phase2
