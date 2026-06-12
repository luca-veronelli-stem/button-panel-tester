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

/// Closed taxonomy of baptism-attempt outcomes — exactly six, per
/// `data-model.md` §4.2 (FR-005). `UnexpectedVariant` carries the
/// announced identity so the GUI names what the panel actually claimed
/// to be; `TransmissionFailure` carries which write faulted.
/// `WaitTimeout` carries no payload here — the FR-005/clarification-4
/// recovery guidance TEXT (late re-announce, re-run Baptize or Reset)
/// is rendered by the baptism service slice, not by Core.
/// Mirrors the Lean inductive `BaptismOutcome` (same case order);
/// closure is witnessed by the Lean theorem `baptize_outcome_total` in
/// `Phase3/BaptismSequence.lean` (T017) and by the FsCheck property
/// `BaptismOutcomeTotal` in
/// `Tests/Property/Can/BaptismSequenceProperties.fs` (T020); adding a
/// case requires updating both.
type BaptismOutcome =
    | Succeeded
    | WaitTimeout
    | UnexpectedVariant of announced: VariantIdentity
    | PanelDisappeared
    | LinkLost
    | TransmissionFailure of step: SequenceStep

/// Baptism-attempt FSM states, per `data-model.md` §4.1:
/// `Idle → ClaimSent → AwaitingAnnounce → Assigning → Terminal`.
/// `AwaitingAnnounce` CARRIES its deadline — entry instant plus
/// `Baptism.announceBudget`, anchored at claim-write completion
/// (CHK010). The six §4.1 `Succeeded`/`Failed_*` sinks collapse into
/// `Terminal outcome`, and a terminal state absorbs every further
/// event (Lean `terminal_absorbs`) — the carrier of the FR-005/
/// clarification-4 never-flip rule: a matching announcement arriving
/// after a reported `WaitTimeout` never flips the outcome.
/// Mirrors the Lean inductive `BaptismState` (T017) — same case
/// order; the Lean model's abstract `Nat` instants refine to
/// `DateTimeOffset` here.
type BaptismState =
    | Idle
    | ClaimSent
    | AwaitingAnnounce of deadline: DateTimeOffset
    | Assigning
    | Terminal of outcome: BaptismOutcome

/// The observable FSM inputs, in the `data-model.md` §4.3 row order,
/// with the RICH source payloads (`WhoIAmFrame`, `PanelsOnBus`,
/// `CanLinkState`). The Lean `BaptismEvent` (T017) abstracts these to
/// the one bit/pair each state consumes — `(uuid, variant)` for
/// announcements, `Bool` for presence and connectivity — and
/// `Baptism.step` performs that reduction: uuid + decoded variant from
/// the frame, `Map.containsKey` on the snapshot, Connected-or-not on
/// the link state.
type BaptismEvent =
    | AnnouncementHeard of WhoIAmFrame
    | Tick of now: DateTimeOffset
    | PanelsChanged of PanelsOnBus
    | LinkChanged of CanLinkState
    | WriteCompleted of now: DateTimeOffset
    | WriteFaulted

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

module Baptism =

    /// The announce-wait budget: 6 s, per research R4 — a settled scope
    /// pin (firmware re-announce delay is `2000 + (Σ uuid words mod
    /// 4000)` ms ∈ [2, 6] s, so the worst-case uuid answers at the very
    /// edge; FR-005's structured `WaitTimeout` covers the tail). Named
    /// constant per CHK010: the window is anchored at CLAIM-WRITE
    /// COMPLETION — see the `ClaimSent` arm of `step`. Mirrors Lean
    /// `announceBudget` (T017), `Nat` milliseconds refined to `TimeSpan`.
    let announceBudget: TimeSpan = TimeSpan.FromSeconds 6.0

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

    /// `AwaitingAnnounce` arm of `step` — the §4.1 wait. A selected-
    /// uuid announcement decoding to the chosen variant advances to
    /// `Assigning` and emits `SendAssign` (FR-004); a selected-uuid
    /// announcement with any other identity ends in `UnexpectedVariant`
    /// carrying the decoded identity; a FOREIGN uuid is a strict no-op
    /// (Lean `foreign_uuid_never_transitions`, FsCheck
    /// `ForeignUuidNeverSatisfiesWait`); a tick at/past the deadline
    /// ends in `WaitTimeout` (FR-005); a snapshot no longer containing
    /// the selected uuid ends in `PanelDisappeared`; the link leaving
    /// `Connected` ends in `LinkLost`; write resolutions self-loop.
    let private stepAwaiting (cfg: AttemptConfig) (deadline: DateTimeOffset) (event: BaptismEvent) =
        match event with
        | AnnouncementHeard frame when frame.Uuid = cfg.SelectedUuid ->
            let announced = VariantDecoder.decode frame.MachineType

            if announced = Marketing cfg.ChosenVariant then
                (Assigning, SendAssign)
            else
                (Terminal(UnexpectedVariant announced), NoAction)
        | Tick now when deadline <= now -> (Terminal WaitTimeout, NoAction)
        | PanelsChanged snapshot when not (Map.containsKey cfg.SelectedUuid snapshot) ->
            (Terminal PanelDisappeared, NoAction)
        | LinkChanged(Connected _) -> (AwaitingAnnounce deadline, NoAction)
        | LinkChanged _ -> (Terminal LinkLost, NoAction)
        | AnnouncementHeard _
        | Tick _
        | PanelsChanged _
        | WriteCompleted _
        | WriteFaulted -> (AwaitingAnnounce deadline, NoAction)

    /// `Assigning` arm of `step`: the assign write resolves. Completion
    /// is `Succeeded` (FR-006); a fault ends in `TransmissionFailure
    /// AssignStep`; the link leaving `Connected` ends in `LinkLost`;
    /// everything else self-loops.
    let private stepAssigning (event: BaptismEvent) : BaptismState * BaptismAction =
        match event with
        | WriteCompleted _ -> (Terminal Succeeded, NoAction)
        | WriteFaulted -> (Terminal(TransmissionFailure AssignStep), NoAction)
        | LinkChanged(Connected _) -> (Assigning, NoAction)
        | LinkChanged _ -> (Terminal LinkLost, NoAction)
        | _ -> (Assigning, NoAction)

    /// Pure TOTAL transition function over (attempt config, state,
    /// event), returning the next state AND the action to perform —
    /// the arm-for-arm transcription of Lean `step` in
    /// `Phase3/BaptismSequence.lean` (T017), per the `data-model.md`
    /// §4.1 transition table, with the Lean event abstractions reduced
    /// here against the rich §4.3 payloads (see `BaptismEvent`).
    /// `Terminal` absorbs every event — terminal-state idempotence
    /// (Lean `terminal_absorbs`), the never-flip rule: once an outcome
    /// is reported, no later event (including a late matching
    /// announcement after `WaitTimeout`) changes it. The governing
    /// theorems `baptize_progress`, `baptize_outcome_total` and
    /// `no_assignment_without_match` (T017) are witnessed at the value
    /// level by the FsCheck properties in
    /// `Tests/Property/Can/BaptismSequenceProperties.fs` (T020).
    let step (cfg: AttemptConfig) (state: BaptismState) (event: BaptismEvent) =
        match state with
        | Terminal _ -> (state, NoAction)
        | Idle -> stepIdle event
        | ClaimSent -> stepClaimSent event
        | AwaitingAnnounce deadline -> stepAwaiting cfg deadline event
        | Assigning -> stepAssigning event
