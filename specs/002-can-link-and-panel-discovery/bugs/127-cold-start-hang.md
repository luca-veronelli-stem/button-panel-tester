# #127 — PcanCanLink cold-start hang (F6, FR-001 violation)

- Issue: <https://github.com/luca-veronelli-stem/button-panel-tester/issues/127>
- Parent spec: `specs/002-can-link-and-panel-discovery/spec.md`
- Parent requirements: **FR-001** (CAN open follows dictionary boot) +
  **SC-001** (Connected within 2 s of dictionary boot completion).
- PR: <https://github.com/luca-veronelli-stem/button-panel-tester/pull/134>
- Severity: HIGH (bench-blocking)

This is a **defect against the existing spec-002**, not a new feature.
The spec is unchanged. What follows is the slim spec + plan + tasks
for the fix, per the resolve-ticket protocol.

## Defect

### Symptom

When the app launches with the PEAK PCAN-USB Pro FD adapter plugged
in on a bus where no panel is powered, the CAN status row sits on
`Initializing…` indefinitely (bench-confirmed at 1m 34s with no
transition). FR-001 expects `Initializing → Connected` within the
SC-001 budget (≤ 2 s after dictionary boot completes); the row never
transitions at all.

When the user then clicks the Reconnect button, the same install
reaches `Connected` within the budget. So the success path through
`ReconnectAsync` is intact — only the cold-start path through
`CanLinkService.InitializeAsync → PcanCanLink.OpenAsync → CanPort.ConnectAsync`
is broken.

### Evidence (from `%LOCALAPPDATA%\Stem\ButtonPanelTester\logs\app.log`)

```
10:42:15.85 INFO  CanLinkService.InitializeAsync at 250000 bps
10:42:16.99 INFO  Dictionary warm-up succeeded
[nothing — no completion log for OpenAsync, no LinkStateChanged
 emission, no PEAK status]
```

No `WARN  PcanCanLink.OpenAsync failed` line (the failure log at
`PcanCanLink.fs:291`) and no follow-up `Connected` log — the call
ran silently to completion without surfacing a state transition.

### Root cause (Phase 1)

The defect sits in **`CanPort`'s constructor**: the initial `_state`
is computed as a snapshot of `driver.IsConnected`, which is set
*before* any subscriber can attach to `StateChanged`. The snapshot is
inherently lossy — any subscriber attaching after construction has
already missed the only state transition the port will ever emit on
the cold-start success path.

Concrete sequence (verified by reading the vendored sources):

1. **`PCANManager` constructor calls `Initialize` eagerly.** With the
   adapter plugged in, `PCANBasic.Initialize(Channel, _currentBaudRate)`
   returns `PCAN_ERROR_OK`. The setter assigns `IsConnected = true`
   (`Infrastructure.Protocol/Hardware/PCANManager.cs:88, 97-100`) and
   fires `ConnectionStatusChanged(true)`. No subscriber exists at this
   point; the event is dropped on the floor.

2. **`CanPort` constructor snapshots driver state.** Its body sets
   `_state = driver.IsConnected ? Connected : Disconnected` *before*
   attaching `ConnectionStatusChanged`
   (`Infrastructure.Protocol/Hardware/CanPort.cs:46-52`). With the
   adapter plugged in, `_state` is `Connected` on construction. No
   `Transition(Connected)` runs, so `CanPort.StateChanged` does not
   fire.

3. **`PcanCanLink.openInternal → port.ConnectAsync` early-returns
   on `Connected`.** `ConnectAsync` checks `if (State == Connected)
   return;` (`CanPort.cs:73`) and exits without firing any
   transition. `PcanCanLink`'s `onStateChanged` handler is never
   invoked, so `LinkStateChanged` never emits `Connected` and
   `currentState` stays at `Initializing`
   (`PcanCanLink.fs:141, 192-195, 270-295`).

The reconnect path works because `CloseAsync` synchronously transitions
the port to `Disconnected` (and `_driver.Disconnect()` flips
`IsConnected = false`), so the subsequent `OpenAsync` polls
`IsConnected` until `PCANManager`'s reconnect-monitor task re-flips
it and fires `ConnectionStatusChanged` to the now-subscribed
`CanPort`, which fires `StateChanged(Connected)` to the now-subscribed
`PcanCanLink`. The cold-start path is unique in racing the first
`IsConnected = true` against subscription.

In short: **`CanPort` claims to be `Connected` before anyone has
asked it to connect — a category error that no adjacent layer can
correctly compensate for.**

## Fix locus

The fix lives in **`CanPort.cs`**, not in `PcanCanLink.fs`.

`Infrastructure.Protocol/Hardware/CanPort.cs` is verbatim-vendored
from `stem-device-manager` SHA `4700c2db65c858f53b4796971a174508b99bce0a`
(`VENDOR.md`). The vendoring discipline tracks every local
modification in the `VENDOR.md` "Local modifications" table and
gates drift via `VENDOR.sha256`. There is precedent for a local
patch ahead of an upstream merge: `PCANManager.cs` already carries a
`+78/-9` local diff for `IAsyncDisposable`, mirrored by an open PR
against `stem-device-manager` (`#117` upstream). The same pattern
applies here.

**The one-line change** (CanPort.cs constructor):

```diff
-        _state = (int)(driver.IsConnected
-            ? ConnectionState.Connected
-            : ConnectionState.Disconnected);
+        _state = (int)ConnectionState.Disconnected;
```

After the change, `ConnectAsync` polls `_driver.IsConnected` on the
first attempt of its existing poll loop. If the driver is already
connected, the loop calls `Transition(ConnectionState.Connected)` on
attempt 0, which fires `StateChanged(Connected)` to any subscriber
attached after construction — including `PcanCanLink`. The fix is
small, surgical, and preserves every existing contract:

- Idempotent `ConnectAsync` from `Connected`: no caller relies on the
  ctor-time snapshot (PcanCanLink always calls ConnectAsync first;
  the vendored `ProtocolService` only constructs `CanPort` through
  the same lifecycle path).
- Idempotent `DisconnectAsync` from `Disconnected`: unchanged.
- No semantics change for any code path that explicitly awaits
  `ConnectAsync`.

### Why not work around in `PcanCanLink`

Compensating in `PcanCanLink` — e.g., synthesising a `Connected`
emission when `port.State == Connected` post-`ConnectAsync` — would:

- Add code that exists solely to mask a defect upstream.
- Couple `PcanCanLink` to `CanPort`'s constructor-time initialisation
  policy, leaking the defect's mental model across the port boundary.
- Become dead weight when `#111` retires the vendored stack toward
  `Stem.Communication`.
- Let the latent defect persist in `stem-device-manager` for the
  next consumer to rediscover.

The vendoring discipline exists to keep us synchronised with
upstream, not to forbid upstream fixes. Routing this through the
"local modification + upstream PR" precedent is what the discipline
is *for*.

## Acceptance criteria

- AC1 — **RED unit test against `CanPort`** with a fake `IPcanDriver`
  whose `IsConnected` is `true` from construction. The test attaches
  a `StateChanged` handler after constructing the port, then calls
  `ConnectAsync`, and asserts that the handler receives
  `ConnectionState.Connected` exactly once. This test must fail on
  the pre-fix `main` and pass after the one-line change.
- AC2 — **Regression unit test against `PcanCanLink`** with a fake
  `ICommunicationPort` whose post-`ConnectAsync` transitions
  exercise the cold-start contract end-to-end:
  `Initializing → Connected(_, _)` emitted exactly once on the first
  `OpenAsync`; `CurrentState` reads `Connected` consistently.
- AC3 — The existing fail-fast path (adapter absent, `ConnectAsync`
  raises) continues to surface `Error(Recoverable, _)` via
  `LinkStateChanged` exactly once. Regression-tested.
- AC4 — `VENDOR.md` "Local modifications" table updated with a row
  for `Hardware/CanPort.cs` (lines, why, upstream PR URL).
  `VENDOR.sha256` regenerated via
  `pwsh ./eng/vendor-protocol-stack.ps1 -RehashOnly`. The
  pre-commit hash check passes.
- AC5 — Upstream PR opened against `luca-veronelli-stem/stem-device-manager`
  with the same one-line change + a regression test in upstream's
  shape (xUnit C# against a fake driver). PR URL recorded in
  `VENDOR.md`. (The upstream PR does not need to be merged before
  #127 lands locally — same gate the IAsyncDisposable mod uses.)
- AC6 — `llm/reviews/gate.ps1` passes (build + tests + format + lake).
- AC7 — Bench-replay: launch the app with the PEAK adapter plugged
  in and bus silent; status row reaches `Connected` within the
  SC-001 budget (≤ 2 s post dictionary warm-up). Recorded in the PR
  description.

## Explicit exclusions

- Spec-002 (`FR-001`, `SC-001`, contracts, `research.md`) is unchanged.
- No change to `Infrastructure.Can/PcanCanLink.fs` or
  `Services.Can/CanLinkService.fs`. The existing translation +
  R8-escalation logic handle a correct `Connected` emission already;
  the bug is the missing emission, not the handling of it.
- No change to `PCANManager.cs` or `IPcanDriver.cs`. The eager
  `Initialize` in `PCANManager`'s ctor is upstream-canonical; the
  fix at `CanPort`'s seam neutralises its effect on the public
  contract without touching it.
- No new hardware-required tests. AC1 and AC2 run against fakes; the
  existing skipped `PcanLifecycleTests` cover the live path
  (tracked separately by `#112`).
