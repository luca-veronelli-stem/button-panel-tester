namespace Stem.ButtonPanelTester.GUI.Dictionary

open System
open Avalonia.Controls
open Avalonia.Controls.Shapes
open Avalonia.Layout
open Avalonia.Media
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Stem.ButtonPanelTester.Core.Dictionary

/// FuncUI view for the dictionary status row, per
/// `specs/001-fetch-dictionary/spec.md` §US1 and §US3. Renders five
/// observable parts:
///
///   1. **Indicator pill** — green when `Live`, orange when `Cached`.
///      Opacity drops to 0.7 while a refresh is in flight (the
///      "pulsing" cue from `research.md` R8; ship as a fixed reduced
///      opacity, the actual 800 ms cycle animation is a polish
///      follow-up).
///   2. **Headline** — `Live · synced HH:MM` or
///      `Cached · last synced YYYY-MM-DD`; appends ` · refreshing…`
///      while in flight (R8 ellipsis cue).
///   3. **Detail affordance** — `ToolTip` on the headline surfacing
///      the cache path, the `CacheOrigin` label, and any
///      `LastFailureReason`.
///   4. **Refresh button** — FR-006 manual refresh control;
///      disabled and showing a spinner glyph while in flight.
///   5. **Re-register button** — appears only when
///      `LastFailureReason = Some Unauthorized` (FR-018; `research.md`
///      R11). Re-opens `RegistrationDialogWindow` without deleting
///      the existing credential — atomic server-side rotation per
///      `stem-dictionaries-manager` v0.8.0 (#74).
///
/// The view is a pure render. Refresh-state tracking and the
/// callbacks themselves live on the App.fs host (T052 wires the
/// `IDictionaryService.RefreshAsync` invocation and the
/// `RegistrationDialogWindow` re-open through here).
[<RequireQualifiedAccess>]
module DictionaryStatusRow =

    /// In-flight observability state for the row. `Idle` is the
    /// steady state; `Refreshing` flips the pulsing-opacity /
    /// spinner-glyph / refreshing-ellipsis cues per `research.md`
    /// R8 until `IDictionaryService.RefreshAsync` resolves.
    type RefreshState =
        | Idle
        | Refreshing

    /// Colour-coded indicator brush per the table in `spec.md` §US1.
    let indicatorBrush (source: DictionarySource) : IBrush =
        match source with
        | Live _ -> Brushes.Green :> IBrush
        | Cached _ -> Brushes.Orange :> IBrush

    /// Headline text. `Live · synced HH:MM` for live sources;
    /// `Cached · last synced YYYY-MM-DD` for cached sources. Date
    /// formatting uses the ISO-style invariant form (so the headline
    /// reads consistently regardless of the host's locale per Luca's
    /// English-only-strings convention), but the wall-clock fields
    /// project through `.LocalDateTime` so the technician sees their
    /// own clock — `DateTimeOffset.UtcNow` from `SystemClock` is a
    /// UTC timestamp, and rendering it raw would show 15:50 in Italy
    /// at 17:50 CEST.
    let headlineBase (source: DictionarySource) : string =
        match source with
        | Live fetchedAt ->
            let local = fetchedAt.LocalDateTime
            sprintf "Live · synced %02d:%02d" local.Hour local.Minute
        | Cached(fetchedAt, _, _) ->
            let local = fetchedAt.LocalDateTime
            sprintf "Cached · last synced %04d-%02d-%02d"
                local.Year local.Month local.Day

    let headline (source: DictionarySource) (refreshState: RefreshState) : string =
        let baseText = headlineBase source
        match refreshState with
        | Refreshing -> baseText + " · refreshing…"
        | Idle -> baseText

    /// Human-readable `CacheOrigin` label.
    let originLabel (origin: CacheOrigin) : string =
        match origin with
        | FromEmbeddedSeed -> "from embedded seed"
        | FromLocalFile -> "from local copy"

    let detailText (cachePath: string) (source: DictionarySource) : string =
        match source with
        | Live _ ->
            sprintf "Cache file: %s" cachePath
        | Cached(_, origin, lastFailure) ->
            let origin = originLabel origin
            let failure =
                match lastFailure with
                | Some reason -> sprintf " · last refresh failed: %A" reason
                | None -> ""
            sprintf "Cache file: %s · %s%s" cachePath origin failure

    /// `true` iff the row should render the Re-register affordance,
    /// per FR-018. Triggered exclusively by an `Unauthorized` chip —
    /// other failure reasons either resolve themselves on the next
    /// refresh (network, server) or do not point at a credential
    /// problem (cache, not-found, malformed payload).
    let shouldOfferReregister (source: DictionarySource) : bool =
        match source with
        | Cached(_, _, Some Unauthorized) -> true
        | _ -> false

    /// `research.md` R9: 90-day soft threshold on seed staleness. The
    /// muted-yellow advisory glyph appears next to the headline
    /// when the in-memory dictionary still originates from the
    /// embedded seed and no live refresh has succeeded for over
    /// 90 days. No hard block — the technician can still operate.
    let private seedStalenessDays = 90.0

    /// `true` iff the row should render the seed-staleness advisory
    /// glyph. False for any `Live` source, for `Cached(_,
    /// FromLocalFile, _)` (a live fetch has happened at some
    /// point), and for `Cached(_, FromEmbeddedSeed, _)` where the
    /// seed's `seededAt` is within the threshold.
    let shouldOfferStaleGlyph (source: DictionarySource) (now: DateTimeOffset) : bool =
        match source with
        | Cached(seededAt, FromEmbeddedSeed, _) ->
            (now - seededAt).TotalDays > seedStalenessDays
        | _ -> false

    /// Tooltip text on the stale-glyph element, per `research.md`
    /// R9. The `YYYY-MM-DD` formatting is the same locale-invariant
    /// shape the headline uses, projected through `.LocalDateTime`
    /// so the displayed date matches the technician's wall-clock.
    let staleTooltipText (seededAt: DateTimeOffset) : string =
        let local = seededAt.LocalDateTime
        sprintf
            "Last refreshed by STEM %04d-%02d-%02d; update via Refresh when network is available."
            local.Year local.Month local.Day

    /// Indicator-pill opacity. Reduced while refreshing so the
    /// technician sees a visual change without the indicator
    /// flipping colour (which would imply a state transition that
    /// has not yet happened).
    let indicatorOpacity (refreshState: RefreshState) : float =
        match refreshState with
        | Idle -> 1.0
        | Refreshing -> 0.7

    /// Refresh button caption. Spinner glyph (`⟳`) while refreshing,
    /// plain "Refresh" otherwise. Single-glyph spinner is enough to
    /// signal in-flight without graphical assets; a richer animation
    /// is a polish follow-up.
    let refreshButtonCaption (refreshState: RefreshState) : string =
        match refreshState with
        | Idle -> "Refresh"
        | Refreshing -> "⟳"

    /// Pure rendering function. Refresh-state tracking, callbacks,
    /// and the wall-clock `now` (used by the seed-staleness check)
    /// live on the App.fs host.
    let view
        (cachePath: string)
        (source: DictionarySource)
        (now: DateTimeOffset)
        (refreshState: RefreshState)
        (onRefresh: unit -> unit)
        (onReregister: unit -> unit)
        : IView =
        let reregisterChild : IView option =
            if shouldOfferReregister source then
                Button.create [
                    Button.name "ReregisterButton"
                    Button.content "Re-register"
                    Button.isEnabled (match refreshState with Idle -> true | Refreshing -> false)
                    Button.onClick (fun _ -> onReregister ())
                ]
                :> IView
                |> Some
            else
                None

        let staleGlyphChild : IView option =
            if shouldOfferStaleGlyph source now then
                let seededAt =
                    match source with
                    | Cached(t, _, _) -> t
                    | Live t -> t
                TextBlock.create [
                    TextBlock.name "StaleSeedGlyph"
                    TextBlock.text "⚠"  // U+26A0 WARNING SIGN
                    TextBlock.foreground (Brushes.Goldenrod :> IBrush)
                    TextBlock.verticalAlignment VerticalAlignment.Center
                    ToolTip.tip (staleTooltipText seededAt)
                ]
                :> IView
                |> Some
            else
                None

        let baseChildren : IView list = [
            // 1. Indicator pill — colour-coded; opacity drop while
            //    refreshing.
            Ellipse.create [
                Ellipse.name "IndicatorPill"
                Ellipse.width 12.0
                Ellipse.height 12.0
                Ellipse.fill (indicatorBrush source)
                Ellipse.opacity (indicatorOpacity refreshState)
            ]
            // 2. Headline text with the detail tooltip attached
            //    (the same TextBlock carries both observable parts
            //    so the test surface and the user-facing layout
            //    stay in lockstep). The trailing " · refreshing…"
            //    ellipsis is appended in `headline` for the
            //    `Refreshing` state.
            TextBlock.create [
                TextBlock.name "Headline"
                TextBlock.text (headline source refreshState)
                TextBlock.verticalAlignment VerticalAlignment.Center
                ToolTip.tip (detailText cachePath source)
            ]
        ]

        // Stale glyph (T053) renders RIGHT NEXT TO the headline per
        // R9, before the action buttons. Re-register button renders
        // last per FR-018 layout.
        let withStale =
            match staleGlyphChild with
            | Some g -> baseChildren @ [ g ]
            | None -> baseChildren

        let withRefresh =
            withStale @ [
                Button.create [
                    Button.name "RefreshButton"
                    Button.content (refreshButtonCaption refreshState)
                    Button.isEnabled (match refreshState with Idle -> true | Refreshing -> false)
                    Button.onClick (fun _ -> onRefresh ())
                ]
            ]

        let allChildren =
            match reregisterChild with
            | Some r -> withRefresh @ [ r ]
            | None -> withRefresh

        StackPanel.create [
            StackPanel.orientation Orientation.Horizontal
            StackPanel.spacing 8.0
            StackPanel.verticalAlignment VerticalAlignment.Center
            StackPanel.children allChildren
        ]
        :> IView
