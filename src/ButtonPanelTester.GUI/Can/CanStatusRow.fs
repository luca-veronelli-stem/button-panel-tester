namespace Stem.ButtonPanelTester.GUI.Can

open Avalonia.Controls
open Avalonia.Controls.Shapes
open Avalonia.Layout
open Avalonia.Media
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Stem.ButtonPanelTester.Core.Can

/// FuncUI view for the CAN status row, per
/// `specs/002-can-link-lifecycle/spec.md` §US1 and
/// `tasks.md` T038. Renders three observable parts of the
/// `CanLinkState`:
///
///   1. **Chip pill** — colour-coded by link state:
///      green = `Connected`, grey = `Disconnected | Initializing`,
///      red = `Error _`. The chip is the at-a-glance "is CAN up?"
///      cue from the spec's "answers 'is my CAN link up?' at a
///      glance" goal.
///   2. **Headline** — `Connected · <channel name>`,
///      `Disconnected · <reason>`, `Recoverable · <detail>`, or
///      `Fatal · <detail>` per the amended FR-002a (clarification
///      2026-05-26). The "Error" prefix is omitted because the red
///      chip already encodes the Error state family; detail strings
///      follow `<cause> — <imperative suggestion>` so the suggested
///      action appears on the row for free.
///   3. **Detail affordance** — `ToolTip` on the headline surfacing
///      the full `AdapterIdentification` (FR-004) when Connected,
///      the disconnect reason + `since` timestamp when Disconnected,
///      and the Recoverable/Fatal sub-classification (FR-002a) when
///      Error.
///   4. **Reconnect button** — visible whenever the state is NOT
///      `Connected`; click invokes the supplied callback the App
///      wires to `ICanLinkService.ReconnectAsync` (FR-003). Caption
///      switches to "Reconnect (unlikely to help)" on `Error.Fatal`
///      so the technician understands the second click probably
///      won't recover.
///
/// Pure render. The host (`App.fs`, commit 6 of PR-C) subscribes to
/// `ICanLinkService.LinkStateChanged`, marshals onto the UI thread,
/// and calls `view` with the latest state + the reconnect callback.
[<RequireQualifiedAccess>]
module CanStatusRow =

    /// Colour-coded chip brush per the T038 spec. `Initializing` is
    /// the brief pre-Open state; rendering it grey matches the
    /// Disconnected family because both mean "not yet on the bus".
    let chipBrush (state: CanLinkState) : IBrush =
        match state with
        | Connected _ -> Brushes.Green :> IBrush
        | Error _ -> Brushes.Red :> IBrush
        | Disconnected _
        | Initializing -> Brushes.Gray :> IBrush

    /// Human-readable phrase for a `DisconnectReason`. Used in both
    /// the headline and the detail tooltip.
    let disconnectReasonPhrase (reason: DisconnectReason) : string =
        match reason with
        | NoAdapterPresent -> "no PEAK adapter found"
        | LinkNotYetOpened -> "link not yet opened"
        | MidSessionUnplug -> "adapter unplugged mid-session"
        | ReconnectPending -> "reconnect pending"

    /// Returns the first newline-separated line of `s`. Convention
    /// (see `PcanCanLink.buildFailureState`): adapters that need to
    /// pair a short headline with technical detail encode both into
    /// the `ErrorClassification` detail string using `\n` as the
    /// separator. The headline takes the first line; the tooltip
    /// (`detailText`) carries the full multi-line text. Keeps the
    /// data model unchanged while still letting the GUI render a
    /// compact row + full diagnostics on hover.
    let private firstLine (s: string) : string =
        match s.IndexOf('\n') with
        | -1 -> s
        | i -> s.Substring(0, i)

    /// Headline text shown next to the chip. Format per the amended
    /// FR-002a (`specs/002-can-link-lifecycle/spec.md`,
    /// clarification session 2026-05-26):
    /// `Connected · <channel name>`, `Disconnected · <reason>`,
    /// `Recoverable · <detail>`, or `Fatal · <detail>`. The "Error"
    /// prefix is omitted because the red chip already encodes the
    /// state family. Detail strings are mandated to follow
    /// `<cause> — <imperative suggestion>` joined by em-dash, so the
    /// suggested action appears on the row verbatim without per-state
    /// renderer logic. Multi-line technical context (if any) is
    /// truncated at the first newline; the full text stays in
    /// `detailText`.
    let headline (state: CanLinkState) : string =
        match state with
        | Initializing -> "Initializing…"
        | Connected(adapter, _) -> sprintf "Connected · %s" adapter.ChannelName
        | Disconnected(reason, _) -> sprintf "Disconnected · %s" (disconnectReasonPhrase reason)
        | Error(Recoverable detail, _) -> sprintf "Recoverable · %s" (firstLine detail)
        | Error(Fatal detail, _) -> sprintf "Fatal · %s" (firstLine detail)

    /// Detail tooltip text. Surfaces `AdapterIdentification` when
    /// Connected (FR-004), the disconnect reason + the `since`
    /// timestamp when Disconnected, and the Recoverable/Fatal
    /// sub-classification + detail + `since` when Error (FR-002a).
    let detailText (state: CanLinkState) : string =
        match state with
        | Initializing -> "CAN link not yet attempted."
        | Connected(adapter, openedAt) ->
            let local = openedAt.LocalDateTime

            sprintf
                "%s · device id %s · %d bps · opened %02d:%02d"
                adapter.ChannelName
                adapter.DeviceId
                adapter.BaudrateBps
                local.Hour
                local.Minute
        | Disconnected(reason, since) ->
            let local = since.LocalDateTime

            sprintf
                "%s · since %02d:%02d"
                (disconnectReasonPhrase reason)
                local.Hour
                local.Minute
        | Error(Recoverable detail, since) ->
            let local = since.LocalDateTime
            sprintf "Recoverable: %s · since %02d:%02d" detail local.Hour local.Minute
        | Error(Fatal detail, since) ->
            let local = since.LocalDateTime
            sprintf "Fatal: %s · since %02d:%02d" detail local.Hour local.Minute

    /// `true` iff the row should render the Reconnect button. Per
    /// FR-003's visibility table (amended in PR #126):
    /// hidden in `Initializing` and `Disconnected · ReconnectPending`
    /// because both represent in-flight work where a click races the
    /// existing call; hidden in `Connected` because the link is
    /// already up; shown in every other Disconnected sub-case and in
    /// both Error sub-classifications. The full matrix is exercised
    /// by `ShouldShowReconnectButton_MatchesFR003Table` in
    /// `tests/.../CanStatusRowTests.fs`.
    let shouldShowReconnectButton (state: CanLinkState) : bool =
        match state with
        | Initializing -> false
        | Connected _ -> false
        | Disconnected(ReconnectPending, _) -> false
        | Disconnected _ -> true
        | Error _ -> true

    /// Reconnect button caption per T038. Switches to the
    /// "unlikely to help" wording on `Error.Fatal` so the
    /// technician knows the escalation rule has fired (the same
    /// PEAK status has now been observed twice across a reconnect
    /// per `research.md` R8); they can still click, but the second
    /// reconnect is mostly a confirmation gesture rather than a
    /// recovery path.
    let reconnectButtonCaption (state: CanLinkState) : string =
        match state with
        | Error(Fatal _, _) -> "Reconnect (unlikely to help)"
        | _ -> "Try reconnect"

    /// PEAK driver downloads page. The ad-tracking query string PEAK
    /// appends in marketing links (`gclid`, `gad_*`) is intentionally
    /// omitted — the bare path resolves to the same page.
    let private peakDriverUrl =
        System.Uri("https://www.peak-system.com/support/downloads/drivers/")

    /// `true` iff `state` is the missing-PEAK-driver `Error.Fatal` —
    /// the only state that should offer the driver-download affordance
    /// (#143). INTERIM string-match against a stable substring of the
    /// shipped Fatal headline (`PcanCanLink.buildFailureState`,
    /// "PEAK PCANBasic native DLL not found — install the PEAK driver").
    /// A structured cause carried on the DU would be cleaner and would
    /// not couple the GUI to headline wording, but that needs a Core DU
    /// + Lean change — the descoped full-fidelity #143, deferred.
    let isDriverMissing (state: CanLinkState) : bool =
        match state with
        | Error(Fatal detail, _) -> detail.Contains "PCANBasic native DLL not found"
        | _ -> false

    /// Pure rendering function. The host subscribes to the link
    /// service's `LinkStateChanged` observable, marshals onto the UI
    /// thread, and calls `view` with the latest state + the
    /// reconnect callback that wraps `ICanLinkService.ReconnectAsync`.
    let view (state: CanLinkState) (onReconnect: unit -> unit) : IView =
        let baseChildren: IView list =
            [ Ellipse.create [
                  Ellipse.name "ChipPill"
                  Ellipse.width 12.0
                  Ellipse.height 12.0
                  Ellipse.fill (chipBrush state)
              ]
              TextBlock.create [
                  TextBlock.name "Headline"
                  TextBlock.text (headline state)
                  TextBlock.verticalAlignment VerticalAlignment.Center
                  ToolTip.tip (detailText state)
              ] ]

        let withReconnect =
            if shouldShowReconnectButton state then
                baseChildren
                @ [ Button.create [
                        Button.name "ReconnectButton"
                        Button.content (reconnectButtonCaption state)
                        Button.onClick (fun _ -> onReconnect ())
                    ] ]
            else
                baseChildren

        // On the missing-driver Fatal, offer a one-click route to the
        // PEAK downloads page (#143). FuncUI 1.5.1 has no HyperlinkButton
        // DSL, so this is a Button whose click opens the system browser
        // via the shell. The URL is rendered verbatim in the content so
        // it stays readable headless / for accessibility (the launch
        // itself isn't observable in a headless harness).
        let allChildren =
            if isDriverMissing state then
                withReconnect
                @ [ Button.create [
                        Button.name "DriverDownloadLink"
                        Button.content ("Download PEAK driver: " + string peakDriverUrl)
                        Button.onClick (fun _ ->
                            System.Diagnostics.Process.Start(
                                System.Diagnostics.ProcessStartInfo(
                                    string peakDriverUrl,
                                    UseShellExecute = true
                                )
                            )
                            |> ignore)
                    ] ]
            else
                withReconnect

        StackPanel.create [
            StackPanel.orientation Orientation.Horizontal
            StackPanel.spacing 8.0
            StackPanel.verticalAlignment VerticalAlignment.Center
            StackPanel.children allChildren
        ]
        :> IView
