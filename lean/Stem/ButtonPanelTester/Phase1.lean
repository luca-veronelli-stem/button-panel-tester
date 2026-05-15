-- Umbrella module for the Phase 1 formal track of spec 001.
--
-- Sub-modules (per specs/001-fetch-dictionary/tasks.md):
--   * Phase1.DictionarySource       — T024 (5d5b2d0)
--   * Phase1.FetchFailureReason     — T025 (cdcb234)
--   * Phase1.DictionaryProvider     — T026 (this commit)
--   * Phase1.CacheConsistency       — T027
--
-- Each sub-module is `import`-ed below as it lands so `lake build` on the
-- umbrella forces every Phase-1 theorem file to elaborate. T024, T025,
-- and T026 carry the `[P]` parallel marker in tasks.md:77,78,79 — all
-- three ship in this range; T027 will extend this list identically.

import Stem.ButtonPanelTester.Phase1.DictionarySource
import Stem.ButtonPanelTester.Phase1.FetchFailureReason
import Stem.ButtonPanelTester.Phase1.DictionaryProvider

namespace Stem.ButtonPanelTester.Phase1
end Stem.ButtonPanelTester.Phase1
