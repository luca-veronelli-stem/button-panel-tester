# Phase 4 — User Story 2: Registration ceremony (P2)

This document scopes the fourth phase-PR for spec 001 (dictionary fetch and
status display). It is the durable anchor for the Phase 4 work driven by the
`resolve-ticket` (unsupervised) protocol.

## Scope

Ship User Story 2: on a freshly-installed machine with no credential file, a
modal `Register your tool` dialog appears on launch, accepts a pasted
bootstrap token, exchanges it via `POST /register` for an installation
credential, persists the credential under DPAPI, and never reopens on
subsequent launches. Independent acceptance criterion from
[`../spec.md`](../spec.md) §US2 (FR-014 – FR-017).

Concretely, Phase 4 lands:

- The DPAPI on-disk credential store
  (`DataProtectionScope.CurrentUser`, `optionalEntropy: null`, atomic
  temp+rename writes, idempotent delete,
  `CryptographicException → warn-and-treat-as-absent`).
- The HTTP registration client (`POST /register` with the
  `{ "bootstrapToken": ... }` body and the `User-Agent` /
  `Content-Type` / `Accept` headers from
  `contracts/registration-api.md`; 10 s timeout, no retries; full
  status-code → `RegistrationError` table).
- The Avalonia + FuncUI Elmish registration dialog with the
  three-state model (`Idle | Submitting | Failed`) and the
  `TokenChanged | Submit | RegistrationCompleted` messages described in
  `research.md` R7.
- The `App.fs` extension that opens the dialog modally on
  window-loaded when no credential is present, and the
  `CompositionRoot.fs` extension that replaces the US1 no-op
  `ICredentialStore` / `IRegistrationClient` bindings with the real
  adapters and registers `IHttpClientFactory` + `IOptions<DictionaryOptions>`.
- Four test files: unit (DPAPI store), integration (HTTP client over a
  stubbed `HttpMessageHandler`), GUI (registration dialog via
  `Avalonia.Headless.XUnit`), integration (end-to-end registration
  flow through the extracted orchestration helper).

No live-fetch refresh, no `Re-register` affordance, no in-flight UX,
no `X-Api-Key`-injecting HTTP handler. Those land in Phase 5 (US3 /
T049–T057).

## Tasks (T040..T048)

Mapped one-to-one to the task list in [`../tasks.md`](../tasks.md) lines
122..133 and to GitHub issues #47..#55:

- T040 — issue [#47](https://github.com/luca-veronelli-stem/button-panel-tester/issues/47):
  `src/ButtonPanelTester.Infrastructure/Persistence/DpapiCredentialStore.fs`
  per `contracts/credential-format.md` — `Protect`/`Unprotect` with
  `DataProtectionScope.CurrentUser` and `optionalEntropy: null`, atomic
  temp+rename `SaveAsync`, idempotent `DeleteAsync`,
  `CryptographicException` on `LoadAsync` logged at `Warning` and
  surfaced as `None`.
- T041 — issue [#48](https://github.com/luca-veronelli-stem/button-panel-tester/issues/48):
  `src/ButtonPanelTester.Infrastructure/Http/HttpRegistrationClient.fs`
  per `contracts/registration-api.md` — `POST /register` with
  `{ "bootstrapToken": ... }`, 10 s timeout, header
  `User-Agent: Stem.ButtonPanelTester/<assemblyVersion>`, status mapping
  `200 → Ok`, `400/409 → TokenInvalid`, other 4xx + 5xx →
  `RegistrationServerError httpStatus`, network errors →
  `RegistrationNetwork NetworkUnreachable | Timeout`. Takes `HttpClient`
  + `IOptions<DictionaryOptions>` via DI.
- T042 — issue [#49](https://github.com/luca-veronelli-stem/button-panel-tester/issues/49):
  `src/ButtonPanelTester.GUI/Dictionary/RegistrationDialog.fs` per
  `research.md` R7 — FuncUI Elmish window with
  `Model = { Token: string; State: Idle | Submitting | Failed of string }`
  and three messages `TokenChanged`, `Submit`,
  `RegistrationCompleted of Result<InstallationCredential, RegistrationError>`.
  `Submit` dispatches `IRegistrationClient.RegisterAsync` via
  `Cmd.OfTask`; on `Ok` calls `ICredentialStore.SaveAsync` then
  `window.Close()`. Inline error text per the `RegistrationError →
  message` table in `contracts/registration-api.md`.
- T043 — issue [#50](https://github.com/luca-veronelli-stem/button-panel-tester/issues/50):
  extend `src/ButtonPanelTester.GUI/App.fs` so window-loaded checks
  `ICredentialStore.ExistsAsync`: if `false`, opens `RegistrationDialog`
  modally and blocks user interaction with the main window until it
  closes (FR-014). If the dialog is dismissed without a successful
  registration the tool continues with the seeded dictionary already
  loaded by US1 (edge case "No credential, no network" in `spec.md`).
  On subsequent launches with a credential present, no dialog opens
  (FR-017).
- T044 — issue [#51](https://github.com/luca-veronelli-stem/button-panel-tester/issues/51):
  extend `src/ButtonPanelTester.GUI/Composition/CompositionRoot.fs` —
  replace the US1 no-op `ICredentialStore` and `IRegistrationClient`
  bindings with `DpapiCredentialStore` and `HttpRegistrationClient`;
  register `IHttpClientFactory` (`services.AddHttpClient()`) and
  `IOptions<DictionaryOptions>` bound to the `Dictionary` section of
  `appsettings.json`.
- T045 — issue [#52](https://github.com/luca-veronelli-stem/button-panel-tester/issues/52):
  `tests/ButtonPanelTester.Tests.Windows/Unit/DpapiCredentialStoreTests.fs` —
  `[<Fact>]`s: save then load returns the same `InstallationCredential`;
  load with no file returns `None`; load after writing a tampered
  ciphertext returns `None` and emits a warning log entry; delete is
  idempotent. Lives in `Tests.Windows` (`net10.0-windows`) per #76 —
  the DPAPI adapter sits in the `net10.0-windows` Infrastructure
  project.
- T046 — issue [#53](https://github.com/luca-veronelli-stem/button-panel-tester/issues/53):
  `tests/ButtonPanelTester.Tests.Windows/Integration/HttpRegistrationClientTests.fs` —
  feed a stubbed `HttpMessageHandler` scripted with each
  `registration-api.md` response: 200 → `Ok credential`; 400 / 409 →
  `Error TokenInvalid`; 503 → `Error (RegistrationServerError 503)`;
  network failure → `Error (RegistrationNetwork NetworkUnreachable)`;
  `TaskCanceledException` due to client timeout →
  `Error (RegistrationNetwork Timeout)`. Asserts request body is
  `{ "bootstrapToken": "<value>" }` and **no** `X-Api-Key` header is
  sent. Lives in `Tests.Windows` (`net10.0-windows`) — the registration
  client sits in the `net10.0-windows` Infrastructure project alongside
  DPAPI.
- T047 — issue [#54](https://github.com/luca-veronelli-stem/button-panel-tester/issues/54):
  `tests/ButtonPanelTester.Tests.Windows/Gui/RegistrationDialogTests.fs` —
  `Avalonia.Headless.XUnit` drives the FuncUI message loop. Cases:
  typing into the token field updates `Model.Token`; clicking `Submit`
  with empty input is a no-op (validated by `BootstrapToken.TryCreate`);
  clicking `Submit` with a valid token dispatches `RegisterAsync` via
  an `InMemoryRegistrationClient` and on `Ok` the window closes; on
  `Error TokenInvalid` the dialog stays open with the inline-error
  string from the `registration-api.md` mapping and focus returns to
  the token field. Reuses the existing
  `Tests.Windows/TestApp.fs` `AvaloniaTestApplication` harness.
- T048 — issue [#55](https://github.com/luca-veronelli-stem/button-panel-tester/issues/55):
  `tests/ButtonPanelTester.Tests/Integration/RegistrationFlowTests.fs` —
  wires the extracted GUI startup orchestration through
  `InMemoryCredentialStore` + `InMemoryRegistrationClient` (no real
  Avalonia window). Cases: empty store on launch →
  `App.tryRegister` returns `RegistrationOutcome.Completed credential`
  and store now contains it; non-empty store on launch →
  `App.tryRegister` returns `RegistrationOutcome.Skipped`; dialog
  dismissed without success → store stays empty and main window
  proceeds with seeded data. Lives in `tests/ButtonPanelTester.Tests/`
  (`net10.0`) — the orchestration helper is extracted to a pure F#
  function in `Services` so the test can exercise it without
  referencing the `net10.0-windows` GUI shell.

## Exit state

- `dotnet build -c Release` green across all six projects, 0 warnings.
- `dotnet test -c Release --no-build` green, ≥ 4 new passing test files
  added on top of the Phase 3 baseline (T045 Unit Windows, T046
  Integration Windows, T047 GUI Windows, T048 Integration).
- `cd lean && lake build` green — the four Phase 1 theorems still
  elaborate with no `sorry` and no custom axioms (Constitution
  Principle I gate, no new Lean work in Phase 4).
- `dotnet run --project src/ButtonPanelTester.GUI` against a clean
  `%LOCALAPPDATA%\Stem.ButtonPanelTester\` directory (no
  `credential.dpapi`) blocks on the registration dialog at launch;
  pasting the dev key `STEM-BT-DEV-KEY-2026` from `quickstart.md` §3
  closes the dialog and creates `credential.dpapi` whose contents are
  not readable in plain text; relaunching with the credential present
  skips the dialog and proceeds directly to the main window (SC-003,
  SC-006, FR-014 – FR-017).

## Cross-TFM test placement

The two-TFM test split established in T005a/b/c per
[issue #76](https://github.com/luca-veronelli-stem/button-panel-tester/issues/76)
governs Phase 4 test placement:

- `tests/ButtonPanelTester.Tests/` (`net10.0`): T048 (orchestration
  helper over `InMemoryCredentialStore` + `InMemoryRegistrationClient`).
- `tests/ButtonPanelTester.Tests.Windows/` (`net10.0-windows`): T045
  (`DpapiCredentialStore` exercises the OS DPAPI primitive through the
  Infrastructure project), T046 (`HttpRegistrationClient` is in the
  `net10.0-windows` Infrastructure project alongside DPAPI), T047
  (`Avalonia.Headless.XUnit` binds to the GUI project, reusing the
  existing `TestApp.fs` `AvaloniaTestApplication` harness shipped in
  Phase 3 T038).

The path strings in [`../tasks.md`](../tasks.md) reflect the pre-#76
single-test-project layout. GitHub issues #52, #53, and #54 still carry
those pre-#76 paths; the work commits land at the corrected paths and
supersede the issue text. T048's path is unchanged because the
orchestration helper it exercises is extracted to `Services`
(`net10.0`), not GUI.

## Protocol customization

The `resolve-ticket` lifecycle for Phase 4 starts at `WorkRequired`. The
spec, plan, tasks, research, data model, contracts, and quickstart
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
Phase 3. No new Lean elaboration is added in Phase 4, but `lake build`
stays in the gate so the Principle I check still fires on every
handoff.

Commit cadence: one durable commit per T-task. Single feature branch
`feat/001-phase-4`. One PR encompassing all 9 commits, rebase-merged on
completion.
