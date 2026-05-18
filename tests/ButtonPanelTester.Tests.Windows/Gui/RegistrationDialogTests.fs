module Stem.ButtonPanelTester.Tests.Windows.Gui.RegistrationDialogTests

open System
open System.Collections.Generic
open Avalonia.Controls
open Avalonia.FuncUI.VirtualDom
open Avalonia.Headless.XUnit
open Xunit
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.GUI.Dictionary

/// Tests for `RegistrationDialog.view` + `update` + `canSubmit` +
/// `errorMessage` per `phase-4.md` §T047. Lives in `Tests.Windows`
/// (net10.0-windows) because the dialog ships in
/// `ButtonPanelTester.GUI` (net10.0-windows for Avalonia). Reuses
/// the assembly-level `AvaloniaTestApplication` declared in
/// `TestApp.fs`.
///
/// The headless harness lets `[<AvaloniaFact>]` materialise FuncUI
/// views through `VirtualDom.create` and inspect the resulting
/// Avalonia control tree (`TextBox`, `Button`, `TextBlock`) without
/// painting pixels.

// --- helpers ---

let private renderToStackPanel (model: RegistrationDialog.Model)
                               (dispatch: RegistrationDialog.Message -> unit) =
    let materialized =
        VirtualDom.create (RegistrationDialog.view model dispatch)

    materialized :?> StackPanel

let private firstDescendant<'T when 'T :> Control>
    (panel: StackPanel)
    : 'T =
    let rec walk (parent: Panel) : 'T option =
        let directHit =
            parent.Children
            |> Seq.choose (fun c ->
                match box c with
                | :? 'T as t -> Some t
                | _ -> None)
            |> Seq.tryHead

        match directHit with
        | Some t -> Some t
        | None ->
            parent.Children
            |> Seq.choose (fun c ->
                match c with
                | :? Panel as p -> walk p
                | _ -> None)
            |> Seq.tryHead

    match walk panel with
    | Some t -> t
    | None ->
        Assert.Fail(sprintf "no descendant of type %s found in panel" typeof<'T>.Name)
        Unchecked.defaultof<_>

// --- pure-function tests (no Avalonia) ---

[<Fact>]
let CanSubmit_EmptyToken_False () =
    Assert.False(RegistrationDialog.canSubmit RegistrationDialog.initial)

[<Fact>]
let CanSubmit_WhitespaceOnlyToken_False () =
    let model = { RegistrationDialog.initial with Token = "   " }
    Assert.False(RegistrationDialog.canSubmit model)

[<Fact>]
let CanSubmit_NonEmptyTokenIdle_True () =
    let model = { RegistrationDialog.initial with Token = "valid-token" }
    Assert.True(RegistrationDialog.canSubmit model)

[<Fact>]
let CanSubmit_NonEmptyTokenSubmitting_False () =
    let model =
        { RegistrationDialog.initial with
            Token = "valid-token"
            State = RegistrationDialog.Submitting }

    Assert.False(RegistrationDialog.canSubmit model)

[<Fact>]
let Update_TokenChanged_UpdatesToken () =
    let next =
        RegistrationDialog.update
            (RegistrationDialog.TokenChanged "abc")
            RegistrationDialog.initial

    Assert.Equal("abc", next.Token)
    Assert.Equal(RegistrationDialog.Idle, next.State)

[<Fact>]
let Update_SubmitWithEmptyToken_IsNoOp () =
    let next =
        RegistrationDialog.update RegistrationDialog.Submit RegistrationDialog.initial

    Assert.Equal(RegistrationDialog.initial, next)

[<Fact>]
let Update_SubmitWithValidToken_TransitionsToSubmitting () =
    let model = { RegistrationDialog.initial with Token = "valid" }
    let next = RegistrationDialog.update RegistrationDialog.Submit model
    Assert.Equal(RegistrationDialog.Submitting, next.State)
    Assert.Equal("valid", next.Token)

[<Fact>]
let Update_RegistrationCompletedError_PutsModelInFailedState () =
    let model =
        { RegistrationDialog.initial with
            Token = "valid"
            State = RegistrationDialog.Submitting }

    let next =
        RegistrationDialog.update
            (RegistrationDialog.RegistrationCompleted(Error TokenInvalid))
            model

    match next.State with
    | RegistrationDialog.Failed message ->
        Assert.Equal(RegistrationDialog.errorMessage TokenInvalid, message)
    | other ->
        Assert.Fail(sprintf "expected Failed _, got %A" other)

[<Fact>]
let ErrorMessage_TokenInvalid_UsesContractText () =
    Assert.Equal(
        "The token was not accepted. Check it and try again, or contact STEM for a fresh one.",
        RegistrationDialog.errorMessage TokenInvalid
    )

[<Fact>]
let ErrorMessage_TokenAlreadyUsed_NamesPriorRegistration () =
    Assert.Equal(
        "This bootstrap token has already been used. If you need to re-register this machine, contact STEM to revoke the existing installation.",
        RegistrationDialog.errorMessage TokenAlreadyUsed
    )

[<Fact>]
let ErrorMessage_TokenExpired_NamesExpiry () =
    Assert.Equal(
        "The bootstrap token has expired. Ask STEM for a fresh one.",
        RegistrationDialog.errorMessage TokenExpired
    )

[<Fact>]
let ErrorMessage_TokenRevoked_NamesRevocation () =
    Assert.Equal(
        "The bootstrap token has been revoked. Contact STEM.",
        RegistrationDialog.errorMessage TokenRevoked
    )

[<Fact>]
let ErrorMessage_DescriptorRejected_NamesClientBug () =
    // 400 from the server: the dialog message must NOT echo the
    // server's `error` body (a developer hint). It surfaces a
    // user-actionable "report this" message instead.
    Assert.Equal(
        "The client sent a malformed registration request. Please report this — include the application version and the time of the attempt.",
        RegistrationDialog.errorMessage (DescriptorRejected "registration failed")
    )

[<Fact>]
let ErrorMessage_RegistrationServerError500_NamesUnavailability () =
    Assert.Equal(
        "The dictionary service is temporarily unavailable (HTTP 500). Try again later.",
        RegistrationDialog.errorMessage (RegistrationServerError 500)
    )

[<Fact>]
let ErrorMessage_RegistrationServerError503_NamesUnavailability () =
    Assert.Equal(
        "The dictionary service is temporarily unavailable (HTTP 503). Try again later.",
        RegistrationDialog.errorMessage (RegistrationServerError 503)
    )

[<Fact>]
let ErrorMessage_NetworkUnreachable_NamesNetwork () =
    Assert.Equal(
        "Could not reach the dictionary service. Check your network and try again.",
        RegistrationDialog.errorMessage (RegistrationNetwork NetworkUnreachable)
    )

[<Fact>]
let ErrorMessage_Timeout_NamesTimeout () =
    Assert.Equal(
        "The registration request timed out. Try again.",
        RegistrationDialog.errorMessage (RegistrationNetwork Timeout)
    )

// --- headless view tests ---

[<AvaloniaFact>]
let View_IdleEmptyToken_SubmitButtonDisabled () =
    let captured = ResizeArray<RegistrationDialog.Message>()
    let dispatch (msg: RegistrationDialog.Message) = captured.Add(msg)

    let panel = renderToStackPanel RegistrationDialog.initial dispatch
    let submit = firstDescendant<Button> panel

    Assert.False(submit.IsEnabled)

[<AvaloniaFact>]
let View_IdleValidToken_SubmitButtonEnabled () =
    let captured = ResizeArray<RegistrationDialog.Message>()
    let dispatch (msg: RegistrationDialog.Message) = captured.Add(msg)

    let model = { RegistrationDialog.initial with Token = "valid" }
    let panel = renderToStackPanel model dispatch
    let submit = firstDescendant<Button> panel

    Assert.True(submit.IsEnabled)

[<AvaloniaFact>]
let View_SubmitButtonClick_DispatchesSubmit () =
    let captured = ResizeArray<RegistrationDialog.Message>()
    let dispatch (msg: RegistrationDialog.Message) = captured.Add(msg)

    let model = { RegistrationDialog.initial with Token = "valid" }
    let panel = renderToStackPanel model dispatch
    let submit = firstDescendant<Button> panel

    let routedEvent =
        Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)

    submit.RaiseEvent(routedEvent)

    Assert.Contains(RegistrationDialog.Submit, captured)

[<AvaloniaFact>]
let View_TextBoxChange_DispatchesTokenChanged () =
    let captured = ResizeArray<RegistrationDialog.Message>()
    let dispatch (msg: RegistrationDialog.Message) = captured.Add(msg)

    let panel = renderToStackPanel RegistrationDialog.initial dispatch
    let textBox = firstDescendant<TextBox> panel

    textBox.Text <- "typed-token"

    let tokenChanges =
        captured
        |> Seq.choose (fun m ->
            match m with
            | RegistrationDialog.TokenChanged t -> Some t
            | _ -> None)
        |> Seq.toList

    Assert.Contains("typed-token", tokenChanges)

[<AvaloniaFact>]
let View_Submitting_TextBoxDisabledAndButtonShowsSubmittingCaption () =
    let captured = ResizeArray<RegistrationDialog.Message>()
    let dispatch (msg: RegistrationDialog.Message) = captured.Add(msg)

    let model =
        { RegistrationDialog.initial with
            Token = "valid"
            State = RegistrationDialog.Submitting }

    let panel = renderToStackPanel model dispatch
    let textBox = firstDescendant<TextBox> panel
    let submit = firstDescendant<Button> panel

    Assert.False(textBox.IsEnabled)
    Assert.False(submit.IsEnabled)

    match submit.Content with
    | :? string as s -> Assert.Equal("Submitting…", s)
    | other -> Assert.Fail(sprintf "expected string content, got %A" other)

[<AvaloniaFact>]
let View_FailedState_RendersInlineErrorMessage () =
    let captured = ResizeArray<RegistrationDialog.Message>()
    let dispatch (msg: RegistrationDialog.Message) = captured.Add(msg)

    let model =
        { RegistrationDialog.initial with
            Token = "stale-token"
            State = RegistrationDialog.Failed(RegistrationDialog.errorMessage TokenInvalid) }

    let panel = renderToStackPanel model dispatch

    let allTextBlocks =
        let acc = ResizeArray<TextBlock>()
        let rec walk (parent: Panel) =
            for child in parent.Children do
                match child with
                | :? TextBlock as tb -> acc.Add(tb)
                | :? Panel as p -> walk p
                | _ -> ()

        walk panel
        acc :> IReadOnlyList<TextBlock>

    let expectedMessage = RegistrationDialog.errorMessage TokenInvalid

    Assert.Contains(
        allTextBlocks,
        fun tb ->
            match tb.Text with
            | null -> false
            | t -> t = expectedMessage
    )
