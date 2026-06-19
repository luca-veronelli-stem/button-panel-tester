module Stem.ButtonPanelTester.Tests.Integration.Can.ClaimWriteToctouTests

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Services.Can
open Stem.ButtonPanelTester.Tests.Fakes
open Stem.ButtonPanelTester.Tests.Fakes.Can

/// #231 regression for the residual TOCTOU window in
/// `BaptismService.BaptizeAsync`: the gap between the entry guard passing
/// (under the entry lock) and the OUT-OF-LOCK claim write. A CAN link-down
/// landing in that gap must not leak a stray `WHO_ARE_YOU` — the attempt
/// resolves `LinkLost` AND the transmitter records ZERO sends. C5 (`d141f13`)
/// already closed the worse wrong-outcome variant; this pins the residual
/// "one stray frame, correct outcome" to zero frames.
///
/// The window is reproduced deterministically on a single thread by a
/// flip-on-read `ICanLinkService` double (`FlipOnReadLink` below):
/// `CurrentState` reads `Connected` for the first two reads — the discovery
/// WHO_I_AM ingest (`PanelDiscoveryService.onWhoIAm`, read #1) and the
/// `BaptizeAsync` entry guard (read #2) — and `Disconnected(MidSessionUnplug,
/// …)` from the third read on, which is the fire-time re-validation the fix
/// performs immediately before the claim write. `LinkStateChanged` NEVER
/// emits, so `apply(LinkChanged …)` is never driven: the FSM is transitioned
/// to `LinkLost` solely by the fix's under-lock re-check — exactly the path
/// #231 closes. Pre-fix this records one `WhoAreYouSent` (the leak); post-fix
/// it records none.

let private fixedNow = DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero)

let private fixedAdapter: AdapterIdentification =
    { ChannelName = "PCAN-USB (1)"; DeviceId = "0x01"; BaudrateBps = 250_000 }

let private announcedFwType = 0x000Fus

let private frameOf (machineType: byte) (u0, u1, u2) : WhoIAmFrame =
    { MachineType = MachineTypeByte machineType
      FwType = FwType announcedFwType
      Uuid = PanelUuid(u0, u1, u2) }

/// Test-only `ICanLinkService` whose `CurrentState` returns `Connected` for
/// the first `connectedReads` reads and `Disconnected(MidSessionUnplug, …)`
/// thereafter — the single-thread seam for "link down BETWEEN the entry guard
/// and the claim write". `connectedReads = 2` covers the discovery-ingest read
/// + the entry-guard read, so the THIRD read (the fix's fire-time re-check)
/// observes the drop. `LinkStateChanged` is a never-emitting subject (the
/// double never drives `apply`, so the FSM transitions only via the re-check);
/// the two lifecycle calls are inert (the test never invokes them).
type private FlipOnReadLink(connectedReads: int) =
    let gate = obj ()
    let mutable reads = 0

    /// How many times `CurrentState` has been read — exposed so the test can
    /// assert the read budget landed where expected (entry guard on Connected,
    /// fire-time re-check on Disconnected) and document the chosen threshold.
    member _.Reads = lock gate (fun () -> reads)

    interface ICanLinkService with
        member _.CurrentState =
            lock gate (fun () ->
                reads <- reads + 1

                if reads <= connectedReads then
                    Connected(fixedAdapter, fixedNow)
                else
                    Disconnected(MidSessionUnplug, fixedNow))

        member _.LinkStateChanged =
            { new IObservable<CanLinkState> with
                member _.Subscribe(_observer) =
                    { new IDisposable with
                        member _.Dispose() = () } }

        member _.InitializeAsync(_ct) = Task.CompletedTask
        member _.ReconnectAsync(_ct) = Task.CompletedTask

// --- link down BETWEEN the entry guard and the claim write → LinkLost, zero sends ---

[<Fact>]
let LinkDown_BetweenEntryGuardAndClaimWrite_LinkLostWithZeroSends () =
    let clock = FrozenClock(fixedNow)
    // Connected for read #1 (discovery ingest) and read #2 (entry guard); the
    // fix's fire-time re-check is read #3 → Disconnected. Threshold pinned at 2:
    // discovery's `PanelsOnBus` pull does not read link state, and the deadline /
    // prune timers never read it, so the read budget is exactly these three.
    let link = FlipOnReadLink(2)
    let observer = InMemoryWhoIAmObserver()
    let ackObserver = InMemorySetAddressAckObserver()

    use discovery =
        new PanelDiscoveryService(observer, link, clock, NullLogger<PanelDiscoveryService>.Instance)

    let transmitter = InMemoryMasterSequenceTransmitter(clock :> IClock)

    use service =
        new BaptismService(
            transmitter :> IMasterSequenceTransmitter,
            observer,
            ackObserver,
            discovery,
            link,
            clock,
            NullLogger<BaptismService>.Instance)

    // Make the virgin panel visible while the link still reads Connected (read
    // #1): the entry guard must find it so the attempt STARTS and reaches the
    // out-of-lock claim write — the only way to exercise the residual window.
    let uuid = (0x1u, 0x2u, 0x3u)
    observer.Emit(frameOf 0xFFuy uuid)

    let task = service.BaptizeAsync(PanelUuid uuid, EdenXp, CancellationToken.None)

    // ZERO sends: the fire-time re-validation suppressed the stray claim. Pre-fix
    // the captured `SendClaim` fires regardless and this records one
    // `WhoAreYouSent` — the #231 leak. Asserting emptiness FIRST makes the RED
    // signal immediate and never hangs (the leaked send is recorded
    // synchronously by the time `BaptizeAsync` returns).
    Assert.Empty(transmitter.Sent)

    // `LinkLost`, resolved synchronously by the fix's `ResolveLinkLost` arm
    // INSIDE `BaptizeAsync`, so the bounded wait stays instant on green and
    // fails fast — rather than hanging on the never-advancing `FrozenClock` —
    // if the gate ever regresses and the attempt is left in `AwaitingAnnounce`.
    Assert.True(task.Wait(TimeSpan.FromSeconds 5.0), "BaptizeAsync did not resolve")
    Assert.Equal(LinkLost, task.Result)

    // The read budget landed as designed: the entry guard saw Connected and the
    // fire-time re-check saw Disconnected (≥ 3 reads total).
    Assert.True(link.Reads >= 3, $"expected at least 3 CurrentState reads, saw {link.Reads}")
