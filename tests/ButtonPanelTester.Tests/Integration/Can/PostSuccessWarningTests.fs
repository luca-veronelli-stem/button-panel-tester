module Stem.ButtonPanelTester.Tests.Integration.Can.PostSuccessWarningTests

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

/// FR-007 post-success watch integration tests for `BaptismService`
/// (spec-004 C5, T025), per `data-model.md` §4.4. After an attempt reaches
/// `Terminal Succeeded` the service watches `IWhoIAmObserver` for the
/// CLAIMED uuid over ONE 15 s window (the spec-003 pruning constant,
/// anchored at the success instant). A claimed panel that took the claim
/// goes silent; if its uuid is heard AGAIN within the window the claim did
/// not take — `WarningRaised` fires once with the claimed uuid. Volatile
/// in-memory only (FR-013); a new attempt or link loss cancels the watch,
/// and expiry is silent.
///
/// Builds the C4 harness — a real `CanLinkService` (over `InMemoryCanLink`
/// Connected) driven to `Connected`, a real `PanelDiscoveryService`, the
/// synchronous `InMemoryWhoIAmObserver` + `InMemoryMasterSequenceTransmitter`
/// fakes, and a `FrozenClock` — so every instant is scripted; no wall-clock
/// sleeps.
///
/// THE KEY SUBTLETY: the SUCCESS-triggering announcement (the matching
/// variant frame that drives the attempt to `Succeeded`) must NOT itself
/// fire the warning — only a SUBSEQUENT re-announcement within the window
/// does. The cases assert exactly that by collecting every `WarningRaised`
/// emission and checking the count.
///
/// Coverage:
///   - heard within window → exactly one warning (and the success-triggering
///     announcement did NOT already fire one);
///   - new attempt cancels the prior watch → no warning;
///   - link loss cancels the watch → no warning;
///   - expiry is silent → no warning, even on a later re-announcement.

// --- fixtures ---

let private fixedNow = DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero)

let private fixedAdapter: AdapterIdentification =
    { ChannelName = "PCAN-USB (1)"; DeviceId = "0x01"; BaudrateBps = 250_000 }

let private announcedFwType = 0x000Fus

let private frameOf (machineType: byte) (u0, u1, u2) : WhoIAmFrame =
    { MachineType = MachineTypeByte machineType
      FwType = FwType announcedFwType
      Uuid = PanelUuid(u0, u1, u2) }

/// A real `CanLinkService` (over an `InMemoryCanLink`) driven to
/// `Connected` via `InitializeAsync` (the `BaptismE2ETests` precedent).
let private connectedLink (clock: IClock) : ICanLinkService =
    let link = InMemoryCanLink(seq { (Connected(fixedAdapter, fixedNow), TimeSpan.Zero) })
    let svc = CanLinkService(link, clock, NullLogger<CanLinkService>.Instance)
    (svc :> ICanLinkService).InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()
    svc :> ICanLinkService

/// A scripted link: Connected, then Disconnected. `InitializeAsync` reaches
/// Connected; a `ReconnectAsync` dequeues the Disconnected step so the link
/// leaves `Connected` (the `LinkLossAbortsTests` precedent).
let private connectThenDisconnectLink (clock: IClock) : ICanLinkService =
    let link =
        InMemoryCanLink(seq {
            (Connected(fixedAdapter, fixedNow), TimeSpan.Zero)
            (Disconnected(MidSessionUnplug, fixedNow), TimeSpan.Zero)
        })
    let svc = CanLinkService(link, clock, NullLogger<CanLinkService>.Instance)
    (svc :> ICanLinkService).InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()
    svc :> ICanLinkService

/// Subscribe to the warning feed and accumulate every raised uuid
/// (the `DiscoveryE2ETests.collect` precedent).
let private collectWarnings (svc: IBaptismService) =
    let seen = List<PanelUuid>()
    svc.WarningRaised |> Observable.subscribe (fun u -> seen.Add u) |> ignore
    seen

/// Drive one full success through the corrected F6 gate: emit the virgin
/// frame so discovery sees the panel, launch `BaptizeAsync` WITHOUT awaiting
/// (the synchronous claim write parks the FSM in `AwaitingAnnounce`), emit
/// the matching variant frame (→ Assigning → assign write → AwaitingAdoption,
/// NOT yet success), then confirm adoption — observe the `0x25` ACK and hold
/// silence past the adoption deadline (assign-complete + adoptionBudget) so a
/// closing tick reaches `Succeeded` at `fixedNow + adoptionBudget + 1 s`.
/// Returns the completed outcome (asserted `Succeeded` by callers).
let private driveSuccess
    (clock: FrozenClock)
    (service: BaptismService)
    (observer: InMemoryWhoIAmObserver)
    (ackObserver: InMemorySetAddressAckObserver)
    (uuid: uint32 * uint32 * uint32)
    (variant: MarketingVariant)
    : BaptismOutcome =
    observer.Emit(frameOf 0xFFuy uuid)
    let task = service.BaptizeAsync(PanelUuid uuid, variant, CancellationToken.None)
    observer.Emit(frameOf (BoardVariant.encode variant) uuid)
    ackObserver.Emit fixedNow
    clock.SetTo(fixedNow + Baptism.adoptionBudget + TimeSpan.FromSeconds 1.0)
    service.RunDeadlineTick()
    task.GetAwaiter().GetResult()

// --- (1) heard within window → exactly one warning ---

[<Fact>]
let PostSuccess_ClaimedUuidHeardWithinWindow_RaisesWarningOnce () =
    let clock = FrozenClock(fixedNow)
    let link = connectedLink (clock :> IClock)
    let observer = InMemoryWhoIAmObserver()
    let ackObserver = InMemorySetAddressAckObserver()
    use discovery = new PanelDiscoveryService(observer, link, clock, NullLogger<PanelDiscoveryService>.Instance)
    let transmitter = InMemoryMasterSequenceTransmitter(clock :> IClock)

    use service =
        new BaptismService(transmitter, observer, ackObserver, discovery, link, clock, NullLogger<BaptismService>.Instance)

    let warnings = collectWarnings (service :> IBaptismService)
    let uuid = (0x177Cu, 0x126Du, 0x7308u)

    Assert.Equal(Succeeded, driveSuccess clock service observer ackObserver uuid EdenXp)

    // The success-triggering announcement must NOT already have fired a
    // warning — the watch was armed BY that very announcement.
    Assert.Empty warnings

    // Within the 15 s post-success window (success was at fixedNow + 7 s, so
    // the window runs to fixedNow + 22 s) the claimed uuid is heard again →
    // the claim did not take, and the residual FR-007 backstop fires once.
    clock.SetTo(fixedNow.AddSeconds 10.0)
    observer.Emit(frameOf (BoardVariant.encode EdenXp) uuid)

    Assert.Equal<PanelUuid list>([ PanelUuid uuid ], List.ofSeq warnings)

// --- (2) new attempt cancels the prior watch ---

[<Fact>]
let PostSuccess_NewAttemptCancelsWatch_NoWarning () =
    let clock = FrozenClock(fixedNow)
    let link = connectedLink (clock :> IClock)
    let observer = InMemoryWhoIAmObserver()
    let ackObserver = InMemorySetAddressAckObserver()
    use discovery = new PanelDiscoveryService(observer, link, clock, NullLogger<PanelDiscoveryService>.Instance)
    let transmitter = InMemoryMasterSequenceTransmitter(clock :> IClock)

    use service =
        new BaptismService(transmitter, observer, ackObserver, discovery, link, clock, NullLogger<BaptismService>.Instance)

    let warnings = collectWarnings (service :> IBaptismService)
    let uuid = (0x1u, 0x2u, 0x3u)

    Assert.Equal(Succeeded, driveSuccess clock service observer ackObserver uuid EdenXp)
    Assert.Empty warnings

    // A fresh attempt starts and cancels the prior watch (`data-model.md`
    // §4.4). The panel is still present in discovery from `driveSuccess`
    // (well within the 15 s TTL), so no re-emit is needed — re-emitting the
    // claimed uuid here would itself be a within-window re-announcement and
    // legitimately fire the OLD watch before the new attempt cancels it.
    let _ = service.BaptizeAsync(PanelUuid uuid, EdenXp, CancellationToken.None)

    // The claimed uuid heard now belongs to the cancelled watch → no warning.
    // (The new attempt parks in `AwaitingAnnounce`; this matching frame only
    // drives it to `AwaitingAdoption` — without an ACK + closing tick it never
    // reaches a second success, and it never re-arms the old, cancelled watch.)
    clock.SetTo(fixedNow.AddSeconds 8.0)
    observer.Emit(frameOf (BoardVariant.encode EdenXp) uuid)

    Assert.Empty warnings

// --- (3) link loss cancels the watch ---

[<Fact>]
let PostSuccess_LinkLossCancelsWatch_NoWarning () =
    let clock = FrozenClock(fixedNow)
    let link = connectThenDisconnectLink (clock :> IClock)
    let observer = InMemoryWhoIAmObserver()
    let ackObserver = InMemorySetAddressAckObserver()
    use discovery = new PanelDiscoveryService(observer, link, clock, NullLogger<PanelDiscoveryService>.Instance)
    let transmitter = InMemoryMasterSequenceTransmitter(clock :> IClock)

    use service =
        new BaptismService(transmitter, observer, ackObserver, discovery, link, clock, NullLogger<BaptismService>.Instance)

    let warnings = collectWarnings (service :> IBaptismService)
    let uuid = (0x1u, 0x2u, 0x3u)

    Assert.Equal(Succeeded, driveSuccess clock service observer ackObserver uuid EdenXp)
    Assert.Empty warnings

    // Drive the link out of Connected — this cancels the pending watch silently.
    link.ReconnectAsync(CancellationToken.None).GetAwaiter().GetResult()

    // A re-announcement of the claimed uuid now must not fire a warning.
    clock.SetTo(fixedNow.AddSeconds 10.0)
    observer.Emit(frameOf (BoardVariant.encode EdenXp) uuid)

    Assert.Empty warnings

// --- (4) expiry is silent ---

[<Fact>]
let PostSuccess_WindowExpires_NoWarning () =
    let clock = FrozenClock(fixedNow)
    let link = connectedLink (clock :> IClock)
    let observer = InMemoryWhoIAmObserver()
    let ackObserver = InMemorySetAddressAckObserver()
    use discovery = new PanelDiscoveryService(observer, link, clock, NullLogger<PanelDiscoveryService>.Instance)
    let transmitter = InMemoryMasterSequenceTransmitter(clock :> IClock)

    use service =
        new BaptismService(transmitter, observer, ackObserver, discovery, link, clock, NullLogger<BaptismService>.Instance)

    let warnings = collectWarnings (service :> IBaptismService)
    let uuid = (0x1u, 0x2u, 0x3u)

    Assert.Equal(Succeeded, driveSuccess clock service observer ackObserver uuid EdenXp)
    Assert.Empty warnings

    // Advance past the 15 s window (success was at fixedNow + 7 s, so it ends at
    // fixedNow + 22 s) and tick the deadline timer — expiry is silent.
    clock.SetTo(fixedNow.AddSeconds 23.0)
    service.RunDeadlineTick()
    Assert.Empty warnings

    // A later re-announcement (now past the window) still raises nothing.
    observer.Emit(frameOf (BoardVariant.encode EdenXp) uuid)
    Assert.Empty warnings
