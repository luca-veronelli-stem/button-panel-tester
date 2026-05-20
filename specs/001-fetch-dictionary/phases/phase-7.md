# Phase 7 — Cold-start tolerance

This document scopes the seventh phase-PR for spec 001 (dictionary
fetch and status display). Issue [#92](https://github.com/luca-veronelli-stem/button-panel-tester/issues/92).
Phase 6 closed the original feat/001 scope; Phase 7 is a follow-up
hardening pass driven by operational evidence collected after the
production deployment.

## Problem

The dictionary service is hosted on Azure App Service Free tier with
Always-On off. The worker process unloads after ~30 min of idle
traffic and the first request after idle pays cold-boot latency.
Diagnostics from PR #91 (10 cold-start probes against
`app-dictionaries-manager-prod.azurewebsites.net`, Italy North, Free
tier) show:

```
sorted (s):  11.57, 11.94, 13.46, 35.46, 38.75, 41.00, 43.27, 48.21, 87.16, 89.91
min:     11.57 s   max: 89.91 s   median: 41.00 s   mean: 42.07 s
```

All 10 probes succeeded (HTTP 200); the Azure front-end holds the
open connection while the worker boots. The bottleneck is purely
worker warm-up latency.

The two production HTTP clients (`HttpDictionaryProvider`,
`HttpRegistrationClient`) currently apply a **10 s** client-side
timeout. Every cold-start refresh therefore fails with
`Failed(Timeout, _)` (or `Error(RegistrationNetwork Timeout)`), the
technician retries, and the second click succeeds in <1 s. Recoverable
papercut, but the first-click failure feels broken.

## Decision

Adopt **option 1 + option 3** from #92's discussion:

- **Option 1.** Raise both clients' `HttpClient.Timeout` (technically
  the internal linked-CTS deadline; the production wire shape is
  unchanged) from 10 s to **90 s**, and surface a status-row hint
  during refresh that warns the technician a first refresh after
  idle can take up to a minute.
- **Option 3.** On app startup, fire-and-forget a warm-up call
  against the dictionary endpoint so the worker is warm by the time
  the technician clicks Refresh, and log the observed duration.

Options 2 (smart retry) and 4 (server-side Basic SKU + Always-On) are
explicitly **out of scope** for this PR:

- Option 2 buys a modest UX win ("retrying due to cold start") at
  the cost of ~30 lines of retry orchestration in `DictionaryService`
  and the registration client. Option 1's 90 s deadline already
  covers all observed cold-starts; second-click retry is the safety
  net for the residual slow-tail. Not worth the orchestration
  complexity yet.
- Option 4 eliminates the problem at the source for ~€12/month but
  needs STEM approval. Tracked in `stem-dictionaries-manager` if and
  when the spend is approved; this PR does not depend on it.

## Spec change

SC-004 currently reads:

> A refresh that fails because the service is unreachable surfaces a
> human-readable reason in the status display within 12 seconds of
> the click.

Written assuming dominant failure modes are network/server errors
that fast-fail in <5 s naturally. In this deployment cold-start is
the dominant silent-hang population. The criterion is amended to
distinguish warm-state failures (which still fast-fail) from cold-start
absorption (which legitimately takes up to ~90 s):

> A refresh that fails against a **warm** service surfaces a
> human-readable reason within 12 seconds of the click. A refresh
> against a **cold** service is absorbed for up to 90 s before
> surfacing as Timeout; during this window the status row carries
> a hint that explains the wait.

## Contract changes

- `contracts/dictionary-api.md` §"Timeout and retries" — `10 s` →
  `90 s`. Add one line linking to phase-7.md for the rationale.
- `contracts/registration-api.md` §"Timeout and retries" — same.
  The two clients stay uniform per the existing "uniform user
  expectation" rule, just at the new value.

The HTTP outcome → `FetchFailureReason` / `RegistrationError` maps
do **not** change. `Failed(Timeout, _)` still surfaces when the
client-side deadline fires, just at 90 s instead of 10 s.

## Design

Four vertical slices. Slices 1 and 2 land the original options 1 + 3
from the issue's discussion; slices 3 and 4 are reworks driven by
review findings during the PR (race / unregistered-install / log
noise / LOGGING-standard deviation).

### Slice 1 — 90 s timeout + UI hint + docs

End-to-end: production code change + UI hint + spec + contracts +
tests, all in one bisect-safe commit.

Production change:

- `HttpDictionaryProvider`: lift the `TimeSpan.FromSeconds(10.0)`
  literal to a `static member val TimeoutSeconds = 90.0` so the
  value is asserted from a test without needing a real-time wait.
  The internal linked-CTS reads the member; outward behaviour is
  unchanged on cancellation routing and result mapping.
- `HttpRegistrationClient`: same treatment, same value.
- `DictionaryStatusRow`: add a `coldStartHintText` function and a
  named `ColdStartHint` `TextBlock` child that is appended to the
  row's `StackPanel` only while `RefreshState = Refreshing`. The
  text reads: "This may take up to a minute if the service has been
  idle." Locale-invariant English per the COMMENTS / language
  defaults.

Documentation:

- `spec.md` SC-004 — reword as above.
- `contracts/dictionary-api.md` §"Timeout and retries" — `10 s` →
  `90 s` + cross-reference.
- `contracts/registration-api.md` §"Timeout and retries" — same.
- `quickstart.md` §"Troubleshooting" — append a one-paragraph note
  explaining the cold-start wait.
- XML doc strings on the two adapters — replace `client timeout
  (10 s)` with `(90 s)`.

Proof:

- `HttpDictionaryProviderTimeoutTests` (new module) — one fact
  asserting `HttpDictionaryProvider.TimeoutSeconds = 90.0`. Failing
  on `main` because no such member exists; passing once the
  production change lands. The existing
  `FetchAsync_TaskCanceledFromInsideHandler_ReturnsFailedTimeout`
  fact already covers the behavioural mapping and stays untouched.
- `HttpRegistrationClientTimeoutTests` (new module) — symmetric.
- `DictionaryStatusRowTests` — new `[<AvaloniaFact>]`s asserting:
  the `ColdStartHint` `TextBlock` is **absent** when
  `RefreshState = Idle`, **present** when `Refreshing`, with the
  documented text. Same headless harness, same materialisation path
  as the existing tests.

### Slice 2 — warm-up ping

End-to-end: a small `WarmUp` module in `Services.Dictionary` exposing
a pure function `runAsync : IDictionaryProvider -> IClock -> ILogger
-> CancellationToken -> Task<unit>`. It calls `FetchAsync` once,
measures elapsed wall-clock via `IClock`, logs the duration and the
result classification (success vs failure reason) at `Information`
verbosity, and swallows any non-cancellation outcome (this is a
warm-up; the production fetch path is the source of truth). The
existing `OperationCanceledException`-from-the-caller's-`ct`
contract is preserved.

`CompositionRoot.configure` is unchanged. `App.fs`
`this.Opened.Add` already fire-and-forgets a `task`; the warm-up
runs alongside `service.InitializeAsync` so the technician's first
explicit click finds the worker warm.

Proof:

- `WarmUpTests` (new module in the cross-platform `Tests` project) —
  using a tiny stub `IDictionaryProvider` that records call count
  and returns a scripted `Success`/`Failed`, assert:
  1. `runAsync` calls `FetchAsync` exactly once.
  2. `runAsync` returns successfully even when the provider returns
     `Failed(_, _)` (swallow non-cancellation outcomes).
  3. `runAsync` propagates `OperationCanceledException` if the
     caller's `ct` was cancelled (port-contract preservation).

Why not a CompositionRoot integration test? `CompositionRoot.configure`
already has no behavioural assertions today (the wiring shape is the
test). Putting the warm-up in a named service whose contract is unit
tested keeps the proof at the right level; the App.fs invocation is
a one-line `let _ : Task = WarmUp.runAsync ...` whose only failure
mode (typo / forgotten call) is caught at compile time.

### Slice 3 — rework warm-up: `/health` endpoint + class shape

Driven by three findings surfaced during PR #95 review:

1. **Unregistered installs send a spurious 401.** Slice 2's `FetchAsync`
   warm-up reaches `/api/dictionaries/{id}/resolved`, which requires
   `X-Api-Key`. On a fresh install the credential store is empty,
   the `ApiKeyAuthHandler` omits the header, and the server replies
   401 — logged as `Failed(Unauthorized, _)` in the warm-up's
   Information line. The 401 also pollutes the dictionary service's
   access logs.
2. **The 401 is, however, load-bearing for the registration UX.**
   The wasted request still cold-boots the worker. On a fresh
   install the technician then opens the registration dialog and
   spends seconds-to-tens-of-seconds finding their bootstrap token;
   by the time they click Submit, the worker is warm and `POST
   /register` returns in <1 s. Gating the warm-up on credential
   presence would regress the registration UX (silent 60 s
   "Submitting…").
3. **The warm-up's logger shape deviates from LOGGING.** `WarmUp` is
   an F# module with no `T` for `ILogger<T>`. The standard's F#
   section addresses classes but not modules.

The rework hits all three at once:

- **New port** `Stem.ButtonPanelTester.Core.Dictionary.IDictionaryServiceWarmUp`
  with one method `WarmUpAsync : CancellationToken -> Task<unit>`.
  Throws on transport failure, swallows nothing — exception-based
  control flow at the adapter boundary, swallow-policy at the
  orchestration.
- **New adapter** `Infrastructure.Http.HttpDictionaryServiceWarmUp`,
  an F# **class** with `ILogger<HttpDictionaryServiceWarmUp>`,
  issuing `GET /health` against the dictionary service's
  unauthenticated endpoint (`stem-dictionaries-manager`
  `Program.cs:86-87` + `ApiKeyMiddleware` allow-list). Reuses the
  named "Dictionary" `HttpClient` — `ApiKeyAuthHandler` is harmless
  on `/health` (server ignores `X-Api-Key` for allow-listed paths;
  unregistered installs omit the header). 90 s timeout, uniform
  with the other adapters.
- **`WarmUp` module → `DictionaryWarmUp` class.** With a port and
  DI, the canonical archetype-A class shape is the natural fit.
  `ILogger<DictionaryWarmUp>` resolves the LOGGING-standard
  deviation entirely. Orchestration policy (Stopwatch, log
  Information on success, swallow non-cancellation, propagate
  cancellation) stays unchanged.
- **`CompositionRoot`** binds the new port + adapter and the
  `DictionaryWarmUp` class as singletons.
- **`App.fs`** resolves `DictionaryWarmUp` from DI and calls
  `RunAsync(CancellationToken.None)` fire-and-forget, replacing
  the module function call.

Why `/health` and not a dedicated `/warmup` endpoint:

- Cold-boot in Azure App Service is process-level (JIT, DI, EF
  `DbContext` init). Any endpoint pays it; the worker is warm the
  moment the first response returns.
- `/health` already exercises `AddDbContextCheck<AppDbContext>`,
  which primes EF's query plan cache more thoroughly than a
  constant-return endpoint would.
- `/health` is already used by the dictionary service's deploy
  workflow for post-deploy cold-start probing — there's prior art.
- A dedicated `/warmup` adds nothing concrete, just self-documenting
  semantics, at the cost of a coordinated server-side rollout.

Proof:

- `HttpDictionaryServiceWarmUpTests` (new module in `Tests.Windows`)
  — stubbed `HttpMessageHandler` asserts: GET issued to `/health`,
  `Accept: application/json` header, 200 returns unit, non-200 still
  returns unit (server liveness is what matters for cold-boot
  purposes, not the health-check verdict), `HttpRequestException`
  propagates, internal timeout maps to `OperationCanceledException`
  per the same guarded-catch pattern as `HttpDictionaryProvider`.
- `WarmUpTests` (refactored) — stub `IDictionaryServiceWarmUp`
  records call count and yields scripted outcomes; the three
  contracts (called once, swallow non-cancellation, propagate
  cancellation) move from the module function to the class
  `RunAsync` method.

### Slice 4 — registration dialog cold-start hint

The cold-start hint added in slice 1 only lives in
`DictionaryStatusRow`. On a fresh install where the warm-up's
worker-priming didn't complete in time (technician registers
immediately, no human-time window), `POST /register` pays the
cold-boot itself and the dialog's `Submitting` state stays visible
for ~60 s with no explanation — reads as "broken".

Slice 4 mirrors slice 1's pattern in `RegistrationDialog.fs`:

- Add a `ColdStartHint` named `TextBlock` rendered only in the
  `Submitting` model state, with the same documented text:
  "This may take up to a minute if the service has been idle."
- The hint is absent in `Idle` (steady state) and `Failed`
  (post-error state, where a different explanation belongs).

Proof: `[<AvaloniaFact>]`s in `RegistrationDialogTests.fs`
covering all three model states, same headless-harness pattern as
the existing tests.

## Out of scope

- Smart-retry orchestration (option 2).
- Server-side SKU upgrade (option 4).
- Operator-side `eng/refresh-seed.ps1` timeout — already raised to
  120 s in PR #91 on the same diagnostic data; this PR matches but
  is not re-raising.
- Configurable timeout via `DictionaryOptions` — the value is
  pragmatic, not policy. If a deployment requires a different
  value (e.g. post-option-4 it could safely drop back to 10 s),
  that's a future spec change.

## Acceptance gate

`llm/reviews/local-fix-92-cold-start-tolerance/gate.ps1`:

```powershell
dotnet build -c Release
dotnet test
dotnet format --verify-no-changes
```

Boundary smoke: not added. The cold-start probe data already lives
in PR #91 + this issue's discussion; re-running it against
`app-dictionaries-manager-prod.azurewebsites.net` is an operator
follow-up, not a per-commit gate. The unit-level proof for the four
slices is sufficient because the production code changes are a
literal value (90 s), two additive UI elements, one new port and
adapter, and a class-shape conversion.

## Tasks

Mapped one-to-one to entries in [`../tasks.md`](../tasks.md):

- T067 — Slice 1: raise timeout to 90 s in both adapters + UI hint
  in `DictionaryStatusRow` + spec/contract/quickstart updates +
  new timeout-value tests + new status-row hint tests.
- T068 — Slice 2: initial warm-up via `IDictionaryProvider.FetchAsync`
  + `WarmUp` module + invocation from `App.fs` `Opened` handler +
  unit tests. Reworked in T069.
- T069 — Slice 3: introduce `IDictionaryServiceWarmUp` port +
  `HttpDictionaryServiceWarmUp` adapter hitting `GET /health` +
  convert `WarmUp` module to `DictionaryWarmUp` class with
  `ILogger<DictionaryWarmUp>` + refactor `WarmUpTests` + new
  `HttpDictionaryServiceWarmUpTests` integration test +
  `CompositionRoot` and `App.fs` rewiring.
- T070 — Slice 4: `ColdStartHint` in `RegistrationDialog.Submitting`
  model state + `[<AvaloniaFact>]`s in `RegistrationDialogTests`.
