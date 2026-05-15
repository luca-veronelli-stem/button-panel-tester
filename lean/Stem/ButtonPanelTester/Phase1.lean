-- Umbrella module for the Phase 1 formal track of spec 001.
--
-- Sub-modules (per specs/001-fetch-dictionary/tasks.md):
--   * Phase1.DictionarySource       — T024 (this commit)
--   * Phase1.FetchFailureReason     — T025
--   * Phase1.DictionaryProvider     — T026
--   * Phase1.CacheConsistency       — T027
--
-- Each sub-module is `import`-ed below as it lands so `lake build` on the
-- umbrella forces every Phase-1 theorem file to elaborate. T024 ships the
-- first sub-module; T025-T027 will extend this list.

import Stem.ButtonPanelTester.Phase1.DictionarySource

namespace Stem.ButtonPanelTester.Phase1
end Stem.ButtonPanelTester.Phase1
