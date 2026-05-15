-- Umbrella module for the Phase 1 formal track of spec 001.
--
-- Sub-modules (per specs/001-fetch-dictionary/tasks.md):
--   * Phase1.DictionarySource       — T024 (5d5b2d0)
--   * Phase1.FetchFailureReason     — T025 (cdcb234)
--   * Phase1.DictionaryProvider     — T026 (34cb58c)
--   * Phase1.CacheConsistency       — T027 (this commit)
--
-- Each sub-module is `import`-ed below as it lands so `lake build` on the
-- umbrella forces every Phase-1 theorem file to elaborate. T024-T027 carry
-- the `[P]` parallel marker in tasks.md:77,78,79,80 — all four ship in this
-- range. With T027 the Phase-1 Lean track reaches its final four-module shape
-- per `plan.md` line 35-39.

import Stem.ButtonPanelTester.Phase1.DictionarySource
import Stem.ButtonPanelTester.Phase1.FetchFailureReason
import Stem.ButtonPanelTester.Phase1.DictionaryProvider
import Stem.ButtonPanelTester.Phase1.CacheConsistency

namespace Stem.ButtonPanelTester.Phase1
end Stem.ButtonPanelTester.Phase1
