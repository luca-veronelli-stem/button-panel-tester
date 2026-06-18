module Stem.ButtonPanelTester.Tests.Property.Can.BaptismSequenceProperties

open System
open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit
open Stem.ButtonPanelTester.Core.Can

/// FsCheck mirrors of the Lean `BaptismSequence` theorems in
/// `lean/Stem/ButtonPanelTester/Phase3/BaptismSequence.lean` (T017),
/// per `specs/004-baptism-workflow/data-model.md` §4. The Lean side
/// proves each invariant over the abstract model; every property here
/// exercises the REAL `Baptism.step` over generator-scripted event
/// sequences realized into the rich §4.3 payloads.

let private baseInstant = DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero)

let private frameFor (uuid: PanelUuid) (machineType: byte) : WhoIAmFrame =
    { MachineType = MachineTypeByte machineType
      FwType = FwType 0x0004us
      Uuid = uuid }

/// Derive a uuid guaranteed ≠ `uuid` (XOR with a non-zero constant
/// flips bits in the first word), so scripted foreign announcements
/// can never accidentally collide with the selected panel.
let private foreignOf (PanelUuid(u0, u1, u2)) = PanelUuid(u0 ^^^ 0xFFFF0000u, u1, u2)

let private connectedLink: CanLinkState =
    Connected({ ChannelName = "PCAN-USB (1)"; DeviceId = "0x51"; BaudrateBps = 250_000 }, baseInstant)

/// One of the three non-`Connected` `CanLinkState` shapes — the FSM
/// treats Initializing/Disconnected/Error uniformly as "link left
/// Connected" (data-model §4.3: `LinkChanged` in all non-terminal states).
let private nonConnectedLink (shape: int) : CanLinkState =
    match abs shape % 3 with
    | 0 -> Initializing
    | 1 -> Disconnected(MidSessionUnplug, baseInstant)
    | _ -> Error(Recoverable "bus-off detected", baseInstant)

let private snapshotKeeping (uuid: PanelUuid) : PanelsOnBus =
    PanelsOnBus.observe baseInstant (frameFor uuid 0x03uy) PanelsOnBus.empty

let private snapshotDropping (uuid: PanelUuid) : PanelsOnBus =
    PanelsOnBus.observe baseInstant (frameFor (foreignOf uuid) 0x03uy) PanelsOnBus.empty

/// Mirror of Lean `runFrom` (T017): fold the state component of the
/// real `Baptism.step` over an observed event list.
let private runFrom (cfg: AttemptConfig) (state: BaptismState) (events: BaptismEvent list) =
    events |> List.fold (fun s e -> fst (Baptism.step cfg s e)) state

/// Mirror of Lean `run` (T017): an attempt runs from `start`'s state
/// (`ClaimSent`, the post-press state).
let private run (cfg: AttemptConfig) (events: BaptismEvent list) =
    runFrom cfg (fst Baptism.start) events

/// `(state-before, event)` trace of a run — the decomposition scans of
/// `BaptismSucceedsIffConfirmedAdoption` key off the state each
/// event was consumed in.
let private transitions (cfg: AttemptConfig) (events: BaptismEvent list) =
    let folder (state, acc) ev =
        (fst (Baptism.step cfg state ev), (state, ev) :: acc)

    events |> List.fold folder (fst Baptism.start, []) |> snd |> List.rev

/// Mirror of Lean `closingSchedule` (T017/RW01): the canonical suffix that
/// drives any state past its pending work — the outstanding write resolves,
/// then the clock passes the deadline. From `Assigning` the schedule must
/// cross BOTH the assign write AND the adoption deadline (the assign write
/// lands in `AwaitingAdoption`, not a terminal — F6); from `AwaitingAdoption`
/// one tick past the deadline closes it (`Succeeded` if `ackSeen`, else
/// `ClaimNotAdopted`).
let private closingSchedule (state: BaptismState) : BaptismEvent list =
    match state with
    | Idle -> [ LinkChanged(nonConnectedLink 0) ]
    | ClaimSent -> [ WriteCompleted baseInstant; Tick(baseInstant + Baptism.announceBudget) ]
    | AwaitingAnnounce deadline -> [ Tick deadline ]
    | Assigning -> [ WriteCompleted baseInstant; Tick(baseInstant + Baptism.adoptionBudget) ]
    | AwaitingAdoption(deadline, _) -> [ Tick deadline ]
    | Terminal _ -> []

/// Wildcard-free witness that a reached outcome is one of the exactly
/// SEVEN §4.2 cases — an eighth `BaptismOutcome` case breaks this
/// `match` AND the Lean `baptize_outcome_total` proof together.
let private outcomeWitnessed (outcome: BaptismOutcome) : bool =
    match outcome with
    | Succeeded -> true
    | WaitTimeout -> true
    | UnexpectedVariant _ -> true
    | ClaimNotAdopted -> true
    | PanelDisappeared -> true
    | LinkLost -> true
    | TransmissionFailure ClaimStep -> true
    | TransmissionFailure AssignStep -> true

let private isClaimCompletion (state: BaptismState, ev: BaptismEvent) =
    match state, ev with
    | ClaimSent, WriteCompleted _ -> true
    | _ -> false

let private isMatchingAnnouncementWhileWaiting (cfg: AttemptConfig) (state, ev) =
    match state, ev with
    | AwaitingAnnounce _, AnnouncementHeard frame ->
        frame.Uuid = cfg.SelectedUuid
        && VariantDecoder.decode frame.MachineType = Marketing cfg.ChosenVariant
    | _ -> false

let private isAssignCompletion (state: BaptismState, ev: BaptismEvent) =
    match state, ev with
    | Assigning, WriteCompleted _ -> true
    | _ -> false

/// The `SetAddressAcked` pivot: the `0x25` ACK consumed while the adoption
/// window is still ACK-pending (`ackSeen = false`) — the F6 fast positive.
let private isAck (state: BaptismState, ev: BaptismEvent) =
    match state, ev with
    | AwaitingAdoption(_, false), SetAddressAcked -> true
    | _ -> false

/// The closing-tick pivot: a tick at/past the adoption deadline while the
/// ACK has already been seen and the silence held — the FR-006 confirmation
/// that reaches `Succeeded`.
let private isClosingTick (state: BaptismState, ev: BaptismEvent) =
    match state, ev with
    | AwaitingAdoption(deadline, true), Tick now -> deadline <= now
    | _ -> false

/// Skip past the first pivot match, returning the trace suffix after it.
let private afterPivot (pred: BaptismState * BaptismEvent -> bool) (steps: (BaptismState * BaptismEvent) list) =
    steps |> List.tryFindIndex pred |> Option.map (fun i -> steps |> List.skip (i + 1))

/// Ordered FIVE-pivot scan over a run's trace — the RHS shape of Lean
/// `baptize_progress` (RW01): a claim `WriteCompleted`, then a matching
/// announcement heard WHILE WAITING, then an assign `WriteCompleted`, then
/// the `SetAddressAcked` ACK while ACK-pending, then a closing `Tick` at/past
/// the adoption deadline with the silence held (F6 confirmed adoption). Each
/// `afterPivot` consumes the matched pivot and threads the suffix forward, so
/// the pivots must appear in this exact order.
let private hasSuccessDecomposition (cfg: AttemptConfig) (steps: (BaptismState * BaptismEvent) list) =
    steps
    |> afterPivot isClaimCompletion
    |> Option.bind (afterPivot (isMatchingAnnouncementWhileWaiting cfg))
    |> Option.bind (afterPivot isAssignCompletion)
    |> Option.bind (afterPivot isAck)
    |> Option.bind (afterPivot isClosingTick)
    |> Option.isSome

/// Generator-level event script. Deadline-relative ticks cannot be
/// realized up front (the announce deadline is anchored at the claim
/// write's completion instant, CHK010), so scripts are realized by
/// `realizeFrom`, which tracks the live FSM state.
type ScriptedEvent =
    | MatchingAnnouncement
    | WrongVariantAnnouncement of raw: byte
    | VirginAnnouncement
    | ForeignAnnouncement of raw: byte
    | TickBeforeDeadline
    | TickAtDeadline
    | SnapshotKeepsSelected
    | SnapshotDropsSelected
    | LinkStaysConnected
    | LinkLeavesConnected of shape: int
    | WriteOk
    | WriteFault
    | SetAddressAck

/// Realize one scripted event against the live state. The wrong-variant
/// fix-up substitutes the virgin marker when the generated byte happens
/// to equal the chosen variant's byte, so the announced identity is
/// guaranteed ≠ `Marketing cfg.ChosenVariant`.
let private realizeOne (cfg: AttemptConfig) (state: BaptismState) (now: DateTimeOffset) scripted =
    match scripted with
    | MatchingAnnouncement ->
        AnnouncementHeard(frameFor cfg.SelectedUuid (BoardVariant.encode cfg.ChosenVariant))
    | WrongVariantAnnouncement raw ->
        let chosen = BoardVariant.encode cfg.ChosenVariant
        let other = if raw = chosen then BoardVariant.virginMarker else raw
        AnnouncementHeard(frameFor cfg.SelectedUuid other)
    | VirginAnnouncement -> AnnouncementHeard(frameFor cfg.SelectedUuid BoardVariant.virginMarker)
    | ForeignAnnouncement raw -> AnnouncementHeard(frameFor (foreignOf cfg.SelectedUuid) raw)
    | TickBeforeDeadline ->
        match state with
        | AwaitingAnnounce deadline -> Tick(deadline.AddMilliseconds(-1.0))
        | AwaitingAdoption(deadline, _) -> Tick(deadline.AddMilliseconds(-1.0))
        | _ -> Tick now
    | TickAtDeadline ->
        match state with
        | AwaitingAnnounce deadline -> Tick deadline
        | AwaitingAdoption(deadline, _) -> Tick deadline
        | _ -> Tick(now + Baptism.announceBudget)
    | SnapshotKeepsSelected -> PanelsChanged(snapshotKeeping cfg.SelectedUuid)
    | SnapshotDropsSelected -> PanelsChanged(snapshotDropping cfg.SelectedUuid)
    | LinkStaysConnected -> LinkChanged connectedLink
    | LinkLeavesConnected shape -> LinkChanged(nonConnectedLink shape)
    | WriteOk -> WriteCompleted now
    | WriteFault -> WriteFaulted
    | SetAddressAck -> SetAddressAcked

/// Realize a script into concrete `BaptismEvent`s, folding the real
/// `step` so deadline-relative ticks land on the intended side of the
/// announce window. The clock cursor advances 100 ms per event.
let private realizeFrom (cfg: AttemptConfig) (start: BaptismState) (script: ScriptedEvent list) =
    let folder (state, now: DateTimeOffset, acc) scripted =
        let ev = realizeOne cfg state now scripted
        (fst (Baptism.step cfg state ev), now.AddMilliseconds 100.0, ev :: acc)

    let (_, _, evs) = script |> List.fold folder (start, baseInstant, [])
    List.rev evs

/// Scripted run shape. `SuccessShaped` is the canonical successful
/// decomposition of Lean `baptize_progress`'s RHS (RW01, the 6-list /
/// 3-instant shape) — claim-pending self-loops, the claim `WriteOk` pivot,
/// window-keeping self-loops, the matching announcement, assign-pending
/// self-loops, the assign `WriteOk` pivot, ACK-pending self-loops, the
/// `SetAddressAck` pivot (F6), silence-window self-loops, the closing
/// `TickAtDeadline` past the adoption deadline, then an absorbed tail.
/// `ArbitraryScript` is an unconstrained event mix.
type BaptismScript =
    | SuccessShaped of
        claimPending: ScriptedEvent list *
        waitWindow: ScriptedEvent list *
        assignPending: ScriptedEvent list *
        ackPending: ScriptedEvent list *
        silenceWindow: ScriptedEvent list *
        tail: ScriptedEvent list
    | ArbitraryScript of ScriptedEvent list

let private scriptEvents (script: BaptismScript) : ScriptedEvent list =
    match script with
    | SuccessShaped(claimPending, waitWindow, assignPending, ackPending, silenceWindow, tail) ->
        claimPending
        @ [ WriteOk ]
        @ waitWindow
        @ [ MatchingAnnouncement ]
        @ assignPending
        @ [ WriteOk ]
        @ ackPending
        @ [ SetAddressAck ]
        @ silenceWindow
        @ [ TickAtDeadline ]
        @ tail
    | ArbitraryScript events -> events

/// One generated baptism run: the attempt config, the scripted event
/// sequence, and an extra suffix used to assert terminal absorption.
type BaptismScenario =
    { Config: AttemptConfig
      Script: BaptismScript
      More: ScriptedEvent list }

/// One generated `(config, state, event)` triple for the step-level
/// properties, biased so the rare `SendAssign`-producing combination
/// (`AwaitingAnnounce` + matching announcement) is well represented.
type StateEventPair =
    { PairConfig: AttemptConfig
      PairState: BaptismState
      PairEvent: BaptismEvent }

let private byteGen: Gen<byte> = Gen.choose (0, 255) |> Gen.map byte

let private configGen: Gen<AttemptConfig> =
    gen {
        let! u0 = Gen.choose (0, 1_000_000)
        let! u1 = Gen.choose (0, 1_000_000)
        let! u2 = Gen.choose (0, 1_000_000)
        let! variant = Gen.elements [ EdenXp; OptimusXp; R3LXp; EdenBs8 ]

        return
            { SelectedUuid = PanelUuid(uint32 u0, uint32 u1, uint32 u2)
              ChosenVariant = variant }
    }

/// Unconstrained scripted-event mix (every realizable shape; `WriteOk`
/// biased so arbitrary scripts regularly advance past the write-pending
/// states instead of self-looping forever).
let private anyScripted: Gen<ScriptedEvent> =
    Gen.frequency
        [ 2, Gen.constant MatchingAnnouncement
          2, Gen.map WrongVariantAnnouncement byteGen
          1, Gen.constant VirginAnnouncement
          2, Gen.map ForeignAnnouncement byteGen
          2, Gen.constant TickBeforeDeadline
          2, Gen.constant TickAtDeadline
          1, Gen.constant SnapshotKeepsSelected
          1, Gen.constant SnapshotDropsSelected
          1, Gen.constant LinkStaysConnected
          1, Gen.map LinkLeavesConnected (Gen.choose (0, 2))
          3, Gen.constant WriteOk
          1, Gen.constant WriteFault
          2, Gen.constant SetAddressAck ]

/// Self-loop-safe while a write is pending (`ClaimSent` / `Assigning`):
/// everything except a write resolution or link loss self-loops there.
let private writePendingSafe: Gen<ScriptedEvent> =
    Gen.frequency
        [ 2, Gen.constant MatchingAnnouncement
          2, Gen.map WrongVariantAnnouncement byteGen
          2, Gen.map ForeignAnnouncement byteGen
          2, Gen.constant TickBeforeDeadline
          1, Gen.constant TickAtDeadline
          1, Gen.constant SnapshotKeepsSelected
          1, Gen.constant SnapshotDropsSelected
          1, Gen.constant LinkStaysConnected ]

/// Keeps the announce window open in `AwaitingAnnounce`: foreign uuids,
/// selected-uuid VIRGIN re-announces (F1: panel still mid-cycle, keeps
/// waiting), ticks strictly before the deadline, selected-keeping snapshots,
/// link up, and write resolutions (which self-loop while waiting).
let private waitWindowSafe: Gen<ScriptedEvent> =
    Gen.frequency
        [ 3, Gen.map ForeignAnnouncement byteGen
          2, Gen.constant VirginAnnouncement
          3, Gen.constant TickBeforeDeadline
          1, Gen.constant SnapshotKeepsSelected
          1, Gen.constant LinkStaysConnected
          1, Gen.constant WriteOk
          1, Gen.constant WriteFault ]

/// Keeps the adoption window open in `AwaitingAdoption` (either ack state):
/// foreign uuids (no-op), ticks strictly before the adoption deadline,
/// selected-keeping snapshots (a no-op in adoption — the matched panel cannot
/// prune inside the window), link up, and write resolutions (which self-loop).
/// A selected-uuid announce (matching / wrong / virgin), a tick at/past the
/// deadline, or link loss all LEAVE the window, so none appear here.
let private adoptionWindowSafe: Gen<ScriptedEvent> =
    Gen.frequency
        [ 3, Gen.map ForeignAnnouncement byteGen
          3, Gen.constant TickBeforeDeadline
          1, Gen.constant SnapshotKeepsSelected
          1, Gen.constant LinkStaysConnected
          1, Gen.constant WriteOk
          1, Gen.constant WriteFault ]

let private scriptGen: Gen<BaptismScript> =
    Gen.frequency
        [ 1,
          gen {
              let! claimPending = Gen.listOf writePendingSafe
              let! waitWindow = Gen.listOf waitWindowSafe
              let! assignPending = Gen.listOf writePendingSafe
              let! ackPending = Gen.listOf adoptionWindowSafe
              let! silenceWindow = Gen.listOf adoptionWindowSafe
              let! tail = Gen.listOf anyScripted
              return SuccessShaped(claimPending, waitWindow, assignPending, ackPending, silenceWindow, tail)
          }
          1, Gen.map ArbitraryScript (Gen.listOf anyScripted) ]

let private outcomeGen: Gen<BaptismOutcome> =
    Gen.elements
        [ Succeeded
          WaitTimeout
          UnexpectedVariant(Unknown 0x42uy) // F1: UnexpectedVariant only on a non-virgin variant
          ClaimNotAdopted
          PanelDisappeared
          LinkLost
          TransmissionFailure ClaimStep
          TransmissionFailure AssignStep ]

let private stateGen: Gen<BaptismState> =
    Gen.frequency
        [ 1, Gen.constant Idle
          2, Gen.constant ClaimSent
          4,
          Gen.choose (0, 3600)
          |> Gen.map (fun s -> AwaitingAnnounce(baseInstant.AddSeconds(float s)))
          2, Gen.constant Assigning
          3,
          gen {
              let! s = Gen.choose (0, 3600)
              let! ack = Gen.elements [ true; false ]
              return AwaitingAdoption(baseInstant.AddSeconds(float s), ack)
          }
          1, Gen.map Terminal outcomeGen ]

/// FsCheck `Arbitrary` container — passed to `[<Property>]` via
/// `Arbitrary = [| typeof<BaptismGenerators> |]` (house pattern, see
/// `CacheConsistencyTests.FetchOutcomeArb`).
type BaptismGenerators =
    static member Scenario() : Arbitrary<BaptismScenario> =
        gen {
            let! config = configGen
            let! script = scriptGen
            let! more = Gen.listOf anyScripted

            return
                { Config = config
                  Script = script
                  More = more }
        }
        |> Arb.fromGen

    static member Pair() : Arbitrary<StateEventPair> =
        gen {
            let! config = configGen
            let! state = stateGen
            let! scripted = anyScripted

            return
                { PairConfig = config
                  PairState = state
                  PairEvent = realizeOne config state baseInstant scripted }
        }
        |> Arb.fromGen

/// FsCheck property mirroring the Lean theorem `baptize_outcome_total`
/// in `Phase3/BaptismSequence.lean` (T017), per `data-model.md` §4.2 /
/// FR-005: any scripted run, extended past its pending work by the
/// state's closing schedule (resolve the outstanding write, tick past
/// the deadline), terminates in `Terminal outcome` for exactly one of
/// the six §4.2 outcomes (the wildcard-free `outcomeWitnessed` match is
/// the closure witness), and the outcome is stable under arbitrary
/// further events — terminal absorption, the carrier of the FR-005/
/// clarification-4 never-flip rule.
[<Property(Arbitrary = [| typeof<BaptismGenerators> |])>]
let BaptismOutcomeTotal (scenario: BaptismScenario) =
    let cfg = scenario.Config
    let events = realizeFrom cfg (fst Baptism.start) (scriptEvents scenario.Script)
    let reached = run cfg events
    let closed = runFrom cfg reached (closingSchedule reached)

    match closed with
    | Terminal outcome ->
        let more = realizeFrom cfg closed scenario.More
        outcomeWitnessed outcome && runFrom cfg closed more = Terminal outcome
    | _ -> false

/// FsCheck property mirroring the Lean theorem `baptize_progress` in
/// `Phase3/BaptismSequence.lean` (RW01), per the confirmation-rework
/// data-model §4.1 / FR-004/FR-006: a run reaches `Terminal Succeeded` IFF
/// its trace decomposes as claim `WriteCompleted`, then an announcement
/// matching the selected uuid AND chosen variant heard while waiting, then
/// assign `WriteCompleted`, then the `SetAddressAcked` ACK while ACK-pending,
/// then a closing `Tick` at/past the adoption deadline with the silence held
/// (F6 confirmed adoption — the assign write completing no longer succeeds).
/// `SuccessShaped` scripts assert the completeness direction (the canonical
/// shape always confirms adoption and succeeds); `ArbitraryScript` runs assert
/// the equivalence — soundness is the Lean decomposition, and decomposition ⇒
/// success follows because the closing-tick pivot lands in `Terminal
/// Succeeded`, which absorbs the rest of the run (`terminal_absorbs`).
[<Property(Arbitrary = [| typeof<BaptismGenerators> |])>]
let BaptismSucceedsIffConfirmedAdoption (scenario: BaptismScenario) =
    let cfg = scenario.Config
    let events = realizeFrom cfg (fst Baptism.start) (scriptEvents scenario.Script)
    let succeeded = run cfg events = Terminal Succeeded
    let decomposed = hasSuccessDecomposition cfg (transitions cfg events)

    match scenario.Script with
    | SuccessShaped _ -> succeeded && decomposed
    | ArbitraryScript _ -> succeeded = decomposed

/// FsCheck property mirroring the Lean theorem `virgin_keeps_waiting` in
/// `Phase3/BaptismSequence.lean` (RW01), the F1 fix: in `AwaitingAnnounce` a
/// selected-uuid VIRGIN re-announce is a strict no-op (the panel is still
/// mid-cycle, not rejected), and `UnexpectedVariant` fires ONLY on a
/// non-virgin, non-chosen variant (`Marketing other` / `Unknown`). Stated at
/// the transition level over a generated config, deadline, and announced byte.
[<Property>]
let VirginAnnounceKeepsWaiting (cfg: AttemptConfig) (machineType: byte) (seconds: int) =
    let deadline = baseInstant.AddSeconds(float (abs (seconds % 3600)))

    let virginStep =
        Baptism.step cfg (AwaitingAnnounce deadline) (AnnouncementHeard(frameFor cfg.SelectedUuid BoardVariant.virginMarker))

    let virginIsNoOp = virginStep = (AwaitingAnnounce deadline, NoAction)

    let next, _ =
        Baptism.step cfg (AwaitingAnnounce deadline) (AnnouncementHeard(frameFor cfg.SelectedUuid machineType))

    let unexpectedOnlyOnOther =
        match next with
        | Terminal(UnexpectedVariant announced) -> announced <> Virgin && announced <> Marketing cfg.ChosenVariant
        | _ -> true

    virginIsNoOp && unexpectedOnlyOnOther

/// FsCheck property for the FR-006a rule (confirmation-rework data-model
/// §4.1): in `AwaitingAdoption`, ANY selected-uuid re-announce — matching,
/// wrong, or virgin, regardless of `ackSeen` — means the panel is still
/// announcing, i.e. it did NOT adopt the claim, so the attempt ends
/// `ClaimNotAdopted` (silence is authoritative). Stated at the transition
/// level; the run-level carrier is `BaptismSucceedsIffConfirmedAdoption`.
[<Property>]
let ClaimNotAdoptedWhenStillAnnouncing (cfg: AttemptConfig) (machineType: byte) (ackSeen: bool) (seconds: int) =
    let deadline = baseInstant.AddSeconds(float (abs (seconds % 3600)))

    let next, action =
        Baptism.step cfg (AwaitingAdoption(deadline, ackSeen)) (AnnouncementHeard(frameFor cfg.SelectedUuid machineType))

    next = Terminal ClaimNotAdopted && action = NoAction

/// FsCheck property mirroring the Lean theorem `no_success_without_adoption`
/// in `Phase3/BaptismSequence.lean` (RW01), the F6 carrier of "never a false
/// success on write-completion": a run can reach `Terminal Succeeded` only if
/// a `SetAddressAcked` (the `0x25` ACK) was observed AND a closing `Tick`
/// happened — the two events the corrected gate adds over the shipped
/// write-completion model.
[<Property(Arbitrary = [| typeof<BaptismGenerators> |])>]
let NoSuccessWithoutAdoption (scenario: BaptismScenario) =
    let cfg = scenario.Config
    let events = realizeFrom cfg (fst Baptism.start) (scriptEvents scenario.Script)

    if run cfg events = Terminal Succeeded then
        let sawAck = events |> List.exists (function SetAddressAcked -> true | _ -> false)
        let sawTick = events |> List.exists (function Tick _ -> true | _ -> false)
        sawAck && sawTick
    else
        true

/// FsCheck property mirroring the Lean theorem
/// `foreign_uuid_never_transitions` in `Phase3/BaptismSequence.lean`
/// (T017), per the data-model §4.1 edge case: an announcement whose
/// uuid ≠ selected is a STRICT no-op in every state — same state back,
/// `NoAction` — so in particular it never advances `AwaitingAnnounce`.
/// The `==>` implication skips the (vanishingly rare) generated uuid
/// collision, mirroring the Lean hypothesis `uuid ≠ cfg.selectedUuid`.
[<Property>]
let ForeignUuidNeverSatisfiesWait
    (cfg: AttemptConfig)
    (state: BaptismState)
    (machineType: byte)
    (u0: uint32)
    (u1: uint32)
    (u2: uint32)
    =
    let uuid = PanelUuid(u0, u1, u2)

    uuid <> cfg.SelectedUuid
    ==> lazy (Baptism.step cfg state (AnnouncementHeard(frameFor uuid machineType)) = (state, NoAction))

/// FsCheck property mirroring the Lean theorem
/// `no_assignment_without_match` in `Phase3/BaptismSequence.lean`
/// (T017), per FR-004: if `step` emits `SendAssign`, the state was
/// `AwaitingAnnounce` and the event was an announcement matching BOTH
/// the selected uuid and the chosen variant (and the FSM advanced to
/// `Assigning`). Stated as a total boolean implication rather than
/// `==>` — the premise is rare under random pairs, and `==>` would
/// bottom out the sample space. The `Pair` generator biases toward
/// `AwaitingAnnounce` + matching announcements so the premise branch
/// is exercised every run. The recorded-sends variant over the service
/// arrives in a later slice (T022e).
[<Property(Arbitrary = [| typeof<BaptismGenerators> |])>]
let NoAssignmentWithoutMatch_StepLevel (pair: StateEventPair) =
    let cfg = pair.PairConfig
    let nextState, action = Baptism.step cfg pair.PairState pair.PairEvent

    action <> SendAssign
    || (match pair.PairState, pair.PairEvent with
        | AwaitingAnnounce _, AnnouncementHeard frame ->
            frame.Uuid = cfg.SelectedUuid
            && VariantDecoder.decode frame.MachineType = Marketing cfg.ChosenVariant
            && nextState = Assigning
        | _ -> false)
