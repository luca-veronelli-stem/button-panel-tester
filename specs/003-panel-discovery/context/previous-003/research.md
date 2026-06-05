# Research: Panel Discovery via Passive WHO_I_AM Observation

**Phase 0 output for**: [plan.md](./plan.md)

This document records the discovery-side Phase 0 decisions. Cross-cutting decisions (vendor inventory, port-shape split, IObservable, Lean conventions, composition with feat-001) live in [`../002-can-link-lifecycle/research.md`](../002-can-link-lifecycle/research.md). Decisions specific to the discovery pipeline are extracted below.

---

## R2 — Spec-003 sits BELOW PacketDecoder

**Decision**: spec-003 consumes raw `RawCanFrame { canId; payload; timestamp }` directly from `ICanFrameStream`, filters for CAN ID `0x1FFFFFFF`, and parses the 15-byte WHO_I_AM payload in F# (`Core/Can/WhoIAmFrame.fs`). The vendored `PacketDecoder` is **not invoked** in spec-003. The `KnownStemCommands` / `KnownProtocolAddresses` modules described in CORRECTIONS.md §C5 are deferred to spec-004+.

**Rationale**: WHO_I_AM is an auto-address-layer frame, not an application-layer protocol command. Its CAN ID is fixed (`SRID_BROADCAST = 0x1FFFFFFF`) and its 15-byte payload (`machineType`, `fwType`, `uuid0..2`) is parsable without command-table lookup. `PacketDecoder` would synthesize an "unknown command" event for it anyway (verified in Phase 0 mining: `PacketDecoder.cs:92-105` synthesizes for missing commands). Skipping `PacketDecoder` for spec-003 means:
  - The Lean parse/encode round-trip for `WhoIAmFrame` is self-contained — no dependency on a Lean model of `PacketDecoder`.
  - Spec-004's baptize flow (the first to need command resolution) is the natural place to introduce both the hardcoded protocol metadata and the `PacketDecoder` wiring.

**Alternatives considered**: wire `PacketDecoder` anyway for symmetry (rejected, costs an extra stopgap); skip the vendored stack entirely and parse against `Peak.PCANBasic.NET` (rejected, wastes upstream hardening — see lifecycle spec's R1 alt 2).

**Update to CORRECTIONS.md** (non-blocking): a follow-up paragraph in §C5 noting that the hardcoded-protocol-metadata stopgap is a spec-004+ concern.

---

## R7 — `RawCanFrame` shape (struct + ReadOnlyMemory)

**Decision**: `RawCanFrame` is an F# struct record:

```fsharp
[<Struct>]
type RawCanFrame = {
    CanId : uint32
    Payload : System.ReadOnlyMemory<byte>
    ReceivedAt : System.DateTimeOffset
}
```

Frame production rate on a fully-loaded 250 kbps bus is ≈ 500 frames/s typical (theoretical max ≈ 3000 fps; practical traffic on this bench is far lower). Struct + `ReadOnlyMemory<byte>` avoids per-frame heap allocation on the receive thread.

**Rationale**: GC pressure on the receive thread can cause UI hitches. A struct record costs zero allocation; `ReadOnlyMemory<byte>` lets the vendored stack hand us a pooled buffer without forcing a copy at the port boundary. For the WHO_I_AM filter alone (≈ 1 frame per panel per ~6 s) this is overkill — but the same port serves spec-004+ where transmit-side framing produces denser bursts.

**Alternatives considered**: reference record + `byte[]` (rejected, allocates ≈ 30 KB/s of garbage at fully-loaded bus); raw tuple (rejected, same problem); pre-allocated pool (rejected, premature optimisation).

**Caveat**: the `ReadOnlyMemory<byte>` payload is only valid for the duration of the `OnNext` callback. Subscribers that need to retain the bytes MUST copy (documented in [contracts/can-frame-stream-port.md](./contracts/can-frame-stream-port.md)).

---

## R5 — Pruning loop architecture (timer in CanLinkService)

**Lifecycle hosts the timer; discovery owns the logic.** The pruning timer lives inside `CanLinkService.fs` (which cohabits lifecycle + discovery, see spec-002 plan §Project Structure).

**Decision**: `CanLinkService` owns a single `System.Threading.Timer` ticking at 1 s. On each tick, it computes `now - lastSeen > 15s` per row and removes stale rows from the `PanelsOnBus` map. The pruning is observable through a `PanelsOnBusChanged : IObservable<PanelsOnBus>` event that the GUI subscribes to.

**Rationale**: pruning is a temporal concern owned by the service layer, not by the port. A 1 s tick is fine-grained enough to feel responsive (worst-case 1 s lag past the 15 s threshold) without thrashing the UI. The single timer avoids the per-row timer-leak class of bug.

**Alternatives considered**: Reactive `Throttle`/`Sample` (rejected, requires `System.Reactive`); lazy-on-read pruning (rejected, no observable signal); on-every-frame pruning (rejected, bursts cause storms); 100 ms tick (rejected, no benefit over 1 s).

---

## Cross-references

See [`../002-can-link-lifecycle/research.md`](../002-can-link-lifecycle/research.md) for:
- **R1** — vendor file inventory (shared infrastructure).
- **R3** — two-port split rationale (lifecycle's `ICanLink` + discovery's `ICanFrameStream`).
- **R4** — `IObservable<T>` + hand-rolled subjects.
- **R6** — Lean Phase 2 conventions (shared `[[lean_lib]]`).
- **R8** — Error-state classification escalation (lifecycle-owned).
- **R9** — composition with feat-001's main window.
