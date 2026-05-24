# Contract: `ICanFrameStream` port

**Phase 1 output for**: [../plan.md](../plan.md)
**Implements**: FR-007 (receive side; observation upstream of the panel-list view)

## Port definition (F#)

Lives in `src/ButtonPanelTester.Core/Can/Ports.fs` (alongside `ICanLink`).

```fsharp
[<Struct>]
type RawCanFrame = {
    CanId : uint32                     // arbitration ID (extended-frame format)
    Payload : ReadOnlyMemory<byte>     // 0–8 bytes for classic CAN at 250 kbps
    ReceivedAt : DateTimeOffset        // adapter-provided timestamp; falls back to wall clock if absent
}

type ICanFrameStream =
    /// Hot observable of every raw CAN frame received while the link is up.
    /// Frames received while the link is down are dropped silently (no buffering across reconnects).
    abstract member RawFramesReceived : IObservable<RawCanFrame>
```

## Adapter contract

### Production: `PcanCanFrameStream`

`src/ButtonPanelTester.Infrastructure/Can/PcanCanFrameStream.fs`

- Subscribes to the vendored stack's `PacketReceived` event on `CanPort`.
- Translates `CANPacketEventArgs` → `RawCanFrame`: `CanId` from `ArbitrationId`, `Payload` from `Data` wrapped as `ReadOnlyMemory<byte>`, `ReceivedAt` from `Timestamp`.
- Uses `ReadOnlyMemory<byte>` to avoid per-frame allocation on the receive thread (see [../research.md](../research.md) R7).

### Virtual: `InMemoryCanFrameStream`

`tests/ButtonPanelTester.Tests/Fakes/Can/InMemoryCanFrameStream.fs`

- Constructor takes a scripted `seq<RawCanFrame * TimeSpan>` (each frame paired with its delay relative to the previous frame).
- `Start()` walks the script, emitting each frame after the corresponding delay.
- Used by `PanelsOnBusCoalescing`, `PruningCorrectness`, and every `Integration/Can/` test.

## Frame contract

Spec-002 only acts on frames matching **both** of:
- `CanId = 0x1FFFFFFF` (extended-frame broadcast — `SRID_BROADCAST` in panel firmware), AND
- `Payload.Length = 15` (the WHO_I_AM wire size — see [who-i-am-wire-format.md](./who-i-am-wire-format.md)).

All other frames are dropped at the `CanLinkService` layer (NOT the port — the port is a generic receive surface that later specs reuse for transmit-side responses).

## Threading

- `RawFramesReceived` fires on the vendored stack's read thread. The service layer (`CanLinkService`) marshalls to its own pruning timer thread and then to the GUI via FuncUI's `Cmd.ofSub`.
- The `ReadOnlyMemory<byte>` payload is only valid for the duration of the `OnNext` callback. Subscribers that need to retain the bytes MUST copy (`payload.Span.ToArray()` or `payload.ToArray()`).
- Property tests' fake stream emits on a thread-pool worker, so concurrency hazards in service code are exercised by every property test that uses the fake.

## Test coverage targets

| Test | Layer | File |
|---|---|---|
| `WhoIAmFrameRoundtrip` (FsCheck) | Property | `Property/Can/WhoIAmFrameProperties.fs` |
| `WhoIAmFrameRejectsMalformed` (FsCheck) | Property | `Property/Can/WhoIAmFrameProperties.fs` |
| `PanelsOnBusCoalescing` (FsCheck) | Property | `Property/Can/PanelsOnBusProperties.fs` |
| `PanelsOnBusLastSeenMonotonic` (FsCheck) | Property | `Property/Can/PanelsOnBusProperties.fs` |
| `VariantByteMappingTotal` (FsCheck) | Property | `Property/Can/VariantDecoderProperties.fs` |
| Fixture-based parse tests | Unit | `Unit/Can/WhoIAmFrameFixtureTests.fs` |
| `DiscoveryE2EThroughCanLinkService` | Integration | `Integration/Can/DiscoveryE2ETests.fs` |
| Bench `ObserveLiveVirginPanel` | Hardware E2E | `Integration/Can/Hardware/DiscoveryHardwareTests.fs` (`Category=Hardware`) |
