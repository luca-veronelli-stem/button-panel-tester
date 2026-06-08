module Stem.ButtonPanelTester.Tests.Integration.Can.PruningE2ETests

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

// Helpers mirror DiscoveryE2ETests (that module's are private to it).
let private fixedNow = DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero)

let private fixedAdapter : AdapterIdentification =
    { ChannelName = "PCAN-USB (1)"; DeviceId = "0x01"; BaudrateBps = 250_000 }

let private broadcastId = 0x1FFFFFFFu

let private connectedLink (clock: IClock) : ICanLinkService =
    let link = InMemoryCanLink(seq { (Connected(fixedAdapter, fixedNow), TimeSpan.Zero) })
    let svc = CanLinkService(link, clock, NullLogger<CanLinkService>.Instance)
    (svc :> ICanLinkService).InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()
    svc :> ICanLinkService

let private whoIam (canId: uint32) (u0, u1, u2) : RawCanFrame =
    let payload =
        WhoIAmFrame.encode
            { MachineType = MachineTypeByte 0xFFuy
              FwType = FwType 0x0004us
              Uuid = PanelUuid(u0, u1, u2) }
    { CanId = canId; Payload = ReadOnlyMemory(payload); ReceivedAt = fixedNow }

// (1) row exactly at the TTL boundary survives the tick (kept-iff <= ttl)
[<Fact>]
let Prune_RowExactlyAtTtl_StillPresentAfterTick () =
    let clock = FrozenClock(fixedNow)
    let link = connectedLink (clock :> IClock)
    let stream = InMemoryCanFrameStream(Seq.empty)
    use svc = new PanelDiscoveryService(stream, link, clock)
    let view = svc :> IPanelDiscoveryService
    let uuid = (0x1u, 0x2u, 0x3u)
    stream.Emit(whoIam broadcastId uuid)

    clock.SetTo(fixedNow.AddSeconds 15.0)
    svc.RunPruneTick()

    Assert.True(view.PanelsOnBus.ContainsKey(PanelUuid uuid))

// (2) row past the TTL is pruned and published exactly once
[<Fact>]
let Prune_RowPastTtl_PrunedAndPublishedOnce () =
    let clock = FrozenClock(fixedNow)
    let link = connectedLink (clock :> IClock)
    let stream = InMemoryCanFrameStream(Seq.empty)
    use svc = new PanelDiscoveryService(stream, link, clock)
    let view = svc :> IPanelDiscoveryService
    let uuid = (0x1u, 0x2u, 0x3u)
    stream.Emit(whoIam broadcastId uuid)

    // Subscribe AFTER the observe publish, so we count only the prune publish.
    let pruneEvents = List<PanelsOnBus>()
    view.PanelsOnBusChanged |> Observable.subscribe (fun m -> pruneEvents.Add m) |> ignore

    clock.SetTo(fixedNow.AddSeconds 16.0)
    svc.RunPruneTick()

    Assert.Equal(0, view.PanelsOnBus.Count)
    Assert.Equal(1, pruneEvents.Count)
    Assert.True(pruneEvents.[0].IsEmpty)

// (3) a tick with nothing expiring emits no duplicate PanelsOnBusChanged
[<Fact>]
let Prune_NothingExpired_EmitsNoDuplicate () =
    let clock = FrozenClock(fixedNow)
    let link = connectedLink (clock :> IClock)
    let stream = InMemoryCanFrameStream(Seq.empty)
    use svc = new PanelDiscoveryService(stream, link, clock)
    let view = svc :> IPanelDiscoveryService
    stream.Emit(whoIam broadcastId (0x1u, 0x2u, 0x3u))

    let pruneEvents = List<PanelsOnBus>()
    view.PanelsOnBusChanged |> Observable.subscribe (fun m -> pruneEvents.Add m) |> ignore

    clock.SetTo(fixedNow.AddSeconds 5.0)   // within TTL — nothing expires
    svc.RunPruneTick()

    Assert.Equal(1, view.PanelsOnBus.Count)
    Assert.Empty(pruneEvents)
