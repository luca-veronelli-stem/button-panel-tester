module Stem.ButtonPanelTester.Tests.Integration.Can.CanLinkServiceLifecycleTests

open System
open System.Collections.Generic
open System.Threading
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Services.Can
open Stem.ButtonPanelTester.Tests.Fakes
open Stem.ButtonPanelTester.Tests.Fakes.Can

/// Integration tests for `CanLinkService` per
/// `specs/002-can-link-and-panel-discovery/tasks.md` T040. Wires the
/// service through `InMemoryCanLink` (scripted state sequences) +
/// `FrozenClock` (reused from feat-001's `Fakes/Wiring.fs`) so the
/// lifecycle observable surface is exercised without a real PEAK
/// adapter.
///
/// Coverage:
///   - (a) `Connected` script → service emits `Connected` and
///         `CurrentState` matches.
///   - (b) `Error(Fatal "PEAK driver not installed", _)` script on
///         Open → service forwards the same `Error.Fatal` verbatim
///         (the escalation-from-Recoverable logic lands in T041).
///   - (c) `Disconnected(NoAdapterPresent) → Connected →
///         Disconnected(MidSessionUnplug)` round-trip preserves the
///         observable state at each transition.

// --- fixtures ---

let private fixedNow =
    DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero)

let private fixedClock () = FrozenClock(fixedNow)

let private fixedAdapter : AdapterIdentification =
    { ChannelName = "PCAN-USB (1)"
      SerialNumber = "00000001"
      BaudrateBps = 250_000 }

let private collectStates (service: ICanLinkService) : List<CanLinkState> =
    let collected = List<CanLinkState>()

    let _ =
        service.LinkStateChanged
        |> Observable.subscribe (fun state -> collected.Add state)

    collected

// --- (a) Connected ---

[<Fact>]
let InitializeAsync_ScriptedConnected_EmitsConnectedAndUpdatesCurrentState () =
    let script =
        seq {
            (Connected(fixedAdapter, fixedNow), TimeSpan.Zero)
        }

    let link = InMemoryCanLink(script)
    let clock = fixedClock ()

    let service =
        CanLinkService(link, clock, NullLogger<CanLinkService>.Instance)

    let observed = collectStates service

    (service :> ICanLinkService).InitializeAsync(CancellationToken.None)
        .GetAwaiter().GetResult()

    Assert.Equal<CanLinkState>(Connected(fixedAdapter, fixedNow), (service :> ICanLinkService).CurrentState)
    Assert.Equal(1, observed.Count)
    Assert.Equal<CanLinkState>(Connected(fixedAdapter, fixedNow), observed.[0])

// --- (b) Error.Fatal on Open ---

[<Fact>]
let InitializeAsync_ScriptedFatalDriverMissing_ServiceForwardsErrorFatal () =
    let fatalCause = "PEAK driver not installed"

    let script =
        seq {
            (Error(Fatal fatalCause, fixedNow), TimeSpan.Zero)
        }

    let link = InMemoryCanLink(script)
    let clock = fixedClock ()

    let service =
        CanLinkService(link, clock, NullLogger<CanLinkService>.Instance)

    let observed = collectStates service

    (service :> ICanLinkService).InitializeAsync(CancellationToken.None)
        .GetAwaiter().GetResult()

    Assert.Equal<CanLinkState>(Error(Fatal fatalCause, fixedNow), (service :> ICanLinkService).CurrentState)
    Assert.Equal(1, observed.Count)
    Assert.Equal<CanLinkState>(Error(Fatal fatalCause, fixedNow), observed.[0])

// --- (c) Disconnected → Connected → Disconnected round-trip ---

[<Fact>]
let Lifecycle_DisconnectedConnectedDisconnected_PreservesObservableStateAtEachTransition () =
    let openedAt = fixedNow
    let unpluggedAt = fixedNow.AddSeconds(5.0)

    let script =
        seq {
            (Disconnected(NoAdapterPresent, fixedNow), TimeSpan.Zero)
            (Connected(fixedAdapter, openedAt), TimeSpan.Zero)
            (Disconnected(MidSessionUnplug, unpluggedAt), TimeSpan.Zero)
        }

    let link = InMemoryCanLink(script)
    let clock = fixedClock ()

    let service =
        CanLinkService(link, clock, NullLogger<CanLinkService>.Instance)

    let observed = collectStates service
    let canService = service :> ICanLinkService

    canService.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()
    Assert.Equal<CanLinkState>(Disconnected(NoAdapterPresent, fixedNow), canService.CurrentState)

    canService.ReconnectAsync(CancellationToken.None).GetAwaiter().GetResult()
    Assert.Equal<CanLinkState>(Connected(fixedAdapter, openedAt), canService.CurrentState)

    canService.ReconnectAsync(CancellationToken.None).GetAwaiter().GetResult()
    Assert.Equal<CanLinkState>(Disconnected(MidSessionUnplug, unpluggedAt), canService.CurrentState)

    Assert.Equal(3, observed.Count)
    Assert.Equal<CanLinkState>(Disconnected(NoAdapterPresent, fixedNow), observed.[0])
    Assert.Equal<CanLinkState>(Connected(fixedAdapter, openedAt), observed.[1])
    Assert.Equal<CanLinkState>(Disconnected(MidSessionUnplug, unpluggedAt), observed.[2])
