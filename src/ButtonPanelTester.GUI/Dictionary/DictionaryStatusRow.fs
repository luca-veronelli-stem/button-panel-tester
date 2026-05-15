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
/// `specs/001-fetch-dictionary/spec.md` §US1. The view renders the
/// four observable parts of `DictionarySource`:
///
///   1. **Indicator pill** — green when `Live`, orange when `Cached`.
///   2. **Headline** — `Live · synced HH:MM` or
///      `Cached · last synced YYYY-MM-DD`.
///   3. **Detail affordance** — a `ToolTip` attached to the headline
///      surfacing the cache path, the `CacheOrigin` label, and any
///      `LastFailureReason`.
///   4. (Refresh button + in-flight UX) — deferred to T052 (US3 /
///      Phase 5).
///
/// The pure rendering function takes the cache file path (for the
/// detail tooltip) and the current `DictionarySource`. T038's
/// `Avalonia.Headless.XUnit` tests construct hand-crafted
/// `DictionarySource` values and assert on the rendered tree
/// without going through `IDictionaryService`. The subscription
/// pipeline that turns `IDictionaryService.SourceChanged` events
/// into view-model updates lives in `App.fs` (T035).
[<RequireQualifiedAccess>]
module DictionaryStatusRow =

    /// Colour-coded indicator brush per the table in `spec.md` §US1.
    /// `Brushes.Green` and `Brushes.Orange` are the BCL named colours;
    /// the FuncUI test surface in T038 reads `Fill` on the rendered
    /// `Ellipse` and compares with these brushes by reference.
    let indicatorBrush (source: DictionarySource) : IBrush =
        match source with
        | Live _ -> Brushes.Green :> IBrush
        | Cached _ -> Brushes.Orange :> IBrush

    /// Headline text. `Live · synced HH:MM` for live sources;
    /// `Cached · last synced YYYY-MM-DD` for cached sources. Date
    /// formatting uses the ISO-style invariant form so the headline
    /// reads consistently regardless of the host's locale (the GUI
    /// strings are English-only per Luca's repo convention).
    let headline (source: DictionarySource) : string =
        match source with
        | Live fetchedAt ->
            sprintf "Live · synced %02d:%02d"
                fetchedAt.Hour fetchedAt.Minute
        | Cached(fetchedAt, _, _) ->
            sprintf "Cached · last synced %04d-%02d-%02d"
                fetchedAt.Year fetchedAt.Month fetchedAt.Day

    /// Human-readable `CacheOrigin` label, surfaced in the detail
    /// tooltip. The exact strings ("from embedded seed" / "from local
    /// copy") are asserted on by T038 (spec.md §US1 Independent Test).
    let originLabel (origin: CacheOrigin) : string =
        match origin with
        | FromEmbeddedSeed -> "from embedded seed"
        | FromLocalFile -> "from local copy"

    /// Detail tooltip text. Always includes the cache path; for
    /// `Cached` sources also includes the origin label and any
    /// `LastFailureReason`.
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

    /// Pure rendering function. Returns an `IView` suitable for hosting
    /// in any FuncUI parent (a `Window` content, a panel child, the
    /// headless test runner). Subscribing to `SourceChanged` and
    /// re-rendering on each transition is the host's responsibility
    /// (T035 `App.fs` wires this through a `Component` wrapper).
    let view (cachePath: string) (source: DictionarySource) : IView =
        StackPanel.create [
            StackPanel.orientation Orientation.Horizontal
            StackPanel.spacing 8.0
            StackPanel.verticalAlignment VerticalAlignment.Center
            StackPanel.children [
                // 1. Indicator pill — colour-coded, fixed 12 px circle.
                Ellipse.create [
                    Ellipse.width 12.0
                    Ellipse.height 12.0
                    Ellipse.fill (indicatorBrush source)
                ]
                // 2. Headline text with the detail tooltip attached
                //    (the same TextBlock carries both observable parts
                //    so the test surface and the user-facing layout
                //    stay in lockstep).
                TextBlock.create [
                    TextBlock.text (headline source)
                    TextBlock.verticalAlignment VerticalAlignment.Center
                    ToolTip.tip (detailText cachePath source)
                ]
            ]
        ]
