module Stem.ButtonPanelTester.Tests.Integration.Can.LinkLossAbortsTests

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Services.Can
open Stem.ButtonPanelTester.Tests.Fakes
open Stem.ButtonPanelTester.Tests.Fakes.Can

/// CHK015 link-loss integration tests for `BaptismService` (spec-004 C4,
/// T024): the link leaving `Connected` in EACH non-terminal step
/// (`ClaimSent`, `AwaitingAnnounce`, `Assigning`) ends the attempt in
/// `LinkLost`, transmits no further, and never retries.
///
/// The shipped `InMemoryMasterSequenceTransmitter` completes sends
/// synchronously, so the FSM blows past `ClaimSent` / `Assigning` before
/// link-down can be injected. To catch those two states this file uses a
/// file-private `DeferredTransmitter` that HOLDS the next send open on a
/// `TaskCompletionSource` until the test releases it — so the FSM rests in
/// the write-pending state while link-down is emitted. The
/// `AwaitingAnnounce` case needs no held write (the claim already
/// completed). Link-down is driven through the real `CanLinkService` over
/// a scripted `InMemoryCanLink` (the `LinkLossClearsListTests` precedent).

let private fixedNow = DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero)

let private fixedAdapter: AdapterIdentification =
    { ChannelName = "PCAN-USB (1)"; DeviceId = "0x01"; BaudrateBps = 250_000 }

let private announcedFwType = 0x000Fus

let private frameOf (machineType: byte) (u0, u1, u2) : WhoIAmFrame =
    { MachineType = MachineTypeByte machineType
      FwType = FwType announcedFwType
      Uuid = PanelUuid(u0, u1, u2) }

/// A scripted link: Connected, then Disconnected. The first
/// `InitializeAsync` reaches Connected; a `ReconnectAsync` dequeues the
/// Disconnected step (and synthesizes a `Disconnected(ReconnectPending)`
/// first — either way the link leaves `Connected`, the CHK015 trigger).
let private connectThenDisconnectLink (clock: IClock) : ICanLinkService =
    let link =
        InMemoryCanLink(seq {
            (Connected(fixedAdapter, fixedNow), TimeSpan.Zero)
            (Disconnected(MidSessionUnplug, fixedNow), TimeSpan.Zero)
        })
    CanLinkService(link, clock, NullLogger<CanLinkService>.Instance) :> ICanLinkService

/// File-private deferred transmitter: records each send oldest-first and
/// holds its completion open on a per-call `TaskCompletionSource` so the
/// caller (the FSM driver) parks in the write-pending state. `Release`
/// completes the n-th held send. Synchronous recording happens before the
/// task is returned, so `Sent` reflects a send the instant the call is
/// made; only the task COMPLETION is deferred.
type private DeferredTransmitter() =
    let gate = obj ()
    let mutable sentNewestFirst: MasterSequenceSend list = []
    let pending = List<TaskCompletionSource<unit>>()

    let send (command: MasterSequenceSend) : Task =
        // No `RunContinuationsAsynchronously`: `Release` must run the service's
        // `ExecuteSynchronously` write-completion continuation INLINE so the
        // FSM has advanced (claim → AwaitingAnnounce) by the time the next
        // assertion reads `CurrentState`.
        let tcs = TaskCompletionSource<unit>()
        lock gate (fun () ->
            sentNewestFirst <- command :: sentNewestFirst
            pending.Add tcs)
        tcs.Task :> Task

    /// Complete the `index`-th held send (1-based) so the awaiting FSM
    /// continuation runs. Used to advance claim → AwaitingAnnounce.
    member _.Release(index: int) =
        let tcs = lock gate (fun () -> pending.[index - 1])
        tcs.SetResult()

    /// Every send in call order (oldest first).
    member _.Sent: MasterSequenceSend list =
        lock gate (fun () -> List.rev sentNewestFirst)

    interface IMasterSequenceTransmitter with
        member _.SendWhoAreYouAsync(machineType, fwType, reset, _ct) =
            send (WhoAreYouSent(machineType, fwType, reset))

        member _.SendSetAddressAsync(uuid, spAddress, _ct) =
            send (SetAddressSent(uuid, spAddress))

let private hasSetAddress (sends: MasterSequenceSend list) =
    sends |> List.exists (function SetAddressSent _ -> true | _ -> false)

// --- (1) link loss while in ClaimSent → LinkLost, nothing further ---

[<Fact>]
let LinkLoss_DuringClaimSent_AbortsWithLinkLost () =
    let clock = FrozenClock(fixedNow)
    let canLink = connectThenDisconnectLink (clock :> IClock)
    let observer = InMemoryWhoIAmObserver()
    let ackObserver = InMemorySetAddressAckObserver()
    use discovery = new PanelDiscoveryService(observer, canLink, clock, NullLogger<PanelDiscoveryService>.Instance)
    let transmitter = DeferredTransmitter()

    use service =
        new BaptismService(
            transmitter :> IMasterSequenceTransmitter,
            observer,
            ackObserver,
            discovery,
            canLink,
            clock,
            NullLogger<BaptismService>.Instance)

    canLink.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult() // -> Connected
    let uuid = (0x1u, 0x2u, 0x3u)
    observer.Emit(frameOf 0xFFuy uuid)

    // The claim write is held open, so the FSM rests in ClaimSent.
    let task = service.BaptizeAsync(PanelUuid uuid, EdenXp, CancellationToken.None)
    Assert.Equal(ClaimSent, service.CurrentState)

    // Link leaves Connected while in ClaimSent.
    canLink.ReconnectAsync(CancellationToken.None).GetAwaiter().GetResult()

    Assert.Equal(LinkLost, task.GetAwaiter().GetResult())
    Assert.False(hasSetAddress transmitter.Sent)
    // Only the claim was ever sent — no retry.
    Assert.Equal<MasterSequenceSend list>([ WhoAreYouSent(BoardVariant.encode EdenXp, announcedFwType, true) ], transmitter.Sent)

// --- (2) link loss while in AwaitingAnnounce → LinkLost ---

[<Fact>]
let LinkLoss_DuringAwaitingAnnounce_AbortsWithLinkLost () =
    let clock = FrozenClock(fixedNow)
    let canLink = connectThenDisconnectLink (clock :> IClock)
    let observer = InMemoryWhoIAmObserver()
    let ackObserver = InMemorySetAddressAckObserver()
    use discovery = new PanelDiscoveryService(observer, canLink, clock, NullLogger<PanelDiscoveryService>.Instance)
    let transmitter = DeferredTransmitter()

    use service =
        new BaptismService(
            transmitter :> IMasterSequenceTransmitter,
            observer,
            ackObserver,
            discovery,
            canLink,
            clock,
            NullLogger<BaptismService>.Instance)

    canLink.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult() // -> Connected
    let uuid = (0x1u, 0x2u, 0x3u)
    observer.Emit(frameOf 0xFFuy uuid)

    let task = service.BaptizeAsync(PanelUuid uuid, EdenXp, CancellationToken.None)
    // Release the claim so the FSM advances into AwaitingAnnounce.
    transmitter.Release 1
    Assert.Equal(AwaitingAnnounce(fixedNow + Baptism.announceBudget), service.CurrentState)

    // Link leaves Connected while waiting for the announcement.
    canLink.ReconnectAsync(CancellationToken.None).GetAwaiter().GetResult()

    Assert.Equal(LinkLost, task.GetAwaiter().GetResult())
    Assert.False(hasSetAddress transmitter.Sent)

// --- (3) link loss while in Assigning → LinkLost ---

[<Fact>]
let LinkLoss_DuringAssigning_AbortsWithLinkLost () =
    let clock = FrozenClock(fixedNow)
    let canLink = connectThenDisconnectLink (clock :> IClock)
    let observer = InMemoryWhoIAmObserver()
    let ackObserver = InMemorySetAddressAckObserver()
    use discovery = new PanelDiscoveryService(observer, canLink, clock, NullLogger<PanelDiscoveryService>.Instance)
    let transmitter = DeferredTransmitter()

    use service =
        new BaptismService(
            transmitter :> IMasterSequenceTransmitter,
            observer,
            ackObserver,
            discovery,
            canLink,
            clock,
            NullLogger<BaptismService>.Instance)

    canLink.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult() // -> Connected
    let uuid = (0x1u, 0x2u, 0x3u)
    let variant = EdenXp
    observer.Emit(frameOf 0xFFuy uuid)

    let task = service.BaptizeAsync(PanelUuid uuid, variant, CancellationToken.None)
    // Release the claim → AwaitingAnnounce; a matching announcement fires the
    // assign write, which is HELD open, so the FSM rests in Assigning.
    transmitter.Release 1
    observer.Emit(frameOf (BoardVariant.encode variant) uuid)
    Assert.Equal(Assigning, service.CurrentState)

    // Link leaves Connected while assigning.
    canLink.ReconnectAsync(CancellationToken.None).GetAwaiter().GetResult()

    Assert.Equal(LinkLost, task.GetAwaiter().GetResult())
    // The assign WAS sent (it is what put the FSM in Assigning), but no further
    // sends and no retry happened after link-down.
    Assert.Equal<MasterSequenceSend list>(
        [ WhoAreYouSent(BoardVariant.encode variant, announcedFwType, true)
          SetAddressSent(PanelUuid uuid, SetAddressFrame.spAddress 0uy (BoardVariant.encode variant) announcedFwType 1uy) ],
        transmitter.Sent)
