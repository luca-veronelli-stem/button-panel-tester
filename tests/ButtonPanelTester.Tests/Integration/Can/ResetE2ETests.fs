module Stem.ButtonPanelTester.Tests.Integration.Can.ResetE2ETests

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

/// Integration tests for `BaptismService.ResetAsync` (spec-004 D3, T030)
/// over the fake transmitter + a real `CanLinkService` wrapping
/// `InMemoryCanLink` — the `BaptismE2ETests` / `LinkLossAbortsTests` harness.
/// Reset is a LINEAR flow behind the confirmation seam (`data-model.md` §5):
///
///   (a) confirmed → exactly TWO recorded WHO_ARE_YOU broadcasts, in order,
///       wire payloads `FF 00 04 01` then `FF 00 0F 01` (the T007 fixtures),
///       outcome `Sent` on write completion (FR-010);
///   (b) declined → ZERO recorded sends, outcome `Declined` (FR-009);
///   (c) scripted fault on the first OR second write →
///       `ResetTransmissionFailure`, no retry, no further send;
///   (d) link not Connected at entry / dropping mid-pair → `ResetLinkLost`.

// --- fixtures ---

let private fixedNow = DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero)

let private fixedAdapter: AdapterIdentification =
    { ChannelName = "PCAN-USB (1)"; DeviceId = "0x01"; BaudrateBps = 250_000 }

/// A real `CanLinkService` (wrapping `InMemoryCanLink`) driven to
/// `Connected` via `InitializeAsync` (the `BaptismE2ETests` precedent).
let private connectedLink (clock: IClock) : ICanLinkService =
    let link = InMemoryCanLink(seq { (Connected(fixedAdapter, fixedNow), TimeSpan.Zero) })
    let svc = CanLinkService(link, clock, NullLogger<CanLinkService>.Instance)
    (svc :> ICanLinkService).InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()
    svc :> ICanLinkService

/// A real `CanLinkService` left UN-initialized, so its `CurrentState` is
/// `Initializing` (not `Connected`) — the entry-guard `ResetLinkLost` fixture.
let private notConnectedLink (clock: IClock) : ICanLinkService =
    let link = InMemoryCanLink(seq { (Connected(fixedAdapter, fixedNow), TimeSpan.Zero) })
    CanLinkService(link, clock, NullLogger<CanLinkService>.Instance) :> ICanLinkService

/// A scripted link: Connected, then Disconnected — `InitializeAsync` reaches
/// Connected, a later `ReconnectAsync` dequeues the Disconnected step (the
/// `LinkLossAbortsTests` precedent), so the link can be dropped mid-broadcast.
let private connectThenDisconnectLink (clock: IClock) : ICanLinkService =
    let link =
        InMemoryCanLink(seq {
            (Connected(fixedAdapter, fixedNow), TimeSpan.Zero)
            (Disconnected(MidSessionUnplug, fixedNow), TimeSpan.Zero)
        })
    CanLinkService(link, clock, NullLogger<CanLinkService>.Instance) :> ICanLinkService

/// File-private deferred transmitter mirroring `LinkLossAbortsTests`: records
/// each send oldest-first synchronously, then holds its completion open on a
/// per-call `TaskCompletionSource` so the reset broadcast parks between the
/// two writes while link-down is injected. `Release` completes the n-th held
/// send. Reset only ever sends WHO_ARE_YOU.
type private DeferredTransmitter() =
    let gate = obj ()
    let mutable sentNewestFirst: MasterSequenceSend list = []
    let pending = List<TaskCompletionSource<unit>>()

    let send (command: MasterSequenceSend) : Task =
        let tcs = TaskCompletionSource<unit>()
        lock gate (fun () ->
            sentNewestFirst <- command :: sentNewestFirst
            pending.Add tcs)
        tcs.Task :> Task

    member _.Release(index: int) =
        let tcs = lock gate (fun () -> pending.[index - 1])
        tcs.SetResult()

    member _.Sent: MasterSequenceSend list =
        lock gate (fun () -> List.rev sentNewestFirst)

    interface IMasterSequenceTransmitter with
        member _.SendWhoAreYouAsync(machineType, fwType, reset, _ct) =
            send (WhoAreYouSent(machineType, fwType, reset))

        member _.SendSetAddressAsync(uuid, spAddress, _ct) =
            send (SetAddressSent(uuid, spAddress))

type private Harness =
    { Clock: FrozenClock
      Transmitter: InMemoryMasterSequenceTransmitter
      Service: BaptismService }

let private newHarnessWith (link: IClock -> ICanLinkService) : Harness =
    let clock = FrozenClock(fixedNow)
    let canLink = link (clock :> IClock)
    let observer = InMemoryWhoIAmObserver()
    let ackObserver = InMemorySetAddressAckObserver()
    let discovery = new PanelDiscoveryService(observer, canLink, clock, NullLogger<PanelDiscoveryService>.Instance)
    let transmitter = InMemoryMasterSequenceTransmitter(clock :> IClock)

    let service =
        new BaptismService(transmitter, observer, ackObserver, discovery, canLink, clock, NullLogger<BaptismService>.Instance)

    { Clock = clock; Transmitter = transmitter; Service = service }

let private newHarness () : Harness = newHarnessWith connectedLink

/// The wire payload of each recorded WHO_ARE_YOU send, via the production
/// codec — pins the exact bytes (`FF 00 04 01`, `FF 00 0F 01`), not just the
/// structured fields.
let private whoAreYouPayloads (sends: (MasterSequenceSend * DateTimeOffset) list) : byte[] list =
    sends
    |> List.choose (fun (s, _) ->
        match s with
        | WhoAreYouSent(machineType, fwType, reset) ->
            Some(WhoAreYouFrame.encode { MachineType = machineType; FwType = fwType; Reset = reset })
        | SetAddressSent _ -> None)

// --- (a) confirmed → two ordered broadcasts, Sent ---

[<Fact>]
let Reset_Confirmed_EmitsBothBroadcastsInOrderAndSucceeds () =
    let h = newHarness ()

    let outcome = h.Service.ResetAsync(true, CancellationToken.None).GetAwaiter().GetResult()

    Assert.Equal(Sent, outcome)

    // Structured: exactly the dual-fwType pair, 12 V then 24 V, reset set.
    Assert.Equal<(MasterSequenceSend * DateTimeOffset) list>(
        [ (WhoAreYouSent(0xFFuy, 0x0004us, true), fixedNow)
          (WhoAreYouSent(0xFFuy, 0x000Fus, true), fixedNow) ],
        h.Transmitter.Sent)

    // Wire bytes: the exact T007 fixture payloads, in order.
    Assert.Equal<byte[] list>(
        [ [| 0xFFuy; 0x00uy; 0x04uy; 0x01uy |]
          [| 0xFFuy; 0x00uy; 0x0Fuy; 0x01uy |] ],
        whoAreYouPayloads h.Transmitter.Sent)

// --- (b) declined → zero sends, Declined ---

[<Fact>]
let Reset_Declined_TransmitsNothing () =
    let h = newHarness ()

    let outcome = h.Service.ResetAsync(false, CancellationToken.None).GetAwaiter().GetResult()

    Assert.Equal(Declined, outcome)
    Assert.Empty(h.Transmitter.Sent)

// --- (c) scripted fault on first / second write → ResetTransmissionFailure, no retry ---

[<Fact>]
let Reset_FirstWriteFaults_TransmissionFailureNoRetry () =
    let h = newHarness ()
    h.Transmitter.ScriptFault(1, exn "first reset broadcast failed")

    let outcome = h.Service.ResetAsync(true, CancellationToken.None).GetAwaiter().GetResult()

    Assert.Equal(ResetTransmissionFailure, outcome)
    // The faulted first write recorded nothing, and the second was never sent.
    Assert.Empty(h.Transmitter.Sent)

[<Fact>]
let Reset_SecondWriteFaults_TransmissionFailureNoRetry () =
    let h = newHarness ()
    h.Transmitter.ScriptFault(2, exn "second reset broadcast failed")

    let outcome = h.Service.ResetAsync(true, CancellationToken.None).GetAwaiter().GetResult()

    Assert.Equal(ResetTransmissionFailure, outcome)
    // Only the first (12 V) broadcast recorded; the faulted second recorded
    // nothing and was not retried.
    Assert.Equal<(MasterSequenceSend * DateTimeOffset) list>(
        [ (WhoAreYouSent(0xFFuy, 0x0004us, true), fixedNow) ],
        h.Transmitter.Sent)

// --- (d) link not Connected at entry → ResetLinkLost, zero sends ---

[<Fact>]
let Reset_LinkNotConnectedAtEntry_LinkLostNoSends () =
    let h = newHarnessWith notConnectedLink

    let outcome = h.Service.ResetAsync(true, CancellationToken.None).GetAwaiter().GetResult()

    Assert.Equal(ResetLinkLost, outcome)
    Assert.Empty(h.Transmitter.Sent)

// --- (d) link drops between the two broadcasts → ResetLinkLost, only first sent ---

[<Fact>]
let Reset_LinkDropsMidPair_LinkLostAfterFirstBroadcast () =
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

    // First broadcast is held open, so the reset parks between the two writes.
    let task = service.ResetAsync(true, CancellationToken.None)
    Assert.Equal<MasterSequenceSend list>([ WhoAreYouSent(0xFFuy, 0x0004us, true) ], transmitter.Sent)

    // Link leaves Connected before the second broadcast, then the first write completes.
    canLink.ReconnectAsync(CancellationToken.None).GetAwaiter().GetResult()
    transmitter.Release 1

    Assert.Equal(ResetLinkLost, task.GetAwaiter().GetResult())
    // Only the first (12 V) broadcast ever went out — the link check before
    // the second caught the drop; no further send.
    Assert.Equal<MasterSequenceSend list>([ WhoAreYouSent(0xFFuy, 0x0004us, true) ], transmitter.Sent)
