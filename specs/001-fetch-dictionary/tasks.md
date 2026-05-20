---

description: "Task list for feat/001-fetch-dictionary"
---

# Tasks: Dictionary Fetch and Status Display

**Input**: Design documents from `specs/001-fetch-dictionary/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md).

**Tests**: REQUIRED. The plan's Constitution Check (Principles II + IV) mandates property tests, integration tests against virtual adapters, GUI tests via `Avalonia.Headless`, and Lean Phase1 proofs. Test tasks are interleaved with implementation, not optional.

**Organization**: Tasks group by user story (US1 — status row + seed; US2 — registration; US3 — manual refresh). Phases 1–2 are shared scaffolding and foundational types. Each user story phase ends with a checkpoint where that story is independently testable per spec.md's "Independent Test" criterion.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel — different `.fsproj` projects or independent files outside the F# compile graph (Lean, JSON fixtures, docs).
- **[Story]**: User story this task serves (US1, US2, US3). Setup/Foundational/Polish carry no story label.
- File paths are absolute from repo root.

## Path Conventions

Archetype A, two-TFM split (see `plan.md` §Project Structure):

- `src/ButtonPanelTester.Core/` — `net10.0` F# domain + ports
- `src/ButtonPanelTester.Services/` — `net10.0` F# use cases
- `src/ButtonPanelTester.Infrastructure/` — `net10.0-windows` F# adapters (DPAPI is Windows-only)
- `src/ButtonPanelTester.GUI/` — `net10.0-windows` F# Avalonia + FuncUI shell + composition root
- `tests/ButtonPanelTester.Tests/` — `net10.0` F# xUnit + FsCheck + Avalonia.Headless
- `lean/Stem/ButtonPanelTester/Phase1/` — Lean 4 Phase 1 modules

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: scaffold the four new projects and supporting workspaces so every later task lands on a compiling solution.

- [x] T001 Add `System.Security.Cryptography.ProtectedData` (10.0.7) to the centrally-managed package list in `Directory.Packages.props` — required by Infrastructure's DPAPI adapter (`research.md` R2).
- [x] T002 Create `src/ButtonPanelTester.Services/ButtonPanelTester.Services.fsproj` (TFM `net10.0`, `RootNamespace=Stem.ButtonPanelTester.Services`, ProjectReference to Core) with no `<Compile>` items yet — empty F# project must still compile.
- [x] T003 Create `src/ButtonPanelTester.Infrastructure/ButtonPanelTester.Infrastructure.fsproj` (TFM `net10.0-windows`, ProjectReferences to Core + Services, PackageReferences `System.Security.Cryptography.ProtectedData`) with no `<Compile>` items yet.
- [x] T003a Pin `Tmds.DBus.Protocol` to `0.21.3` in `Directory.Packages.props` and enable `<CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>` — remediate [GHSA-xrw6-gwf8-vvr9](https://github.com/advisories/GHSA-xrw6-gwf8-vvr9) in the `Avalonia.Desktop` 11.3.7 → `Avalonia.X11` 11.3.7 → `Tmds.DBus.Protocol` 0.21.2 transitive (T004 prerequisite). Issue #75.
- [x] T004 Create `src/ButtonPanelTester.GUI/ButtonPanelTester.GUI.fsproj` (TFM `net10.0-windows`, `OutputType=WinExe`, PackageReferences `Avalonia` + `Avalonia.Desktop` + `Avalonia.Themes.Fluent` + `Avalonia.Diagnostics` + `Avalonia.FuncUI` + `Microsoft.Extensions.{DependencyInjection,Configuration,Configuration.Json,Options}`, ProjectReferences to Core + Services + Infrastructure) with no `<Compile>` items yet.
- [x] T005 Update `Stem.ButtonPanelTester.slnx` to add the three new projects (Services, Infrastructure, GUI) alongside the existing Core and Tests entries.
- [x] T005a [Issue #76] Update `tests/ButtonPanelTester.Tests/ButtonPanelTester.Tests.fsproj` — add `ProjectReference` to `ButtonPanelTester.Services` (Core already referenced). TFM stays `net10.0` (inherited from `Directory.Build.props`); Services is `net10.0` so this edge of the dependency graph is TFM-compatible. Cross-TFM references to Infrastructure + GUI move to a sibling `Tests.Windows` project in T005b per #76 (NU1201 hard error on net10.0 → net10.0-windows ProjectReference).
- [x] T005b [Issue #76] Create `tests/ButtonPanelTester.Tests.Windows/ButtonPanelTester.Tests.Windows.fsproj` (TFM `net10.0-windows` explicit, `RootNamespace=Stem.ButtonPanelTester.Tests.Windows`, ProjectReferences to Infrastructure + GUI, PackageReferences `xunit` + `xunit.runner.visualstudio` + `Microsoft.NET.Test.Sdk`) with no `<Compile>` items yet — `Avalonia.Headless.XUnit` and `FsCheck.Xunit` are added in T006. Update `plan.md` §"Project Structure" to document the two-project test layout (Tests `net10.0` over Core+Services, Tests.Windows `net10.0-windows` over Infrastructure+GUI).
- [x] T005c [Issue #76, standards #82] Update `Stem.ButtonPanelTester.slnx` to register `ButtonPanelTester.Tests.Windows` alongside the existing entries. Ordering: Core → Services → Infrastructure → GUI → Tests → Tests.Windows. (Originally deferred — see [standards #82](https://github.com/luca-veronelli-stem/standards/issues/82) — until the v1.5.3 `dotnet-ci.yml` Linux test step learned to skip `*.Tests.Windows.*` projects; lands in the standards-v1.5.3 realignment PR alongside the `.github/workflows/ci.yml@v1.5.3` bump.)
- [x] T006 Update test projects to wire the property-based + GUI test infrastructure — add `<PackageReference Include="FsCheck.Xunit" />` to `tests/ButtonPanelTester.Tests/ButtonPanelTester.Tests.fsproj` (net10.0) and `<PackageReference Include="Avalonia.Headless.XUnit" />` to `tests/ButtonPanelTester.Tests.Windows/ButtonPanelTester.Tests.Windows.fsproj` (net10.0-windows). Both versionless under CPM. Per #76, cross-TFM ProjectReferences have already been resolved by T005a/b/c.
- [x] T007 [P] Create Lean 4 workspace skeleton: `lean/lakefile.toml` declaring package `stem-button-panel-tester` with one library target `Stem.ButtonPanelTester.Phase1`, and `lean/lean-toolchain` pinning a current `leanprover/lean4` toolchain.
- [x] T008 [P] Create empty folders for test partitioning under `tests/ButtonPanelTester.Tests/` — `Unit/`, `Property/`, `Integration/`, `Gui/`, `Fakes/`, `Fixtures/` (one `.gitkeep` per folder so `git add` retains them).
- [x] T009 Create `src/ButtonPanelTester.GUI/appsettings.json` (production: `Dictionary:BaseUrl` placeholder + `Dictionary:Id = 2`) and `src/ButtonPanelTester.GUI/appsettings.Development.json` (`Dictionary:BaseUrl = https://localhost:7065`), both wired as `<Content CopyToOutputDirectory="PreserveNewest" />` in `ButtonPanelTester.GUI.fsproj`.

**Checkpoint**: solution restores and builds green; `dotnet test` still passes (`PlaceholderTests` is the only test). No behaviour change yet.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: domain types, ports, in-memory fakes, property tests for closed-enum invariants, and the four Lean Phase 1 modules. All three user stories depend on this layer.

**Critical**: no user-story task may start until Phase 2 is complete and `dotnet test` + `lake build` are both green.

- [x] T010 Add `src/ButtonPanelTester.Core/Dictionary/ButtonPanelDictionary.fs` — `Variable`, `PanelType`, `ButtonPanelDictionary` records per `data-model.md` §1.1; register in `ButtonPanelTester.Core.fsproj`; remove the now-redundant `Placeholder.fs` entry from the fsproj and delete the file (`PlaceholderTests.fs` is removed in T028 once replacement tests exist).
- [x] T011 Add `src/ButtonPanelTester.Core/Dictionary/ContentHash.fs` — `module ContentHash` exposing `compute : byte[] -> string` returning 64-char lowercase hex SHA-256 per `research.md` R3 and `data-model.md` §6.
- [x] T012 Add `src/ButtonPanelTester.Core/Dictionary/DictionarySource.fs` — `CacheOrigin` (`FromEmbeddedSeed | FromLocalFile`) and `DictionarySource` (`Live of DateTimeOffset | Cached of DateTimeOffset * CacheOrigin * FetchFailureReason option`) per `data-model.md` §1.2. *(Forward-references `FetchFailureReason`; place `FetchFailureReason.fs` ahead of this file in fsproj order — see T013.)*
- [x] T013 Add `src/ButtonPanelTester.Core/Dictionary/FetchFailureReason.fs` — closed DU with **eight cases**: `NetworkUnreachable | Timeout | Unauthorized | NotFound | MalformedPayload | ServerError | CacheAbsent | CacheUnreadable`. The last two extend the six wire-failure cases listed in `data-model.md` §1.3 to cover the cache-read failure modes called out in `contracts/cache-format.md:74`. Position **before** `DictionarySource.fs` in fsproj order.
- [x] T014 Add `src/ButtonPanelTester.Core/Dictionary/DictionaryFetchResult.fs` — DU `Success of ButtonPanelDictionary * FetchedAt:DateTimeOffset | Failed of Reason:FetchFailureReason * Detail:string option` per `data-model.md` §1.3.
- [x] T015 Add `src/ButtonPanelTester.Core/Dictionary/RegistrationTypes.fs` — single-case-DU smart constructors `BootstrapToken` (trim + non-empty) and `InstallationCredential` (opaque), plus `RegistrationError` (`TokenInvalid | RegistrationServerError of int | RegistrationNetwork of FetchFailureReason`) per `data-model.md` §1.4.
- [x] T016 Add `src/ButtonPanelTester.Core/Dictionary/Ports.fs` — five port interfaces `IClock`, `IDictionaryProvider`, `IDictionaryCache`, `ICredentialStore`, `IRegistrationClient` exactly as specified in `data-model.md` §2 and `contracts/ports.md`.
- [x] T017 Add `src/ButtonPanelTester.Services/Dictionary/IDictionaryService.fs` — `DictionaryStateUpdate` DU and `IDictionaryService` interface per `data-model.md` §3 (snapshot, `SourceChanged` CLI event, `InitializeAsync`, `RefreshAsync`); register in `ButtonPanelTester.Services.fsproj`.
- [x] T018 Add `src/ButtonPanelTester.Infrastructure/Clock.fs` — `SystemClock` production adapter implementing `IClock` per `contracts/ports.md` §IClock; register in `ButtonPanelTester.Infrastructure.fsproj`.
- [x] T019 [P] Add `tests/ButtonPanelTester.Tests/Fakes/Wiring.fs` — five in-memory test adapters (`FrozenClock`, `InMemoryDictionaryProvider`, `InMemoryDictionaryCache`, `InMemoryCredentialStore`, `InMemoryRegistrationClient`) per the test-adapter blocks in `contracts/ports.md`; register in `ButtonPanelTester.Tests.fsproj` before any `Property/`, `Integration/`, or `Gui/` test file.
- [x] T020 [P] Add `tests/ButtonPanelTester.Tests/Fixtures/DictionaryResolvedDto.json` — sample JSON payload mirroring the `200 OK` shape in `contracts/dictionary-api.md` (one panel type, three variables, mixed nullable fields). Wired into the test project as `<Content CopyToOutputDirectory="PreserveNewest" />`.
- [x] T021 Add `tests/ButtonPanelTester.Tests/Property/DictionarySerializationTests.fs` — FsCheck property: `ButtonPanelDictionary` JSON round-trip preserves value equality (covers `plan.md` Principle II ① "DictionarySerialization").
- [x] T022 Add `tests/ButtonPanelTester.Tests/Property/ContentHashTests.fs` — FsCheck properties: same input bytes ⇒ same hex hash; differing bytes ⇒ differing hash; output always matches `^[0-9a-f]{64}$` (covers `plan.md` Principle II ② "ContentHash").
- [x] T023 Add `tests/ButtonPanelTester.Tests/Property/FetchFailureReasonClosureTests.fs` — FsCheck exhaustion property: for every `FetchFailureReason` value, an exhaustive pattern match returns a non-null label string (covers Principle II ③ "FetchFailureReasonClosure"; pairs with the Lean theorem in T025).
- [x] T024 [P] Add `lean/Stem/ButtonPanelTester/Phase1/DictionarySource.lean` — define `CacheOrigin`, `DictionarySource`; prove `theorem source_data_preserved`: a `Live → Cached` re-label preserves the in-memory dictionary value. No `sorry`, no custom axioms (Constitution Principle I).
- [x] T025 [P] Add `lean/Stem/ButtonPanelTester/Phase1/FetchFailureReason.lean` — define the eight-case closed `inductive FetchFailureReason` (matching T013); prove `theorem failure_reason_exhaustion`: every observable HTTP / network / cache outcome maps to exactly one variant.
- [x] T026 [P] Add `lean/Stem/ButtonPanelTester/Phase1/DictionaryProvider.lean` — abstract port spec; prove `theorem provider_success_xor_failed`: every `IDictionaryProvider.FetchAsync` invocation returns exactly one of `Success` or `Failed`.
- [x] T027 [P] Add `lean/Stem/ButtonPanelTester/Phase1/CacheConsistency.lean` — operational model of cache-and-memory-in-lockstep; prove `theorem cache_memory_equal_post_first_success`: after the first successful live fetch in a session, the on-disk cache file and the in-memory dictionary are byte-equal at every observable point (mechanises FR-010).
- [x] T028 Delete `tests/ButtonPanelTester.Tests/PlaceholderTests.fs` and remove its `<Compile>` entry from `ButtonPanelTester.Tests.fsproj`. By this point T021–T023 supply real test coverage so the test count strictly grows.

**Checkpoint**: `dotnet build -c Release` green; `dotnet test` green (≥ three property suites passing); `lake build` green inside `lean/` (four theorems compile with no `sorry`). Working tree is the design substrate for all three user stories.

---

## Phase 3: User Story 1 — Status row + seed extraction (Priority: P1) 🎯 MVP

**Goal**: technician launches the tool on a fresh, offline machine and within 1 s sees a colour-coded status row reading `Cached · last synced <seed build date>` whose detail affordance discloses the seed origin and source path.

**Independent Test** (from `spec.md` §US1): launch on a freshly-installed machine with no internet access. Verify the status row is populated within 1 second of the window appearing with a meaningful headline (e.g. `Cached · last synced 2026-04-15`) and that the detail view explains the origin of the data.

### Implementation for User Story 1

- [x] T029 [US1] Add `src/ButtonPanelTester.Infrastructure/Persistence/JsonFileDictionaryCache.fs` implementing `IDictionaryCache` per `contracts/cache-format.md`: atomic temp+rename writes, SHA-256 sidecar (64 hex + LF), `ExistsAsync`, `ReadAsync` returning `Failed(CacheAbsent | CacheUnreadable, …)` on missing-pair or sidecar mismatch, skip-write optimisation when content hash matches. Register in `ButtonPanelTester.Infrastructure.fsproj`.
- [x] T030 [US1] Add `src/ButtonPanelTester.GUI/Assets/dictionary.seed.json` — initial seed payload (one panel type, a handful of variables, top-level `"seededAt": "<ISO 8601 UTC>"`) wired as `<EmbeddedResource Include="Assets/dictionary.seed.json" />` in `ButtonPanelTester.GUI.fsproj`. Replaced by `eng/refresh-seed.ps1` at release time (T061).
- [x] T031 [US1] Add `src/ButtonPanelTester.Infrastructure/Persistence/EmbeddedSeedExtractor.fs` per `research.md` R4: reads the manifest resource `Stem.ButtonPanelTester.GUI.Assets.dictionary.seed.json` from the GUI assembly via `Assembly.GetManifestResourceStream`, writes through `JsonFileDictionaryCache.WriteAsync` so the same atomic path applies, no-op when the cache file already exists.
- [x] T032 [P] [US1] Add `src/ButtonPanelTester.Services/Dictionary/DictionaryService.fs` — minimal implementation of `IDictionaryService` covering the offline path: `InitializeAsync` calls `ExtractSeedIfMissingAsync` then `ReadAsync`, emits `SourceChanged Cached(t, FromEmbeddedSeed | FromLocalFile, None)` and returns `Updated`. `RefreshAsync` is a `notSupported` placeholder stub for now (US3 fills it in at T052). Register in `ButtonPanelTester.Services.fsproj`.
- [x] T033 [US1] Add `src/ButtonPanelTester.GUI/Composition/CompositionRoot.fs` — Microsoft.Extensions.DI wiring per `contracts/ports.md` §Composition root. For US1: `IClock = SystemClock`, `IDictionaryCache = JsonFileDictionaryCache`, `IDictionaryService = DictionaryService`. `IDictionaryProvider` and `IRegistrationClient` are bound to no-op fakes that always return `Failed(NetworkUnreachable, None)` / `Error(RegistrationNetwork NetworkUnreachable)` so offline launch succeeds. Real adapters land in US2 (T045) and US3 (T053). Register in `ButtonPanelTester.GUI.fsproj`.
- [x] T034 [US1] Add `src/ButtonPanelTester.GUI/Dictionary/DictionaryStatusRow.fs` — FuncUI view rendering the four observable parts of `DictionarySource`: a colour-coded indicator pill (green for `Live`, orange for `Cached`), a headline `Live · synced HH:MM` or `Cached · last synced <date>`, and a click-or-hover detail affordance showing the cache path and (when `Cached`) the `CacheOrigin` label "from embedded seed" / "from local copy" and any `LastFailureReason`. Refresh button + in-flight UX defer to US3 (T054).
- [x] T035 [US1] Add `src/ButtonPanelTester.GUI/App.fs` and `src/ButtonPanelTester.GUI/Program.fs` — FuncUI app shell hosting the main window with the status row docked at top; `Program.main` builds the host via `Microsoft.Extensions.Hosting`-style composition root (T033), starts the Avalonia app, and calls `IDictionaryService.InitializeAsync` on window-loaded so the status row populates before the first paint completes (FR-004 / SC-001 / SC-002).

### Tests for User Story 1

- [x] T036 [P] [US1] Add `tests/ButtonPanelTester.Tests.Windows/Unit/JsonFileDictionaryCacheTests.fs` — `[<Fact>]`s covering: read returns `Failed(CacheAbsent, …)` when files are missing; read returns `Failed(CacheUnreadable, …)` when sidecar hash does not match; write+read round-trip preserves `ButtonPanelDictionary` and `FetchedAt`; concurrent write+kill (simulated via temp file pre-existence) leaves the cache in a `Failed(CacheUnreadable, …)` readable state, not a torn state; `ExtractSeedIfMissingAsync` is a no-op when cache already exists. Lives in `Tests.Windows` (`net10.0-windows`) per #76 — the cache adapter sits in the `net10.0-windows` Infrastructure project.
- [x] T037 [P] [US1] Add `tests/ButtonPanelTester.Tests/Integration/DictionaryServiceInitializeTests.fs` — wires `DictionaryService` through `InMemoryDictionaryCache` (seeded via `SeedWith`) and `FrozenClock`. Cases: (a) empty disk + available seed ⇒ `Updated` with `Cached(seedTime, FromEmbeddedSeed, None)`; (b) pre-existing cache from prior session ⇒ `Updated` with `Cached(t, FromLocalFile, None)`; (c) cache integrity failure ⇒ falls back to seed and emits `Cached(seedTime, FromEmbeddedSeed, Some CacheUnreadable)` (FR-019).
- [x] T038 [P] [US1] Add `tests/ButtonPanelTester.Tests.Windows/Gui/DictionaryStatusRowTests.fs` — `Avalonia.Headless.XUnit` tests driving the FuncUI message loop. Cases: status row renders orange indicator + `Cached · last synced …` headline when `SourceChanged` emits `Cached(_, FromEmbeddedSeed, None)`; detail affordance text contains the literal "from embedded seed"; renders green + `Live · synced HH:MM` for `Live` source. Lives in `Tests.Windows` (`net10.0-windows`) per #76 — Avalonia headless tests bind to the `net10.0-windows` GUI project.
- [x] T039 [P] [US1] Add `tests/ButtonPanelTester.Tests/Property/DictionaryServiceTransitionsTests.fs` — FsCheck property: starting from any reachable `DictionarySource`, applying any `DictionaryFetchResult` lands in another reachable state per the Lean spec in T024 + T027 (covers `plan.md` Principle II ④ "DictionaryServiceTransitions").

**Checkpoint (US1)**: `dotnet run --project src/ButtonPanelTester.GUI` on a machine with no `%LOCALAPPDATA%\Stem.ButtonPanelTester\dictionary.json` and no network connectivity shows the main window with the status row populated as `Cached · last synced <seed date>` within 1 s of paint, and the detail affordance discloses "from embedded seed". `dotnet test` adds ≥ 4 passing test files. US1 is independently demoable.

---

## Phase 4: User Story 2 — Registration ceremony (Priority: P2)

**Goal**: on a freshly-installed machine with no credential file, a modal `Register your tool` dialog appears on launch, accepts a pasted bootstrap token, exchanges it via `POST /register` for an installation credential, persists the credential under DPAPI, and never reopens on subsequent launches (FR-014 – FR-017).

**Independent Test** (from `spec.md` §US2): on a freshly-installed machine launch the tool, observe the registration dialog appears blocking, paste a known-valid bootstrap token, submit, and verify the dialog closes and `credential.dpapi` is written to `%LOCALAPPDATA%\Stem.ButtonPanelTester\` (existence check only — contents must not be readable in plain text).

### Implementation for User Story 2

- [x] T040 [US2] Add `src/ButtonPanelTester.Infrastructure/Persistence/DpapiCredentialStore.fs` implementing `ICredentialStore` per `contracts/credential-format.md`: `Protect`/`Unprotect` with `DataProtectionScope.CurrentUser` and `optionalEntropy: null`, atomic temp+rename `SaveAsync`, idempotent `DeleteAsync`, `CryptographicException` on `LoadAsync` is logged at `Warning` and surfaces as `None`. Register in `ButtonPanelTester.Infrastructure.fsproj`.
- [x] T041 [US2] Add `src/ButtonPanelTester.Infrastructure/Http/HttpRegistrationClient.fs` implementing `IRegistrationClient` per `contracts/registration-api.md`: `POST /register` with `{ "bootstrapToken": ... }`, 10 s timeout, header `User-Agent: Stem.ButtonPanelTester/<assemblyVersion>`, response-status mapping `200 → Ok`, `400/409 → TokenInvalid`, other 4xx + 5xx → `RegistrationServerError httpStatus`, network errors → `RegistrationNetwork NetworkUnreachable | Timeout`. Takes `HttpClient` + `IOptions<DictionaryOptions>` via DI.
- [x] T042 [US2] Add `src/ButtonPanelTester.GUI/Dictionary/RegistrationDialog.fs` per `research.md` R7: FuncUI Elmish window with `Model = { Token: string; State: Idle | Submitting | Failed of string }` and three messages `TokenChanged`, `Submit`, `RegistrationCompleted of Result<InstallationCredential, RegistrationError>`. `Submit` dispatches `IRegistrationClient.RegisterAsync` via `Cmd.OfTask`; on `Ok` calls `ICredentialStore.SaveAsync` then `window.Close()`. Inline error text per the `RegistrationError → message` table in `contracts/registration-api.md`. Hosted via `Window.ShowDialog(MainWindow)`.
- [x] T043 [US2] Extend `src/ButtonPanelTester.GUI/App.fs` so window-loaded checks `ICredentialStore.ExistsAsync`: if `false`, opens `RegistrationDialog` modally and blocks user interaction with the main window until it closes (FR-014). If the dialog is dismissed without a successful registration the tool continues with the seeded dictionary already loaded by US1 (edge case "No credential, no network" in `spec.md`). On subsequent launches with a credential present, no dialog opens (FR-017). Orchestration helper extracted to `src/ButtonPanelTester.Services/Registration/App.fs` so T048 exercises it from `net10.0`.
- [x] T044 [US2] Extend `src/ButtonPanelTester.GUI/Composition/CompositionRoot.fs` — replace the US1 no-op `ICredentialStore` and `IRegistrationClient` bindings with `DpapiCredentialStore` and `HttpRegistrationClient`; register `IHttpClientFactory` (`services.AddHttpClient()`), `services.AddLogging()`, and `IOptions<DictionaryOptions>` bound to `Dictionary` section of `appsettings.json`.

### Tests for User Story 2

- [x] T045 [P] [US2] Add `tests/ButtonPanelTester.Tests.Windows/Unit/DpapiCredentialStoreTests.fs` — `[<Fact>]`s: save then load returns the same `InstallationCredential`; load with no file returns `None`; load after writing a tampered ciphertext returns `None` and emits a warning log entry; delete is idempotent. Lives in `Tests.Windows` (`net10.0-windows`) per #76 — the DPAPI adapter sits in the `net10.0-windows` Infrastructure project. Folded with T040.
- [x] T046 [P] [US2] Add `tests/ButtonPanelTester.Tests.Windows/Integration/HttpRegistrationClientTests.fs` — feed a stubbed `HttpMessageHandler` scripted with each `registration-api.md` response: 200 with `apiCredential` → `Ok credential`; 400 → `Error TokenInvalid`; 409 → `Error TokenInvalid`; 503 → `Error (RegistrationServerError 503)`; non-status network failure → `Error (RegistrationNetwork NetworkUnreachable)`; `TaskCanceledException` due to client timeout → `Error (RegistrationNetwork Timeout)`. Asserts request body is `{ "bootstrapToken": "<value>" }` and **no** `X-Api-Key` header is sent. Lives in `Tests.Windows` per #76 — the HTTP client sits in the `net10.0-windows` Infrastructure project alongside DPAPI. Folded with T041.
- [x] T047 [P] [US2] Add `tests/ButtonPanelTester.Tests.Windows/Gui/RegistrationDialogTests.fs` — `Avalonia.Headless.XUnit` drives the FuncUI message loop. Cases: typing into the token field updates `Model.Token`; clicking `Submit` with empty input is a no-op (validated by `BootstrapToken.TryCreate`); clicking `Submit` with a valid token dispatches `RegisterAsync` via an `InMemoryRegistrationClient` and on `Ok` the window closes; on `Error TokenInvalid` the dialog stays open with the inline-error string from the `registration-api.md` mapping and focus returns to the token field. Lives in `Tests.Windows` per #76 — the GUI project is `net10.0-windows`. Folded with T042.
- [x] T048 [P] [US2] Add `tests/ButtonPanelTester.Tests/Integration/RegistrationFlowTests.fs` — wires the extracted `App.tryRegister` orchestration through `InMemoryCredentialStore` + `InMemoryRegistrationClient` (no real Avalonia window). Cases: empty store on launch → `App.tryRegister` returns `RegistrationOutcome.Completed credential` and store now contains it; non-empty store on launch → `App.tryRegister` returns `RegistrationOutcome.Skipped`; dialog dismissed without success → store stays empty and main window proceeds with seeded data. Stays in `tests/ButtonPanelTester.Tests/` (`net10.0`) because the orchestration helper extracts to `Services` (`net10.0`), not GUI.

**Checkpoint (US2)**: launching against a clean `%LOCALAPPDATA%\Stem.ButtonPanelTester\` directory blocks on the registration dialog; pasting the dev key `STEM-BT-DEV-KEY-2026` (`quickstart.md` §3) closes the dialog and creates `credential.dpapi`; relaunching skips the dialog. `dotnet test` adds ≥ 4 passing test files. US2 is independently demoable.

---

## Phase 5: User Story 3 — Manual refresh (Priority: P3)

**Goal**: a Refresh control in the status row re-fetches the dictionary via `GET /api/dictionaries/{id}/resolved` with `X-Api-Key`. Successful fetches advance the timestamp and overwrite the local copy when content differs; failures preserve in-memory state and surface a typed failure mode; concurrent refresh clicks coalesce to one HTTP call (FR-006 – FR-013).

**Independent Test** (from `spec.md` §US3): with the tool registered and showing `Live`, click Refresh while the service is reachable — verify an in-flight indicator appears and the row settles to `Live` with a newer timestamp. Repeat with the service unreachable — verify the row settles to `Cached` with a failure-reason chip and the in-memory dictionary is unchanged.

### Implementation for User Story 3

- [x] T049 [US3] Add `src/ButtonPanelTester.Infrastructure/Http/HttpDictionaryProvider.fs` implementing `IDictionaryProvider` per `contracts/dictionary-api.md`: `GET /api/dictionaries/{id}/resolved` with `X-Api-Key: <credential>` header sourced from `ICredentialStore.LoadAsync`, 10 s timeout, no retries. Response-status mapping `200 → Success(dict, IClock.UtcNow())`; `400 → Failed(MalformedPayload, …)`; `401 → Failed(Unauthorized, …)`; `404 → Failed(NotFound, …)`; other 5xx → `Failed(ServerError, …)`; `TaskCanceledException` → `Failed(Timeout, …)`; `HttpRequestException` → `Failed(NetworkUnreachable, …)`; deserialisation failure → `Failed(MalformedPayload, ex.Message)`. Computes `ContentHash` over the canonicalised JSON of the deserialised `ButtonPanelDictionary` (per `research.md` R3). Folded with T054.
- [x] T050 [US3] Extend `src/ButtonPanelTester.Services/Dictionary/DictionaryService.fs` — implement `RefreshAsync` per `research.md` R5: a `lock`-guarded `inFlight: TaskCompletionSource<DictionaryStateUpdate> voption` so concurrent callers observe the same task (FR-007). On `Success`: compare new `ContentHash` to current in-memory dictionary's hash; if different, write through `IDictionaryCache.WriteAsync`; emit `SourceChanged Live(fetchedAt)`. On `Failed`: keep in-memory dictionary, emit `SourceChanged Cached(previousFetchedAt, FromLocalFile, Some reason)` (re-label only — FR-011, FR-012). Identical-content fetches skip the cache write but still emit `Live(now)` (covers `cache-format.md` "Skip-write optimisation" + edge case "Successful fetch returns identical data"). Folded with T055.
- [x] T051 [US3] Extend `src/ButtonPanelTester.GUI/Composition/CompositionRoot.fs` — replace the US1 no-op `IDictionaryProvider` binding with `HttpDictionaryProvider`; register an `HttpClient` named `Dictionary` with `BaseAddress = options.Dictionary.BaseUrl` and a `DelegatingHandler` that injects `X-Api-Key` from `ICredentialStore` on every request.
- [x] T052 [US3] Extend `src/ButtonPanelTester.GUI/Dictionary/DictionaryStatusRow.fs` — add the **Refresh** button (FR-006); add the in-flight UX (reduced indicator opacity, spinner glyph on the button, trailing `… refreshing` ellipsis on the headline) per `research.md` R8 (the 800 ms `Animation` pulsing is a polish follow-up; the headless test surface is a deterministic state); add the **Re-register** affordance inside the detail panel that appears only when `LastFailureReason = Some Unauthorized` (FR-018; re-opens `RegistrationDialogWindow` from T042 without deleting the existing credential — `research.md` R11, atomic server-side rotation via `stem-dictionaries-manager` v0.8.0 #74).
- [x] T053 [US3] Extend `src/ButtonPanelTester.GUI/Dictionary/DictionaryStatusRow.fs` — add the muted-yellow seed-staleness advisory glyph next to the headline when `DictionarySource = Cached(t, FromEmbeddedSeed, _)` and `IClock.UtcNow() - t > TimeSpan.FromDays 90.0` (`research.md` R9). Tooltip: "Last refreshed by STEM YYYY-MM-DD; update via Refresh when network is available." No hard block. Folded with T056.

### Tests for User Story 3

- [x] T054 [P] [US3] Add `tests/ButtonPanelTester.Tests.Windows/Integration/HttpDictionaryProviderTests.fs` — stubbed `HttpMessageHandler` (wrapped in `ApiKeyAuthHandler`) exercises each `dictionary-api.md` outcome: 200 → `Success` (using `Fixtures/DictionaryResolvedDto.json` from T020, `<Content Include … Link>` shared from the Tests project); 400 → `Failed(MalformedPayload, …)`; 401 → `Failed(Unauthorized, …)`; 404 → `Failed(NotFound, None)`; 503 → `Failed(ServerError, …)`; 200 with truncated body → `Failed(MalformedPayload, …)`; client timeout → `Failed(Timeout, …)`; `HttpRequestException` → `Failed(NetworkUnreachable, …)`. Asserts the request carries `X-Api-Key: <credential>` and `Accept: application/json` and omits X-Api-Key when no credential is present. Lives in `Tests.Windows` per #76 — the producer ships in the net10.0-windows Infrastructure project. Folded with T049.
- [x] T055 [P] [US3] Add `tests/ButtonPanelTester.Tests/Integration/DictionaryServiceRefreshTests.fs` — `DictionaryService` wired through `InMemoryDictionaryProvider` (scripted result sequences), `InMemoryDictionaryCache`, `FrozenClock`. Cases: two concurrent `RefreshAsync` calls observe the same in-flight TCS and call the provider exactly once (coalescing — FR-007); failed refresh preserves in-memory dictionary byte-for-byte and previous `FetchedAt` (FR-011, FR-012, SC-007); identical-content success skips the cache write (asserted via `InMemoryDictionaryCache.WriteCount`) but emits `Live(now)`; differing-content success writes the cache before emitting `Live(now)` (FR-009, FR-010); 401 surfaces as `Cached(_, _, Some Unauthorized)` and leaves the credential file untouched. Folded with T050.
- [x] T056 [P] [US3] Add `tests/ButtonPanelTester.Tests.Windows/Gui/DictionaryStatusRowRefreshTests.fs` — `Avalonia.Headless.XUnit`. Cases: clicking Refresh raises the expected callback and the in-flight UX (reduced opacity + spinner glyph + ellipsis on headline) becomes visible; on the in-flight task resolving `Live` the row settles to green; on `Failed Unauthorized` the row settles orange and the Re-register button is present; clicking Re-register raises its callback; non-Unauthorized sources do not show the Re-register button; when `Cached(_, FromEmbeddedSeed, _)` and seed `seededAt` is older than 90 days the stale-glyph element is rendered; younger seed and `FromLocalFile` sources do not render it. Lives in `Tests.Windows` per #76 — the producer ships in the net10.0-windows GUI project. Folded with T053.
- [x] T057 [P] [US3] Add `tests/ButtonPanelTester.Tests/Property/CacheConsistencyTests.fs` — FsCheck property mirroring `CacheConsistency.lean` (T027): for any sequence of fetch outcomes where at least one is `Success`, after the first `Success` the on-disk JSON's `ContentHash` equals the in-memory `ButtonPanelDictionary.ContentHash` at every observable point (FR-010, SC-007).

**Checkpoint (US3)**: with the dev `stem-dictionaries-manager` running, the tool refreshes successfully and the row flips `Cached → Live`. Stopping the service and clicking Refresh settles the row to `Cached · last synced … · refresh failed (server unavailable)` with the in-memory dictionary unchanged. Double-clicking Refresh fires only one HTTP request. `dotnet test` adds ≥ 4 passing test files. US3 is independently demoable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: release-hardening tasks that do not belong to a single story.

- [x] T058 Add `eng/refresh-seed.ps1` — PowerShell 7 script that reads `$env:STEM_DICT_KEY`, calls `GET {BaseUrl}/api/dictionaries/{id}/resolved` against the production URL, normalises the response, stamps top-level `"seededAt": "<ISO 8601 UTC>"`, and writes `src/ButtonPanelTester.GUI/Assets/dictionary.seed.json`. Header comment documents prereqs (`research.md` "Open follow-ups"). No secret-management — the operator supplies the key per invocation.
- [x] T059 [P] Add the feat/001 entry to `CHANGELOG.md` under `[Unreleased]` — one line summarising "Dictionary fetch with status row, registration ceremony, and manual refresh".
- [x] T060 [P] Update `README.md` — link to `specs/001-fetch-dictionary/quickstart.md`; one-paragraph mention of the dictionary status row and registration flow as the user-visible surface.
- [x] T061 [P] Add XML doc comments to every public type and member listed in `data-model.md` §1 and §2 per the COMMENTS standard — `Variable`, `PanelType`, `ButtonPanelDictionary`, `CacheOrigin`, `DictionarySource`, `FetchFailureReason`, `DictionaryFetchResult`, `BootstrapToken`, `InstallationCredential`, `RegistrationError`, all five port interfaces, `DictionaryStateUpdate`, `IDictionaryService`.
- [x] T062 [P] Logging audit per the LOGGING standard — confirm every adapter and the `DictionaryService` use `ILogger<T>` via DI, no `Console.WriteLine`, no string-interpolation in log messages (parameterised templates only), no `BootstrapToken.Value` or `InstallationCredential.Value` appears in any log statement at any verbosity level (`contracts/credential-format.md` "Logging" section).
- [x] T063 [P] Compliance check for FR-020: grep the HTTP layer for any field that could carry machine name, OS user, machine identifier, MAC, or SID. Expected zero hits — the wire surface is `Dictionary:Id` (URL) + `X-Api-Key` (header) only. Document the audit result in a one-line `# Compliance` note inside `specs/001-fetch-dictionary/quickstart.md` "Troubleshooting" tail.
- [x] T064 Run `eng/refresh-seed.ps1` once against a known-good dictionary service to generate a real `dictionary.seed.json` for the release; commit the refreshed seed.
- [ ] T065 End-to-end validation: walk `quickstart.md` §1–§9 on a freshly-provisioned machine; verify SC-001 (status row within 1 s on launch), SC-002 (cold-start usable without network), SC-003 (registration end-to-end < 30 s), SC-004 (failed refresh surfaced < 12 s), SC-006 (no re-prompts during a 4-hour session), SC-007 (failed refresh has zero effect on in-memory dictionary). SC-005 is a usability metric and is not directly testable from CI — flagged as a follow-up for supplier-side observation.
- [x] T066 `cd lean && lake build` — confirm all four Phase 1 theorems still compile with no `sorry` and no custom axioms after every preceding task (Constitution Principle I gate).

**Checkpoint (release-ready)**: all 66 tasks complete; `dotnet build -c Release` green; `dotnet test` green across Unit / Property / Integration / Gui partitions; `lake build` green; quickstart end-to-end signed off on a fresh machine.

---

## Phase 7: Cold-start tolerance (post-release hardening)

**Purpose**: absorb Azure App Service Free-tier cold-start latency in the two production HTTP adapters and the status-row UX, per [`phases/phase-7.md`](phases/phase-7.md) and issue [#92](https://github.com/luca-veronelli-stem/button-panel-tester/issues/92).

- [x] T067 Slice 1 — raise the client-side timeout in `HttpDictionaryProvider` and `HttpRegistrationClient` from 10 s to 90 s by lifting the literal to a `static member val TimeoutSeconds = 90.0` on each adapter; add a `ColdStartHint` named `TextBlock` to `DictionaryStatusRow.view` rendered only while `RefreshState = Refreshing` with the text "This may take up to a minute if the service has been idle."; amend `spec.md` SC-004 to distinguish warm-state (<12 s) from cold-start (<90 s); update both `contracts/dictionary-api.md` and `contracts/registration-api.md` §"Timeout and retries" from `10 s` to `90 s` and cross-reference `phases/phase-7.md`; update the `quickstart.md` Troubleshooting tail with the cold-start note; refresh the inline XML doc strings on the two adapters' "client timeout" rows. Tests: new `HttpDictionaryProviderTimeoutTests` / `HttpRegistrationClientTimeoutTests` asserting `TimeoutSeconds = 90.0`; new `DictionaryStatusRowTests` cases asserting the `ColdStartHint` element is absent when `Idle` and present with the documented text when `Refreshing`. Files: `src/ButtonPanelTester.Infrastructure/Http/HttpDictionaryProvider.fs`, `src/ButtonPanelTester.Infrastructure/Http/HttpRegistrationClient.fs`, `src/ButtonPanelTester.GUI/Dictionary/DictionaryStatusRow.fs`, `specs/001-fetch-dictionary/spec.md`, `specs/001-fetch-dictionary/contracts/dictionary-api.md`, `specs/001-fetch-dictionary/contracts/registration-api.md`, `specs/001-fetch-dictionary/quickstart.md`, `tests/ButtonPanelTester.Tests.Windows/Integration/HttpDictionaryProviderTimeoutTests.fs`, `tests/ButtonPanelTester.Tests.Windows/Integration/HttpRegistrationClientTimeoutTests.fs`, `tests/ButtonPanelTester.Tests.Windows/Gui/DictionaryStatusRowTests.fs`.
- [x] T068 Slice 2 — initial warm-up via `IDictionaryProvider.FetchAsync` in a `Services.Dictionary.WarmUp` module + fire-and-forget invocation from `App.fs MainWindow.Opened` + unit tests. Reworked in T069: review surfaced that hitting the authenticated `/api/dictionaries/{id}/resolved` endpoint produces a spurious 401 on unregistered installs and that the F# module shape violates the LOGGING standard's `ILogger<T>` requirement. The slice-2 commit is preserved in history as `feat(001/T068)` (commit `1db68ac`); the file `src/ButtonPanelTester.Services/Dictionary/WarmUp.fs` and its tests are rewritten in T069.
- [x] T069 Slice 3 — rework the warm-up to hit the unauthenticated `GET /health` endpoint and convert it to the canonical archetype-A class shape: add `IDictionaryServiceWarmUp` port to `src/ButtonPanelTester.Core/Dictionary/Ports.fs`; add `src/ButtonPanelTester.Infrastructure/Http/HttpDictionaryServiceWarmUp.fs` adapter (F# class, `ILogger<HttpDictionaryServiceWarmUp>`, 90 s timeout uniform with the other adapters, reuses the named `"Dictionary"` `HttpClient` — `ApiKeyAuthHandler` is harmless on `/health` per the server's unauth allow-list); convert `src/ButtonPanelTester.Services/Dictionary/WarmUp.fs` from a module to a `DictionaryWarmUp` class with `ILogger<DictionaryWarmUp>` (orchestration policy unchanged: Stopwatch, log Information on success, swallow non-cancellation, propagate cancellation); update `CompositionRoot.fs` bindings; update `App.fs` to resolve `DictionaryWarmUp` from DI and call `RunAsync`. Tests: new `tests/ButtonPanelTester.Tests.Windows/Integration/HttpDictionaryServiceWarmUpTests.fs` exercising the adapter against a stubbed `HttpMessageHandler` (GET issued to `/health`, success/non-200/exception/timeout paths); refactor `tests/ButtonPanelTester.Tests/Unit/WarmUpTests.fs` to use a stub `IDictionaryServiceWarmUp` and assert the same three orchestration contracts (called once, swallow non-cancellation, propagate cancellation) on the new class. Files: `src/ButtonPanelTester.Core/Dictionary/Ports.fs`, `src/ButtonPanelTester.Infrastructure/Http/HttpDictionaryServiceWarmUp.fs`, `src/ButtonPanelTester.Infrastructure/ButtonPanelTester.Infrastructure.fsproj`, `src/ButtonPanelTester.Services/Dictionary/WarmUp.fs`, `src/ButtonPanelTester.GUI/Composition/CompositionRoot.fs`, `src/ButtonPanelTester.GUI/App.fs`, `tests/ButtonPanelTester.Tests/Unit/WarmUpTests.fs`, `tests/ButtonPanelTester.Tests.Windows/Integration/HttpDictionaryServiceWarmUpTests.fs`, `tests/ButtonPanelTester.Tests.Windows/ButtonPanelTester.Tests.Windows.fsproj`.
- [x] T070 Slice 4 — mirror slice 1's `ColdStartHint` in the registration dialog so a cold-boot `POST /register` surfaces the same explanation as a cold-boot Refresh. Add a `ColdStartHint` named `TextBlock` to `src/ButtonPanelTester.GUI/Dictionary/RegistrationDialog.fs` rendered only when the FuncUI Elmish model is in the `Submitting` state, with the documented text "This may take up to a minute if the service has been idle." (locale-invariant, identical to the `DictionaryStatusRow` hint). Tests: new `[<AvaloniaFact>]`s in `tests/ButtonPanelTester.Tests.Windows/Gui/RegistrationDialogTests.fs` asserting the hint is absent in `Idle` and `Failed` model states and present with the documented text in `Submitting`. Files: `src/ButtonPanelTester.GUI/Dictionary/RegistrationDialog.fs`, `tests/ButtonPanelTester.Tests.Windows/Gui/RegistrationDialogTests.fs`.

**Checkpoint (Phase 7 complete)**: T067 + T068 + T069 + T070 ticked; `dotnet build -c Release` green; `dotnet test` green across both TFM legs; `dotnet format --verify-no-changes` green; cold-start probe re-run against the production service is an operator follow-up tracked outside this PR.

---

## Dependencies & Execution Order

### Phase dependencies

- **Phase 1 (Setup)**: no dependencies — can start immediately.
- **Phase 2 (Foundational)**: depends on Phase 1 — blocks **all** user stories.
- **Phase 3 (US1, P1)**: depends on Phase 2 — independently testable and demoable on completion.
- **Phase 4 (US2, P2)**: depends on Phase 2; logically follows US1 because the registration dialog ships inside the same app shell US1 introduces (T035), but US2 does **not** depend on US1's offline-cache behaviour — the dialog and DPAPI store are orthogonal.
- **Phase 5 (US3, P3)**: depends on Phase 2 and US1's `DictionaryService` skeleton (T032). US3 also benefits from US2 being complete because the "Re-register" affordance (T052) re-opens the dialog from T042 — but US3 itself can ship before US2 if the re-register affordance is temporarily disabled (one toggle).
- **Phase 6 (Polish)**: depends on whichever user stories shipped — most polish tasks tolerate partial completion (e.g. CHANGELOG entry can list only US1 if shipping early).

### Within-story dependencies

- **US1**: T029 (cache) → T031 (seed extractor uses cache) → T032 (service uses both) → T033 (composition wires service) → T035 (app calls service). T034 (status row view) is independent of cache plumbing and can run alongside T032 in time. Tests T036–T039 land **after** their respective implementations (no TDD-first ordering imposed — tests run against finished adapters).
- **US2**: T040 (DPAPI store) + T041 (registration client) are parallelisable (different files, different concerns). T042 (dialog) depends on T041. T043 (app startup logic) depends on T040 + T042. T044 (composition) replaces US1's no-op bindings with real adapters. Tests T045–T048 follow.
- **US3**: T049 (provider) and T050 (refresh in service) are parallelisable in principle but share semantics — recommend T049 first to fix the wire shape, then T050. T051 (composition) replaces US1's no-op provider. T052 + T053 extend the status row view. Tests T054–T057 follow.

### Parallel opportunities

- All four Lean tasks (T024–T027) run in parallel with each other and with the F# foundational tasks — Lean and F# share no toolchain.
- The five in-memory test fakes (T019) and the JSON fixture (T020) are independent.
- Per-story test files (T036–T039 for US1; T045–T048 for US2; T054–T057 for US3) sit in different files in the test project and can be authored in parallel, with the caveat that the test `.fsproj` orders `<Compile>` entries — coordinate fsproj edits if writing genuinely concurrently.

### Parallel example: User Story 1 tests

```text
# After T029–T035 land, the four US1 test files are independent:
Task: T036 — JsonFileDictionaryCacheTests.fs
Task: T037 — DictionaryServiceInitializeTests.fs
Task: T038 — DictionaryStatusRowTests.fs
Task: T039 — DictionaryServiceTransitionsTests.fs
```

---

## Implementation Strategy

### MVP first (US1 only)

1. Complete Phase 1 (T001–T009): scaffolding lands; `dotnet build` green.
2. Complete Phase 2 (T010–T028): domain types, ports, fakes, property tests, Lean Phase 1 — substrate is on disk and proved.
3. Complete Phase 3 (T029–T039): status row + seed + cache + minimal service + offline composition root.
4. **Stop. Validate against `spec.md` §US1 acceptance scenarios on a fresh machine offline.** Demo if ready.

### Incremental delivery

- After US1: ship internally to confirm the status-row + seed UX. Edge cases ("No credential, no network" — `spec.md` §Edge Cases) already covered.
- Add US2 (T040–T048): bring the registration ceremony. Demo end-to-end with a dev token.
- Add US3 (T049–T057): bring manual refresh and the live state. Demo the full happy path.
- Phase 6 (T058–T066): polish + quickstart validation + Lean re-check before merging.

### Risk-ordered notes

- **Highest-risk task**: T050 (in-flight coalescing in `DictionaryService.RefreshAsync`). The legacy comment in `Services.FSharp/Dictionary/DictionaryService.fs:124-138` documents the subtle TCS ordering — replicate, don't reinvent. Property test T039 + integration test T055 are the safety net.
- **Watch the fsproj `<Compile>` order**: F# is order-sensitive. `FetchFailureReason.fs` (T013) must precede `DictionarySource.fs` (T012) in `Core.fsproj` despite the lower T-number on T012. Bisect-safe rule applies — every commit must compile.
- **Lean `lake build` is `SLOW`**: do not gate every commit on it; the Constitution Principle I check runs in CI and at T066. Locally, prefer `lean_diagnostic_messages` via the lean-lsp MCP for incremental feedback.

---

## Notes

- `[P]` markers are conservative: they apply to tasks in different `.fsproj`s, in Lean (different toolchain), or to non-compiled data files. Tasks within the same project share fsproj edits, so file ordering plus parallel edit conflicts both apply.
- `[Story]` labels (`US1`, `US2`, `US3`) trace each task back to the user story in `spec.md`.
- Every task ends with an absolute file path under the repo root.
- Tests run on every commit; Lean re-builds on every PR per the CI standard.
- Commit-cadence preference (per WIP.md and the user's prior guidance): one commit per phase. Don't bundle phases; don't splinter inside a phase.
- No-attribution rule (`~/.claude/rules/no-attribution.md`): no AI footers in any commit, PR, comment, or file emitted by these tasks.
- The "Action for implementation" note in `contracts/cache-format.md:74` — extend `FetchFailureReason` with `CacheAbsent` and `CacheUnreadable` plus update the Lean exhaustion theorem — is folded into T013 + T025. No standalone task is needed for it.
