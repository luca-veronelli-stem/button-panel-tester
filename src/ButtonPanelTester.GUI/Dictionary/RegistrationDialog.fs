namespace Stem.ButtonPanelTester.GUI.Dictionary

open System
open System.Threading
open System.Threading.Tasks
open Avalonia
open Avalonia.Controls
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Hosts
open Avalonia.FuncUI.Types
open Avalonia.FuncUI.VirtualDom
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Threading
open Microsoft.Extensions.Logging
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Services.Registration

/// FuncUI registration dialog, per `specs/001-fetch-dictionary/spec.md`
/// §US2 (FR-014 – FR-017) and `research.md` R7. The dialog is hosted
/// in an Avalonia `Window` opened modally with `ShowDialog(owner)` by
/// the startup orchestration (`App.fs` T043).
///
/// The view is a pure `Model → Dispatch → IView` function for
/// headless testability (T047). The Window class owns the dispatch
/// loop and the side effects:
///
///   - On `Submit`: dispatch a background task that calls
///     `IRegistrationClient.RegisterAsync token`, then re-dispatches
///     `RegistrationCompleted` on the UI thread.
///   - On `RegistrationCompleted (Ok credential)`: persist via
///     `ICredentialStore.SaveAsync` then close the window with the
///     `RegistrationOutcome.Completed credential` result.
///   - On `RegistrationCompleted (Error err)`: rest the dialog at
///     `Failed (errorMessage err)` so the inline error renders and
///     the technician can correct the token and retry.
///   - On Window close without a successful registration: the
///     `OutcomeTask` resolves to `RegistrationOutcome.Dismissed`.
module RegistrationDialog =

    /// Three substates of the dialog. `Failed` carries the
    /// human-readable inline error message keyed off
    /// `RegistrationError` per `contracts/registration-api.md`
    /// §"RegistrationError → Message".
    type State =
        | Idle
        | Submitting
        | Failed of message: string

    /// View model. `Token` mirrors the `TextBox`'s text; `State`
    /// drives the inline error text + the Submit button caption /
    /// enablement.
    type Model = { Token: string; State: State }

    /// Empty initial state — no token typed, no submission in flight,
    /// no inline error visible.
    let initial: Model = { Token = ""; State = Idle }

    /// Elmish-style messages dispatched by the view.
    type Message =
        | TokenChanged of token: string
        | Submit
        | RegistrationCompleted of result: Result<InstallationCredential, RegistrationError>

    /// Map a typed `RegistrationError` to the inline message text per
    /// `contracts/registration-api.md` §"Status → RegistrationError
    /// map", aligned with `stem-dictionaries-manager` v0.7.0. The
    /// strings here are the canonical user-facing surface — they are
    /// shown verbatim in the dialog's `Failed` state. The server's
    /// `error` body field is NOT surfaced (it's a developer hint,
    /// not user-facing copy).
    let errorMessage (err: RegistrationError) : string =
        match err with
        | TokenInvalid ->
            "The token was not accepted. Check it and try again, or contact STEM for a fresh one."
        | TokenAlreadyUsed ->
            "This bootstrap token has already been used. If you need to re-register this machine, contact STEM to revoke the existing installation."
        | TokenExpired ->
            "The bootstrap token has expired. Ask STEM for a fresh one."
        | TokenRevoked ->
            "The bootstrap token has been revoked. Contact STEM."
        | DescriptorRejected _ ->
            // 400 from the server: client bug, technician cannot fix.
            // The server's `detail` is dropped here for the dialog
            // surface — it lands in the log via the adapter's
            // LogWarning instead.
            "The client sent a malformed registration request. Please report this — include the application version and the time of the attempt."
        | RegistrationServerError status ->
            sprintf
                "The dictionary service is temporarily unavailable (HTTP %d). Try again later."
                status
        | RegistrationNetwork NetworkUnreachable ->
            "Could not reach the dictionary service. Check your network and try again."
        | RegistrationNetwork Timeout ->
            "The registration request timed out. Try again."
        | RegistrationNetwork _ ->
            "A network error occurred during registration."

    /// Whether the Submit button should be enabled. Disabled while a
    /// submission is in flight (`Submitting`), or while the token
    /// input does not parse via `BootstrapToken.TryCreate` (covers
    /// empty + whitespace-only input — same validation the production
    /// adapter applies).
    let canSubmit (model: Model) : bool =
        match model.State with
        | Submitting -> false
        | _ ->
            match BootstrapToken.TryCreate model.Token with
            | Ok _ -> true
            | Error _ -> false

    /// Pure state-transition function. Side effects (RegisterAsync,
    /// SaveAsync, window.Close) live in the Window's dispatch loop
    /// below; this function is total and referentially transparent.
    let update (msg: Message) (model: Model) : Model =
        match msg with
        | TokenChanged token -> { model with Token = token }
        | Submit ->
            if canSubmit model then
                { model with State = Submitting }
            else
                model
        | RegistrationCompleted(Ok _) ->
            // Window close is the host's job; the model is irrelevant
            // after a successful submit.
            model
        | RegistrationCompleted(Error err) ->
            { model with State = Failed(errorMessage err) }

    /// Pure rendering function. `dispatch` is invoked from the view's
    /// `onTextChanged` / `onClick` handlers; the host's loop is the
    /// only consumer in production, the test's captured-list dispatch
    /// in T047.
    let view (model: Model) (dispatch: Message -> unit) : IView =
        let inlineError =
            match model.State with
            | Failed message ->
                TextBlock.create [
                    TextBlock.text message
                    TextBlock.foreground Brushes.IndianRed
                    TextBlock.textWrapping TextWrapping.Wrap
                ]
                :> IView
            | _ ->
                // Reserved-height filler so the layout doesn't reflow
                // when the error appears or disappears.
                TextBlock.create [
                    TextBlock.text ""
                    TextBlock.height 0.0
                ]
                :> IView

        let submitCaption =
            match model.State with
            | Submitting -> "Submitting…"
            | _ -> "Submit"

        StackPanel.create [
            StackPanel.margin (Thickness 20.0)
            StackPanel.spacing 12.0
            StackPanel.children [
                TextBlock.create [
                    TextBlock.text "Register your tool"
                    TextBlock.fontSize 18.0
                    TextBlock.fontWeight FontWeight.Bold
                ]
                TextBlock.create [
                    TextBlock.text
                        "Paste the bootstrap token STEM sent you out of band, then submit."
                    TextBlock.textWrapping TextWrapping.Wrap
                ]
                TextBox.create [
                    TextBox.name "TokenInput"
                    TextBox.text model.Token
                    TextBox.watermark "Bootstrap token"
                    TextBox.isEnabled (model.State <> Submitting)
                    TextBox.onTextChanged (fun text ->
                        let value =
                            match text with
                            | null -> ""
                            | s -> s
                        dispatch (TokenChanged value))
                ]
                inlineError
                StackPanel.create [
                    StackPanel.orientation Orientation.Horizontal
                    StackPanel.spacing 8.0
                    StackPanel.horizontalAlignment HorizontalAlignment.Right
                    StackPanel.children [
                        Button.create [
                            Button.name "SubmitButton"
                            Button.content submitCaption
                            Button.isEnabled (canSubmit model)
                            Button.onClick (fun _ -> dispatch Submit)
                        ]
                    ]
                ]
            ]
        ]
        :> IView


/// Modal Window hosting the `RegistrationDialog` view. Owns the
/// dispatch loop, the side effects (RegisterAsync, SaveAsync,
/// Close), and the `RegistrationOutcome` `Task` the host awaits.
///
/// Constructor parameters:
///   - `registrationClient` — `IRegistrationClient` resolved from
///     the DI container at T044. `RegisterAsync` is invoked on every
///     `Submit` dispatch with the validated `BootstrapToken`.
///   - `credentialStore` — `ICredentialStore` resolved from the DI
///     container at T044. `SaveAsync` is invoked on a successful
///     registration before the window closes.
///   - `logger` — required `ILogger<RegistrationDialogWindow>` per
///     the STEM LOGGING standard (archetype A). Logs at Information
///     on successful save and at Warning on persistence failure.
///     Token + credential plaintexts never appear at any log level.
type RegistrationDialogWindow
    (
        registrationClient: IRegistrationClient,
        credentialStore: ICredentialStore,
        logger: ILogger<RegistrationDialogWindow>
    ) as this =
    inherit HostWindow()

    let outcomeTcs = TaskCompletionSource<RegistrationOutcome>()

    /// FuncUI `Component` wrapping the dialog. The component owns the
    /// reactive model via `ctx.useState` so FuncUI's patcher applies
    /// diff-based updates to the existing control tree — the
    /// `TextBox`'s focus is preserved across keystrokes instead of
    /// being thrown away on every `TokenChanged` (which is what
    /// happened with the previous `this.Content <- VirtualDom.create`
    /// approach: each render created a fresh `TextBox` and the
    /// originating focus pointed at the removed instance).
    let dialogComponent =
        Component(fun ctx ->
            let model = ctx.useState(RegistrationDialog.initial)

            let rec dispatch (msg: RegistrationDialog.Message) =
                let next = RegistrationDialog.update msg model.Current
                model.Set(next)

                match msg with
                | RegistrationDialog.Submit when next.State = RegistrationDialog.Submitting ->
                    match BootstrapToken.TryCreate next.Token with
                    | Ok token ->
                        let _ =
                            task {
                                let! result =
                                    registrationClient.RegisterAsync(
                                        token,
                                        CancellationToken.None
                                    )

                                Dispatcher.UIThread.Post(fun () ->
                                    dispatch (
                                        RegistrationDialog.RegistrationCompleted result
                                    ))
                            }

                        ()
                    | Error _ ->
                        // canSubmit guard validated already.
                        ()
                | RegistrationDialog.RegistrationCompleted(Ok credential) ->
                    let _ =
                        task {
                            try
                                do!
                                    credentialStore.SaveAsync(
                                        credential,
                                        CancellationToken.None
                                    )

                                logger.LogInformation(
                                    "Installation credential persisted; closing registration dialog."
                                )

                                Dispatcher.UIThread.Post(fun () ->
                                    outcomeTcs.TrySetResult(Completed credential)
                                    |> ignore

                                    this.Close())
                            with ex ->
                                logger.LogWarning(
                                    ex,
                                    "Failed to persist installation credential after successful registration."
                                )
                        }

                    ()
                | _ -> ()

            RegistrationDialog.view model.Current dispatch)

    do
        this.Title <- "Register your tool"
        this.Width <- 480.0
        this.Height <- 280.0
        this.WindowStartupLocation <- WindowStartupLocation.CenterOwner
        this.CanResize <- false
        this.Content <- dialogComponent

        this.Closed.Add(fun _ ->
            // If the window closed without a Completed outcome,
            // surface Dismissed. TrySetResult is a no-op when
            // Completed was already set above.
            outcomeTcs.TrySetResult(Dismissed) |> ignore)

    /// `Task` that resolves when the dialog closes:
    ///   - `RegistrationOutcome.Completed credential` after a
    ///     successful registration + persistence,
    ///   - `RegistrationOutcome.Dismissed` after any other close
    ///     (close button, ESC, programmatic close without success).
    member _.OutcomeTask: Task<RegistrationOutcome> = outcomeTcs.Task
