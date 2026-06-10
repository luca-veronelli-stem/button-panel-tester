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

/// Example-based integration tests for the WHO_I_AM ingest pipeline in
/// `PanelDiscoveryService` (spec-003 B1, T010; re-sourced onto the reassembly
/// observer in R3, T040). Drives a real `CanLinkService` (wrapping
/// `InMemoryCanLink`) + the synchronous `InMemoryWhoIAmObserver.Emit` +
/// `FrozenClock` so the coalesce → publish path is exercised deterministically,
/// one decoded frame at a time, without touching real PEAK hardware. The
/// reassembly adapter (R2) owns reassembly/command/parse, so the service now
/// receives already-decoded `WhoIAmFrame`s — the wrong-length / non-broadcast
/// drop cases moved to `WhoIAmReassemblyObserverTests` (T037).
///
/// Coverage:
///   - (a) Connected + one observation → one decoded row, ≥1 change.
///   - (b) re-observation of the same UUID after a clock advance coalesces
///         in place and advances `LastSeen` (FR-002 / SC-002).
///   - (c) two distinct UUIDs → two rows.
///   - (f) a valid observation while the link is not Connected is dropped
///         (FR-007).
///   - (g) one observation publishes exactly once, within the SC-001
///         latency budget (trivial under `FrozenClock`).
///   - (h) an adapter-drop (observer emits nothing) then a valid observation,
///         over a real `CanLinkService`, never flips the link to Error (FR-007).

// --- fixtures ---

let private fixedNow =
    DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero)

let private fixedAdapter : AdapterIdentification =
    { ChannelName = "PCAN-USB (1)"
      DeviceId = "0x01"
      BaudrateBps = 250_000 }

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

/// Build a decoded WHO_I_AM frame for the observer feed. The machine-type
/// byte is `0xFF` (virgin); the fwType is the 12 V `0x0004` hardware variant.
let private whoIamFrame (u0, u1, u2) : WhoIAmFrame =
    { MachineType = MachineTypeByte 0xFFuy; FwType = FwType 0x0004us; Uuid = PanelUuid(u0, u1, u2) }

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
    let observer = InMemoryWhoIAmObserver()

    let svc =
        new PanelDiscoveryService(observer, link, clock, NullLogger<PanelDiscoveryService>.Instance) :> IPanelDiscoveryService

    let changes = collect svc

    let uuid = (0x177Cu, 0x126Du, 0x7308u)
    observer.Emit(whoIamFrame uuid)

    Assert.Equal(1, svc.PanelsOnBus.Count)
    let row = svc.PanelsOnBus.[PanelUuid uuid]
    Assert.Equal<VariantIdentity>(VariantDecoder.decode (MachineTypeByte 0xFFuy), row.VariantIdentity)
    Assert.True(changes.Count >= 1)

// --- (b) re-broadcast coalesces and advances LastSeen ---

[<Fact>]
let Ingest_SameUuidReBroadcastAfterAdvance_CoalescesAndAdvancesLastSeen () =
    let clock = FrozenClock(fixedNow)
    let link = connectedLink (clock :> IClock)
    let observer = InMemoryWhoIAmObserver()

    let svc =
        new PanelDiscoveryService(observer, link, clock, NullLogger<PanelDiscoveryService>.Instance) :> IPanelDiscoveryService

    let uuid = (0x1u, 0x2u, 0x3u)
    observer.Emit(whoIamFrame uuid)
    Assert.Equal<DateTimeOffset>(fixedNow, svc.PanelsOnBus.[PanelUuid uuid].LastSeen)

    clock.Advance(TimeSpan.FromSeconds 3.0)
    observer.Emit(whoIamFrame uuid)

    Assert.Equal(1, svc.PanelsOnBus.Count)
    Assert.Equal<DateTimeOffset>(fixedNow.AddSeconds 3.0, svc.PanelsOnBus.[PanelUuid uuid].LastSeen)

// --- (c) two distinct UUIDs → two rows ---

[<Fact>]
let Ingest_TwoDistinctUuids_AddsTwoRows () =
    let clock = FrozenClock(fixedNow)
    let link = connectedLink (clock :> IClock)
    let observer = InMemoryWhoIAmObserver()

    let svc =
        new PanelDiscoveryService(observer, link, clock, NullLogger<PanelDiscoveryService>.Instance) :> IPanelDiscoveryService

    observer.Emit(whoIamFrame (0x1u, 0x2u, 0x3u))
    observer.Emit(whoIamFrame (0x4u, 0x5u, 0x6u))

    Assert.Equal(2, svc.PanelsOnBus.Count)

// --- (f) broadcast while link not Connected is dropped ---

[<Fact>]
let Ingest_LinkNotConnected_DropsBroadcastSilently () =
    let clock = FrozenClock(fixedNow)
    let link = notConnectedLink (clock :> IClock)
    let observer = InMemoryWhoIAmObserver()

    let svc =
        new PanelDiscoveryService(observer, link, clock, NullLogger<PanelDiscoveryService>.Instance) :> IPanelDiscoveryService

    observer.Emit(whoIamFrame (0x1u, 0x2u, 0x3u))

    Assert.Equal(0, svc.PanelsOnBus.Count)

// --- (g) one broadcast publishes exactly once, within latency budget ---

[<Fact>]
let Ingest_ConnectedSingleBroadcast_PublishesOnceWithinLatencyBudget () =
    let clock = FrozenClock(fixedNow)
    let link = connectedLink (clock :> IClock)
    let observer = InMemoryWhoIAmObserver()

    let svc =
        new PanelDiscoveryService(observer, link, clock, NullLogger<PanelDiscoveryService>.Instance) :> IPanelDiscoveryService

    let changes = collect svc

    let uuid = (0x1u, 0x2u, 0x3u)
    observer.Emit(whoIamFrame uuid)

    Assert.Equal(1, changes.Count)
    let row = svc.PanelsOnBus.[PanelUuid uuid]
    Assert.True(row.LastSeen - fixedNow <= TimeSpan.FromSeconds 6.0)

// --- (h) adapter-drop then valid observation never flips the link ---

// (h) FR-007 no-Error-flip, end-to-end over a real CanLinkService. The reassembly adapter has no
//     link-state surface, so neither a dropped input (modeled as the observer emitting nothing — the
//     real-adapter malformed drop is proven in WhoIAmReassemblyObserverTests/T037) nor a valid
//     observation flips the link to Error: the discovery path only READS link state.
[<Fact>]
let Ingest_DropThenObserveWhileConnected_LinkNeverFlips () =
    let clock = FrozenClock(fixedNow)
    let link = connectedLink (clock :> IClock)
    let observer = InMemoryWhoIAmObserver()
    use svc = new PanelDiscoveryService(observer, link, clock, NullLogger<PanelDiscoveryService>.Instance)
    let view = svc :> IPanelDiscoveryService

    // adapter-drop: nothing reaches the service -> no row, link untouched.
    Assert.Equal(0, view.PanelsOnBus.Count)
    Assert.True(match link.CurrentState with Connected _ -> true | _ -> false)

    // a valid observation adds a row and STILL leaves the link Connected.
    observer.Emit(whoIamFrame (0x1u, 0x2u, 0x3u))
    Assert.Equal(1, view.PanelsOnBus.Count)
    Assert.True(match link.CurrentState with Connected _ -> true | _ -> false)
