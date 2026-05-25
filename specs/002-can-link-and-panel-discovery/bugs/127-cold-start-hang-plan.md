# #127 — PcanCanLink cold-start hang — Implementation Plan

**Branch**: `fix/127-pcan-cold-start-hang` | **Date**: 2026-05-25
**Spec**: [`./127-cold-start-hang.md`](./127-cold-start-hang.md) @ sha `595fde7`
**Spec review**: [`../../../llm/reviews/spec-review.md`](../../../llm/reviews/spec-review.md) (Approved)
**Parent feature**: [`../spec.md`](../spec.md) (unchanged) — FR-001 + SC-001
**Issue**: <https://github.com/luca-veronelli-stem/button-panel-tester/issues/127>
**PR**: <https://github.com/luca-veronelli-stem/button-panel-tester/pull/134>

## Summary

The cold-start path through `CanLinkService.InitializeAsync →
PcanCanLink.OpenAsync → CanPort.ConnectAsync` silently leaves the CAN
status row at `Initializing…` when the PEAK adapter is plugged in but
the bus is silent — violating FR-001 (Connected within the SC-001
budget after dictionary boot). The root cause is `CanPort`'s
constructor snapshotting `driver.IsConnected` *before* any subscriber
can attach, so the only `Connected` transition the port will emit on
the success path has already been dropped on the floor by the time
`PcanCanLink` subscribes.

The fix is a one-line surgical change in the vendored
`CanPort.cs` constructor — initialise `_state` unconditionally to
`Disconnected`, so the existing `ConnectAsync` poll loop emits the
`Connected` transition on attempt 0 against the now-subscribed
handler. The change is paired with the established vendor-modification
pattern (`VENDOR.md` "Local modifications" row + `VENDOR.sha256`
regenerate + upstream PR against `stem-device-manager`), mirroring
the precedent set by `PCANManager.cs`'s IAsyncDisposable diff (upstream
`stem-device-manager#117`).

Two layers of regression coverage land alongside the fix:

- **Slice 1 (durable commit, bundled per `vertical-commits.md`)** —
  AC1 RED-then-GREEN as a `[<Theory>]` over `driver.IsConnected ∈
  {true, false}` at ctor time, paired with the one-line `CanPort.cs`
  change and the `VENDOR.md` + `VENDOR.sha256` update.
- **Slice 2 (durable commit, test-only)** — AC2 cold-start regression
  + AC3 fail-fast regression, both against `PcanCanLink` with a fake
  `ICommunicationPort`.

Neither slice touches `CanLinkService.fs`, `PcanCanLink.fs`,
`PCANManager.cs`, or `IPcanDriver.cs`. The existing
`CanLinkServiceLifecycleTests` are not modified.

## Technical Context

**Language/Version**: F# 9 (`net10.0-windows`) for tests; C# 13
(`net10.0-windows`) for the one-line fix.

**Primary Dependencies**: xUnit 2.9.x (no FsCheck for this slice;
example/Theory-driven per Principle II rationale below);
Microsoft.Extensions.Logging.Abstractions for `NullLogger<_>` in
slice 2.

**Storage**: N/A (defect-fix track).

**Testing**: xUnit `[<Fact>]` + `[<Theory>]` + `[<InlineData>]`. No
FsCheck property; no Avalonia.Headless. Manual fakes only (constitution
"No mocking libraries"). Hardware-bound coverage stays out — the live
path remains under `PcanLifecycleTests` (tracked by #112).

**Target Platform**: Windows (the vendored protocol stack targets
`net10.0-windows`; PEAK PCANBasic is Windows-only).

**Project Type**: Defect fix against an existing spec/feature. No new
project, no new package reference, no new namespace.

**Performance Goals**: Restore FR-001's SC-001 budget — Connected
within 2 s of dictionary warm-up completion on the cold-start path.

**Constraints**: Every commit must build + pass tests on its own
(`bisect-safe`); vertical commits (`vertical-commits`); local
`gate.ps1` (build + tests + format + lake) must pass before handoff.

**Scale/Scope**: One-file production diff (`CanPort.cs`, +1/-3 lines),
two test files (AC1 + AC2/AC3), `VENDOR.md` row + `VENDOR.sha256`
regenerate. Out-of-band: one upstream PR against
`luca-veronelli-stem/stem-device-manager` mirroring the C# change
(AC5).

## Constitution Check

*GATE: PASS — every principle is addressed without unresolved violation.*

- **I. Formal Verification of Invariants** *(NON-NEGOTIABLE)* — No
  Lean module update required. The defect lives strictly inside the
  vendored `Infrastructure.Protocol` C# adapter, below the
  `ICanLink` port surface that the Lean track formalises. No domain
  state-changing action is introduced or modified; the existing
  Phase-1 preservation theorems remain authoritative for
  `CanLinkState` translation, and the fix only restores the
  `ConnectionState → CanLinkState` emission contract those theorems
  already assume. **Justification:** defect-fix track against an
  already-formalised spec; no preservation theorem regresses.
- **II. Property-Driven Correctness** — AC1 is widened to a
  `[<Theory>]` parameterised over `driver.IsConnected ∈ {true, false}`
  at ctor time; both branches assert exactly one `Connected` emission
  after `ConnectAsync`. AC2 and AC3 are `[<Fact>]` regression tests at
  the `PcanCanLink` layer.

  **Rationale for example/Theory coverage** (per Principle II's
  "documenting concrete protocol fixtures" carve-out): the AC1 Theory
  is anchored to a specific subscriber-attach-order invariant of the
  vendored stack — a discrete combinatorial input (`IsConnected`
  is a two-valued boolean at ctor time), not a property over a rich
  input space. AC2/AC3 are likewise fixture tests against the
  `ICommunicationPort` contract: they encode the exact event-fanout
  sequence the link wrapper relies on, not a property holding over an
  unbounded input domain. No reasonable FsCheck property would
  strengthen the regression boundary beyond the two-branch Theory
  + two contract fixtures; reaching for a property here would add
  ceremony without coverage gain.
- **III. Ports and Adapters for Every External Boundary** — The fix
  lives at the concrete adapter `CanPort` (behind `ICommunicationPort`,
  defined in `Core.Interfaces`). The virtual adapter at the `ICanLink`
  boundary (`InMemoryCanLink` in `tests/ButtonPanelTester.Tests/Fakes/Can/`)
  is untouched. AC1 uses a hand-rolled fake `IPcanDriver`; AC2/AC3 use
  a hand-rolled fake `ICommunicationPort`. Both fakes are manual (no
  Moq / NSubstitute) per the constitution. The port surface is
  preserved by construction. **PASS.**
- **IV. CI Greens the Whole Stack; Hardware Tests Are Explicit** —
  AC1/AC2/AC3 are hardware-free unit tests added to
  `tests/ButtonPanelTester.Tests.Windows/Unit/Can/`. No new
  `Category=Hardware` trait is introduced. No new `[<Fact(Skip = …)>]`
  is introduced. The live-hardware coverage stays in
  `Integration/Can/Hardware/PcanLifecycleTests.fs` (tracked by #112).
  Local `gate.ps1` covers build + tests + format + lake; AC6 invokes
  it before handoff. AC7's bench-replay confirmation goes in the PR
  description (manual evidence per Principle IV). **PASS.**
- **V. Supplier-Deployed Identity Is Hashed at Capture** *(NON-NEGOTIABLE)* —
  No identity-bearing data on this feature's path. The fix touches
  CAN-bus event flow; no OS user, machine name, SID, MAC, or related
  field is read, hashed, or stored. **N/A.**
- **VI. Stopgap Discipline** — The local `CanPort.cs` modification is
  **not** a stopgap. It rides the established vendor-modification
  pattern (paired with an upstream PR against `stem-device-manager`,
  same shape as `PCANManager.cs`'s IAsyncDisposable `+78, -9` diff
  paired with `stem-device-manager#117`). The vendor manifest's
  "Local modifications" table is the canonical record; no waiver
  document is required because the upstream-PR-paired pattern is the
  blessed vendor-fix path, not a knowingly-deferred bypass.
  Per F5 of the spec review: aligns with the standing
  fix-at-source-not-adjacent preference. **PASS.**

**Result:** PASS. No "Complexity Tracking" entries required.

## Project Structure

### Documentation (this fix)

```text
specs/002-can-link-and-panel-discovery/bugs/
├── 127-cold-start-hang.md          # spec (unchanged this slice)
└── 127-cold-start-hang-plan.md     # THIS FILE
```

No `research.md`, `data-model.md`, `quickstart.md`, or `contracts/`
artefacts — defect-fix track against an existing feature; the parent
feature's `../research.md` and `../contracts/` remain authoritative.

### Source Code (touched paths)

```text
src/ButtonPanelTester.Infrastructure.Protocol/
├── Hardware/CanPort.cs              # +1/-3 ctor change (slice 1)
├── VENDOR.md                        # new "Local modifications" row (slice 1)
└── VENDOR.sha256                    # regenerated by eng/vendor-protocol-stack.ps1 -RehashOnly (slice 1)

tests/ButtonPanelTester.Tests.Windows/
├── ButtonPanelTester.Tests.Windows.fsproj
│   # add <Compile Include="Unit/Can/CanPortCtorTests.fs" />               (slice 1)
│   # add <Compile Include="Unit/Can/PcanCanLinkColdStartTests.fs" />      (slice 2)
└── Unit/Can/
    ├── CanPortCtorTests.fs              # NEW — AC1 Theory + fake IPcanDriver (slice 1)
    └── PcanCanLinkColdStartTests.fs     # NEW — AC2 + AC3 + fake ICommunicationPort (slice 2)
```

**Structure Decision.** Tests live in `Tests.Windows` (not the default
`Tests` project) because `Infrastructure.Protocol.csproj` targets
`net10.0-windows` — `CanPort` cannot be referenced from a `net10.0`
test project. This matches the placement of `PcanLifecycleTests.fs`
under the same TFM-split rule recorded in
[memory: button-panel-tester tests split by TFM]. Compile-list
placement (after `Unit/InstallationDescriptorProviderTests.fs`, before
`Integration/HttpRegistrationClientTests.fs`) is cosmetic — neither
new file depends on or is depended on by any sibling.

## Phase 0 — Background (no new research)

The root-cause sequence is fully traced in the spec
(`127-cold-start-hang.md`, §"Root cause"). The spec-review verified
every cited line. **One tighter citation flows into the eventual
commit body (F1 follow-through):** the `IsConnected` setter that
actually fires `ConnectionStatusChanged` lives at `PCANManager.cs:72-83`,
with the `?.Invoke(this, _isConnected)` call site at `:80`. The
spec's `:88, 97-100` citation points at the ctor + `Initialize`
method, which is correct but one method up the call stack; the commit
body for slice 1 will cite `:72-83` (especially `:80`) for the actual
emission, alongside `:85-90` (ctor) and `:97-100` (Initialize body)
for the eager-Initialize trigger.

No `NEEDS CLARIFICATION` items. No new dependency. No new integration.

## Phase 1 — Design

### Diff sketch (slice 1)

**`CanPort.cs` constructor** (`src/ButtonPanelTester.Infrastructure.Protocol/Hardware/CanPort.cs`,
lines 43-52, ctor body):

```diff
 public CanPort(IPcanDriver driver)
 {
     ArgumentNullException.ThrowIfNull(driver);
     _driver = driver;
-    _state = (int)(driver.IsConnected
-        ? ConnectionState.Connected
-        : ConnectionState.Disconnected);
+    _state = (int)ConnectionState.Disconnected;
     _driver.PacketReceived += OnDriverPacketReceived;
     _driver.ConnectionStatusChanged += OnDriverConnectionChanged;
 }
```

After the change, `ConnectAsync` (lines 70-88) polls
`_driver.IsConnected` on the first iteration of its existing 20-attempt
poll loop. If the driver is already connected, attempt 0 calls
`Transition(ConnectionState.Connected)` which fires `StateChanged`
to the now-subscribed `PcanCanLink.onStateChanged` handler. If the
driver is not connected, the existing fail-fast path
(`Transition(Error)` + throw at lines 85-87) runs unchanged after 20
× 100 ms — preserving AC3's contract.

### Vendor manifest updates (slice 1)

`VENDOR.md` — add a row under "Local modifications":

```markdown
| Hardware/CanPort.cs | +1, -3 | Drop the constructor-time `IsConnected` snapshot so cold-start `ConnectAsync` always transitions through `Connecting → Connected`, restoring FR-001's emission contract for subscribers that attach after construction (issue #127). | <upstream PR URL — recorded when AC5 PR opens> |
```

`VENDOR.sha256` — regenerated by:

```powershell
pwsh ./eng/vendor-protocol-stack.ps1 -RehashOnly
```

The script hashes every file under `src/ButtonPanelTester.Infrastructure.Protocol/`
except `VENDOR.md` / `VENDOR.sha256` / `bin/` / `obj/`. The regenerated
file replaces `Hardware/CanPort.cs`'s hash row. The pre-commit hash
check passes against the new sidecar (AC4).

### Test design (slices 1 & 2)

**Slice 1 — `Unit/Can/CanPortCtorTests.fs` (AC1):**

- Hand-rolled fake `IPcanDriver` exposing `IsConnected` as a
  ctor-injected `bool`, `PacketReceived` + `ConnectionStatusChanged`
  as never-fired events, `SendMessageAsync` as
  `Task.FromResult(true)`, and `Disconnect()` as a no-op.
- `[<Theory>]` with `[<InlineData(true)>]` and `[<InlineData(false)>]`:
  ```text
  Construct CanPort(fake).
  Attach a StateChanged handler that captures emissions.
  Await ConnectAsync().
  Assert: handler observed exactly one ConnectionState.Connected.
  Assert: port.State == Connected.
  ```
- **Pre-fix expected behaviour (run RED before applying the fix):**
  the `true` InlineData fails — `State == Connected` already at ctor
  time, `ConnectAsync` early-returns at `CanPort.cs:73`, no
  `StateChanged` emission; the `false` InlineData passes
  (`ConnectAsync` polls `IsConnected = false` for 20 attempts then
  throws — but the Theory will need to time-cap or set
  `IsConnected = true` post-construction; see below).
- **Driving the `false` branch to `Connected`:** the fake's
  `IsConnected` getter must be settable mid-test so the poll loop
  observes `false → true`. Two options:
  1. Fake exposes a `SetConnected(bool)` test-only method; the test
     attaches the handler, calls `SetConnected(true)`, then awaits
     `ConnectAsync` (the poll picks up `true` on attempt 0).
  2. Fake's `IsConnected` is a mutable cell flipped by the test
     before calling `ConnectAsync`.
  Either way both Theory branches must converge on "handler observed
  exactly one `Connected`" — option 1 is cleaner because the fake's
  initial-state semantics stay invariant of the test's intent.
- **Post-fix expected behaviour:** both InlineData rows pass. The
  `true` branch goes through `Disconnected → Connecting → Connected`
  (the contract under the fix); the `false` branch goes through
  `Disconnected → Connecting → Connected` once the test flips the
  fake to `true` — same observable. The Theory documents the
  invariant: regardless of `IsConnected` at ctor time, attaching a
  subscriber and calling `ConnectAsync` produces exactly one
  `Connected` emission. (F6 satisfied: both branches in the matrix.)

**Slice 2 — `Unit/Can/PcanCanLinkColdStartTests.fs` (AC2 + AC3):**

- Hand-rolled fake `ICommunicationPort` exposing:
  - `Kind` = `ChannelKind.Can`,
  - `State` initially `Disconnected` (the post-fix port shape),
  - `IsConnected` = `State == Connected`,
  - `PacketReceived` + `StateChanged` as `Event<…>` instances,
  - `ConnectAsync` scriptable — either fires
    `StateChanged(Connected)` and returns, or throws an exception
    (for AC3 fail-fast),
  - `DisconnectAsync` / `SendAsync` / `Dispose` no-op (not exercised).
- **AC2 cold-start regression (`[<Fact>]`):** construct
  `PcanCanLink(fun () -> fakePort, NullLogger<PcanCanLink>.Instance)`;
  subscribe to `LinkStateChanged`; await `OpenAsync(250_000, CancellationToken.None)`;
  the fake's `ConnectAsync` fires one `StateChanged(Connected)` then
  returns. Assert: `LinkStateChanged` emitted exactly one
  `Connected(_, _)`; `CurrentState` reads `Connected(_, _)`.
- **AC3 fail-fast regression (`[<Fact>]`):** same setup but the fake's
  `ConnectAsync` raises (e.g. `InvalidOperationException` — same
  shape `CanPort` raises on poll-timeout) *after* synchronously firing
  `StateChanged(Error)` (mirroring `CanPort.cs:85-87`). Assert:
  `LinkStateChanged` emitted exactly one `Error(Recoverable _, _)`;
  `CurrentState` reads `Error(Recoverable _, _)`; no exception
  surfaces from `OpenAsync` (per `PcanCanLink.openInternal`'s
  contract — failures surface via `LinkStateChanged`, not throws).

### Fake placement

Both fakes are file-private (inline in the test module). They are not
shared between slice 1 and slice 2 — each is minimal and tied to its
test's invariants. Promoting either to a shared `Fakes/Can/`
file would be premature abstraction (the constitution's "interfaces
only where they earn their keep" lens applies to fakes too); if a
second test ever needs the same fake, refactor then.

### Layering vs the existing test surface (F3 follow-through)

| Test surface | Production target | Fake | Status under this plan |
|---|---|---|---|
| `Unit/Can/CanPortCtorTests.fs` (slice 1, NEW) | `CanPort` (C#, `Infrastructure.Protocol/Hardware/`) | `IPcanDriver` (hand-rolled, file-private) | adds AC1 |
| `Unit/Can/PcanCanLinkColdStartTests.fs` (slice 2, NEW) | `PcanCanLink` (F#, `Infrastructure/Can/`) | `ICommunicationPort` (hand-rolled, file-private) | adds AC2 + AC3 |
| `Integration/Can/CanLinkServiceLifecycleTests.fs` (existing) | `CanLinkService` (F#, `Services/Can/`) | `InMemoryCanLink` emitting `CanLinkState` directly | **unchanged** — bypasses the defect entirely (F4) |
| `Integration/Can/Hardware/PcanLifecycleTests.fs` (existing) | `PcanCanLink` + real `PCANManager` over physical PEAK | none (hardware) | unchanged — `[<Trait("Category", "Hardware")>]`, tracked by #112 |

Slices 1 and 2 add coverage at two distinct layers that the existing
tests do not touch. The existing service-level tests cannot regress
under the fix because `InMemoryCanLink` skips the
`ConnectionState → CanLinkState` translation that the defect lives in.

## Slice plan (TDD vertical commits)

Two durable commits, both bisect-safe and vertical.

### Slice 1 — AC1 + fix + vendor manifest (bundled)

Rationale for bundling per `vertical-commits.md`: the test and the
fix travel together; the test demonstrates RED on pre-fix and GREEN
post-fix. A horizontal split (test-only commit, then fix-only commit)
would intentionally introduce a known-failing intermediate state —
exactly what `bisect-safe.md` forbids.

**Files (single commit):**

- `tests/ButtonPanelTester.Tests.Windows/Unit/Can/CanPortCtorTests.fs` (NEW)
- `tests/ButtonPanelTester.Tests.Windows/ButtonPanelTester.Tests.Windows.fsproj`
  (one new `<Compile Include="Unit/Can/CanPortCtorTests.fs" />` row, slotted
  after the existing `Unit/InstallationDescriptorProviderTests.fs` row)
- `src/ButtonPanelTester.Infrastructure.Protocol/Hardware/CanPort.cs`
  (ctor `_state` assignment, +1/-3)
- `src/ButtonPanelTester.Infrastructure.Protocol/VENDOR.md`
  (new "Local modifications" row)
- `src/ButtonPanelTester.Infrastructure.Protocol/VENDOR.sha256`
  (regenerated via `pwsh ./eng/vendor-protocol-stack.ps1 -RehashOnly`)

**TDD evidence the worker records before commit:**

1. Apply only the test file + `.fsproj` row; run `dotnet test
   tests/ButtonPanelTester.Tests.Windows -c Release --filter
   "FullyQualifiedName~CanPortCtorTests"`. Expect the `(true)`
   InlineData to FAIL with "no StateChanged emission observed". (RED
   evidence captured in `work-review.md`.)
2. Apply the `CanPort.cs` ctor change. Re-run the same filter. Both
   InlineData rows PASS. (GREEN evidence captured.)
3. Run `pwsh ./eng/vendor-protocol-stack.ps1 -RehashOnly` to refresh
   `VENDOR.sha256`. Update `VENDOR.md`'s "Local modifications" table.
4. Run the full `llm/reviews/gate.ps1` (build + tests + format + lake);
   record the exit code in `work-review.md`.
5. Stage all five paths atomically. Single commit, Conventional
   Commits, English imperative.

**Suggested commit title** (reviewer will refine and supply final
title/body at `WorkApproved`):

```text
fix(infrastructure-protocol): #127 drop CanPort ctor IsConnected snapshot
```

Body talking points the eventual commit message must carry (per F1):

- Cite `CanPort.cs:43-52` for the ctor; `:73` for the early-return
  branch that masks the missing emission.
- Cite `PCANManager.cs:72-83` (setter) — especially `:80` (the
  `?.Invoke`) — for the event-fire site that is dropped on the floor
  when no subscriber has attached. Also cite `:85-90` (ctor) and
  `:97-100` (`Initialize` body) for the eager-Initialize trigger that
  flips `IsConnected` to `true` synchronously inside the
  `PCANManager` constructor.
- Cite `PcanCanLink.fs:235-264` (`ensurePort`) — specifically `:244`
  (`port.StateChanged.AddHandler handler`) — for the subscription
  attach that happens after `portFactory ()` returns.
- Vendor manifest discipline: new "Local modifications" row, upstream
  PR URL placeholder until AC5's stem-device-manager PR opens (the
  body can ship with the placeholder; F5 documents that this is the
  blessed pattern, matching `PCANManager.cs`'s IAsyncDisposable diff).
- Closes #127 (auto-close via PR body — keep `Closes #N` out of
  individual commit bodies per `github-pr-auto-close.md` since
  rebase-merge replays them onto `main`; use `Refs #127` here and
  `Closes #127` in the PR body only).

### Slice 2 — AC2 + AC3 link-layer regression

**Files (single commit):**

- `tests/ButtonPanelTester.Tests.Windows/Unit/Can/PcanCanLinkColdStartTests.fs` (NEW)
- `tests/ButtonPanelTester.Tests.Windows/ButtonPanelTester.Tests.Windows.fsproj`
  (one new `<Compile Include="Unit/Can/PcanCanLinkColdStartTests.fs" />`
  row, slotted directly after the slice-1 row)

**TDD evidence:**

1. Apply both files. Run `dotnet test
   tests/ButtonPanelTester.Tests.Windows -c Release --filter
   "FullyQualifiedName~PcanCanLinkColdStartTests"`. All cases pass —
   these are pure regression tests against the post-fix contract;
   slice 1 already restored the underlying emission so the link
   wrapper's translation runs end-to-end.
2. Run the full `gate.ps1`.

Slice 2 is test-only — no production code change. Bisect-safe by
construction.

**Suggested commit title:**

```text
test(infrastructure): #127 regression — PcanCanLink cold-start + fail-fast
```

### Out-of-band (AC5, not a local commit)

Open a PR against `luca-veronelli-stem/stem-device-manager` with:

- The same `CanPort.cs` one-line ctor change.
- An xUnit C# regression test against a fake `IPcanDriver`, mirroring
  AC1's Theory shape.

Record the upstream PR URL in `VENDOR.md`'s "Local modifications"
row once the PR opens. This update can ride as a small amendment to
the slice-1 commit during review (per the `pr` skill's "in-place fix"
rule) or as a follow-up doc-only commit during finalisation — the
worker chooses based on timing with the AC5 PR's creation.

## Risks

- **R1 — VENDOR.sha256 regenerate must run from the worktree root.**
  `eng/vendor-protocol-stack.ps1` computes `$repoRoot` from
  `$PSScriptRoot` (the script's own location), so it's CWD-tolerant.
  The risk is forgetting the step entirely — the contract's pre-commit
  hash check would fail without it. Mitigation: AC4 + this plan call
  the regenerate explicitly in the slice-1 evidence checklist; the
  gate's `dotnet build` will surface the failure if the manifest
  drifts from the bytes on disk.
- **R2 — Upstream PR ordering (AC5).** The local fix lands before the
  upstream `stem-device-manager` PR merges, identical to the
  IAsyncDisposable mod's ordering precedent. On the next re-vendor,
  the worker re-applies the row from `VENDOR.md` "Local modifications"
  if upstream hasn't merged yet, or removes the row if it has. The
  re-vendoring procedure in
  `contracts/vendor-manifest.md` already documents this; no plan
  change required.
- **R3 — PR-D #116 rebase impact.** Per the standing fix-before-PR-D
  roadmap (memory: `bpt_spec002_roadmap.md`), this PR queues ahead of
  PR-D #116. The `CanPort.cs` diff is +1/-3 in a section PR-D does
  not touch (PR-D operates on `CanLinkService` + `PcanCanLink`, not
  on the C# port adapter). The new test files land under
  `Unit/Can/`, which PR-D does not touch either. Rebase risk is
  negligible.
- **R4 — Cross-platform.** All touched files are
  `net10.0-windows`-only: `Infrastructure.Protocol.csproj`,
  `Infrastructure.fsproj`, `Tests.Windows.fsproj`. The new tests do
  not affect the `Tests` project (`net10.0`) or the Lean workspace.
  CI's Windows runner exercises everything; the Linux runner skips
  Windows-targeted projects per the existing matrix.
- **R5 — Theory parameter mid-test mutability.** The slice-1 fake
  `IPcanDriver` needs a way to flip `IsConnected` between
  construction and `ConnectAsync` for the `(false)` InlineData branch.
  The fake exposes a test-only `SetConnected(bool)` shim (option 1 in
  Phase 1). Risk: future readers might assume the shim is part of the
  production interface — mitigated by file-private placement (the
  fake doesn't escape the test module) and an XML/`///` comment on
  `SetConnected` calling out the test-only intent.
- **R6 — Hash-check timing for the slice-1 commit.** If a precommit
  hook validates `VENDOR.sha256` against the vendored bytes, the
  hash must match at the moment of `git commit`. The TDD evidence
  order in slice 1 (apply test → apply fix → run rehash → run gate
  → stage → commit) lands the hash refresh before staging, so the
  staged tree is internally consistent.

## What does NOT change

Reaffirming the spec's exclusions (the plan does not relax any of them):

- **Spec-002 is unchanged.** `FR-001`, `SC-001`, contracts, and
  `research.md` stay authoritative.
- **`Infrastructure/Can/PcanCanLink.fs`** — no production code change.
  The translation logic in `translateState` and the
  `Recoverable → Fatal` escalation in `CanLinkService` already handle
  a correct `Connected` emission; the bug is the missing emission, not
  the handling of it.
- **`Services/Can/CanLinkService.fs`** — untouched. R8's escalation
  tracker stays as-is.
- **`Infrastructure.Protocol/Hardware/PCANManager.cs`** — untouched.
  The eager `Initialize` in the ctor is upstream-canonical; the
  fix at `CanPort`'s seam neutralises the timing without modifying
  the driver's contract.
- **`Infrastructure.Protocol/Hardware/IPcanDriver.cs`** — untouched.
  The port abstraction stays minimal.
- **`tests/ButtonPanelTester.Tests/Integration/Can/CanLinkServiceLifecycleTests.fs`** —
  untouched (F4). Scripts `CanLinkState` directly through
  `InMemoryCanLink`, skipping the `ConnectionState → CanLinkState`
  translation entirely; cannot regress under the fix.
- **No new hardware-required tests.** AC1/AC2/AC3 run against fakes.
  The live-hardware path stays under
  `PcanLifecycleTests` (#112).

## Acceptance criteria trace

Mapping from the spec's AC# to plan slices:

| Spec AC | Slice | Artefact |
|---|---|---|
| AC1 — RED test against `CanPort` with fake `IPcanDriver` | Slice 1 | `Unit/Can/CanPortCtorTests.fs` (`[<Theory>]` over both `IsConnected` branches; F2 + F6) |
| AC2 — Regression against `PcanCanLink` with fake `ICommunicationPort` | Slice 2 | `Unit/Can/PcanCanLinkColdStartTests.fs` (cold-start `[<Fact>]`) |
| AC3 — Existing fail-fast path preserved | Slice 2 | `Unit/Can/PcanCanLinkColdStartTests.fs` (fail-fast `[<Fact>]`) |
| AC4 — `VENDOR.md` + `VENDOR.sha256` updated | Slice 1 | manifest row + `pwsh ./eng/vendor-protocol-stack.ps1 -RehashOnly` |
| AC5 — Upstream PR against `stem-device-manager` | Out-of-band | PR URL recorded in `VENDOR.md` once opened |
| AC6 — `gate.ps1` passes | Each slice handoff | `work-review.md` records exit code |
| AC7 — Bench-replay confirmation | PR finalisation | recorded in PR description |
