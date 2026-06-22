# Contract: `IButtonStateObserver` Port

**Spec**: [../spec.md](../spec.md) | **Research**: [../research.md](../research.md) §R5 |
**Status**: living (the service codes against this port)

A new RX observation seam over the existing CAN boundary (`ICanFrameStream`), mirroring spec-003's
`IWhoIAmObserver`. No new external boundary — the port is consumed by `ButtonPressTestService` and
backed by a production adapter and a virtual adapter (Constitution III).

## Port (`Core/Can/Ports.fs`)

```fsharp
/// Decoded SP_APP VAR_WRITE button-state reports from the panel under test.
/// Hot observable; late subscribers do not replay. See button-state-wire-format.md.
type IButtonStateObserver =
    abstract member ButtonStateObserved : IObservable<ButtonStateFrame>
```

- Emits one `ButtonStateFrame` (`data-model.md` §1) per accepted VAR_WRITE on a recognised
  button-state address; the virgin sentinel `0x80FE` and non-button addresses are dropped.
- Hot, fan-out via `SubjectFanOut<ButtonStateFrame>`; thread-safe, callback not held under the lock
  (spec-002/003 `SubjectFanOut` precedent).
- Emission carries the raw bitmap; **edge detection is the consumer's job** (the FSM service runs the
  `pressEdges` detector across consecutive frames). The observer is stateless w.r.t. press/release.

## Adapters

| Adapter | Project | Role |
|---|---|---|
| `ButtonStateReassemblyObserver` | `Infrastructure/Can` (`net10.0-windows`) | subscribes `ICanFrameStream.RawFramesReceived`, reuses a `PacketReassembler`, filters command `0x00:0x02` + button-state address set, calls `ButtonStateFrame.parse`, republishes |
| `InMemoryButtonStateObserver` | `Tests/Fakes/Can` (`net10.0`) | `Emit(frame)` pushes synchronously to subscribers — deterministic test driver (mirror `InMemoryWhoIAmObserver`) |

## Consumed surfaces (not modified)

| Surface | Owner | Use |
|---|---|---|
| `ICanFrameStream.RawFramesReceived` | spec-002/003 | raw frame input |
| `PacketReassembler` | vendored (#111) | SP_APP reassembly (reused, not modified) |
| `ICanLinkService` / `CanLinkState` | spec-002 | Connected gate + link-lost interruption |
| `IPanelDiscoveryService` / `PanelsOnBus` | spec-003 | selected-panel observability + panel-lost interruption |
| `IClock` | spec-002 | per-button timeout deadline |
