# Parked: Phase B five-family FSM redesign (CAN link lifecycle)

> **PARKED on 2026-05-29. This is an exploratory redesign, NOT the spec of record.**
> The shipped CAN-link FSM is four-family (`Initializing | Connected | Disconnected | Error`,
> with `Recoverable / Fatal` severity) and is described by the canonical
> [`../spec.md`](../spec.md). These files are kept for their design thinking only.

## What this is

A complete design package for re-carving the shipped four-family CAN-link FSM into a
five-family model (`Idle | Searching | Opening | Open | Faulted`) with named `FaultCause`
constructors, an `AdapterCandidate` payload, multi-adapter iteration, and dictionary-boot
decoupling. Sequenced as a 24-task additive-then-remove migration (`T201`–`T224`) in
[`tasks.md`](./tasks.md); the row-by-row substrate→Phase-B mapping is in
[`migration-map.md`](./migration-map.md).

## Why it was parked

The redesign began mid-work ("the shipped spec needed improvements") and grew into a full
**inline rewrite** of every spec-002 artifact — spec, plan, research, data-model, tasks — for
an FSM that already ships, is tested, and is Lean-proven. That inline rewrite clobbered the
frozen trace of what shipped (restored 2026-05-29) and is the exact *cascading rewrites*
anti-pattern the speckit supersede protocol warns against. The cost — a cross-layer 24-task
migration touching Core, Services, Infrastructure, GUI, Lean, and tests — far exceeds the
value of a cleaner internal DU for a feature that already works. The genuine user-facing
improvements it bundled were extracted as small standalone tickets against the four-family
types (below); the type re-carving itself delivers no user-visible benefit and was dropped.

## Real improvements it bundled — salvaged as standalone tickets on the four-family types

| Improvement | Ticket |
|---|---|
| Mid-session unplug — distinct headline | [#117](https://github.com/luca-veronelli-stem/button-panel-tester/issues/117) |
| Hot-plug auto-reconnect | [#132](https://github.com/luca-veronelli-stem/button-panel-tester/issues/132) |
| Cold-start no-adapter classification | [#136](https://github.com/luca-veronelli-stem/button-panel-tester/issues/136) |
| "Adapter present but channel busy" (PCAN `0x40010`) | [#139](https://github.com/luca-veronelli-stem/button-panel-tester/issues/139) |
| Driver-not-installed download affordance | [#143](https://github.com/luca-veronelli-stem/button-panel-tester/issues/143) |
| Per-transition structured logging | [#148](https://github.com/luca-veronelli-stem/button-panel-tester/issues/148) |
| PEAK status → cause + suggestion (no `FaultCause` DU) | [#150](https://github.com/luca-veronelli-stem/button-panel-tester/issues/150) |
| Multi-adapter iteration (FR-012) | _no ticket yet — open one if a multi-adapter bench need is real_ |

## When to unpark

Revisit **only** if the five-family model becomes genuinely load-bearing — e.g. if spec-003
panel-discovery integration or a real multi-adapter bench requirement cannot be met cleanly on
the four-family types. If that happens, treat this folder as *input* to a fresh,
properly-scoped spec via the speckit supersede protocol (tombstone + new numbered folder).
**Do not inline-rewrite the canonical `spec.md` again** — that is what produced this park.
