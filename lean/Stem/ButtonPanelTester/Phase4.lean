-- Umbrella module for the Phase 4 formal track of spec 005 (button-press
-- test, input side).
--
-- Sub-modules land per `specs/005-button-press-test/tasks.md`:
--
--   * Phase4.ButtonStateFrame — T002 (VAR_WRITE button-state codec)
--   * Phase4.KeyStateBitmap   — T003 (masked press-edge detector)
--   * Phase4.ButtonSchema     — T010 (per-variant active-button schema)
--   * Phase4.ButtonPressTest  — T018 (button-press-test session FSM)
--   * Phase4.Enablement       — T021 (button-press-test enablement guard)
--
-- Each sub-module is `import`-ed below as it lands so `lake build` on the
-- umbrella forces every Phase-4 theorem file to elaborate.

import Stem.ButtonPanelTester.Phase4.ButtonStateFrame
import Stem.ButtonPanelTester.Phase4.KeyStateBitmap
import Stem.ButtonPanelTester.Phase4.ButtonSchema
import Stem.ButtonPanelTester.Phase4.ButtonPressTest
import Stem.ButtonPanelTester.Phase4.Enablement

namespace Stem.ButtonPanelTester.Phase4
end Stem.ButtonPanelTester.Phase4
