namespace Stem.ButtonPanelTester.Core.Can

open System

/// Which of the two baptism writes a `TransmissionFailure` outcome
/// faulted on, per `specs/004-baptism-workflow/data-model.md` §4.2
/// (FR-005): the WHO_ARE_YOU claim or the SET_ADDRESS assignment.
/// Mirrors the Lean inductive `SequenceStep` in
/// `lean/Stem/ButtonPanelTester/Phase3/BaptismSequence.lean` (T017) —
/// same name, same case order.
type SequenceStep =
    | ClaimStep
    | AssignStep

/// Closed taxonomy of baptism-attempt outcomes — exactly SEVEN, per
/// `data-model.md` confirmation-rework §4.2 (FR-005/FR-006a; supersedes
/// the parent §4.2's six). `UnexpectedVariant` carries the announced
/// identity so the GUI names what the panel actually claimed to be, and
/// fires ONLY on a different NON-virgin variant (F1: a selected-uuid
/// virgin re-announce keeps waiting, never `UnexpectedVariant`).
/// `ClaimNotAdopted` (NEW, FR-006a) is the F6 non-success — the assign
/// wrote but the panel kept announcing, or the adoption window elapsed
/// without the `0x25` ACK; it carries no payload (its FR-015 reset →
/// re-baptize guidance is fixed). `TransmissionFailure` carries which
/// write faulted. `WaitTimeout` / `ClaimNotAdopted` carry no payload
/// here — their recovery guidance TEXT is rendered by the service/GUI
/// slice, not by Core.
/// Mirrors the Lean inductive `BaptismOutcome` (same case order); closure
/// is witnessed by the Lean theorem `baptize_outcome_total` in
/// `Phase3/BaptismSequence.lean` (T017/RW01) and by the FsCheck property
/// `BaptismOutcomeTotal` in
/// `Tests/Property/Can/BaptismSequenceProperties.fs`. The F6 success gate
/// is the formal carrier `no_success_without_adoption` and the F1 keep-
/// waiting is `virgin_keeps_waiting`; adding a case requires updating both
/// the Lean proof and the FsCheck coverage.
type BaptismOutcome =
    | Succeeded
    | WaitTimeout
    | UnexpectedVariant of announced: VariantIdentity
    | ClaimNotAdopted
    | PanelDisappeared
    | LinkLost
    | TransmissionFailure of step: SequenceStep

/// Baptism-attempt FSM states, per `data-model.md` confirmation-rework
/// §4.1: `Idle → ClaimSent → AwaitingAnnounce → Assigning →
/// AwaitingAdoption → Terminal`. `AwaitingAnnounce` CARRIES its deadline
/// — entry instant plus `Baptism.announceBudget`, anchored at claim-write
/// completion (CHK010). `AwaitingAdoption` (NEW, F6) is the adoption-
/// confirmation window the assign write opens INSTEAD of succeeding: it
/// carries its deadline (entry instant plus `Baptism.adoptionBudget`,
/// anchored at assign-write completion) AND `ackSeen` — whether the
/// `0x25` application ACK has been observed yet; `Succeeded` requires it.
/// The `Succeeded`/`Failed_*` sinks collapse into `Terminal outcome`, and
/// a terminal state absorbs every further event (Lean `terminal_absorbs`)
/// — the carrier of the FR-005/clarification-4 never-flip rule: a
/// matching announcement arriving after a reported `WaitTimeout` never
/// flips the outcome. Mirrors the Lean inductive `BaptismState`
/// (T017/RW01) — same case order; the Lean model's abstract `Nat`
/// instants refine to `DateTimeOffset` here.
type BaptismState =
    | Idle
    | ClaimSent
    | AwaitingAnnounce of deadline: DateTimeOffset
    | Assigning
    | AwaitingAdoption of deadline: DateTimeOffset * ackSeen: bool
    | Terminal of outcome: BaptismOutcome

/// The observable FSM inputs, in the `data-model.md` §4.3 row order,
/// with the RICH source payloads (`WhoIAmFrame`, `PanelsOnBus`,
/// `CanLinkState`). The Lean `BaptismEvent` (T017/RW01) abstracts these
/// to the one bit/pair each state consumes — `(uuid, variant)` for
/// announcements, `Bool` for presence and connectivity — and
/// `Baptism.step` performs that reduction: uuid + decoded variant from
/// the frame, `Map.containsKey` on the snapshot, Connected-or-not on
/// the link state. `SetAddressAcked` (NEW, confirmation-rework §4.3) is
/// the RX observation of the `0x25` SET_ADDRESS application ACK addressed
/// to the tool (the RW03 `ISetAddressAckObserver`) — the adoption fast-
/// positive consumed in `AwaitingAdoption`; it carries no payload (the
/// load-bearing fact is that it fired).
type BaptismEvent =
    | AnnouncementHeard of WhoIAmFrame
    | Tick of now: DateTimeOffset
    | PanelsChanged of PanelsOnBus
    | LinkChanged of CanLinkState
    | WriteCompleted of now: DateTimeOffset
    | WriteFaulted
    | SetAddressAcked

/// The effect channel of `Baptism.step`: what the service must
/// transmit after a transition. `SendClaim` is the WHO_ARE_YOU claim
/// write (produced only by `Baptism.start`); `SendAssign` is the
/// SET_ADDRESS write, produced by exactly one arm — the validated-
/// match transition out of `AwaitingAnnounce` (FR-004), mechanised by
/// the Lean theorem `no_assignment_without_match` (T017) and witnessed
/// by the FsCheck property `NoAssignmentWithoutMatch_StepLevel` (T020).
/// Mirrors the Lean inductive `BaptismAction` (T017), with Lean's
/// `none` renamed `NoAction` — an F# case named `None` would collide
/// with `Option.None`.
type BaptismAction =
    | NoAction
    | SendClaim
    | SendAssign

/// Per-attempt configuration fixed at Baptize-press time, per
/// `data-model.md` §4 (FR-002 guards ensure exactly one announcing
/// panel is selected): the selected panel's uuid and the technician-
/// chosen variant. `Baptism.step` never changes it — one attempt, one
/// config; the tool holds no memory between attempts (FR-013).
/// Mirrors the Lean structure `AttemptConfig` (T017) — same field
/// order, with `PanelUuidKey`/`MarketingVariant` refined to the domain
/// `PanelUuid`/`MarketingVariant`.
type AttemptConfig =
    { SelectedUuid: PanelUuid
      ChosenVariant: MarketingVariant }

/// The rendered verdict of an enablement guard, per `data-model.md` §6
/// (FR-002 / FR-008): the GUI surface renders `Enabled` as an active button
/// and `Disabled` as a greyed button whose tooltip is the carried
/// `explanation` — the human-readable text naming the one unmet condition.
/// `explanation` is display-only (a `Detail`-style field, stem-fp §4): the
/// GUI branches on `Enabled` vs `Disabled`, never on the explanation text.
/// Mirrors the Lean inductive `Enablement` in
/// `lean/Stem/ButtonPanelTester/Phase3/Enablement.lean` (T027); the boolean
/// verdict is theorem-backed (`baptize_enabled_iff` / `reset_enabled_iff`)
/// and witnessed at the value level by the FsCheck `EnablementGuards`
/// property in `Tests/Property/Can/EnablementProperties.fs` (T028).
type Enablement =
    | Enabled
    | Disabled of explanation: string

/// Outcome of a reset-to-virgin attempt, per `data-model.md` §5 (FR-008,
/// FR-009, FR-010). Reset is a LINEAR flow, not an FSM: its guards are the
/// `Enablement` theorems (T027), its wire bytes the WHO_ARE_YOU codec
/// theorems (T002) — so this DU is RENDERED, never transitioned, and carries
/// NO Lean theorem of its own (data-model §8 lists no reset theorem). The
/// four cases are pinned by the integration suite `ResetE2ETests` (T030):
///   * `Sent` — the confirmation was given and BOTH dual-fwType broadcasts
///     completed (write completion is the success signal; the firmware never
///     replies, FR-010);
///   * `Declined` — the technician declined at confirmation; nothing was
///     transmitted (FR-009, SC-006-logged);
///   * `ResetLinkLost` — the link was not `Connected` at entry or left
///     `Connected` mid-broadcast;
///   * `ResetTransmissionFailure` — a broadcast write faulted (no retry).
/// The `Reset`-prefixed names avoid colliding with `BaptismOutcome.LinkLost`
/// / `TransmissionFailure` in this open namespace. Wildcard-free projection
/// in `BaptismLogging` (T031) makes a fifth case a compile error.
type ResetOutcome =
    | Sent
    | Declined
    | ResetLinkLost
    | ResetTransmissionFailure

module Baptism =

    /// The announce-wait budget: 6 s, per research R4 — a settled scope
    /// pin (firmware re-announce delay is `2000 + (Σ uuid words mod
    /// 4000)` ms ∈ [2, 6] s, so the worst-case uuid answers at the very
    /// edge; FR-005's structured `WaitTimeout` covers the tail). Named
    /// constant per CHK010: the window is anchored at CLAIM-WRITE
    /// COMPLETION — see the `ClaimSent` arm of `step`. Mirrors Lean
    /// `announceBudget` (T017), `Nat` milliseconds refined to `TimeSpan`.
    let announceBudget: TimeSpan = TimeSpan.FromSeconds 6.0

    /// The adoption-confirmation budget: 6 s, per the confirmation-rework
    /// data-model §Budgets — one worst-case announce period, anchored at
    /// SET_ADDRESS write completion (see the `Assigning` arm of `step`). A
    /// panel that adopted is silent immediately; the window only has to
    /// outlast one announce period to prove the silence is real, not a gap
    /// between announcements. SC-001's definitive-outcome bound is
    /// `announceBudget + adoptionBudget` (the two waits are sequential).
    /// Mirrors Lean `adoptionBudget` (RW01), `Nat` milliseconds refined to
    /// `TimeSpan`.
    let adoptionBudget: TimeSpan = TimeSpan.FromSeconds 6.0

    /// The Baptize-press transition (`Idle → ClaimSent`, §4.1): the
    /// attempt enters `ClaimSent` and the service performs the
    /// WHO_ARE_YOU claim write. The FR-002 enablement guards are
    /// upstream scope. Mirrors Lean `start` (T017).
    let start: BaptismState * BaptismAction = (ClaimSent, SendClaim)

    /// `Idle` arm of `step`: inert except link loss (§4.3 —
    /// `LinkChanged` is consumed in all non-terminal states). Kept for
    /// totality, as in the Lean model: `Idle` is unreachable in a
    /// service run because `start` enters `ClaimSent` directly.
    let private stepIdle (event: BaptismEvent) : BaptismState * BaptismAction =
        match event with
        | LinkChanged(Connected _) -> (Idle, NoAction)
        | LinkChanged _ -> (Terminal LinkLost, NoAction)
        | _ -> (Idle, NoAction)

    /// `ClaimSent` arm of `step`: the claim write resolves. Completion
    /// at `now` opens the announce window with `deadline = now +
    /// announceBudget` — the CHK010 anchor; a fault ends the attempt in
    /// `TransmissionFailure ClaimStep`; the link leaving `Connected`
    /// ends it in `LinkLost`; everything else self-loops.
    let private stepClaimSent (event: BaptismEvent) : BaptismState * BaptismAction =
        match event with
        | WriteCompleted now -> (AwaitingAnnounce(now + announceBudget), NoAction)
        | WriteFaulted -> (Terminal(TransmissionFailure ClaimStep), NoAction)
        | LinkChanged(Connected _) -> (ClaimSent, NoAction)
        | LinkChanged _ -> (Terminal LinkLost, NoAction)
        | _ -> (ClaimSent, NoAction)

    /// `AwaitingAnnounce` arm of `step` — the §4.1 wait, with the F1 fix
    /// (Lean `virgin_keeps_waiting`). A selected-uuid announcement branches
    /// THREE ways on the decoded variant: the chosen marketing variant
    /// advances to `Assigning` and emits `SendAssign` (FR-004); `Virgin`
    /// (`0xFF`) is the panel still mid-cycle, NOT a rejection, so the FSM
    /// KEEPS WAITING; any other non-virgin variant (`Marketing other` /
    /// `Unknown`) ends in `UnexpectedVariant` carrying the decoded identity.
    /// A FOREIGN uuid is a strict no-op (Lean `foreign_uuid_never_transitions`,
    /// FsCheck `ForeignUuidNeverSatisfiesWait`); a tick at/past the deadline
    /// ends in `WaitTimeout` (FR-005); a snapshot no longer containing the
    /// selected uuid ends in `PanelDisappeared`; the link leaving `Connected`
    /// ends in `LinkLost`; write resolutions and the ACK self-loop.
    let private stepAwaiting (cfg: AttemptConfig) (deadline: DateTimeOffset) (event: BaptismEvent) =
        match event with
        | AnnouncementHeard frame when frame.Uuid = cfg.SelectedUuid ->
            let announced = VariantDecoder.decode frame.MachineType

            match announced with
            | Marketing v when v = cfg.ChosenVariant -> (Assigning, SendAssign)
            | Virgin -> (AwaitingAnnounce deadline, NoAction)
            | Marketing _
            | Unknown _ -> (Terminal(UnexpectedVariant announced), NoAction)
        | Tick now when deadline <= now -> (Terminal WaitTimeout, NoAction)
        | PanelsChanged snapshot when not (Map.containsKey cfg.SelectedUuid snapshot) ->
            (Terminal PanelDisappeared, NoAction)
        | LinkChanged(Connected _) -> (AwaitingAnnounce deadline, NoAction)
        | LinkChanged _ -> (Terminal LinkLost, NoAction)
        | AnnouncementHeard _
        | Tick _
        | PanelsChanged _
        | WriteCompleted _
        | WriteFaulted
        | SetAddressAcked -> (AwaitingAnnounce deadline, NoAction)

    /// `Assigning` arm of `step` — the assign write resolves (F6). Completion
    /// at `now` no longer succeeds the attempt: it opens the adoption-
    /// confirmation window `AwaitingAdoption(now + adoptionBudget, ackSeen =
    /// false)`. A fault ends in `TransmissionFailure AssignStep`; the link
    /// leaving `Connected` ends in `LinkLost`; everything else self-loops.
    let private stepAssigning (event: BaptismEvent) : BaptismState * BaptismAction =
        match event with
        | WriteCompleted now -> (AwaitingAdoption(now + adoptionBudget, false), NoAction)
        | WriteFaulted -> (Terminal(TransmissionFailure AssignStep), NoAction)
        | LinkChanged(Connected _) -> (Assigning, NoAction)
        | LinkChanged _ -> (Terminal LinkLost, NoAction)
        | _ -> (Assigning, NoAction)

    /// `AwaitingAdoption` arm of `step` — the §4.1 confirmation wait (F6,
    /// Lean `no_success_without_adoption`). The `0x25` application ACK
    /// (`SetAddressAcked`) records `ackSeen := true`; a selected-uuid
    /// re-announce means the panel is STILL announcing, i.e. NOT adopted →
    /// `ClaimNotAdopted` (FR-006a), regardless of variant or `ackSeen`
    /// (silence is authoritative). A foreign uuid is a strict no-op; a
    /// `PanelsChanged` is a no-op (the just-matched panel cannot prune inside
    /// the window — silence is the absence of an announce, not a prune); a
    /// tick at/past the deadline closes the window — `Succeeded` iff `ackSeen`
    /// (ACK + held silence, FR-006), else `ClaimNotAdopted` (FR-006a, the D2
    /// strict gate: never a false success on a dropped ACK). The link leaving
    /// `Connected` ends in `LinkLost`; writes and sub-deadline ticks self-loop
    /// preserving `ackSeen`.
    let private stepAwaitingAdoption (cfg: AttemptConfig) (deadline: DateTimeOffset) (ackSeen: bool) (event: BaptismEvent) =
        match event with
        | SetAddressAcked -> (AwaitingAdoption(deadline, true), NoAction)
        | AnnouncementHeard frame when frame.Uuid = cfg.SelectedUuid -> (Terminal ClaimNotAdopted, NoAction)
        | Tick now when deadline <= now ->
            (Terminal(if ackSeen then Succeeded else ClaimNotAdopted), NoAction)
        | LinkChanged(Connected _) -> (AwaitingAdoption(deadline, ackSeen), NoAction)
        | LinkChanged _ -> (Terminal LinkLost, NoAction)
        | AnnouncementHeard _
        | Tick _
        | PanelsChanged _
        | WriteCompleted _
        | WriteFaulted -> (AwaitingAdoption(deadline, ackSeen), NoAction)

    /// Pure TOTAL transition function over (attempt config, state,
    /// event), returning the next state AND the action to perform —
    /// the arm-for-arm transcription of Lean `step` in
    /// `Phase3/BaptismSequence.lean` (T017/RW01), per the confirmation-
    /// rework `data-model.md` §4.1 transition table, with the Lean event
    /// abstractions reduced here against the rich §4.3 payloads (see
    /// `BaptismEvent`). `Terminal` absorbs every event — terminal-state
    /// idempotence (Lean `terminal_absorbs`), the never-flip rule: once an
    /// outcome is reported, no later event (including a late matching
    /// announcement after `WaitTimeout`) changes it. The governing theorems
    /// `baptize_progress`, `baptize_outcome_total`, `no_assignment_without_match`,
    /// `virgin_keeps_waiting` (F1) and `no_success_without_adoption` (F6,
    /// RW01) are witnessed at the value level by the FsCheck properties in
    /// `Tests/Property/Can/BaptismSequenceProperties.fs`.
    let step (cfg: AttemptConfig) (state: BaptismState) (event: BaptismEvent) =
        match state with
        | Terminal _ -> (state, NoAction)
        | Idle -> stepIdle event
        | ClaimSent -> stepClaimSent event
        | AwaitingAnnounce deadline -> stepAwaiting cfg deadline event
        | Assigning -> stepAssigning event
        | AwaitingAdoption(deadline, ackSeen) -> stepAwaitingAdoption cfg deadline ackSeen event

    /// Explanation a `Disabled` baptize/reset guard carries when the link is
    /// not `Connected` (the shared link-down conjunct, FR-002 / FR-008).
    [<Literal>]
    let LinkNotConnectedExplanation =
        "The CAN link is not connected; connect the adapter first."

    /// Explanation a `Disabled` baptize guard carries when NO panel is
    /// announcing (the zero-announcing conjunct, FR-002).
    [<Literal>]
    let NoPanelAnnouncingExplanation =
        "No panel is announcing on the bus; only an announcing (virgin) panel can be baptized."

    /// Explanation a `Disabled` baptize guard carries when two or more panels
    /// are announcing (the baptize two-or-more conjunct, FR-002): baptism
    /// targets exactly one selected panel.
    [<Literal>]
    let MultipleAnnouncingBaptizeExplanation =
        "More than one panel is announcing; baptism needs exactly one announcing panel on the bus."

    /// Explanation a `Disabled` baptize guard carries when exactly one panel
    /// is announcing but none is selected (the none-selected conjunct,
    /// FR-002).
    [<Literal>]
    let NoPanelSelectedExplanation =
        "Select the announcing panel before baptizing."

    /// Explanation a `Disabled` reset guard carries when two or more panels
    /// are announcing (the reset two-or-more conjunct, FR-008): the reset
    /// WHO_ARE_YOU is a broadcast and would reach EVERY panel on the bus, so
    /// it is refused while more than one is present.
    [<Literal>]
    let MultipleAnnouncingResetExplanation =
        "More than one panel is announcing; the reset broadcast would reach every panel on the bus."

    /// `true` iff the link state is `Connected` — the one bit both guards read
    /// off the link (data-model §6). Mirrors Lean `isConnected`.
    let private isConnected (link: CanLinkState) : bool =
        match link with
        | Connected _ -> true
        | _ -> false

    /// Baptize enablement guard (data-model §6, FR-002): `Enabled` IFF the
    /// link is `Connected`, exactly one panel is announcing, AND that panel
    /// is selected. Priority-ordered case analysis — link down / zero
    /// announcing / two-or-more announcing / none selected — each `Disabled`
    /// branch naming its one unmet conjunct. `announcingCount` ranges over
    /// ANNOUNCING panels only (silent claimed panels are invisible, CHK019).
    /// The Lean theorem `baptize_enabled_iff` (T027) proves this ordered
    /// analysis equivalent to the flat conjunction; the FsCheck
    /// `EnablementGuards` property (T028) witnesses it at the value level.
    let baptizeEnablement (link: CanLinkState) (announcingCount: int) (selection: PanelUuid option) : Enablement =
        if not (isConnected link) then Disabled LinkNotConnectedExplanation
        elif announcingCount = 0 then Disabled NoPanelAnnouncingExplanation
        elif announcingCount >= 2 then Disabled MultipleAnnouncingBaptizeExplanation
        elif Option.isNone selection then Disabled NoPanelSelectedExplanation
        else Enabled

    /// Reset enablement guard (data-model §6, FR-008): `Enabled` IFF the link
    /// is `Connected` AND at most one panel is announcing. Priority-ordered —
    /// link down / two-or-more announcing — each `Disabled` branch naming its
    /// one unmet conjunct (the two-or-more text states the broadcast reaches
    /// every panel). No selection conjunct: reset is a list-anchor-free
    /// broadcast (FR-008). The Lean theorem `reset_enabled_iff` (T027) proves
    /// the equivalence; the FsCheck `EnablementGuards` property (T028)
    /// witnesses it.
    let resetEnablement (link: CanLinkState) (announcingCount: int) : Enablement =
        if not (isConnected link) then Disabled LinkNotConnectedExplanation
        elif announcingCount >= 2 then Disabled MultipleAnnouncingResetExplanation
        else Enabled

    /// The known firmware-type classes a reset-to-virgin broadcast must cover,
    /// in transmit order, per research R2: `0x0004` (12 V) and `0x000F`
    /// (24 V). A reset cannot know the silent target panel's fwType, so it
    /// broadcasts WHO_ARE_YOU(`0xFF`, fwType, reset=1) once per class as ONE
    /// technician action — each broadcast only matches panels of its hardware
    /// class; the non-matching one is ignored by the slave's fwType gate
    /// (research R2). Reset succeeds when ALL of these writes complete
    /// (FR-010). 12 V leads 24 V to match the `ResetE2ETests` fixture order
    /// (the T007 payloads `FF 00 04 01` then `FF 00 0F 01`).
    let resetFwTypes: uint16 list = [ 0x0004us; 0x000Fus ]
