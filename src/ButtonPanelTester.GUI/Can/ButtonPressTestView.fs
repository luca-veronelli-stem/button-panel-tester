namespace Stem.ButtonPanelTester.GUI.Can

open System
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Styling
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Services.Can
open Stem.ButtonPanelTester.GUI

/// FuncUI view for the spec-005 button-press test surface (Phase F, T030,
/// FR-004 / FR-005 / FR-011). Pure render: the host (`App.fs`) passes the
/// already-computed `ButtonPressTest.testEnablement` verdict, the latest
/// `IButtonPressTestService` FSM state, the run's `ButtonSchema` (for the decal
/// labels FR-004), the clock instant `now` (so the per-button countdown FR-005
/// renders against the `Prompting` deadline without the view holding a clock),
/// and the Run callback, then re-renders on every `StateChanged` emission and on
/// the host's 1 Hz countdown tick. The GUI renders FSM state and the schema's
/// decals; it decides nothing — every outcome and the enablement verdict are
/// consumed from Core / the service, never re-derived here.
///
/// `ButtonPressTestState.Idle` is qualified throughout because the bare `Idle`
/// collides with `BaptismState.Idle` in the open `Core.Can` namespace; the other
/// FSM cases (`Prompting` / `Completed` / `Interrupted`) and `InterruptReason`'s
/// cases (`LinkLost` collides with `BaptismOutcome.LinkLost`) are qualified where
/// ambiguous. Every match over the FSM / outcome DUs is wildcard-free so a future
/// case fails to compile here.
[<RequireQualifiedAccess>]
module ButtonPressTestView =

    /// `true` while a run is in flight — the FSM is `Prompting`. The Run control
    /// is MODAL while a run is active (the service's `RunAsync` is modal and
    /// throws on a concurrent start); disabling Run here closes the GUI-side
    /// re-entrancy window.
    let isRunning (state: ButtonPressTestState) : bool =
        match state with
        | Prompting _ -> true
        | ButtonPressTestState.Idle
        | Completed _
        | Interrupted _ -> false

    /// Whole seconds remaining on a `Prompting` button's countdown (FR-005):
    /// `ceil (deadline - now)`, clamped at `0` once the deadline has passed (the
    /// service's deadline tick then records `Missed`). The view is pure — `now`
    /// is threaded in by the host, which owns the clock.
    let remainingSeconds (deadline: DateTimeOffset) (now: DateTimeOffset) : int =
        let secs = (deadline - now).TotalSeconds
        if secs <= 0.0 then 0 else int (ceil secs)

    /// One human-readable label per `ButtonOutcome` for the result grid
    /// (FR-011). Wildcard-free — adding a `ButtonOutcome` case makes this a
    /// compile error.
    let outcomeLabel (outcome: ButtonOutcome) : string =
        match outcome with
        | Pending -> "…"
        | Pass -> "Pass"
        | Missed -> "Missed"
        | Skipped -> "Skipped"

    /// Whether the Run control is active: the enablement guard says `Enabled`, a
    /// schema is resolvable for the selected panel, AND no run is in flight
    /// (modality). `Enabled` already requires the link `Connected`, a baptized
    /// panel selected, and that panel observable (`testEnablement`), so the
    /// surface NEVER offers a run on an unbaptized panel / non-`Connected` link
    /// (SC-008).
    let runEnabled (enablement: Enablement) (schema: ButtonSchema option) (state: ButtonPressTestState) : bool =
        enablement = Enabled && Option.isSome schema && not (isRunning state)

    /// The decal of the active button carrying wire `bit`, for the transient
    /// `Unexpected` notice (FR-008). A `RecordUnexpected` is always a wrong but
    /// ACTIVE button, so the bit is in the schema; the `bit N` fallback keeps the
    /// lookup total.
    let unexpectedDecal (schema: ButtonSchema) (bit: int) : string =
        schema.Active
        |> List.tryFind (fun a -> a.Bit = bit)
        |> Option.map (fun a -> a.Decal)
        |> Option.defaultValue (sprintf "bit %d" bit)

    /// The per-button results vector carried by every non-`Idle` FSM state — the
    /// grid source (FR-011). `Prompting`/`Completed` carry the live vector,
    /// `Interrupted` the partial at the moment of the halt; `Idle` has no run, so
    /// no grid. Wildcard-free.
    let private resultsOf (state: ButtonPressTestState) : ButtonOutcome[] option =
        match state with
        | Prompting(_, _, results) -> Some results
        | Completed results -> Some results
        | Interrupted(_, partial) -> Some partial
        | ButtonPressTestState.Idle -> None

    /// Pure render of the button-press test surface. The host supplies the
    /// `ButtonPressTest.testEnablement` verdict, the latest FSM `state`, the
    /// run's `schema` (`None` until a baptized variant is resolvable), the clock
    /// `now` (for the FR-005 countdown), the transient `unexpected` wrong-active
    /// press bit (FR-008, surfaced without advancing the prompt), and the Run /
    /// Retry / Skip callbacks. The current prompt renders by decal (FR-004,
    /// firmware name a secondary diagnostic) with the per-button countdown
    /// (FR-005); a `Missed`/in-flight button offers Retry (re-arm) and Skip
    /// (record Skipped + advance) (FR-009); the result grid renders one
    /// decal+outcome row per active button in canonical order; the aggregate
    /// all-active-passed indicator (FR-011) shows ONLY on `Completed` when every
    /// active button scored `Pass`.
    let view
        (enablement: Enablement)
        (state: ButtonPressTestState)
        (schema: ButtonSchema option)
        (now: DateTimeOffset)
        (unexpected: int option)
        (onRun: unit -> unit)
        (onRetry: unit -> unit)
        (onSkip: unit -> unit)
        (theme: ThemeVariant)
        : IView =
        ignore theme

        let runButton: IView =
            Button.create [
                Button.name "RunButtonPressTest"
                Button.content "Run button-press test"
                Button.isEnabled (runEnabled enablement schema state)
                Button.onClick (fun _ -> onRun ())
            ]
            :> IView

        // Current prompt (FR-004 / FR-005): the prompted button's decal as the
        // primary label, its firmware name as a secondary diagnostic detail, and
        // the live whole-seconds countdown against the `Prompting` deadline.
        let promptView: IView list =
            match state, schema with
            | Prompting(index, deadline, _), Some sch when index < sch.Active.Length ->
                let active = sch.Active.[index]

                [ StackPanel.create [
                      StackPanel.name "ButtonPressPrompt"
                      StackPanel.orientation Orientation.Vertical
                      StackPanel.spacing 2.0
                      StackPanel.children [
                          TextBlock.create [
                              TextBlock.name "ButtonPressPromptDecal"
                              TextBlock.text (sprintf "Press: %s" active.Decal)
                          ]
                          TextBlock.create [
                              TextBlock.name "ButtonPressPromptFirmware"
                              TextBlock.text (string active.Button)
                          ]
                          TextBlock.create [
                              TextBlock.name "ButtonPressCountdown"
                              TextBlock.text (sprintf "%d s" (remainingSeconds deadline now))
                          ]
                      ]
                  ]
                  :> IView ]
            | _ -> []

        // Transient Unexpected-press notice (FR-008): a wrong but ACTIVE button
        // pressed while another is prompted is logged-not-counted and does NOT
        // advance the prompt — surfaced here as an operator status line that
        // leaves the prompt (above) on the same button.
        let unexpectedView: IView list =
            match unexpected, schema with
            | Some bit, Some sch ->
                [ TextBlock.create [
                      TextBlock.name "ButtonPressUnexpected"
                      TextBlock.text (
                          sprintf "Unexpected press: %s — not counted; press the prompted button." (unexpectedDecal sch bit)
                      )
                      TextBlock.textWrapping TextWrapping.Wrap
                  ]
                  :> IView ]
            | _ -> []

        // Recovery controls (FR-009): Retry re-arms the current button with a
        // fresh countdown (a `Missed` button returns to `Pending`); Skip records
        // `Skipped` (≠ `Pass`) and advances. Offered while a run is `Prompting`
        // (the in-flight or `Missed` button); the service decides the transition.
        let recoveryView: IView list =
            if isRunning state then
                [ StackPanel.create [
                      StackPanel.name "ButtonPressRecovery"
                      StackPanel.orientation Orientation.Horizontal
                      StackPanel.spacing 4.0
                      StackPanel.children [
                          Button.create [
                              Button.name "ButtonPressRetry"
                              Button.content "Retry"
                              Button.onClick (fun _ -> onRetry ())
                          ]
                          Button.create [
                              Button.name "ButtonPressSkip"
                              Button.content "Skip"
                              Button.onClick (fun _ -> onSkip ())
                          ]
                      ]
                  ]
                  :> IView ]
            else
                []

        // Per-button result grid (FR-011): one decal + outcome row per active
        // button, in the schema's canonical order. The decal blocks share a name
        // so a test collecting them by name reads the canonical order directly.
        let gridView: IView list =
            match resultsOf state, schema with
            | Some results, Some sch when results.Length = sch.Active.Length ->
                let row (active: ActiveButton) (outcome: ButtonOutcome) : IView =
                    StackPanel.create [
                        StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 8.0
                        StackPanel.children [
                            TextBlock.create [
                                TextBlock.name "ButtonPressResultDecal"
                                TextBlock.text active.Decal
                            ]
                            TextBlock.create [
                                TextBlock.name "ButtonPressResultOutcome"
                                TextBlock.text (outcomeLabel outcome)
                            ]
                        ]
                    ]
                    :> IView

                [ StackPanel.create [
                      StackPanel.name "ButtonPressResultGrid"
                      StackPanel.orientation Orientation.Vertical
                      StackPanel.spacing 4.0
                      StackPanel.children [ for active, outcome in List.zip sch.Active (List.ofArray results) -> row active outcome ]
                  ]
                  :> IView ]
            | _ -> []

        // Aggregate "all active passed" indicator (FR-011): positive ONLY on a
        // `Completed` run where every active button scored `Pass`
        // (`allActivePassed`). Never shown for `Interrupted`
        // (`interrupt_excludes_all_passed`) nor while still `Prompting`.
        let allPassedView: IView list =
            match state with
            | Completed results when ButtonPressTest.allActivePassed results ->
                [ TextBlock.create [
                      TextBlock.name "ButtonPressAllPassed"
                      TextBlock.text "All active buttons passed."
                      TextBlock.textWrapping TextWrapping.Wrap
                  ]
                  :> IView ]
            | ButtonPressTestState.Idle
            | Prompting _
            | Completed _
            | Interrupted _ -> []

        let children: IView list =
            [ runButton ] @ promptView @ unexpectedView @ recoveryView @ gridView @ allPassedView

        StackPanel.create [
            StackPanel.name "ButtonPressTestSurface"
            StackPanel.orientation Orientation.Vertical
            StackPanel.spacing 8.0
            StackPanel.children children
        ]
        :> IView
