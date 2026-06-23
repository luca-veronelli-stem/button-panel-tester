namespace Stem.ButtonPanelTester.Services.Can

open Microsoft.Extensions.Logging
open Stem.ButtonPanelTester.Core.Can

/// Pure projection helpers + the structured emit wrappers that
/// `ButtonPressTestService` logs as the FR-012 forensic trail (R10): one record
/// per prompt, observed press (expected AND unexpected), score, timeout, retry,
/// skip, interruption, and completion — each with a timestamp and (for the press
/// records) the observed wire bit. Template messages with named parameters
/// (stem-logging, archetype-A — NO string interpolation, NO `Console.WriteLine`).
/// Level discipline: prompt / score / skip / completion = `Information`;
/// Unexpected / Missed / Interrupted = `Warning`. The projections are total,
/// WILDCARD-FREE matches over the closed `ButtonOutcome` / `InterruptReason` DUs
/// so a future case fails to compile (mirroring Lean `test_outcome_total`, T018).
/// NO operator-identity / OS-user / machine-name field anywhere — the only
/// correlation key is the panel UUID, a device hardware identifier (Principle V).
module ButtonPressTestLogging =

    /// Shared template for the per-button records: every field is a NAMED
    /// parameter so the structured sink (and the `RecordingLogger` test) can read
    /// `Action` / `Index` / `Decal` / `Bit` / `At` without parsing the string.
    [<Literal>]
    let private buttonTemplate = "Button-press {Action} index {Index} {Decal} bit {Bit} at {At}"

    /// Stable, filterable name per `ButtonOutcome` case (closed taxonomy,
    /// data-model §4). WILDCARD-FREE: a fifth outcome must break this match
    /// (mirrors Lean `test_outcome_total`, T018).
    let outcomeName (outcome: ButtonOutcome) : string =
        match outcome with
        | Pending -> "Pending"
        | Pass -> "Pass"
        | Missed -> "Missed"
        | Skipped -> "Skipped"

    /// Stable name per `InterruptReason` case (data-model §4). WILDCARD-FREE: a
    /// third reason must break this match.
    let interruptReasonName (reason: InterruptReason) : string =
        match reason with
        | InterruptReason.LinkLost -> "LinkLost"
        | InterruptReason.PanelLost -> "PanelLost"

    /// Canonical hex triple for a `PanelUuid` — the run correlation key carried
    /// in the `BeginScope`. A device HARDWARE identifier, NOT operator identity
    /// (Principle V); the same "%08X-%08X-%08X" shape `BaptismLogging.uuidText`
    /// uses.
    let uuidText (PanelUuid(u0, u1, u2)) : string =
        sprintf "%08X-%08X-%08X" u0 u1 u2

    /// `Information` — a button is being prompted (run start, and on each advance).
    let logPrompt (logger: ILogger) (index: int) (decal: string) (bit: int) (at: string) : unit =
        logger.LogInformation(buttonTemplate, box "Prompt", box index, box decal, box bit, box at)

    /// `Information` — the prompted button scored `Pass` on the observed press
    /// edge (the `bit` that went `1 → 0`).
    let logPass (logger: ILogger) (index: int) (decal: string) (bit: int) (at: string) : unit =
        logger.LogInformation(buttonTemplate, box "Pass", box index, box decal, box bit, box at)

    /// `Information` — the prompted button was skipped (recorded `Skipped`, ≠ Pass).
    let logSkipped (logger: ILogger) (index: int) (decal: string) (bit: int) (at: string) : unit =
        logger.LogInformation(buttonTemplate, box "Skipped", box index, box decal, box bit, box at)

    /// `Information` — the technician re-armed the current button (Retry).
    let logRetry (logger: ILogger) (index: int) (decal: string) (bit: int) (at: string) : unit =
        logger.LogInformation(buttonTemplate, box "Retry", box index, box decal, box bit, box at)

    /// `Warning` — an observed press for an ACTIVE button other than the prompted
    /// one: logged with its observed `bit`, NOT counted (FR-008).
    let logUnexpected (logger: ILogger) (index: int) (bit: int) (at: string) : unit =
        logger.LogWarning(buttonTemplate, box "Unexpected", box index, box "-", box bit, box at)

    /// `Warning` — the prompted button timed out (recorded `Missed`).
    let logMissed (logger: ILogger) (index: int) (decal: string) (bit: int) (at: string) : unit =
        logger.LogWarning(buttonTemplate, box "Missed", box index, box decal, box bit, box at)

    /// `Warning` — the run was interrupted (the link left `Connected`, or the
    /// selected panel disappeared from the bus).
    let logInterrupted (logger: ILogger) (reason: InterruptReason) (at: string) : unit =
        logger.LogWarning("Button-press {Action} {Reason} at {At}", box "Interrupted", box (interruptReasonName reason), box at)

    /// `Information` — every active button resolved; the run completed with the
    /// aggregate `allPassed` verdict.
    let logCompleted (logger: ILogger) (allPassed: bool) (at: string) : unit =
        logger.LogInformation("Button-press {Action} allPassed {AllPassed} at {At}", box "Completed", box allPassed, box at)
