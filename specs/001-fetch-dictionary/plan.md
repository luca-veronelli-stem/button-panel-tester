# Implementation Plan: Dictionary Fetch and Status Display

**Branch**: `feat/001-fetch-dictionary` | **Date**: 2026-05-14 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from [`specs/001-fetch-dictionary/spec.md`](./spec.md)

## Summary

Deliver the dictionary acquisition path end-to-end. A persistent status row at the top of the main window reports `Live | Cached`; a manual Refresh button re-fetches on demand; a first-launch registration ceremony exchanges a one-time `BootstrapToken` for a long-lived API credential stored under DPAPI; an embedded seed dictionary makes the tool usable on cold start before any network call resolves. The implementation tracks the actual `stem-dictionaries-manager` HTTP API (`X-Api-Key` header + `POST /register`) and the `cache-and-memory-in-lockstep` model the spec locks in. This slice introduces the layering pattern that every subsequent slice (CAN, baptize, run-test) reuses; Principle V is satisfied by construction (no identity-bearing field flows to STEM); zero stopgaps.

## Technical Context

**Language/Version**: F# 10 / .NET 10. `Nullable=enable`, `TreatWarningsAsErrors=true` per BUILD_CONFIG.

**Primary Dependencies**: Avalonia 11.3.7 + Avalonia.FuncUI 1.5.1 (GUI/Elmish-MVU), Microsoft.Extensions.{DependencyInjection,Configuration,Options} 10.0.7, BCL `HttpClient`, `System.Security.Cryptography.ProtectedData` (DPAPI), `System.Text.Json` (BCL).

**Storage**: filesystem only. `%LOCALAPPDATA%\Stem.ButtonPanelTester\dictionary.json` + sidecar `.sha256`, `…\credential.dpapi`. No database.

**Testing**: xUnit 2.9.3 + FsCheck.Xunit 3.3.3 + Avalonia.Headless.XUnit 11.3.7. One tests project (`tests/ButtonPanelTester.Tests/`), F#, partitioned `Unit/`, `Property/`, `Integration/`, `Gui/`, `Fakes/` per TESTING.

**Target Platform**: Windows desktop. `Core` and `Services` are `net10.0` (portable). `Infrastructure` and `GUI` are `net10.0-windows` because `ProtectedData` is Windows-only; this is a deliberate boundary so a future supplier port (Linux/macOS) only re-implements the credential adapter. `GUI`'s TFM bump is purely structural — a project-reference to a `net10.0-windows` Infrastructure project forces the consumer to the same TFM; the GUI sources themselves use no Windows-only APIs. A future port replaces `DpapiCredentialStore` plus the two TFM lines and nothing above (see Constitution §"Locked stack" — "Windows-confined drivers" is read to include this structural cascade).

**Project Type**: desktop app (archetype A).

**Performance Goals**: status row populated within 1 s of window paint (SC-001); failed refresh surfaced within 12 s of click (SC-004); cold start usable without waiting on network (SC-002).

**Constraints**: 10 s HTTP timeout, no retries (the seed/cache is the resilience story). Pre-seeded extraction must complete before the first window paint. Concurrent refresh requests coalesce to a single in-flight HTTP call (FR-007).

**Scale/Scope**: one configured dictionary id per install. ≤ a few hundred panel-types × tens of variables. One credential per (machine, user) pair.

## Constitution Check

*GATE: passes before Phase 0 research. Re-checked after Phase 1 design.*

- **I. Formal Verification of Invariants** *(NON-NEGOTIABLE)* — adds `lean/Stem/ButtonPanelTester/Phase1/`:
  - `DictionarySource.lean` — closed transitions; theorem `source_data_preserved`: a `Live → Cached` re-label preserves the in-memory dictionary.
  - `FetchFailureReason.lean` — closed enum; theorem `failure_reason_exhaustion`: every observable HTTP/network outcome maps to exactly one variant.
  - `DictionaryProvider.lean` — port contract; theorem `provider_success_xor_failed`: every `IDictionaryProvider.FetchAsync` call returns exactly one of `Success` or `Failed`.
  - `CacheConsistency.lean` — operational model; theorem `cache_memory_equal_post_first_success`: after the first successful live fetch in a session, the on-disk cache file and the in-memory dictionary are byte-equal at every observable point. (FR-010 in mechanised form.)
- **II. Property-Driven Correctness** — FsCheck properties in `tests/.../Property/`:
  - `DictionarySerialization`: round-trip `ButtonPanelDictionary` JSON ⇄ value preserves equality.
  - `ContentHash`: same input bytes ⇒ same hash; different bytes ⇒ different hash.
  - `FetchFailureReasonClosure`: pattern-match coverage check (compile-time enforced; FsCheck adds confidence under arbitrary value generation).
  - `DictionaryServiceTransitions`: starting from any reachable `DictionarySource`, applying any provider outcome lands in another reachable state per the Lean spec.
  - Example-based `[<Fact>]` is reserved for documenting concrete API fixtures (one per HTTP status code) — rationale recorded in each test file's docstring.
- **III. Ports and Adapters for Every External Boundary** — five new ports in `<App>.Core/Dictionary/Ports.fs`:
  | Port | Production adapter | Virtual adapter (tests) |
  |---|---|---|
  | `IDictionaryProvider` | `HttpDictionaryProvider` (`HttpClient → /api/dictionaries/{id}/resolved`) | `InMemoryDictionaryProvider` (returns scripted `DictionaryFetchResult` sequences) |
  | `IDictionaryCache` | `JsonFileDictionaryCache` (`File.ReadAllBytes` + sidecar SHA-256) | `InMemoryDictionaryCache` (Dictionary<string,byte[]>) |
  | `ICredentialStore` | `DpapiCredentialStore` (`ProtectedData.Protect/Unprotect`) | `InMemoryCredentialStore` (string) |
  | `IRegistrationClient` | `HttpRegistrationClient` (`POST /register`) | `InMemoryRegistrationClient` (scripted token-→-credential map) |
  | `IClock` | `SystemClock` | `FrozenClock` (`DateTimeOffset` injected) |
- **IV. CI Greens the Whole Stack; Hardware Tests Are Explicit** — every layer ships tests on every PR:
  - Unit: pure F# logic, no IO.
  - Property: FsCheck against ports' contracts.
  - Integration: virtual adapters wired through `DictionaryService`; end-to-end through the cache-and-memory-in-lockstep flow without network or filesystem.
  - GUI: `Avalonia.Headless.XUnit` against `DictionaryStatusRow` and `RegistrationDialog`.
  - No `Category=Hardware` this slice; no `[<Fact(Skip=...)>]`.
- **V. Supplier-Deployed Identity Is Hashed at Capture** *(NON-NEGOTIABLE)* — **No identity-bearing data on this feature's path.** The tool transmits the dictionary id (URL path) and the API credential (`X-Api-Key` header). Neither identifies the supplier's machine, user, MAC, or SID. The credential is a server-issued opaque token, not a derived identity. The `BootstrapToken` is a one-time secret issued by STEM and discarded after exchange. No hash routine is needed because no identity ever leaves the supplier's machine.
- **VI. Stopgap Discipline** — **Zero stopgaps.** The wire-level contract (`X-Api-Key` + `POST /register`) is the API as it actually exists in `stem-dictionaries-manager`. No DPAPI bypass, no endpoint substitution, no credential-in-config shortcut. No `STOPGAP_*.md` files are introduced.

**Result: PASS.** No items move to Complexity Tracking.

**Note on Principle I's ordering and Phase 2 of `tasks.md`:** Principle I mandates *Lean spec → xUnit test → F# implementation*. In `tasks.md` Phase 2 is a feature-bootstrap bundle whose contents (Lean modules, property tests, F# domain types and ports) are required *together* before any user-story implementation begins; the task numbering is suggestive, not a strict execution order. The constitutional ordering is satisfied at the *feature* boundary: the four Lean Phase 1 theorems (T024–T027) and the FsCheck property suites (T021–T023) land **before** any US1/US2/US3 implementation task (T029 onward) executes. Within Phase 2 itself, F# types and Lean modules may be authored in either order so long as both are present and green at the Phase 2 checkpoint.

## Project Structure

### Documentation (this feature)

```text
specs/001-fetch-dictionary/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions + alternatives
├── data-model.md        # Phase 1 — F# types + state-machine diagram
├── contracts/           # Phase 1 — wire and port contracts
│   ├── dictionary-api.md
│   ├── registration-api.md
│   ├── cache-format.md
│   ├── credential-format.md
│   └── ports.md
├── quickstart.md        # Phase 1 — developer onboarding for this slice
└── checklists/
    └── requirements.md  # /speckit.specify quality checklist (already green)
```

### Source code (repository root)

```text
src/
├── ButtonPanelTester.Core/            net10.0  F#  Domain types + ports
│   └── Dictionary/
│       ├── ButtonPanelDictionary.fs   PanelType, Variable, ButtonPanelDictionary
│       ├── DictionarySource.fs        Live | Cached + CacheOrigin
│       ├── DictionaryFetchResult.fs   Success | Failed
│       ├── FetchFailureReason.fs      6 closed cases
│       ├── ContentHash.fs             SHA-256 of canonicalised JSON
│       └── Ports.fs                   IDictionaryProvider, IDictionaryCache,
│                                      ICredentialStore, IRegistrationClient, IClock
├── ButtonPanelTester.Services/        net10.0  F#  Use cases (depends on Core)
│   └── Dictionary/
│       └── DictionaryService.fs       cache-and-memory-in-lockstep orchestration
│                                      (in-flight coalescing via TaskCompletionSource)
├── ButtonPanelTester.Infrastructure/  net10.0-windows  F#  Adapters
│   ├── Http/
│   │   ├── HttpDictionaryProvider.fs  GET /api/dictionaries/{id}/resolved + X-Api-Key
│   │   └── HttpRegistrationClient.fs  POST /register
│   ├── Persistence/
│   │   ├── JsonFileDictionaryCache.fs  read/write JSON + sidecar SHA-256
│   │   ├── EmbeddedSeedExtractor.fs    extract assembly resource on first launch
│   │   └── DpapiCredentialStore.fs     ProtectedData encrypt/decrypt
│   └── Clock.fs                        SystemClock
└── ButtonPanelTester.GUI/             net10.0-windows  F#  Avalonia + FuncUI + composition root
    ├── Composition/
    │   └── CompositionRoot.fs         MEDI wiring (no adapters in Services)
    ├── Dictionary/
    │   ├── DictionaryStatusRow.fs     indicator + headline + Details affordance
    │   └── RegistrationDialog.fs      Elmish modal
    ├── App.fs                         FuncUI app shell
    ├── Program.fs                     entry point
    └── Assets/
        └── dictionary.seed.json       embedded resource (refreshed by eng/refresh-seed.ps1)

tests/
└── ButtonPanelTester.Tests/           net10.0  F#  xUnit + FsCheck + Avalonia.Headless
    ├── Unit/
    ├── Property/
    ├── Integration/
    ├── Gui/
    ├── Fakes/                         InMemory adapters per Port
    └── Fixtures/                      sample DictionaryResolvedDto JSON snapshots

lean/                                  Lean 4 workspace (deviates from REPO_STRUCTURE
├── lakefile.toml                      because spec-kit owns specs/; recorded in
├── lean-toolchain                     constitution + standards#79)
└── Stem/ButtonPanelTester/Phase1/
    ├── DictionarySource.lean
    ├── FetchFailureReason.lean
    ├── DictionaryProvider.lean
    └── CacheConsistency.lean

eng/
└── refresh-seed.ps1                   fetch dictionary, write Assets/dictionary.seed.json,
                                       computes embedded SeededAt timestamp; manual,
                                       run before each release per the seed-maintenance
                                       discussion (see WIP.md).
```

**Structure Decision**: standard archetype A. Two TFM split (`net10.0` for Core/Services, `net10.0-windows` for Infrastructure/GUI) is the boundary that contains DPAPI's Windows-ness. The future cross-platform port is then exactly one adapter swap (`DpapiCredentialStore` → e.g. `KeychainCredentialStore` on macOS) plus a TFM bump in two `.fsproj` files; everything above stays untouched. The `lean/` workspace is a sibling of `specs/` because spec-kit's `specs/NNN-feature-name/` collides with REPO_STRUCTURE.md's claim that `specs/` is the Lean workspace; the deviation is recorded in the constitution and tracked upstream as `standards#79`.

## Complexity Tracking

> Empty — Constitution Check passes without unresolved violations.

## Status

- [x] Phase 0 — research.md (open questions resolved with concrete decisions; see [research.md](./research.md))
- [x] Phase 1 — data-model.md, contracts/, quickstart.md
- [x] Constitution Check (post-design re-evaluation): still PASS
- [ ] `/speckit.tasks` — break this plan into dependency-ordered work units
- [ ] `/speckit.implement`
