module Stem.ButtonPanelTester.Tests.Integration.Can.DiscoveryE2ETests

open System
open System.Collections.Generic
open System.Threading
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Services.Can
open Stem.ButtonPanelTester.Tests.Fakes
open Stem.ButtonPanelTester.Tests.Fakes.Can

/// Example-based integration tests for the live WHO_I_AM ingest pipeline
/// in `PanelDiscoveryService` (spec-003 B1, T010). Drives a real
/// `CanLinkService` (wrapping `InMemoryCanLink`) + the synchronous
/// `InMemoryCanFrameStream.Emit` + `FrozenClock` so the
/// filter → parse → coalesce → publish path is exercised deterministically,
/// one frame at a time, without touching real PEAK hardware.
///
/// Coverage:
///   - (a) Connected + one broadcast → one decoded row, ≥1 change.
///   - (b) re-broadcast of the same UUID after a clock advance coalesces
///         in place and advances `LastSeen` (FR-002 / SC-002).
///   - (c) two distinct UUIDs → two rows.
///   - (d) broadcast with a wrong-length payload is dropped silently and
///         does NOT flip the link to Error (FR-007).
///   - (e) a valid frame on a non-broadcast id is ignored.
///   - (f) a valid broadcast while the link is not Connected is dropped
///         (FR-007).
///   - (g) one broadcast publishes exactly once, within the SC-001
///         latency budget (trivial under `FrozenClock`).

// --- fixtures ---

let private fixedNow =
    DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero)

let private fixedAdapter : AdapterIdentification =
    { ChannelName = "PCAN-USB (1)"
      DeviceId = "0x01"
      BaudrateBps = 250_000 }

/// Broadcast arbitration id every WHO_I_AM frame rides, per
/// `who-i-am-wire-format.md`.
let private broadcastId = 0x1FFFFFFFu

/// A real `CanLinkService` (wrapping `InMemoryCanLink`) driven to
/// `Connected` via `InitializeAsync`.
let private connectedLink (clock: IClock) : ICanLinkService =
    let link =
        InMemoryCanLink(seq { (Connected(fixedAdapter, fixedNow), TimeSpan.Zero) })

    let svc =
        CanLinkService(link, clock, NullLogger<CanLinkService>.Instance)

    (svc :> ICanLinkService).InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()
    svc :> ICanLinkService

/// A link left in `Initializing` (never driven to Connected).
let private notConnectedLink (clock: IClock) : ICanLinkService =
    CanLinkService(InMemoryCanLink(Seq.empty), clock, NullLogger<CanLinkService>.Instance)
    :> ICanLinkService

/// Build a valid 15-byte WHO_I_AM frame via the real encoder. The
/// machine-type byte is `0xFF` (virgin); the fwType is the 12 V
/// `0x0004` hardware variant.
let private whoIam (canId: uint32) (u0, u1, u2) : RawCanFrame =
    let payload =
        WhoIAmFrame.encode
            { MachineType = MachineTypeByte 0xFFuy
              FwType = FwType 0x0004us
              Uuid = PanelUuid(u0, u1, u2) }

    { CanId = canId
      Payload = ReadOnlyMemory(payload)
      ReceivedAt = fixedNow }

/// Subscribe to the change feed and accumulate every published snapshot.
let private collect (svc: IPanelDiscoveryService) =
    let seen = List<PanelsOnBus>()
    svc.PanelsOnBusChanged |> Observable.subscribe (fun m -> seen.Add m) |> ignore
    seen

// --- (a) one broadcast adds one decoded row ---

[<Fact>]
let Ingest_ConnectedSingleBroadcast_AddsOneRowWithDecodedVariant () =
    let clock = FrozenClock(fixedNow)
    let link = connectedLink (clock :> IClock)
    let stream = InMemoryCanFrameStream(Seq.empty)

    let svc =
        new PanelDiscoveryService(stream, link, clock) :> IPanelDiscoveryService

    let changes = collect svc

    let uuid = (0x177Cu, 0x126Du, 0x7308u)
    stream.Emit(whoIam broadcastId uuid)

    Assert.Equal(1, svc.PanelsOnBus.Count)
    let row = svc.PanelsOnBus.[PanelUuid uuid]
    Assert.Equal<VariantIdentity>(VariantDecoder.decode (MachineTypeByte 0xFFuy), row.VariantIdentity)
    Assert.True(changes.Count >= 1)

// --- (b) re-broadcast coalesces and advances LastSeen ---

[<Fact>]
let Ingest_SameUuidReBroadcastAfterAdvance_CoalescesAndAdvancesLastSeen () =
    let clock = FrozenClock(fixedNow)
    let link = connectedLink (clock :> IClock)
    let stream = InMemoryCanFrameStream(Seq.empty)

    let svc =
        new PanelDiscoveryService(stream, link, clock) :> IPanelDiscoveryService

    let uuid = (0x1u, 0x2u, 0x3u)
    stream.Emit(whoIam broadcastId uuid)
    Assert.Equal<DateTimeOffset>(fixedNow, svc.PanelsOnBus.[PanelUuid uuid].LastSeen)

    clock.Advance(TimeSpan.FromSeconds 3.0)
    stream.Emit(whoIam broadcastId uuid)

    Assert.Equal(1, svc.PanelsOnBus.Count)
    Assert.Equal<DateTimeOffset>(fixedNow.AddSeconds 3.0, svc.PanelsOnBus.[PanelUuid uuid].LastSeen)

// --- (c) two distinct UUIDs → two rows ---

[<Fact>]
let Ingest_TwoDistinctUuids_AddsTwoRows () =
    let clock = FrozenClock(fixedNow)
    let link = connectedLink (clock :> IClock)
    let stream = InMemoryCanFrameStream(Seq.empty)

    let svc =
        new PanelDiscoveryService(stream, link, clock) :> IPanelDiscoveryService

    stream.Emit(whoIam broadcastId (0x1u, 0x2u, 0x3u))
    stream.Emit(whoIam broadcastId (0x4u, 0x5u, 0x6u))

    Assert.Equal(2, svc.PanelsOnBus.Count)

// --- (d) wrong-length broadcast dropped, link stays Connected ---

[<Fact>]
let Ingest_BroadcastWrongLengthPayload_DropsSilentlyKeepsLinkConnected () =
    let clock = FrozenClock(fixedNow)
    let link = connectedLink (clock :> IClock)
    let stream = InMemoryCanFrameStream(Seq.empty)

    let svc =
        new PanelDiscoveryService(stream, link, clock) :> IPanelDiscoveryService

    let malformed : RawCanFrame =
        { CanId = broadcastId
          Payload = ReadOnlyMemory(Array.zeroCreate<byte> 14)
          ReceivedAt = fixedNow }

    stream.Emit(malformed)

    Assert.Equal(0, svc.PanelsOnBus.Count)
    Assert.True(
        match link.CurrentState with
        | Connected _ -> true
        | _ -> false
    )

// --- (e) valid frame on a non-broadcast id is ignored ---

[<Fact>]
let Ingest_ValidFrameOnNonBroadcastId_Ignored () =
    let clock = FrozenClock(fixedNow)
    let link = connectedLink (clock :> IClock)
    let stream = InMemoryCanFrameStream(Seq.empty)

    let svc =
        new PanelDiscoveryService(stream, link, clock) :> IPanelDiscoveryService

    stream.Emit(whoIam 0x123u (0x1u, 0x2u, 0x3u))

    Assert.Equal(0, svc.PanelsOnBus.Count)

// --- (f) broadcast while link not Connected is dropped ---

[<Fact>]
let Ingest_LinkNotConnected_DropsBroadcastSilently () =
    let clock = FrozenClock(fixedNow)
    let link = notConnectedLink (clock :> IClock)
    let stream = InMemoryCanFrameStream(Seq.empty)

    let svc =
        new PanelDiscoveryService(stream, link, clock) :> IPanelDiscoveryService

    stream.Emit(whoIam broadcastId (0x1u, 0x2u, 0x3u))

    Assert.Equal(0, svc.PanelsOnBus.Count)

// --- (g) one broadcast publishes exactly once, within latency budget ---

[<Fact>]
let Ingest_ConnectedSingleBroadcast_PublishesOnceWithinLatencyBudget () =
    let clock = FrozenClock(fixedNow)
    let link = connectedLink (clock :> IClock)
    let stream = InMemoryCanFrameStream(Seq.empty)

    let svc =
        new PanelDiscoveryService(stream, link, clock) :> IPanelDiscoveryService

    let changes = collect svc

    let uuid = (0x1u, 0x2u, 0x3u)
    stream.Emit(whoIam broadcastId uuid)

    Assert.Equal(1, changes.Count)
    let row = svc.PanelsOnBus.[PanelUuid uuid]
    Assert.True(row.LastSeen - fixedNow <= TimeSpan.FromSeconds 6.0)
