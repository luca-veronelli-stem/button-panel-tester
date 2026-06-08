module Stem.ButtonPanelTester.Tests.Integration.Can.LinkLossClearsListTests

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

let private whoIam (canId: uint32) (u0, u1, u2) : RawCanFrame =
    let payload =
        WhoIAmFrame.encode
            { MachineType = MachineTypeByte 0xFFuy
              FwType = FwType 0x0004us
              Uuid = PanelUuid(u0, u1, u2) }
    { CanId = canId; Payload = ReadOnlyMemory(payload); ReceivedAt = fixedNow }

// A scripted link: Connected, then Disconnected. A second InitializeAsync re-Opens
// the InMemoryCanLink, dequeuing the Disconnected step WITHOUT ReconnectAsync's
// synthesized ReconnectPending — so the clear fires on the scripted MidSessionUnplug.
let private connectThenDisconnectLink (clock: IClock) : ICanLinkService =
    let link =
        InMemoryCanLink(seq {
            (Connected(fixedAdapter, fixedNow), TimeSpan.Zero)
            (Disconnected(MidSessionUnplug, fixedNow), TimeSpan.Zero)
        })
    CanLinkService(link, clock, NullLogger<CanLinkService>.Instance) :> ICanLinkService

// (1) Connected -> observe a panel -> Disconnected clears the list immediately,
//     publishing empty exactly once (not after the 15 s prune TTL).
[<Fact>]
let LinkLoss_ConnectedObserveThenDisconnected_ClearsAndPublishesEmptyOnce () =
    let clock = FrozenClock(fixedNow)
    let canLink = connectThenDisconnectLink (clock :> IClock)
    let stream = InMemoryCanFrameStream(Seq.empty)
    // Construct the service BEFORE driving the link, so its LinkStateChanged
    // subscription catches the disconnect transition.
    use svc = new PanelDiscoveryService(stream, canLink, clock)
    let view = svc :> IPanelDiscoveryService

    canLink.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()  // -> Connected
    stream.Emit(whoIam broadcastId (0x1u, 0x2u, 0x3u))
    Assert.Equal(1, view.PanelsOnBus.Count)

    // Subscribe AFTER the observe so we count only the clear publish; then advance
    // the scripted link to Disconnected via a second InitializeAsync.
    let events = List<PanelsOnBus>()
    view.PanelsOnBusChanged |> Observable.subscribe (fun m -> events.Add m) |> ignore
    canLink.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()  // -> Disconnected

    Assert.Equal(0, view.PanelsOnBus.Count)
    Assert.Equal(1, events.Count)
    Assert.True(events.[0].IsEmpty)

// (2) A disconnect with an already-empty list emits no publish (publish-on-change).
[<Fact>]
let LinkLoss_DisconnectWithEmptyList_NoPublish () =
    let clock = FrozenClock(fixedNow)
    let canLink = connectThenDisconnectLink (clock :> IClock)
    let stream = InMemoryCanFrameStream(Seq.empty)
    use svc = new PanelDiscoveryService(stream, canLink, clock)
    let view = svc :> IPanelDiscoveryService

    canLink.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()  // Connected, no panel

    let events = List<PanelsOnBus>()
    view.PanelsOnBusChanged |> Observable.subscribe (fun m -> events.Add m) |> ignore
    canLink.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()  // Disconnected, empty map

    Assert.Empty(view.PanelsOnBus)
    Assert.Empty(events)
