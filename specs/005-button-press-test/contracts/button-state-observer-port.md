# Contract: `IButtonStateObserver` Port

**Spec**: [../spec.md](../spec.md) | **Research**: [../research.md](../research.md) §R5 |
**Status**: living (the service codes against this port)

A new RX observation seam over the existing CAN boundary (`ICanFrameStream`), mirroring spec-003's
`IWhoIAmObserver`. No new external boundary — the port is consumed by `ButtonPressTestService` and
backed by a production adapter and a virtual adapter (Constitution III).

> **Re-keyed to the directed-id heartbeat (fix #270, Session 2026-06-24).** A baptized panel is
> silent on WHO_I_AM (`AAS_STAND_BY`; `CORRECTIONS.md` §C1) and heartbeats its button-state on a
> **directed CAN ID** whose machineType byte (bits 23-16) identifies the variant. The observer
> therefore accepts a frame **iff its CAN ID decodes to a known `Marketing` variant** (the broadcast
> id `0x1FFFFFFF` → `0xFF`/Virgin and the tool SRID `0x00000008` → `0x00`/Unknown are dropped for
> free), and emits a `ButtonStateObservation` carrying that variant alongside the frame. A
> button-state observation arriving therefore **is** the evidence that a baptized panel of that
> variant is present — the consumer keys observability off frame recency, not WHO_I_AM discovery.

## Port (`Core/Can/Ports.fs`)

```fsharp
/// Decoded SP_APP VAR_WRITE button-state heartbeats from the panel under test, each carrying the
/// MarketingVariant decoded from its directed CAN ID. Hot observable; late subscribers do not
/// replay. See button-state-wire-format.md.
type IButtonStateObserver =
    abstract member ButtonStateObserved : IObservable<ButtonStateObservation>
```

```fsharp
/// Core/Can/ButtonStateObservation.fs — the emitted envelope.
type ButtonStateObservation =
    { Frame: ButtonStateFrame          // the decoded VAR_WRITE (data-model.md §1)
      Variant: MarketingVariant }      // decoded from (CanId >>> 16) &&& 0xFF (data-model.md §1)
```

- Emits one `ButtonStateObservation` per accepted VAR_WRITE arriving on a **directed CAN ID whose
  machineType decodes to a `Marketing` variant**, on a recognised button-state address; the
  broadcast id (→ Virgin), the tool SRID (→ Unknown), the virgin sentinel address `0x80FE`, and
  non-button addresses are dropped.
- Reassembles **per source CAN ID** (one `PacketReassembler` per id — different panels never share a
  fragment buffer).
- Hot, fan-out via `SubjectFanOut<ButtonStateObservation>`; thread-safe, callback not held under the
  lock (spec-002/003 `SubjectFanOut` precedent).
- Emission carries the raw bitmap; **edge detection is the consumer's job** (the FSM service runs the
  `pressEdges` detector across consecutive frames). The observer is stateless w.r.t. press/release.

## Adapters

| Adapter | Project | Role |
|---|---|---|
| `ButtonStateReassemblyObserver` | `Infrastructure/Can` (`net10.0-windows`) | subscribes `ICanFrameStream.RawFramesReceived`, accepts a frame iff its CAN ID decodes to a `Marketing` variant, reassembles per source CAN ID, filters command `0x00:0x02` + button-state address set, calls `ButtonStateFrame.parse`, republishes a `ButtonStateObservation` (frame + variant) |
| `InMemoryButtonStateObserver` | `Tests/Fakes/Can` (`net10.0`) | `EmitObservation(obs)` (and a frame convenience) pushes synchronously to subscribers — deterministic test driver (mirror `InMemoryWhoIAmObserver`) |

## Consumed surfaces (not modified)

| Surface | Owner | Use |
|---|---|---|
| `ICanFrameStream.RawFramesReceived` | spec-002/003 | raw frame input |
| `PacketReassembler` | vendored (#111) | SP_APP reassembly (reused, not modified) |
| `VariantDecoder.decode` / `MarketingVariant` | spec-003 | the CAN-ID machineType → variant decode the accept rule keys on (fix #270) |
| `ICanLinkService` / `CanLinkState` | spec-002 | Connected gate + link-lost interruption |
| `IClock` | spec-002 | per-button timeout deadline; **button-state-frame recency** (observability + panel-loss, fix #270) |

> **No longer consumed:** `IPanelDiscoveryService` / `PanelsOnBus`. A baptized panel never appears in
> discovery (silent on WHO_I_AM), so the button-press path keys observability and panel-loss off
> button-state-frame recency instead (fix #270).
