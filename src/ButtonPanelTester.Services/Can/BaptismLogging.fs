namespace Stem.ButtonPanelTester.Services.Can

open Microsoft.Extensions.Logging
open Stem.ButtonPanelTester.Core.Can

/// Pure projection helpers that render a baptism attempt into the
/// structured fields `BaptismService` logs once per attempt (FR-012,
/// `specs/004-baptism-workflow/data-model.md` §7), plus the single emit
/// wrapper. No state, no side effects beyond the wrapper's one
/// `LogInformation` call — each projection is a total, WILDCARD-FREE match
/// over a closed DU (`BaptismOutcome` / `MarketingVariant` / `BaptismState`)
/// so a future case fails to compile rather than silently logging a wrong
/// default, exactly as the Lean `baptize_outcome_total` witness demands.
module BaptismLogging =

    /// Stable, filterable name for the `{Outcome}` field — one per
    /// `BaptismOutcome` case (the closed taxonomy of `data-model.md` §4.2).
    /// `TransmissionFailure` appends the faulted `SequenceStep` so dashboards
    /// distinguish a claim fault from an assign fault. WILDCARD-FREE: a
    /// seventh outcome must break this match (mirrors Lean
    /// `baptize_outcome_total`, T017).
    let outcomeName (outcome: BaptismOutcome) : string =
        match outcome with
        | Succeeded -> "Succeeded"
        | WaitTimeout -> "WaitTimeout"
        | UnexpectedVariant _ -> "UnexpectedVariant"
        | PanelDisappeared -> "PanelDisappeared"
        | LinkLost -> "LinkLost"
        | TransmissionFailure ClaimStep -> "TransmissionFailure.ClaimStep"
        | TransmissionFailure AssignStep -> "TransmissionFailure.AssignStep"

    /// Marketing-variant name for the `{Variant}` field — the four marketed
    /// labels. WILDCARD-FREE over the closed `MarketingVariant` DU (a fifth
    /// variant breaks this match, as in `BoardVariant.encode`).
    let variantName (variant: MarketingVariant) : string =
        match variant with
        | EdenXp -> "EdenXp"
        | OptimusXp -> "OptimusXp"
        | R3LXp -> "R3LXp"
        | EdenBs8 -> "EdenBs8"

    /// Furthest FSM phase reached, for the `{StepReached}` field. `Idle`
    /// renders `NotStarted` (the entry-guard rejections never start the FSM);
    /// `Terminal` keeps the match total though it is unreachable as a
    /// *pre-terminal* state (the service threads the state the step was
    /// computed FROM). WILDCARD-FREE over `BaptismState`.
    let stepReached (stepState: BaptismState) : string =
        match stepState with
        | Idle -> "NotStarted"
        | ClaimSent -> "ClaimSent"
        | AwaitingAnnounce _ -> "AwaitingAnnounce"
        | Assigning -> "Assigning"
        | Terminal _ -> "Terminal"

    /// Render a `PanelUuid` as the canonical hex triple for the `{PanelUuid}`
    /// field (the same "%08X-%08X-%08X" shape `PanelDiscoveryService.uuidText`
    /// uses). Written inline because the Services layer must not depend on the
    /// GUI renderer.
    let uuidText (PanelUuid(u0, u1, u2)) : string =
        sprintf "%08X-%08X-%08X" u0 u1 u2

    /// Emit the ONE structured baptize audit record for an attempt (FR-012,
    /// `data-model.md` §7) at `Information`, via a template naming every field
    /// as a named parameter (stem-logging — NO string interpolation). `Action`
    /// is the literal `"Baptize"` (the baptize record; the reset record is
    /// `logResetAttempt` below) so the field is present and filterable. No
    /// operator-identity field (clarification 5). Called by `BaptismService`
    /// OUTSIDE its `stateLock`, exactly once per attempt (either at the FSM
    /// terminal or at an entry-guard rejection).
    let logBaptizeAttempt
        (logger: ILogger)
        (variant: MarketingVariant)
        (uuid: PanelUuid)
        (outcome: BaptismOutcome)
        (stepState: BaptismState)
        (startedAt: string)
        (completedAt: string)
        : unit =
        logger.LogInformation(
            "Baptize attempt {Action} {Variant} {PanelUuid} -> {Outcome} (step {StepReached}, {StartedAt} -> {CompletedAt})",
            "Baptize",
            variantName variant,
            uuidText uuid,
            outcomeName outcome,
            stepReached stepState,
            startedAt,
            completedAt)

    /// Stable, filterable name for the `{Outcome}` field of a RESET attempt —
    /// one per `ResetOutcome` case (`data-model.md` §5). WILDCARD-FREE: a fifth
    /// reset outcome must break this match.
    let resetOutcomeName (outcome: ResetOutcome) : string =
        match outcome with
        | Sent -> "Sent"
        | Declined -> "Declined"
        | ResetLinkLost -> "ResetLinkLost"
        | ResetTransmissionFailure -> "ResetTransmissionFailure"

    /// Furthest reset step reached, for the `{StepReached}` field
    /// (`data-model.md` §7 — reset's two steps are `confirmation` / `broadcast`).
    /// A `Declined` attempt stopped at the confirmation prompt; every other
    /// outcome entered the broadcast phase (the link/fault verdicts are all
    /// reached while broadcasting). WILDCARD-FREE over `ResetOutcome`.
    let resetStepReached (outcome: ResetOutcome) : string =
        match outcome with
        | Declined -> "Confirmation"
        | Sent -> "Broadcast"
        | ResetLinkLost -> "Broadcast"
        | ResetTransmissionFailure -> "Broadcast"

    /// Emit the ONE structured reset audit record for an attempt (FR-012,
    /// `data-model.md` §7; SC-006 — including a declined-at-confirmation
    /// attempt) at `Information`, via the SAME field template as the baptize
    /// record so dashboards filter both uniformly. `Action` is the literal
    /// `"Reset"`; reset carries no variant and no uuid (it is a broadcast — the
    /// target's uuid is unknown), so both render as `"-"` (data-model §7). No
    /// operator-identity field (clarification 5). Called by `BaptismService`
    /// exactly once per reset attempt, on every outcome path (no lock is held).
    let logResetAttempt
        (logger: ILogger)
        (outcome: ResetOutcome)
        (startedAt: string)
        (completedAt: string)
        : unit =
        logger.LogInformation(
            "Reset attempt {Action} {Variant} {PanelUuid} -> {Outcome} (step {StepReached}, {StartedAt} -> {CompletedAt})",
            "Reset",
            "-",
            "-",
            resetOutcomeName outcome,
            resetStepReached outcome,
            startedAt,
            completedAt)
