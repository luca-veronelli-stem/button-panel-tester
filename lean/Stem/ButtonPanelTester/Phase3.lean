-- Umbrella module for the Phase 3 formal track of spec 004 (baptism
-- workflow).
--
-- Sub-modules land per `specs/004-baptism-workflow/tasks.md`:
--
--   * Phase3.WhoAreYouFrame  — T002 (WHO_ARE_YOU TX codec + encodeVariant)
--   * Phase3.SetAddressFrame — T003 (SET_ADDRESS TX codec + byte-echo)
--
-- Later children add (imported as they land):
--
--   * Phase3.BaptismSequence — T017
--   * Phase3.Enablement      — T027
--
-- Each sub-module is `import`-ed below as it lands so `lake build` on the
-- umbrella forces every Phase-3 theorem file to elaborate.

import Stem.ButtonPanelTester.Phase3.WhoAreYouFrame
import Stem.ButtonPanelTester.Phase3.SetAddressFrame

namespace Stem.ButtonPanelTester.Phase3
end Stem.ButtonPanelTester.Phase3
