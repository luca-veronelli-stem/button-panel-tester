# Contract: `IMasterSequenceTransmitter` Port

**Status**: living — consumers code against this file; update it if the port drifts.
**Layer**: port in `src/ButtonPanelTester.Core/Can/Ports.fs`; adapters per Constitution III.
**This is the product's first CAN transmit port.** Spec-002 FR-014 / spec-003 FR-009 forbade
transmission; spec-004 owns the TX boundary, and this port is its single entry point.

## Port

```fsharp
/// Transmits the auto-address master-sequence commands this tool is allowed to send
/// (FR-014: claim / reset via WHO_ARE_YOU, address assignment via SET_ADDRESS — nothing else).
/// See contracts/master-sequence-wire-format.md for the wire shapes.
type IMasterSequenceTransmitter =
    /// Broadcasts WHO_ARE_YOU(machineType, fwType, reset). Completes when the write
    /// has been handed to the bus driver successfully; faults on transmission failure.
    abstract member SendWhoAreYouAsync:
        machineType: byte * fwType: uint16 * reset: bool * ct: CancellationToken -> Task

    /// Broadcasts SET_ADDRESS(uuid, spAddress). The uuid is re-encoded byte-identically
    /// to the announcement it was parsed from (byte-echo invariant). Completes on write
    /// completion; faults on transmission failure. There is no reply on the *transmit* path;
    /// the panel's `0x25` ACK and the adoption confirmation arrive asynchronously on the RX
    /// path and are judged by the service (FR-006, corrected 2026-06-17 — not "no reply ever").
    abstract member SendSetAddressAsync:
        uuid: PanelUuid * spAddress: uint32 * ct: CancellationToken -> Task
```

Semantics:

- **Write-completion contract**: a completed task means the message was written to the bus
  (driver accepted all frames), **not** that any panel acted on it. Success/failure of the
  *sequence* is the `BaptismService`'s judgement (FSM), never the port's.
- **Fault mapping**: any adapter exception ⇒ the service's `TransmissionFailure` outcome, naming
  the step (FR-005). The port does not retry; the service does not retry (spec edge case).
- **No queuing/coalescing**: each call is one SP_APP message, sent immediately. Callers serialize
  (the FSM runs at most one attempt; reset's two fwType broadcasts are awaited sequentially).
- **Cancellation**: honors `ct` co-operatively before/between frame writes; cancellation
  surfaces as `OperationCanceledException`, not a `TransmissionFailure`.
- **Caller discipline (not enforced by the port)**: technician-initiated only, link `Connected`
  only (FR-014) — enforced by the enablement predicates + service, property-tested there.

## Adapter pair (Constitution III)

| Adapter | Project | Behaviour |
|---|---|---|
| `ProtocolMasterSequenceTransmitter` | `ButtonPanelTester.Infrastructure/Can` | Encodes payloads per the [wire-format contract](./master-sequence-wire-format.md); synthesizes the built-in `Command` records (`0x00:0x23`, `0x00:0x25`); delegates packet build/CRC/chunk/frame/write to the vendored `IProtocolService.SendCommandAsync` over the shared `CanPort` (the same port instance the RX stream taps — composition root wires the share). |
| `InMemoryMasterSequenceTransmitter` | `ButtonPanelTester.Tests/Fakes/Can` | Records every send (`Sent: (command, payload, timestamp) list` or equivalent) for assertion (`NoSetAddressWithoutMatch`, FR-014 discipline tests); scriptable per-call fault injection for the `TransmissionFailure` paths. |

Adapter verification (CI): the production adapter's frame synthesis is asserted against a fake
`ICommunicationPort` capturing the exact wire frames (spec-003 Phase-C precedent) and the
committed `masterSequenceFixtures.json` byte fixtures. The live-bus proof is the
`Category=Hardware` bench E2E (#112), excluded from default CI.

## What this port is *not*

- Not a general-purpose CAN TX surface — adding any new command to the bus requires a spec that
  amends FR-014 and this contract, not a new method here "while we're at it".
- Not a reply-correlation surface — the master sequence has **no synchronous replies on the
  transmit path**; this port stays fire-and-forget. The panel *does* acknowledge asynchronously
  (the `0x23`/`0x25` application ACKs) and adoption is confirmed by broadcast-silence; those
  signals are observed on the **RX** side by the service, not surfaced here. The confirmation
  rework (2026-06-17, FR-006) consumes the existing `IWhoIAmObserver` for silence and adds a
  separate RX observation for the `0x25` ACK — the transmit port is unchanged.
