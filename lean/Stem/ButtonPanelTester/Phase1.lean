-- Umbrella module for the Phase 1 formal track of spec 001.
--
-- Sub-modules (per specs/001-fetch-dictionary/tasks.md):
--   * Phase1.DictionarySource       — T024 (5d5b2d0)
--   * Phase1.FetchFailureReason     — T025 (this commit)
--   * Phase1.DictionaryProvider     — T026
--   * Phase1.CacheConsistency       — T027
--
-- Each sub-module is `import`-ed below as it lands so `lake build` on the
-- umbrella forces every Phase-1 theorem file to elaborate. T024 and T025
-- carry the `[P]` parallel marker in tasks.md:77,78 — both ship in this
-- range; T026-T027 will extend this list.

import Stem.ButtonPanelTester.Phase1.DictionarySource
import Stem.ButtonPanelTester.Phase1.FetchFailureReason

namespace Stem.ButtonPanelTester.Phase1
end Stem.ButtonPanelTester.Phase1
