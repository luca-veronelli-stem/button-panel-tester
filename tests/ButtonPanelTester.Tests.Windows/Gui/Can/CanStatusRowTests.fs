module Stem.ButtonPanelTester.Tests.Windows.Gui.Can.CanStatusRowTests

open System
open Avalonia.Controls
open Avalonia.Controls.Shapes
open Avalonia.Interactivity
open Avalonia.Media
open Avalonia.Headless.XUnit
open Avalonia.FuncUI.VirtualDom
open Xunit
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.GUI.Can

/// `Avalonia.Headless.XUnit` tests for `CanStatusRow.view` per
/// `specs/002-can-link-lifecycle/tasks.md` T042. The
/// headless harness configured in the project-level `TestApp.fs`
/// lets `[<AvaloniaFact>]` materialise FuncUI views through
/// `VirtualDom.create` and inspect the resulting Avalonia control
/// tree without painting pixels.
///
/// Lives in `Tests.Windows` (`net10.0-windows`) per #76 — the GUI
/// project is `net10.0-windows`.

// --- helpers ---

let private noop () = ()

let private fixedNow =
    DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero)

let private renderState (state: CanLinkState) : StackPanel =
    let materialized =
        VirtualDom.create (CanStatusRow.view state noop)

    materialized :?> StackPanel

let private renderStateWith
    (state: CanLinkState)
    (onReconnect: unit -> unit)
    : StackPanel =
    let materialized =
        VirtualDom.create (CanStatusRow.view state onReconnect)

    materialized :?> StackPanel

let private chipChild (panel: StackPanel) : Ellipse =
    panel.Children
    |> Seq.choose (fun c ->
        match box c with
        | :? Ellipse as e -> Some e
        | _ -> None)
    |> Seq.exactlyOne

let private headlineChild (panel: StackPanel) : TextBlock =
    panel.Children
    |> Seq.choose (fun c ->
        match box c with
        | :? TextBlock as t when t.Name = "Headline" -> Some t
        | _ -> None)
    |> Seq.exactlyOne

let private tryReconnectButton (panel: StackPanel) : Button option =
    panel.Children
    |> Seq.choose (fun c ->
        match box c with
        | :? Button as b when b.Name = "ReconnectButton" -> Some b
        | _ -> None)
    |> Seq.tryHead

let private buttonText (b: Button) : string =
    match b.Content with
    | null -> ""
    | :? string as s -> s
    | other ->
        match other.ToString() with
        | null -> ""
        | s -> s

let private fixedAdapter: AdapterIdentification =
    { ChannelName = "PCAN-USB Pro FD (1)"
      DeviceId = "0x01"
      BaudrateBps = 250_000 }

// --- FR-003 visibility matrix (issue #128 / analyze finding F5) ---
//
// Single source of truth for the Reconnect-button visibility table
// amended in PR #126. Rows map 1:1 to FR-003's enumeration in
// `specs/002-can-link-lifecycle/spec.md` so a future change
// to the rules surfaces here as a single edit + a failing assertion
// rather than scattered ad-hoc facts.
//
// Asserts on the pure `shouldShowReconnectButton` predicate. The
// renderer (`view`) consults exactly this predicate to decide whether
// to splice the button child into the panel, so covering the matrix
// here is equivalent to covering it on the rendered tree, without
// pulling the headless Avalonia harness in 8 times. The existing
// `[<AvaloniaFact>]` cases below already pin the render-time wiring
// for the four representative states they cover.
let buttonVisibilityCases () : objnull[] seq =
    seq {
        yield [| box "Initializing"; box Initializing; box false |]

        yield
            [| box "Connected"
               box (Connected(fixedAdapter, fixedNow))
               box false |]

        yield
            [| box "Disconnected · NoAdapterPresent"
               box (Disconnected(NoAdapterPresent, fixedNow))
               box true |]

        yield
            [| box "Disconnected · LinkNotYetOpened"
               box (Disconnected(LinkNotYetOpened, fixedNow))
               box true |]

        yield
            [| box "Disconnected · MidSessionUnplug"
               box (Disconnected(MidSessionUnplug, fixedNow))
               box true |]

        yield
            [| box "Disconnected · ReconnectPending"
               box (Disconnected(ReconnectPending, fixedNow))
               box false |]

        yield
            [| box "Error · Recoverable"
               box (Error(Recoverable "Bus-off detected", fixedNow))
               box true |]

        yield
            [| box "Error · Fatal"
               box (Error(Fatal "PEAK status 0x40000 persists", fixedNow))
               box true |]

        yield
            [| box "Error · Fatal driver-missing"
               box (
                   Error(
                       Fatal "PEAK PCANBasic native DLL not found — install the PEAK driver",
                       fixedNow
                   )
               )
               box false |]
    }

[<Theory>]
[<MemberData(nameof buttonVisibilityCases)>]
let ShouldShowReconnectButton_MatchesFR003Table
    (label: string, state: CanLinkState, expectedVisible: bool)
    =
    let actualVisible = CanStatusRow.shouldShowReconnectButton state

    Assert.True(
        (expectedVisible = actualVisible),
        sprintf
            "FR-003 row '%s': expected button visible=%b but got %b"
            label
            expectedVisible
            actualVisible
    )

// --- T042.1: Connected → green chip + "Connected · <channel name>" ---

[<AvaloniaFact>]
let View_Connected_GreenChipAndChannelNameHeadline () =
    let state = Connected(fixedAdapter, fixedNow)

    let panel = renderState state
    let chip = chipChild panel
    let headline = headlineChild panel

    Assert.Same(Brushes.Green, chip.Fill)
    Assert.Equal(sprintf "Connected · %s" fixedAdapter.ChannelName, headline.Text)

[<AvaloniaFact>]
let View_Connected_NoReconnectButton () =
    let state = Connected(fixedAdapter, fixedNow)

    let panel = renderState state

    Assert.Equal(None, tryReconnectButton panel)

[<AvaloniaFact>]
let View_Connected_DetailTooltipMentionsAdapterIdentification () =
    let state = Connected(fixedAdapter, fixedNow)

    let panel = renderState state
    let headline = headlineChild panel

    match ToolTip.GetTip(headline) with
    | null -> Assert.Fail("expected a tooltip on the headline TextBlock")
    | tooltip ->
        let text = tooltip.ToString()
        Assert.Contains(fixedAdapter.ChannelName, text)
        Assert.Contains(fixedAdapter.DeviceId, text)
        Assert.Contains("250000", text)

// --- T042.2: Disconnected(NoAdapterPresent) → grey chip + "no PEAK adapter found" ---

[<AvaloniaFact>]
let View_DisconnectedNoAdapter_GreyChipAndRemediationHint () =
    let state = Disconnected(NoAdapterPresent, fixedNow)

    let panel = renderState state
    let chip = chipChild panel
    let headline = headlineChild panel

    Assert.Same(Brushes.Gray, chip.Fill)
    Assert.Contains("no PEAK adapter found", headline.Text)

[<AvaloniaFact>]
let View_DisconnectedNoAdapter_HasTryReconnectButton () =
    let state = Disconnected(NoAdapterPresent, fixedNow)

    let panel = renderState state

    match tryReconnectButton panel with
    | None -> Assert.Fail("expected a Reconnect button when Disconnected")
    | Some button ->
        Assert.Equal("Try reconnect", buttonText button)
        Assert.True(button.IsEnabled)

// --- T042.3: Error(Recoverable detail) → red chip + headline contains detail ---

[<AvaloniaFact>]
let View_ErrorRecoverable_RedChipAndHeadlineContainsDetail () =
    let detail = "Bus-off detected — try reconnect"
    let state = Error(Recoverable detail, fixedNow)

    let panel = renderState state
    let chip = chipChild panel
    let headline = headlineChild panel

    Assert.Same(Brushes.Red, chip.Fill)
    Assert.Contains(detail, headline.Text)

[<AvaloniaFact>]
let View_ErrorRecoverable_ReconnectButtonReadsTryReconnect () =
    let state = Error(Recoverable "Bus-off detected — try reconnect", fixedNow)

    let panel = renderState state

    match tryReconnectButton panel with
    | None -> Assert.Fail("expected a Reconnect button on Error(Recoverable)")
    | Some button -> Assert.Equal("Try reconnect", buttonText button)

// --- T042.5: Error headline encodes severity (issue #129 / analyze finding F4) ---
//
// Example-based tests under Principle II's "no reasonable property"
// exception — the FR-002a chip headline format (`<Severity> · <cause>
// — <imperative suggestion>`, clarification 2026-05-26) is a
// presentation choice, not an algebraic property, so the right tool
// is two explicit cases pinning the exact string. T042 (closed in
// PR-C) only covered severity in the detail affordance; this
// complements it by locking the headline contract too. The "Error"
// prefix is intentionally absent — the red chip already encodes the
// state family, see the Presentation surfaces section of spec.md.

[<AvaloniaFact>]
let View_ErrorRecoverable_HeadlineEncodesSeverityAndDetail () =
    let detail = "Bus-off detected — try reconnect"
    let state = Error(Recoverable detail, fixedNow)

    let panel = renderState state
    let headline = headlineChild panel

    Assert.Equal(sprintf "Recoverable · %s" detail, headline.Text)

[<AvaloniaFact>]
let View_ErrorFatal_HeadlineEncodesSeverityAndDetail () =
    let detail = "PEAK PCANBasic native DLL not found — install the PEAK driver"
    let state = Error(Fatal detail, fixedNow)

    let panel = renderState state
    let headline = headlineChild panel

    Assert.Equal(sprintf "Fatal · %s" detail, headline.Text)

[<AvaloniaFact>]
let View_ErrorFatal_ReconnectButtonReadsUnlikelyToHelp () =
    let state =
        Error(Fatal "PEAK status 0x40000 persists across reconnect — file bug", fixedNow)

    let panel = renderState state

    match tryReconnectButton panel with
    | None -> Assert.Fail("expected a Reconnect button on Error(Fatal)")
    | Some button -> Assert.Equal("Reconnect (unlikely to help)", buttonText button)

[<AvaloniaFact>]
let View_ErrorFatal_DetailTooltipSurfacesFatalSubClassification () =
    let detail = "PEAK status 0x40000 persists across reconnect — file bug"
    let state = Error(Fatal detail, fixedNow)

    let panel = renderState state
    let headline = headlineChild panel

    match ToolTip.GetTip(headline) with
    | null -> Assert.Fail("expected a tooltip on the headline TextBlock")
    | tooltip ->
        let text = tooltip.ToString()
        Assert.Contains("Fatal", text)
        Assert.Contains(detail, text)

// --- T042.4: Reconnect click raises the supplied callback ---

[<AvaloniaFact>]
let ReconnectClick_RaisesCallback () =
    // The view is pure — it does not know about ICanLinkService. The
    // App-side wiring binds the callback to the service's
    // ReconnectAsync (commit 6 of PR-C). Verifying the callback
    // fires here is sufficient to lock the contract the host wires
    // around.
    let mutable reconnectCount = 0

    let onReconnect () = reconnectCount <- reconnectCount + 1

    let panel =
        renderStateWith (Disconnected(NoAdapterPresent, fixedNow)) onReconnect

    let button =
        tryReconnectButton panel
        |> Option.defaultWith (fun () ->
            Assert.Fail("missing ReconnectButton")
            Unchecked.defaultof<_>)

    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))

    Assert.Equal(1, reconnectCount)

// --- #117 (US3): MidSessionUnplug headline distinct from NoAdapterPresent ---
//
// US3 (survive mid-session unplug): when an adapter that WAS connected
// is physically removed, the row must read "adapter unplugged
// mid-session" — visibly distinct from the boot-time "no PEAK adapter
// found" (NoAdapterPresent) so the technician can tell a yanked cable
// from an adapter that was never there. The button-visibility matrix
// above already covers MidSessionUnplug; this pins the headline string.
// Asserts on the pure `CanStatusRow.headline` (no headless harness
// needed), mirroring `ShouldShowReconnectButton_MatchesFR003Table`.

[<Fact>]
let Headline_DisconnectedMidSessionUnplug_RendersMidSessionPhrase () =
    Assert.Equal(
        "Disconnected · adapter unplugged mid-session",
        CanStatusRow.headline (Disconnected(MidSessionUnplug, fixedNow))
    )

[<Fact>]
let Headline_MidSessionUnplug_DistinctFromNoAdapterPresent () =
    let midSession =
        CanStatusRow.headline (Disconnected(MidSessionUnplug, fixedNow))

    let noAdapter =
        CanStatusRow.headline (Disconnected(NoAdapterPresent, fixedNow))

    Assert.NotEqual<string>(noAdapter, midSession)

// --- #143 (GUI): driver-download link on Fatal driver-missing ---
//
// On `Error(Fatal driver-missing, _)` the row offers a "Download PEAK
// driver" affordance pointing at the pinned PEAK downloads page. The URL
// is rendered as text on the control so it stays readable headless / for
// accessibility (the click opens the system browser, which a headless
// harness can't follow). The DU-level cause (a structured remediation)
// is the descoped full-fidelity #143; this slice is GUI-only and keys off
// a stable substring of the shipped Fatal headline.

let private pinnedDriverUrl =
    "https://www.peak-system.com/support/downloads/drivers/"

// Mirrors the shipped `PcanCanLink.buildFailureState` shape: short
// headline first line + a `\nTechnical detail: …` line (em-dash as in
// production).
let private driverMissingFatal =
    Error(
        Fatal
            "PEAK PCANBasic native DLL not found — install the PEAK driver\nTechnical detail: DllNotFoundException: Unable to load DLL 'PCANBasic'",
        fixedNow
    )

let private tryDriverDownloadLink (panel: StackPanel) : Button option =
    panel.Children
    |> Seq.choose (fun c ->
        match box c with
        | :? Button as b when b.Name = "DriverDownloadLink" -> Some b
        | _ -> None)
    |> Seq.tryHead

[<Fact>]
let IsDriverMissing_DriverMissingFatal_True () =
    Assert.True(CanStatusRow.isDriverMissing driverMissingFatal)

[<Fact>]
let IsDriverMissing_NonDriverFatal_False () =
    let state =
        Error(Fatal "bus-off persists across reconnect — file bug", fixedNow)

    Assert.False(CanStatusRow.isDriverMissing state)

[<Fact>]
let IsDriverMissing_Recoverable_False () =
    let state =
        Error(Recoverable "Bus-off detected — try reconnect", fixedNow)

    Assert.False(CanStatusRow.isDriverMissing state)

[<Fact>]
let IsDriverMissing_Disconnected_False () =
    Assert.False(
        CanStatusRow.isDriverMissing (Disconnected(NoAdapterPresent, fixedNow))
    )

[<Fact>]
let IsDriverMissing_Connected_False () =
    Assert.False(CanStatusRow.isDriverMissing (Connected(fixedAdapter, fixedNow)))

[<AvaloniaFact>]
let View_DriverMissingFatal_RendersCompactLinkWithUrlTooltip () =
    // #166: the caption is compact ("Download PEAK driver") and the full
    // URL rides on the button's tooltip rather than the content.
    let panel = renderState driverMissingFatal

    match tryDriverDownloadLink panel with
    | None -> Assert.Fail("expected a DriverDownloadLink on Fatal driver-missing")
    | Some link ->
        Assert.Equal<string>("Download PEAK driver", buttonText link)

        match ToolTip.GetTip(link) with
        | null -> Assert.Fail("expected the driver URL on the download link's tooltip")
        | tip -> Assert.Contains(pinnedDriverUrl, tip.ToString())

[<AvaloniaFact>]
let View_NonDriverFatal_NoDownloadLink () =
    let state =
        Error(Fatal "bus-off persists across reconnect — file bug", fixedNow)

    let panel = renderState state

    Assert.Equal(None, tryDriverDownloadLink panel)

[<AvaloniaFact>]
let View_Connected_NoDownloadLink () =
    let panel = renderState (Connected(fixedAdapter, fixedNow))

    Assert.Equal(None, tryDriverDownloadLink panel)

// #166: on the missing-driver Fatal the Reconnect button is hidden — a
// reconnect cannot conjure the driver, so the row offers only the
// download link. A non-driver Fatal still shows Reconnect.

[<AvaloniaFact>]
let View_DriverMissingFatal_NoReconnectButton () =
    let panel = renderState driverMissingFatal

    Assert.Equal(None, tryReconnectButton panel)

[<AvaloniaFact>]
let View_NonDriverFatal_HasReconnectButton () =
    let state =
        Error(Fatal "bus-off persists across reconnect — file bug", fixedNow)

    let panel = renderState state

    match tryReconnectButton panel with
    | None -> Assert.Fail("expected a Reconnect button on a non-driver Fatal")
    | Some _ -> ()
