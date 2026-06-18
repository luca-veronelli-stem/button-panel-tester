module Stem.ButtonPanelTester.Tests.Integration.Can.TimeoutE2ETests

open System
open System.Threading
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Services.Can
open Stem.ButtonPanelTester.Tests.Fakes
open Stem.ButtonPanelTester.Tests.Fakes.Can

/// FrozenClock-driven integration tests for the 6 s announce-wait
/// deadline in `BaptismService` (spec-004 C4, T023). The deadline window
/// is anchored at claim-write completion (CHK010): the claim write
/// completes at `fixedNow`, so the deadline is `fixedNow + 6 s`. The
/// service's `RunDeadlineTick()` test hook steps the deadline check under
/// the frozen clock — NO wall-clock sleeps.
///
/// Coverage:
///   - just under the deadline (claim-complete + 6 s − ε): still waiting.
///   - crossing the deadline: `WaitTimeout`, and `BaptismGuidance.
///     recoveryText WaitTimeout` names the three clarification-4 elements.
///   - never-flip: a matching announcement AFTER the reported `WaitTimeout`
///     does not change the outcome (terminal absorption).
///   - a foreign-uuid announcement before the deadline never satisfies the
///     wait.
///   - the adoption deadline (F6): assign written, no `0x25` ACK, the
///     adoption window (assign-complete + `adoptionBudget`) elapses →
///     `ClaimNotAdopted` (D2 strict — never a false success).

let private fixedNow = DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero)

let private fixedAdapter: AdapterIdentification =
    { ChannelName = "PCAN-USB (1)"; DeviceId = "0x01"; BaudrateBps = 250_000 }

let private announcedFwType = 0x000Fus

let private connectedLink (clock: IClock) : ICanLinkService =
    let link = InMemoryCanLink(seq { (Connected(fixedAdapter, fixedNow), TimeSpan.Zero) })
    let svc = CanLinkService(link, clock, NullLogger<CanLinkService>.Instance)
    (svc :> ICanLinkService).InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()
    svc :> ICanLinkService

let private frameOf (machineType: byte) (u0, u1, u2) : WhoIAmFrame =
    { MachineType = MachineTypeByte machineType
      FwType = FwType announcedFwType
      Uuid = PanelUuid(u0, u1, u2) }

type private Harness =
    { Clock: FrozenClock
      Observer: InMemoryWhoIAmObserver
      AckObserver: InMemorySetAddressAckObserver
      Transmitter: InMemoryMasterSequenceTransmitter
      Service: BaptismService }

let private newHarness () : Harness =
    let clock = FrozenClock(fixedNow)
    let link = connectedLink (clock :> IClock)
    let observer = InMemoryWhoIAmObserver()
    let ackObserver = InMemorySetAddressAckObserver()
    let discovery = new PanelDiscoveryService(observer, link, clock, NullLogger<PanelDiscoveryService>.Instance)
    let transmitter = InMemoryMasterSequenceTransmitter(clock :> IClock)

    let service =
        new BaptismService(
            transmitter,
            observer,
            ackObserver,
            discovery,
            link,
            clock,
            NullLogger<BaptismService>.Instance)

    { Clock = clock
      Observer = observer
      AckObserver = ackObserver
      Transmitter = transmitter
      Service = service }

// --- just under the deadline: still waiting ---

[<Fact>]
let Deadline_JustUnder_StillAwaitingNoOutcome () =
    let h = newHarness ()
    let uuid = (0x1u, 0x2u, 0x3u)
    h.Observer.Emit(frameOf 0xFFuy uuid)

    let task = h.Service.BaptizeAsync(PanelUuid uuid, EdenXp, CancellationToken.None)

    // claim completed at fixedNow → deadline = fixedNow + 6 s; advance to 6 s − 1 ms.
    h.Clock.SetTo(fixedNow.Add(Baptism.announceBudget).AddMilliseconds -1.0)
    h.Service.RunDeadlineTick()

    Assert.False(task.IsCompleted)
    Assert.Equal(AwaitingAnnounce(fixedNow + Baptism.announceBudget), h.Service.CurrentState)

// --- crossing the deadline: WaitTimeout + recovery guidance ---

[<Fact>]
let Deadline_Crossed_WaitTimeoutWithRecoveryGuidance () =
    let h = newHarness ()
    let uuid = (0x1u, 0x2u, 0x3u)
    h.Observer.Emit(frameOf 0xFFuy uuid)

    let task = h.Service.BaptizeAsync(PanelUuid uuid, EdenXp, CancellationToken.None)

    h.Clock.SetTo(fixedNow + Baptism.announceBudget)
    h.Service.RunDeadlineTick()

    Assert.Equal(WaitTimeout, task.GetAwaiter().GetResult())

    match BaptismGuidance.recoveryText WaitTimeout with
    | None -> Assert.Fail "expected recovery guidance for WaitTimeout"
    | Some text ->
        // The three clarification-4 elements must each appear.
        Assert.Contains("incomplete", text, StringComparison.OrdinalIgnoreCase)
        Assert.Contains("re-announce", text, StringComparison.OrdinalIgnoreCase)
        Assert.Contains("Baptize", text, StringComparison.OrdinalIgnoreCase)

// --- never-flip: a late matching announcement does not change WaitTimeout ---

[<Fact>]
let Deadline_LateMatchAfterTimeout_OutcomeStaysWaitTimeout () =
    let h = newHarness ()
    let uuid = (0x1u, 0x2u, 0x3u)
    let variant = EdenXp
    h.Observer.Emit(frameOf 0xFFuy uuid)

    let task = h.Service.BaptizeAsync(PanelUuid uuid, variant, CancellationToken.None)

    h.Clock.SetTo(fixedNow + Baptism.announceBudget)
    h.Service.RunDeadlineTick()
    Assert.Equal(WaitTimeout, task.GetAwaiter().GetResult())

    // A matching announcement arriving AFTER the reported timeout is absorbed.
    h.Observer.Emit(frameOf (BoardVariant.encode variant) uuid)

    Assert.Equal(Terminal WaitTimeout, h.Service.CurrentState)
    Assert.DoesNotContain(h.Transmitter.Sent, (fun (send, _) -> match send with SetAddressSent _ -> true | _ -> false))

// --- a foreign-uuid announcement before the deadline never satisfies the wait ---

[<Fact>]
let Deadline_ForeignUuidBeforeDeadline_StillTimesOut () =
    let h = newHarness ()
    let uuid = (0x1u, 0x2u, 0x3u)
    let variant = EdenXp
    h.Observer.Emit(frameOf 0xFFuy uuid)

    let task = h.Service.BaptizeAsync(PanelUuid uuid, variant, CancellationToken.None)

    // A foreign uuid announcing the chosen variant byte: ignored (wrong uuid).
    h.Observer.Emit(frameOf (BoardVariant.encode variant) (0x9u, 0x9u, 0x9u))
    Assert.False(task.IsCompleted)

    h.Clock.SetTo(fixedNow + Baptism.announceBudget)
    h.Service.RunDeadlineTick()

    Assert.Equal(WaitTimeout, task.GetAwaiter().GetResult())
    Assert.DoesNotContain(h.Transmitter.Sent, (fun (send, _) -> match send with SetAddressSent _ -> true | _ -> false))

// --- the adoption deadline: assign written, no ACK, deadline elapses → ClaimNotAdopted ---

[<Fact>]
let AdoptionDeadline_NoAckBeforeDeadline_ClaimNotAdopted () =
    let h = newHarness ()
    let uuid = (0x1u, 0x2u, 0x3u)
    let variant = EdenXp
    h.Observer.Emit(frameOf 0xFFuy uuid)

    let task = h.Service.BaptizeAsync(PanelUuid uuid, variant, CancellationToken.None)

    // Match → Assigning → assign write → AwaitingAdoption: the assign write
    // completed at fixedNow, so the adoption deadline is fixedNow + adoptionBudget.
    h.Observer.Emit(frameOf (BoardVariant.encode variant) uuid)
    Assert.False(task.IsCompleted)
    Assert.Equal(AwaitingAdoption(fixedNow + Baptism.adoptionBudget, false), h.Service.CurrentState)

    // No 0x25 ACK observed; crossing the adoption deadline closes the window
    // `ClaimNotAdopted` (F6 / FR-006a, D2 strict — never a false success).
    h.Clock.SetTo(fixedNow + Baptism.adoptionBudget)
    h.Service.RunDeadlineTick()

    Assert.Equal(ClaimNotAdopted, task.GetAwaiter().GetResult())
