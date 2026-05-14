-- Umbrella module for the Phase 1 formal track of spec 001.
--
-- Sub-modules land in T024-T027 (see specs/001-fetch-dictionary/tasks.md):
--   * Phase1.DictionarySource       — T024
--   * Phase1.FetchFailureReason     — T025
--   * Phase1.DictionaryProvider     — T026
--   * Phase1.CacheConsistency       — T027
--
-- This file is intentionally a namespace-only placeholder: standards v1.5.3
-- `dotnet-ci.yml` runs `lake build` on the Linux leg as the constitution
-- Principle I gate, and the lake_lib target declared in lakefile.toml
-- requires a module file at this path. The real theorem content arrives
-- with the Phase 2 tasks.

namespace Stem.ButtonPanelTester.Phase1
end Stem.ButtonPanelTester.Phase1
