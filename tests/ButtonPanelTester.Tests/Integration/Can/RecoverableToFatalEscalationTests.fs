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
/// `specs/002-can-link-and-panel-discovery/research.md` R8 (T041)
/// and the `since`-stickiness rule from FR-002b (issue #130).
///
/// Coverage:
///   - Same PEAK status observed twice across an explicit
///     `ReconnectAsync` → second observation upgrades to
///     `Error.Fatal "<cause> persists across reconnect — file bug"`
///     and the escalated state's `since` carries the FIRST
///     observation's timestamp (FR-002b).
///   - Reset-on-success: same status → `Connected` → same status →
///     second observation is still `Recoverable`, NOT `Fatal`.
///   - `since` stickiness across multiple reconnect-after-failure
///     cycles: every emission for the same root cause carries the
///     original observation's timestamp.
///   - Root cause change resets the `since` anchor: a distinct
///     Recoverable cause starts a fresh cycle (new `since`, no
///     Fatal escalation), whether arrived directly or re-entered
///     through a transient `Disconnected`.
///   - Asymmetric leave-Error semantics: `Connected` resets the
///     tracker (next same-cause Recoverable gets fresh `since`),
///     `Disconnected` preserves it (re-entering the same cause keeps
///     the original `since` and can still escalate).
///
/// Note on FR-003 click-feedback (#131): every `service.ReconnectAsync`
/// call synthesises a `Disconnected(ReconnectPending, clock.UtcNow())`
/// emission BEFORE delegating to the link. The escalation tracker is
/// untouched by Disconnected observations (R8 only mutates the
/// tracker on Recoverable / Connected / explicit reconnect arming),
/// so the synthesised emission is invisible to escalation semantics
/// but shows up at the observable surface. Assertions below include
/// it explicitly between the prior state and the link's next emission.

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

    Assert.Equal(3, observed.Count)
    Assert.Equal<CanLinkState>(Error(Recoverable peakStatusCause, t1), observed.[0])
    // FR-003 click-feedback (#131): synthesised between Recoverable
    // and the next link emission.
    Assert.Equal<CanLinkState>(Disconnected(ReconnectPending, fixedNow), observed.[1])

    let expectedDetail =
        sprintf "%s persists across reconnect — file bug" peakStatusCause

    // FR-002b: the escalated `Fatal` carries the FIRST Recoverable
    // observation's timestamp, not the re-observation's timestamp.
    Assert.Equal<CanLinkState>(Error(Fatal expectedDetail, t1), observed.[2])
    Assert.Equal<CanLinkState>(Error(Fatal expectedDetail, t1), service.CurrentState)

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

    // Two ReconnectAsync calls each insert a synthesised
    // Disconnected(ReconnectPending, fixedNow) per FR-003 (#131)
    // BEFORE the link's next emission.
    Assert.Equal(5, observed.Count)
    Assert.Equal<CanLinkState>(Error(Recoverable peakStatusCause, t1), observed.[0])
    Assert.Equal<CanLinkState>(Disconnected(ReconnectPending, fixedNow), observed.[1])
    Assert.Equal<CanLinkState>(Connected(fixedAdapter, openedAt), observed.[2])
    Assert.Equal<CanLinkState>(Disconnected(ReconnectPending, fixedNow), observed.[3])
    Assert.Equal<CanLinkState>(Error(Recoverable peakStatusCause, t3), observed.[4])

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

    Assert.Equal(3, observed.Count)
    Assert.Equal<CanLinkState>(Disconnected(NoAdapterPresent, fixedNow), observed.[0])
    // FR-003 click-feedback (#131): synthesised before the link's
    // next emission. ReconnectAsync without a prior Recoverable
    // does not arm the tracker, so the Recoverable observed next
    // starts a fresh cycle (no escalation).
    Assert.Equal<CanLinkState>(Disconnected(ReconnectPending, fixedNow), observed.[1])
    Assert.Equal<CanLinkState>(Error(Recoverable peakStatusCause, t1), observed.[2])

// --- since stickiness across N reconnect cycles (FR-002b, #130) ---

[<Fact>]
let MultipleEscalationCycles_FatalSinceStaysAtFirstObservation () =
    // Bench-confirmed bug from issue #130: clicking Reconnect
    // repeatedly while in the escalated-Fatal state used to update
    // the tooltip's `since` HH:MM on every click because the
    // escalator forwarded each re-observation's `now`. FR-002b
    // requires `since` to anchor to the first observation of the
    // root cause for every subsequent emission of the same cause.
    let t1 = fixedNow
    let t2 = fixedNow.AddSeconds(3.0)
    let t3 = fixedNow.AddSeconds(7.0)
    let t4 = fixedNow.AddSeconds(12.0)

    let script =
        seq {
            (Error(Recoverable peakStatusCause, t1), TimeSpan.Zero)
            (Error(Recoverable peakStatusCause, t2), TimeSpan.Zero)
            (Error(Recoverable peakStatusCause, t3), TimeSpan.Zero)
            (Error(Recoverable peakStatusCause, t4), TimeSpan.Zero)
        }

    let service, observed = newService script

    service.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()
    service.ReconnectAsync(CancellationToken.None).GetAwaiter().GetResult()
    service.ReconnectAsync(CancellationToken.None).GetAwaiter().GetResult()
    service.ReconnectAsync(CancellationToken.None).GetAwaiter().GetResult()

    let expectedDetail =
        sprintf "%s persists across reconnect — file bug" peakStatusCause

    // Three ReconnectAsync calls each insert a synthesised
    // Disconnected(ReconnectPending, fixedNow) per FR-003 (#131)
    // BEFORE the link's next Recoverable emission, which then
    // escalates to Fatal@t1 by the FR-002b stickiness rule.
    Assert.Equal(7, observed.Count)
    Assert.Equal<CanLinkState>(Error(Recoverable peakStatusCause, t1), observed.[0])
    Assert.Equal<CanLinkState>(Disconnected(ReconnectPending, fixedNow), observed.[1])
    Assert.Equal<CanLinkState>(Error(Fatal expectedDetail, t1), observed.[2])
    Assert.Equal<CanLinkState>(Disconnected(ReconnectPending, fixedNow), observed.[3])
    Assert.Equal<CanLinkState>(Error(Fatal expectedDetail, t1), observed.[4])
    Assert.Equal<CanLinkState>(Disconnected(ReconnectPending, fixedNow), observed.[5])
    Assert.Equal<CanLinkState>(Error(Fatal expectedDetail, t1), observed.[6])
    Assert.Equal<CanLinkState>(Error(Fatal expectedDetail, t1), service.CurrentState)

// --- root cause change resets the since anchor (FR-002b, #130) ---

let private otherPeakStatusCause = "PEAK status 0x80000"

[<Fact>]
let DifferentRecoverableCauseAfterReconnect_NewCauseGetsNewSinceWithoutEscalation () =
    // FR-002b clause: "Updates only when the root cause itself
    // changes". A distinct Recoverable cause arriving after the
    // tracker was armed by ReconnectAsync must start a fresh cycle —
    // new `since`, NOT a Fatal escalation (the persists-across-
    // reconnect contract only applies to the same root cause).
    let t1 = fixedNow
    let t2 = fixedNow.AddSeconds(4.0)

    let script =
        seq {
            (Error(Recoverable peakStatusCause, t1), TimeSpan.Zero)
            (Error(Recoverable otherPeakStatusCause, t2), TimeSpan.Zero)
        }

    let service, observed = newService script

    service.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()
    service.ReconnectAsync(CancellationToken.None).GetAwaiter().GetResult()

    Assert.Equal(3, observed.Count)
    Assert.Equal<CanLinkState>(Error(Recoverable peakStatusCause, t1), observed.[0])
    Assert.Equal<CanLinkState>(Disconnected(ReconnectPending, fixedNow), observed.[1])
    Assert.Equal<CanLinkState>(Error(Recoverable otherPeakStatusCause, t2), observed.[2])
    Assert.Equal<CanLinkState>(Error(Recoverable otherPeakStatusCause, t2), service.CurrentState)

// --- leaving Error via Disconnected (not Connected) preserves the tracker (FR-002b) ---

[<Fact>]
let RecoverableThenDisconnectedThenSameRecoverable_PreservesOriginalSince () =
    // FR-002b clause: "the chip leaves Error (to Connected or
    // Disconnected) and re-enters via a DISTINCT cause" updates
    // `since`. By contrast, re-entering via the SAME cause through a
    // transient Disconnected must NOT reset the anchor — only
    // Connected resolves the failure. The second ReconnectAsync also
    // arms the tracker, so the same cause re-emerging here escalates
    // to Fatal with the original `since`.
    let t1 = fixedNow
    let unpluggedAt = fixedNow.AddSeconds(2.0)
    let t3 = fixedNow.AddSeconds(6.0)

    let script =
        seq {
            (Error(Recoverable peakStatusCause, t1), TimeSpan.Zero)
            (Disconnected(MidSessionUnplug, unpluggedAt), TimeSpan.Zero)
            (Error(Recoverable peakStatusCause, t3), TimeSpan.Zero)
        }

    let service, observed = newService script

    service.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()
    service.ReconnectAsync(CancellationToken.None).GetAwaiter().GetResult()
    service.ReconnectAsync(CancellationToken.None).GetAwaiter().GetResult()

    let expectedDetail =
        sprintf "%s persists across reconnect — file bug" peakStatusCause

    Assert.Equal(5, observed.Count)
    Assert.Equal<CanLinkState>(Error(Recoverable peakStatusCause, t1), observed.[0])
    Assert.Equal<CanLinkState>(Disconnected(ReconnectPending, fixedNow), observed.[1])
    Assert.Equal<CanLinkState>(Disconnected(MidSessionUnplug, unpluggedAt), observed.[2])
    Assert.Equal<CanLinkState>(Disconnected(ReconnectPending, fixedNow), observed.[3])
    Assert.Equal<CanLinkState>(Error(Fatal expectedDetail, t1), observed.[4])
    Assert.Equal<CanLinkState>(Error(Fatal expectedDetail, t1), service.CurrentState)

[<Fact>]
let RecoverableThenDisconnectedThenDifferentRecoverable_DistinctCauseGetsNewSince () =
    // FR-002b explicit clause: "the chip leaves Error (to Connected
    // or Disconnected) and re-enters via a distinct cause" → new
    // `since`. A distinct cause is a fresh cycle even when the
    // tracker survives a transient Disconnected.
    let t1 = fixedNow
    let unpluggedAt = fixedNow.AddSeconds(2.0)
    let t3 = fixedNow.AddSeconds(6.0)

    let script =
        seq {
            (Error(Recoverable peakStatusCause, t1), TimeSpan.Zero)
            (Disconnected(MidSessionUnplug, unpluggedAt), TimeSpan.Zero)
            (Error(Recoverable otherPeakStatusCause, t3), TimeSpan.Zero)
        }

    let service, observed = newService script

    service.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()
    service.ReconnectAsync(CancellationToken.None).GetAwaiter().GetResult()
    service.ReconnectAsync(CancellationToken.None).GetAwaiter().GetResult()

    Assert.Equal(5, observed.Count)
    Assert.Equal<CanLinkState>(Error(Recoverable peakStatusCause, t1), observed.[0])
    Assert.Equal<CanLinkState>(Disconnected(ReconnectPending, fixedNow), observed.[1])
    Assert.Equal<CanLinkState>(Disconnected(MidSessionUnplug, unpluggedAt), observed.[2])
    Assert.Equal<CanLinkState>(Disconnected(ReconnectPending, fixedNow), observed.[3])
    Assert.Equal<CanLinkState>(Error(Recoverable otherPeakStatusCause, t3), observed.[4])
    Assert.Equal<CanLinkState>(Error(Recoverable otherPeakStatusCause, t3), service.CurrentState)
