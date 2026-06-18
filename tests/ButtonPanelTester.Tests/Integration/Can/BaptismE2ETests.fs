module Stem.ButtonPanelTester.Tests.Integration.Can.BaptismE2ETests

open System
open System.Threading
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Services.Can
open Stem.ButtonPanelTester.Tests.Fakes
open Stem.ButtonPanelTester.Tests.Fakes.Can

/// Example- and property-based integration tests for `BaptismService`
/// driving the pure baptism FSM (`Baptism.step`) over the consumed
/// observables (spec-004 C4, T022). Builds the spec-003 harness — a real
/// `CanLinkService` (wrapping `InMemoryCanLink`) driven to Connected, a
/// real `PanelDiscoveryService`, the synchronous `InMemoryWhoIAmObserver`
/// + `InMemoryMasterSequenceTransmitter` fakes, and a `FrozenClock` — so
/// the claim → announce → assign path is exercised deterministically with
/// no PEAK hardware and no wall-clock sleeps.
///
/// Determinism: `BaptizeAsync` is launched WITHOUT awaiting; the
/// synchronous claim write drives the FSM into `AwaitingAnnounce` inside
/// the call, then `observer.Emit(frame)` (or a clock advance +
/// `RunDeadlineTick`) drives it terminal, completing the returned task.
///
/// Coverage:
///   - (a) happy path: virgin panel announcing the chosen variant → the
///         WHO_ARE_YOU echoes the announced fwType and the SET_ADDRESS
///         carries the selected uuid + computed spAddress; the assign write
///         opens the F6 adoption window, and `Succeeded` is reached only
///         after the `0x25` ACK plus held silence past the deadline.
///   - (F1) a transient virgin re-announce keeps waiting; the later chosen
///         variant still proceeds (no false `UnexpectedVariant`).
///   - (F6) assign written + selected-uuid re-announce → `ClaimNotAdopted`;
///         ACK then a re-announce breaking silence → `ClaimNotAdopted`;
///         silence held but no ACK by the deadline → `ClaimNotAdopted` (D2);
///         link loss in AwaitingAdoption → `LinkLost`.
///   - (b) wrong variant: a matching uuid announcing a different NON-virgin
///         marketed identity → `UnexpectedVariant`, ZERO SET_ADDRESS (FR-004).
///   - (c) pruned before match: discovery drops the selected uuid before
///         any announcement → `PanelDisappeared`.
///   - (d) scripted faults, no retry: claim fault → `TransmissionFailure
///         ClaimStep`; assign fault → `TransmissionFailure AssignStep`.
///   - (e) `NoSetAddressWithoutMatch`: the service-level mirror of the
///         Lean `no_assignment_without_match` (T017).

// --- fixtures ---

let private fixedNow = DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero)

let private fixedAdapter: AdapterIdentification =
    { ChannelName = "PCAN-USB (1)"; DeviceId = "0x01"; BaudrateBps = 250_000 }

/// A real `CanLinkService` (wrapping `InMemoryCanLink`) driven to
/// `Connected` via `InitializeAsync` (the `DiscoveryE2ETests` precedent).
let private connectedLink (clock: IClock) : ICanLinkService =
    let link = InMemoryCanLink(seq { (Connected(fixedAdapter, fixedNow), TimeSpan.Zero) })
    let svc = CanLinkService(link, clock, NullLogger<CanLinkService>.Instance)
    (svc :> ICanLinkService).InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()
    svc :> ICanLinkService

/// A scripted link: Connected, then Disconnected. `InitializeAsync` reaches
/// Connected; a later `ReconnectAsync` dequeues the Disconnected step so the
/// link leaves `Connected` mid-attempt (the `PostSuccessWarningTests`
/// precedent). Used by the AwaitingAdoption link-loss case.
let private connectThenDisconnectLink (clock: IClock) : ICanLinkService =
    let link =
        InMemoryCanLink(seq {
            (Connected(fixedAdapter, fixedNow), TimeSpan.Zero)
            (Disconnected(MidSessionUnplug, fixedNow), TimeSpan.Zero)
        })
    let svc = CanLinkService(link, clock, NullLogger<CanLinkService>.Instance)
    (svc :> ICanLinkService).InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()
    svc :> ICanLinkService

/// Build a decoded WHO_I_AM frame: the given machine-type byte and a
/// non-trivial 24 V `0x000F` announced fwType, so the WHO_ARE_YOU echo is
/// actually proven distinct from the discovery suites' `0x0004`.
let private announcedFwType = 0x000Fus

let private frameOf (machineType: byte) (u0, u1, u2) : WhoIAmFrame =
    { MachineType = MachineTypeByte machineType
      FwType = FwType announcedFwType
      Uuid = PanelUuid(u0, u1, u2) }

/// Assemble the full harness: clock + connected link + discovery service
/// + observer + transmitter + the SUT `BaptismService` under test.
type private Harness =
    { Clock: FrozenClock
      Observer: InMemoryWhoIAmObserver
      AckObserver: InMemorySetAddressAckObserver
      Discovery: PanelDiscoveryService
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
      Discovery = discovery
      Transmitter = transmitter
      Service = service }

/// Drive the F6 adoption-confirmation window to `Succeeded`: observe the
/// `0x25` ACK, then hold broadcast-silence (no selected-uuid re-announce)
/// past the adoption deadline (assign-write completion + `adoptionBudget`).
/// The assign write completed at `fixedNow`, so the deadline is
/// `fixedNow + adoptionBudget`; a tick one second past it closes the window
/// `Succeeded` (ACK seen + silence held).
let private confirmAdoption (h: Harness) =
    h.AckObserver.Emit fixedNow
    h.Clock.SetTo(fixedNow + Baptism.adoptionBudget + TimeSpan.FromSeconds 1.0)
    h.Service.RunDeadlineTick()

// --- (a) happy path: fwType echo + computed spAddress + Succeeded ---

[<Fact>]
let Baptize_VirginAnnouncesChosenVariant_EchoesFwTypeAndAssigns () =
    let h = newHarness ()
    let uuid = (0x177Cu, 0x126Du, 0x7308u)
    let variant = EdenXp

    // Make the virgin panel visible to discovery before the attempt.
    h.Observer.Emit(frameOf 0xFFuy uuid)

    let task = h.Service.BaptizeAsync(PanelUuid uuid, variant, CancellationToken.None)

    // The matching announcement assigns and opens the F6 adoption window: the
    // assign write completing no longer succeeds — it parks in AwaitingAdoption.
    h.Observer.Emit(frameOf (BoardVariant.encode variant) uuid)
    Assert.False task.IsCompleted

    // Confirmed adoption: the 0x25 ACK plus held broadcast-silence past the
    // adoption deadline closes the window `Succeeded` (FR-006).
    confirmAdoption h

    let outcome = task.GetAwaiter().GetResult()

    Assert.Equal(Succeeded, outcome)

    let expectedSpAddress = SetAddressFrame.spAddress 0uy (BoardVariant.encode variant) announcedFwType 1uy

    Assert.Equal<(MasterSequenceSend * DateTimeOffset) list>(
        [ (WhoAreYouSent(BoardVariant.encode variant, announcedFwType, true), fixedNow)
          (SetAddressSent(PanelUuid uuid, expectedSpAddress), fixedNow) ],
        h.Transmitter.Sent)

// --- (F1) a transient virgin re-announce keeps waiting; the chosen variant still proceeds ---

[<Fact>]
let Baptize_VirginReannounceKeepsWaiting_ThenChosenVariantSucceeds () =
    let h = newHarness ()
    let uuid = (0x1u, 0x2u, 0x3u)
    let variant = EdenXp
    h.Observer.Emit(frameOf 0xFFuy uuid)

    let task = h.Service.BaptizeAsync(PanelUuid uuid, variant, CancellationToken.None)

    // A selected-uuid VIRGIN re-announce mid-cycle is NOT a rejection (F1): the
    // panel is still announcing pre-claim, so the FSM keeps waiting — no
    // SET_ADDRESS, the attempt stays live in AwaitingAnnounce.
    h.Observer.Emit(frameOf 0xFFuy uuid)
    Assert.False task.IsCompleted
    Assert.Equal(AwaitingAnnounce(fixedNow + Baptism.announceBudget), h.Service.CurrentState)
    Assert.DoesNotContain(h.Transmitter.Sent, (fun (send, _) -> match send with SetAddressSent _ -> true | _ -> false))

    // The panel then announces the chosen variant → claim proceeds; once
    // adoption is confirmed (ACK + silence), it succeeds.
    h.Observer.Emit(frameOf (BoardVariant.encode variant) uuid)
    confirmAdoption h

    Assert.Equal(Succeeded, task.GetAwaiter().GetResult())

// --- (F6) assign written, panel keeps announcing → ClaimNotAdopted ---

[<Fact>]
let Baptize_AssignedThenPanelReannounces_ClaimNotAdopted () =
    let h = newHarness ()
    let uuid = (0x1u, 0x2u, 0x3u)
    let variant = EdenXp
    h.Observer.Emit(frameOf 0xFFuy uuid)

    let task = h.Service.BaptizeAsync(PanelUuid uuid, variant, CancellationToken.None)
    // Match → Assigning → assign write → AwaitingAdoption (not yet terminal, F6).
    h.Observer.Emit(frameOf (BoardVariant.encode variant) uuid)
    Assert.False task.IsCompleted

    // The selected panel re-announces during the adoption window: still
    // announcing ⇒ it did not adopt the claim — `ClaimNotAdopted` (FR-006a),
    // deterministically (silence is authoritative).
    h.Observer.Emit(frameOf (BoardVariant.encode variant) uuid)

    Assert.Equal(ClaimNotAdopted, task.GetAwaiter().GetResult())

// --- (ACK then broken silence) the 0x25 ACK arrives but the panel re-announces → ClaimNotAdopted ---

[<Fact>]
let Baptize_AckThenPanelReannounces_ClaimNotAdopted () =
    let h = newHarness ()
    let uuid = (0x1u, 0x2u, 0x3u)
    let variant = EdenXp
    h.Observer.Emit(frameOf 0xFFuy uuid)

    let task = h.Service.BaptizeAsync(PanelUuid uuid, variant, CancellationToken.None)
    h.Observer.Emit(frameOf (BoardVariant.encode variant) uuid) // → AwaitingAdoption

    // The ACK is a FAST POSITIVE only: it arrives, but a subsequent selected-uuid
    // re-announce BREAKS the silence — silence is authoritative, so not adopted.
    h.AckObserver.Emit fixedNow
    h.Observer.Emit(frameOf (BoardVariant.encode variant) uuid)

    Assert.Equal(ClaimNotAdopted, task.GetAwaiter().GetResult())

// --- (D2 strict) silence held but no 0x25 ACK by the deadline → ClaimNotAdopted ---

[<Fact>]
let Baptize_SilenceHeldButNoAck_ClaimNotAdopted () =
    let h = newHarness ()
    let uuid = (0x1u, 0x2u, 0x3u)
    let variant = EdenXp
    h.Observer.Emit(frameOf 0xFFuy uuid)

    let task = h.Service.BaptizeAsync(PanelUuid uuid, variant, CancellationToken.None)
    h.Observer.Emit(frameOf (BoardVariant.encode variant) uuid) // → AwaitingAdoption
    Assert.False task.IsCompleted

    // Silence holds (no re-announce) but the 0x25 ACK never arrives; the adoption
    // deadline elapses → `ClaimNotAdopted` (D2 strict: never a false success on a
    // dropped ACK — re-baptizing an already-adopted panel is harmless).
    h.Clock.SetTo(fixedNow + Baptism.adoptionBudget + TimeSpan.FromSeconds 1.0)
    h.Service.RunDeadlineTick()

    Assert.Equal(ClaimNotAdopted, task.GetAwaiter().GetResult())

// --- (link loss in adoption) the link leaves Connected during AwaitingAdoption → LinkLost ---

[<Fact>]
let Baptize_LinkLostDuringAdoption_LinkLost () =
    let clock = FrozenClock(fixedNow)
    let link = connectThenDisconnectLink (clock :> IClock)
    let observer = InMemoryWhoIAmObserver()
    let ackObserver = InMemorySetAddressAckObserver()
    use discovery = new PanelDiscoveryService(observer, link, clock, NullLogger<PanelDiscoveryService>.Instance)
    let transmitter = InMemoryMasterSequenceTransmitter(clock :> IClock)

    use service =
        new BaptismService(transmitter, observer, ackObserver, discovery, link, clock, NullLogger<BaptismService>.Instance)

    let uuid = (0x1u, 0x2u, 0x3u)
    let variant = EdenXp
    observer.Emit(frameOf 0xFFuy uuid)

    let task = service.BaptizeAsync(PanelUuid uuid, variant, CancellationToken.None)
    observer.Emit(frameOf (BoardVariant.encode variant) uuid) // → AwaitingAdoption
    Assert.False task.IsCompleted

    // The link leaves Connected during the adoption window → LinkLost (CHK015).
    link.ReconnectAsync(CancellationToken.None).GetAwaiter().GetResult()

    Assert.Equal(LinkLost, task.GetAwaiter().GetResult())

// --- (b) wrong variant: UnexpectedVariant, no SET_ADDRESS ---

[<Fact>]
let Baptize_MatchingUuidWrongVariant_UnexpectedVariantNoAssign () =
    let h = newHarness ()
    let uuid = (0x1u, 0x2u, 0x3u)

    h.Observer.Emit(frameOf 0xFFuy uuid)

    let task = h.Service.BaptizeAsync(PanelUuid uuid, EdenXp, CancellationToken.None)

    // Same uuid, but it announces a DIFFERENT marketed identity (OptimusXp).
    h.Observer.Emit(frameOf (BoardVariant.encode OptimusXp) uuid)

    let outcome = task.GetAwaiter().GetResult()

    Assert.Equal(UnexpectedVariant(Marketing OptimusXp), outcome)
    Assert.DoesNotContain(h.Transmitter.Sent, (fun (send, _) -> match send with SetAddressSent _ -> true | _ -> false))

// --- (c) pruned before match: PanelDisappeared ---

[<Fact>]
let Baptize_PanelPrunedBeforeMatch_PanelDisappeared () =
    let h = newHarness ()
    let uuid = (0x1u, 0x2u, 0x3u)

    h.Observer.Emit(frameOf 0xFFuy uuid)

    let task = h.Service.BaptizeAsync(PanelUuid uuid, EdenXp, CancellationToken.None)

    // Drop the selected uuid from discovery (past the 15 s TTL) before any announcement.
    h.Clock.SetTo(fixedNow.AddSeconds 16.0)
    h.Discovery.RunPruneTick()

    let outcome = task.GetAwaiter().GetResult()

    Assert.Equal(PanelDisappeared, outcome)
    Assert.DoesNotContain(h.Transmitter.Sent, (fun (send, _) -> match send with SetAddressSent _ -> true | _ -> false))

// --- (d) scripted faults, no retry ---

[<Fact>]
let Baptize_ClaimWriteFaults_TransmissionFailureClaimStepNoRetry () =
    let h = newHarness ()
    let uuid = (0x1u, 0x2u, 0x3u)
    h.Observer.Emit(frameOf 0xFFuy uuid)

    // Fault the 1st send (the claim).
    h.Transmitter.ScriptFault(1, exn "claim write failed")

    let outcome = h.Service.BaptizeAsync(PanelUuid uuid, EdenXp, CancellationToken.None).GetAwaiter().GetResult()

    Assert.Equal(TransmissionFailure ClaimStep, outcome)
    // The faulted send recorded nothing, and no retry re-attempted it.
    Assert.Empty(h.Transmitter.Sent)

[<Fact>]
let Baptize_AssignWriteFaults_TransmissionFailureAssignStepNoRetry () =
    let h = newHarness ()
    let uuid = (0x1u, 0x2u, 0x3u)
    let variant = EdenXp
    h.Observer.Emit(frameOf 0xFFuy uuid)

    // Fault the 2nd send (the assign), after a matching announcement.
    h.Transmitter.ScriptFault(2, exn "assign write failed")

    let task = h.Service.BaptizeAsync(PanelUuid uuid, variant, CancellationToken.None)
    h.Observer.Emit(frameOf (BoardVariant.encode variant) uuid)

    let outcome = task.GetAwaiter().GetResult()

    Assert.Equal(TransmissionFailure AssignStep, outcome)
    // Only the claim recorded; the faulted assign recorded nothing and was not retried.
    Assert.Equal<(MasterSequenceSend * DateTimeOffset) list>(
        [ (WhoAreYouSent(BoardVariant.encode variant, announcedFwType, true), fixedNow) ],
        h.Transmitter.Sent)

// --- (e) NoSetAddressWithoutMatch property over scripted service runs ---

/// One scripted post-claim event for the property: drive the live SUT
/// through announcements / ticks / faults realized against the harness.
type ServiceScriptedEvent =
    | EmitMatching
    | EmitWrongVariant of raw: byte
    | EmitForeign of raw: byte
    | AdvancePastDeadlineAndTick
    | FaultAssign

let private byteGen: Gen<byte> = Gen.choose (0, 255) |> Gen.map byte

let private scriptedGen: Gen<ServiceScriptedEvent> =
    Gen.frequency
        [ 2, Gen.constant EmitMatching
          2, Gen.map EmitWrongVariant byteGen
          2, Gen.map EmitForeign byteGen
          2, Gen.constant AdvancePastDeadlineAndTick
          1, Gen.constant FaultAssign ]

type ServiceScript = { Variant: MarketingVariant; Events: ServiceScriptedEvent list }

type private ServiceScriptArb =
    static member Script() : Arbitrary<ServiceScript> =
        gen {
            let! variant = Gen.elements [ EdenXp; OptimusXp; R3LXp; EdenBs8 ]
            let! events = Gen.listOf scriptedGen
            return { Variant = variant; Events = events }
        }
        |> Arb.fromGen

/// Service-level mirror of the Lean `no_assignment_without_match` (T017) /
/// the step-level `NoAssignmentWithoutMatch_StepLevel` (T020): driving the
/// REAL `BaptismService` over scripted announcement/tick/fault sequences,
/// `transmitter.Sent` never contains a `SetAddressSent` unless a prior
/// announcement matched BOTH the selected uuid AND the chosen variant. The
/// generator is deterministic (no wall clock — every instant is the
/// `FrozenClock`).
[<Property(Arbitrary = [| typeof<ServiceScriptArb> |])>]
let NoSetAddressWithoutMatch (script: ServiceScript) =
    let h = newHarness ()
    let uuid = (0x4242u, 0x1u, 0x7u)
    let selected = PanelUuid uuid
    let variant = script.Variant
    let chosenByte = BoardVariant.encode variant

    h.Observer.Emit(frameOf 0xFFuy uuid)

    // Track whether a matching announcement was delivered before each
    // possible assign. Once a match happens, an assign is legitimate.
    let mutable matchedBefore = false
    let mutable sawAssignBeforeMatch = false

    let task = h.Service.BaptizeAsync(selected, variant, CancellationToken.None)

    let assignCountSoFar () =
        h.Transmitter.Sent
        |> List.filter (fun (send, _) -> match send with SetAddressSent _ -> true | _ -> false)
        |> List.length

    let mutable priorAssigns = assignCountSoFar ()

    let foreignByte raw =
        // Guarantee the foreign byte never decodes to the chosen variant.
        if raw = chosenByte then BoardVariant.virginMarker else raw

    for ev in script.Events do
        if not task.IsCompleted then
            match ev with
            | EmitMatching ->
                matchedBefore <- true
                h.Observer.Emit(frameOf chosenByte uuid)
            | EmitWrongVariant raw ->
                let other = if raw = chosenByte then BoardVariant.virginMarker else raw
                h.Observer.Emit(frameOf other uuid)
            | EmitForeign raw ->
                // A foreign uuid (different first word) never satisfies the wait.
                h.Observer.Emit(frameOf (foreignByte raw) (0x9999u, 0x1u, 0x7u))
            | AdvancePastDeadlineAndTick ->
                h.Clock.SetTo(fixedNow.AddSeconds 7.0)
                h.Service.RunDeadlineTick()
            | FaultAssign ->
                h.Transmitter.ScriptFault(assignCountSoFar () + 2, exn "assign fault")

        // If a new assign appeared this step, it must have been preceded by a match.
        let nowAssigns = assignCountSoFar ()
        if nowAssigns > priorAssigns && not matchedBefore then
            sawAssignBeforeMatch <- true
        priorAssigns <- nowAssigns

    // Ensure the attempt is closed (no dangling task) — push past the deadline.
    if not task.IsCompleted then
        h.Clock.SetTo(fixedNow.AddSeconds 10.0)
        h.Service.RunDeadlineTick()

    task.GetAwaiter().GetResult() |> ignore

    // Final check: any SET_ADDRESS in the recorded sends implies a match happened.
    let anyAssign =
        h.Transmitter.Sent
        |> List.exists (fun (send, _) -> match send with SetAddressSent _ -> true | _ -> false)

    (not sawAssignBeforeMatch) && (not anyAssign || matchedBefore)
