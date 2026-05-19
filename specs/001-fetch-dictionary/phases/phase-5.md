# Phase 5 — User Story 3: Manual refresh (P3)

This document scopes the fifth phase-PR for spec 001 (dictionary fetch
and status display). It is the durable anchor for the Phase 5 work
driven by the `resolve-ticket` (unsupervised) protocol.

## Scope

Ship User Story 3: a Refresh control in the status row re-fetches the
dictionary via `GET /api/dictionaries/{id}/resolved` with `X-Api-Key`.
Successful fetches advance the timestamp and overwrite the local copy
when content differs; failures preserve in-memory state and surface a
typed failure mode; concurrent refresh clicks coalesce to one HTTP
call (FR-006 – FR-013). Independent acceptance criterion from
[`../spec.md`](../spec.md) §US3.

Concretely, Phase 5 lands:

- The HTTP dictionary fetch client (`GET
  /api/dictionaries/{Dictionary:Id}/resolved` with `X-Api-Key`
  sourced from `ICredentialStore.LoadAsync`, 10 s timeout, no
  retries; full status-code → `FetchFailureReason` table per
  `contracts/dictionary-api.md`; client-side `ContentHash`
  computed over the canonicalised JSON of the deserialised
  `ButtonPanelDictionary` per `research.md` R3).
- The in-flight refresh coalescing in `DictionaryService.RefreshAsync`
  per `research.md` R5 (`lock`-guarded
  `TaskCompletionSource<DictionaryStateUpdate> voption`; identical-
  content skip-write per `cache-format.md` "Skip-write optimisation";
  `Live → Cached` re-label on transient failure preserving the
  in-memory dictionary byte-for-byte per FR-011, FR-012, SC-007).
- The `CompositionRoot.fs` extension that replaces the US1 no-op
  `IDictionaryProvider` binding with `HttpDictionaryProvider`,
  registering a named `HttpClient` (`"Dictionary"`) with
  `BaseAddress = options.Dictionary.BaseUrl` and a `DelegatingHandler`
  that injects `X-Api-Key` from `ICredentialStore` on every request.
- The `DictionaryStatusRow.fs` extensions: the Refresh button
  (FR-006); the in-flight UX per `research.md` R8 (pulsing indicator
  opacity 0.6↔1.0 over 800 ms, spinner glyph on the button, trailing
  `… refreshing` ellipsis on the headline); the Re-register
  affordance that appears only when `LastFailureReason = Some
  Unauthorized` (FR-018) and re-opens `RegistrationDialogWindow`
  (T042) without deleting the existing credential — atomic
  server-side rotation per `research.md` R11, relying on
  `stem-dictionaries-manager` v0.8.0 atomic re-registration
  ([#74](https://github.com/luca-veronelli-stem/stem-dictionaries-manager/issues/74)).
- The muted-yellow seed-staleness advisory glyph on `Cached(_,
  FromEmbeddedSeed, _)` when `IClock.UtcNow() - seededAt >
  TimeSpan.FromDays 90.0` per `research.md` R9 (no hard block;
  tooltip "Last refreshed by STEM YYYY-MM-DD; update via Refresh
  when network is available.").
- Four test files: integration Windows (HTTP client over a stubbed
  `HttpMessageHandler`), integration (refresh coalescing + skip-
  write + re-label through `InMemoryDictionaryProvider`), GUI
  Windows (status-row refresh + in-flight UX + Re-register +
  stale-glyph via `Avalonia.Headless.XUnit`), and a property test
  mirroring `CacheConsistency.lean` (T027).

No new Lean work, no live-fetch on startup, no automatic background
refresh, no `Dictionary:RefreshIntervalSeconds`-driven timer. Those
either ship in Phase 6 / polish or are explicitly out of scope per
`spec.md`.

## Tasks (T049..T057)

Mapped one-to-one to the task list in [`../tasks.md`](../tasks.md) lines
147..158 and to GitHub issues #56..#64:

- T049 — issue [#56](https://github.com/luca-veronelli-stem/button-panel-tester/issues/56):
  `src/ButtonPanelTester.Infrastructure/Http/HttpDictionaryProvider.fs`
  per `contracts/dictionary-api.md` — `GET
  /api/dictionaries/{id}/resolved` with `X-Api-Key: <credential>`
  header sourced from `ICredentialStore.LoadAsync`, 10 s timeout,
  no retries. Response-status mapping `200 → Success(dict,
  IClock.UtcNow())`; `400 → Failed(MalformedPayload, …)`; `401 →
  Failed(Unauthorized, …)`; `404 → Failed(NotFound, …)`; other 5xx
  → `Failed(ServerError, …)`; `TaskCanceledException` →
  `Failed(Timeout, …)`; `HttpRequestException` →
  `Failed(NetworkUnreachable, …)`; deserialisation failure →
  `Failed(MalformedPayload, ex.Message)`. Computes `ContentHash`
  over the canonicalised JSON of the deserialised
  `ButtonPanelDictionary` (per `research.md` R3).
- T050 — issue [#57](https://github.com/luca-veronelli-stem/button-panel-tester/issues/57):
  extend `src/ButtonPanelTester.Services/Dictionary/DictionaryService.fs` —
  implement `RefreshAsync` per `research.md` R5: a `lock`-guarded
  `inFlight: TaskCompletionSource<DictionaryStateUpdate> voption`
  so concurrent callers observe the same task (FR-007). On
  `Success`: compare new `ContentHash` to current in-memory
  dictionary's hash; if different, write through
  `IDictionaryCache.WriteAsync`; emit `SourceChanged Live(fetchedAt)`.
  On `Failed`: keep in-memory dictionary, emit `SourceChanged
  Cached(previousFetchedAt, FromLocalFile, Some reason)` (re-label
  only — FR-011, FR-012). Identical-content fetches skip the cache
  write but still emit `Live(now)` (covers `cache-format.md`
  "Skip-write optimisation" + edge case "Successful fetch returns
  identical data").
- T051 — issue [#58](https://github.com/luca-veronelli-stem/button-panel-tester/issues/58):
  extend `src/ButtonPanelTester.GUI/Composition/CompositionRoot.fs` —
  replace the US1 no-op `IDictionaryProvider` binding with
  `HttpDictionaryProvider`; register an `HttpClient` named
  `"Dictionary"` with `BaseAddress = options.Dictionary.BaseUrl` and
  a `DelegatingHandler` that injects `X-Api-Key` from
  `ICredentialStore` on every request.
- T052 — issue [#59](https://github.com/luca-veronelli-stem/button-panel-tester/issues/59):
  extend `src/ButtonPanelTester.GUI/Dictionary/DictionaryStatusRow.fs` —
  add the **Refresh** button (FR-006); add the in-flight UX
  (pulsing indicator opacity 0.6↔1.0 over 800 ms, spinner glyph
  on the button, trailing `… refreshing` ellipsis on the
  headline) per `research.md` R8; add the **Re-register**
  affordance inside the detail panel that appears only when
  `LastFailureReason = Some Unauthorized` (FR-018; re-opens
  `RegistrationDialogWindow` from T042 without deleting the
  existing credential — `research.md` R11, leveraging
  `stem-dictionaries-manager` v0.8.0 atomic re-registration #74).
- T053 — issue [#60](https://github.com/luca-veronelli-stem/button-panel-tester/issues/60):
  extend `src/ButtonPanelTester.GUI/Dictionary/DictionaryStatusRow.fs` —
  add the muted-yellow seed-staleness advisory glyph next to the
  headline when `DictionarySource = Cached(t, FromEmbeddedSeed, _)`
  and `IClock.UtcNow() - t > TimeSpan.FromDays 90.0`
  (`research.md` R9). Tooltip: "Last refreshed by STEM
  YYYY-MM-DD; update via Refresh when network is available." No
  hard block.
- T054 — issue [#61](https://github.com/luca-veronelli-stem/button-panel-tester/issues/61):
  `tests/ButtonPanelTester.Tests.Windows/Integration/HttpDictionaryProviderTests.fs` —
  stubbed `HttpMessageHandler` exercises each `dictionary-api.md`
  outcome: 200 → `Success` (using `Fixtures/DictionaryResolvedDto.json`
  from T020); 400 → `Failed(MalformedPayload, …)`; 401 →
  `Failed(Unauthorized, …)`; 404 → `Failed(NotFound, …)`; 503 →
  `Failed(ServerError, …)`; 200 with truncated body →
  `Failed(MalformedPayload, …)`; client timeout →
  `Failed(Timeout, …)`; `HttpRequestException` →
  `Failed(NetworkUnreachable, …)`. Asserts the request carries
  `X-Api-Key: <credential>` and `Accept: application/json`. Lives
  in `Tests.Windows` (`net10.0-windows`) per #76 — the
  HttpDictionaryProvider sits in the `net10.0-windows`
  Infrastructure project alongside DPAPI and HttpRegistrationClient.
- T055 — issue [#62](https://github.com/luca-veronelli-stem/button-panel-tester/issues/62):
  `tests/ButtonPanelTester.Tests/Integration/DictionaryServiceRefreshTests.fs` —
  `DictionaryService` wired through `InMemoryDictionaryProvider`
  (scripted result sequences), `InMemoryDictionaryCache`,
  `FrozenClock`. Cases: two concurrent `RefreshAsync` calls
  dequeue exactly one scripted result (coalescing — FR-007);
  failed refresh preserves in-memory dictionary byte-for-byte and
  previous `FetchedAt` (FR-011, FR-012, SC-007); identical-
  content success skips the cache write (asserted via a
  `WriteCount` extension hook on `InMemoryDictionaryCache`) but
  emits `Live(now)`; differing-content success writes the cache
  before emitting `Live(now)` (FR-009, FR-010); 401 surfaces as
  `Cached(_, _, Some Unauthorized)` and leaves the credential
  file untouched.
- T056 — issue [#63](https://github.com/luca-veronelli-stem/button-panel-tester/issues/63):
  `tests/ButtonPanelTester.Tests.Windows/Gui/DictionaryStatusRowRefreshTests.fs` —
  `Avalonia.Headless.XUnit`. Cases: clicking Refresh raises the
  expected GUI message and the in-flight UX (opacity animation +
  spinner glyph + ellipsis on headline) becomes visible; on the
  in-flight task resolving `Live` the row settles to green; on
  `Failed Unauthorized` the row settles orange and the Re-register
  button is present; when `Cached(_, FromEmbeddedSeed, _)` and
  seed `seededAt` is older than 90 days the stale-glyph element
  is rendered. Lives in `Tests.Windows` (`net10.0-windows`) per
  #76 — the GUI project is `net10.0-windows`. Reuses the
  existing `Tests.Windows/TestApp.fs` `AvaloniaTestApplication`
  harness.
- T057 — issue [#64](https://github.com/luca-veronelli-stem/button-panel-tester/issues/64):
  `tests/ButtonPanelTester.Tests/Property/CacheConsistencyTests.fs` —
  FsCheck property mirroring `CacheConsistency.lean` (T027): for
  any sequence of fetch outcomes where at least one is `Success`,
  after the first `Success` the on-disk JSON's `ContentHash`
  equals the in-memory `ButtonPanelDictionary.ContentHash` at
  every observable point (FR-010, SC-007).

## Exit state

- `dotnet build -c Release` green across all six projects, 0 warnings.
- `dotnet test -c Release --no-build` green, ≥ 4 new passing test
  files added on top of the Phase 4 baseline (T054 Integration
  Windows, T055 Integration, T056 GUI Windows, T057 Property).
- `cd lean && lake build` green — the four Phase 1 theorems still
  elaborate with no `sorry` and no custom axioms (Constitution
  Principle I gate, no new Lean work in Phase 5).
- `dotnet run --project src/ButtonPanelTester.GUI` against a
  registered installation with the dev `stem-dictionaries-manager`
  running on `https://localhost:7065`: clicking Refresh flips the
  status row `Cached → Live · synced HH:MM` within ~1 s; stopping
  the service and clicking Refresh settles the row back to
  `Cached · last synced YYYY-MM-DD · refresh failed (server
  unavailable)` with the in-memory dictionary unchanged; double-
  clicking Refresh fires only one HTTP request (FR-006 – FR-013).
- With a tampered credential file on disk, clicking Refresh
  surfaces `Cached(_, _, Some Unauthorized)`, reveals the
  Re-register button, and a successful re-registration round-trip
  reproduces SC-003 / FR-018 (atomic credential rotation —
  `stem-dictionaries-manager` #74, v0.8.0).

## Cross-TFM test placement

The two-TFM test split established in T005a/b/c per
[issue #76](https://github.com/luca-veronelli-stem/button-panel-tester/issues/76)
governs Phase 5 test placement:

- `tests/ButtonPanelTester.Tests/` (`net10.0`): T055
  (`DictionaryService` over `InMemoryDictionaryProvider` +
  `InMemoryDictionaryCache` + `FrozenClock` — all `net10.0`
  producers), T057 (`CacheConsistency` property over Core types
  and in-memory cache — all `net10.0`).
- `tests/ButtonPanelTester.Tests.Windows/` (`net10.0-windows`):
  T054 (`HttpDictionaryProvider` is in the `net10.0-windows`
  Infrastructure project alongside DPAPI and
  `HttpRegistrationClient`), T056 (`Avalonia.Headless.XUnit`
  binds to the GUI project, reusing the existing `TestApp.fs`
  `AvaloniaTestApplication` harness shipped in Phase 3 T038).

The path strings in [`../tasks.md`](../tasks.md) reflect the pre-#76
single-test-project layout. GitHub issues #61 and #63 still carry
those pre-#76 paths; the work commits land at the corrected paths
and supersede the issue text. T055's path is unchanged because the
service it exercises is `net10.0`; T057's path is unchanged because
it only consumes Core types and the in-memory cache fake.

## Protocol customization

The `resolve-ticket` lifecycle for Phase 5 starts at `WorkRequired`.
The spec, plan, tasks, research, data model, contracts, and quickstart
artifacts for spec 001 already exist on `main`, ratified against
constitution v1.0.2. Re-running speckit-specify / speckit-plan /
speckit-tasks per phase-PR would rewrite shared feature artifacts six
times and is forbidden.

The reviewer's per-commit consistency check is: the durable diff
matches the named T-task in `../tasks.md` and respects constitution
Principles I–VI.

Batched amend policy (inherited from Phase 1): approved titles/bodies
are **not** mechanically amended on each `WorkApproved → WorkRequired`
transition. Worker appends each approved title+body+durable-sha to
`llm/reviews/approved-commits.yml`. Owner applies all approved messages
in one rewrite at `FinalizationRequired`. This keeps the durable-commit
graph stable across the run and contains the message-rewrite blast
radius to a single owner-side operation.

Quality gate `llm/reviews/gate.{ps1,sh}` is reused unchanged from
Phase 4. No new Lean elaboration is added in Phase 5, but `lake build`
stays in the gate so the Principle I check still fires on every
handoff.

Commit cadence: one durable commit per T-task. Single feature branch
`feat/001-phase-5`. One PR encompassing all 9 commits, rebase-merged on
completion.
