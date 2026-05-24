module Stem.ButtonPanelTester.Tests.Integration.Can.RecoverableToFatalEscalationTests

open System
open System.Collections.Generic
open System.Threading
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Services.Can
open Stem.ButtonPanelTester.Tests.Fakes
open Stem.ButtonPanelTester.Tests.Fakes.Can

/// Integration tests for the Recoverable→Fatal escalation logic in
/// `CanLinkService` per
/// `specs/002-can-link-and-panel-discovery/research.md` R8 (T041).
///
/// Coverage:
///   - Same PEAK status observed twice across an explicit
///     `ReconnectAsync` → second observation upgrades to
///     `Error.Fatal "<cause> persists across reconnect — file bug"`.
///   - Reset-on-success: same status → `Connected` → same status →
///     second observation is still `Recoverable`, NOT `Fatal`.

// --- fixtures ---

let private fixedNow =
    DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero)

let private peakStatusCause = "PEAK status 0x40000"

let private fixedAdapter: AdapterIdentification =
    { ChannelName = "PCAN-USB (1)"
      SerialNumber = "00000001"
      BaudrateBps = 250_000 }

let private collectStates (service: ICanLinkService) : List<CanLinkState> =
    let collected = List<CanLinkState>()

    let _ =
        service.LinkStateChanged
        |> Observable.subscribe (fun state -> collected.Add state)

    collected

let private newService (script: seq<CanLinkState * TimeSpan>) =
    let link = InMemoryCanLink(script)
    let clock = FrozenClock(fixedNow)

    let service =
        CanLinkService(link, clock, NullLogger<CanLinkService>.Instance)

    service :> ICanLinkService, collectStates service

// --- escalation: same cause after reconnect ---

[<Fact>]
let SameRecoverableAfterReconnect_EscalatesToFatalWithPersistsDetail () =
    let t1 = fixedNow
    let t2 = fixedNow.AddSeconds(3.0)

    let script =
        seq {
            (Error(Recoverable peakStatusCause, t1), TimeSpan.Zero)
            (Error(Recoverable peakStatusCause, t2), TimeSpan.Zero)
        }

    let service, observed = newService script

    service.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()
    service.ReconnectAsync(CancellationToken.None).GetAwaiter().GetResult()

    Assert.Equal(2, observed.Count)
    Assert.Equal<CanLinkState>(Error(Recoverable peakStatusCause, t1), observed.[0])

    let expectedDetail =
        sprintf "%s persists across reconnect — file bug" peakStatusCause

    Assert.Equal<CanLinkState>(Error(Fatal expectedDetail, t2), observed.[1])
    Assert.Equal<CanLinkState>(Error(Fatal expectedDetail, t2), service.CurrentState)

// --- reset-on-success ---

[<Fact>]
let RecoverableThenConnectedThenSameRecoverable_StaysRecoverable () =
    let t1 = fixedNow
    let openedAt = fixedNow.AddSeconds(2.0)
    let t3 = fixedNow.AddSeconds(7.0)

    let script =
        seq {
            (Error(Recoverable peakStatusCause, t1), TimeSpan.Zero)
            (Connected(fixedAdapter, openedAt), TimeSpan.Zero)
            (Error(Recoverable peakStatusCause, t3), TimeSpan.Zero)
        }

    let service, observed = newService script

    service.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()
    service.ReconnectAsync(CancellationToken.None).GetAwaiter().GetResult()
    service.ReconnectAsync(CancellationToken.None).GetAwaiter().GetResult()

    Assert.Equal(3, observed.Count)
    Assert.Equal<CanLinkState>(Error(Recoverable peakStatusCause, t1), observed.[0])
    Assert.Equal<CanLinkState>(Connected(fixedAdapter, openedAt), observed.[1])
    Assert.Equal<CanLinkState>(Error(Recoverable peakStatusCause, t3), observed.[2])

    Assert.Equal<CanLinkState>(Error(Recoverable peakStatusCause, t3), service.CurrentState)

// --- defence-in-depth: ReconnectAsync without a prior Recoverable does not falsely arm the tracker ---

[<Fact>]
let ReconnectThenFirstRecoverable_StaysRecoverable () =
    // The user might click Reconnect at any time (e.g., to recover
    // from a Disconnected). That click MUST NOT pre-arm the
    // escalation tracker so the very first Recoverable observation
    // surfaces as Fatal.
    let t1 = fixedNow.AddSeconds(2.0)

    let script =
        seq {
            (Disconnected(NoAdapterPresent, fixedNow), TimeSpan.Zero)
            (Error(Recoverable peakStatusCause, t1), TimeSpan.Zero)
        }

    let service, observed = newService script

    service.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()
    service.ReconnectAsync(CancellationToken.None).GetAwaiter().GetResult()

    Assert.Equal(2, observed.Count)
    Assert.Equal<CanLinkState>(Disconnected(NoAdapterPresent, fixedNow), observed.[0])
    Assert.Equal<CanLinkState>(Error(Recoverable peakStatusCause, t1), observed.[1])
