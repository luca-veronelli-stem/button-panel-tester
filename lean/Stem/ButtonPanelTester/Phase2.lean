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
-- Each sub-module is `import`-ed below as it lands so `lake build` on the
-- umbrella forces every Phase-2 theorem file to elaborate.

import Stem.ButtonPanelTester.Phase2.CanLinkState

namespace Stem.ButtonPanelTester.Phase2
end Stem.ButtonPanelTester.Phase2
