module Stem.ButtonPanelTester.Tests.Windows.Gui.DictionaryStatusRowTests

open System
open Avalonia.Controls
open Avalonia.Controls.Shapes
open Avalonia.Media
open Avalonia.Headless.XUnit
open Avalonia.FuncUI.VirtualDom
open Xunit
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Services.Dictionary
open Stem.ButtonPanelTester.GUI.Dictionary

/// `Avalonia.Headless.XUnit` tests for `DictionaryStatusRow.view`
/// per `phase-3.md` §T038. The headless harness configured in
/// `TestApp.fs` (assembly-level `AvaloniaTestApplication`) lets
/// `[<AvaloniaFact>]` materialise FuncUI views through
/// `VirtualDom.create` and inspect the resulting Avalonia control
/// tree without painting pixels.

// --- helpers ---

let private cacheFilePath =
    @"C:\Users\test\AppData\Local\Stem.ButtonPanelTester\dictionary.json"

let private noop () = ()

let private fixedNow =
    DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero)

let private renderToStackPanel (source: DictionarySource) : StackPanel =
    let materialized =
        VirtualDom.create (
            DictionaryStatusRow.view
                cacheFilePath
                source
                fixedNow
                DictionaryStatusRow.Idle
                noop
                noop)

    materialized :?> StackPanel

let private renderToStackPanelWithState
    (source: DictionarySource)
    (refreshState: DictionaryStatusRow.RefreshState)
    : StackPanel =
    let materialized =
        VirtualDom.create (
            DictionaryStatusRow.view
                cacheFilePath
                source
                fixedNow
                refreshState
                noop
                noop)

    materialized :?> StackPanel

let private tryColdStartHint (panel: StackPanel) : TextBlock option =
    panel.Children
    |> Seq.choose (fun c ->
        match box c with
        | :? TextBlock as t when t.Name = "ColdStartHint" -> Some t
        | _ -> None)
    |> Seq.tryHead

let private ellipseChild (panel: StackPanel) : Ellipse =
    panel.Children
    |> Seq.choose (fun c ->
        match box c with
        | :? Ellipse as e -> Some e
        | _ -> None)
    |> Seq.exactlyOne

let private textBlockChild (panel: StackPanel) : TextBlock =
    // The status row may now host multiple TextBlocks (headline +
    // optional stale-glyph). The legacy T038 tests expect "the
    // headline", which is the TextBlock named "Headline".
    panel.Children
    |> Seq.choose (fun c ->
        match box c with
        | :? TextBlock as t when t.Name = "Headline" -> Some t
        | _ -> None)
    |> Seq.exactlyOne

// --- tests ---

[<AvaloniaFact>]
let View_CachedFromEmbeddedSeed_OrangePillAndCachedHeadline () =
    // Mid-day UTC so the local-date projection lands on 2026-05-15
    // regardless of the host's timezone offset (CI runs UTC, dev
    // runs CEST/CET; both fall comfortably inside the same calendar
    // day at noon UTC).
    let seedFetchedAt = DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero)
    let source = Cached(seedFetchedAt, FromEmbeddedSeed, None)

    let panel = renderToStackPanel source
    let pill = ellipseChild panel
    let headline = textBlockChild panel

    Assert.Same(Brushes.Orange, pill.Fill)
    Assert.Equal("Cached · last synced 2026-05-15", headline.Text)

[<AvaloniaFact>]
let View_CachedFromEmbeddedSeed_DetailTooltipMentionsEmbeddedSeed () =
    let source =
        Cached(
            DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero),
            FromEmbeddedSeed,
            None
        )

    let panel = renderToStackPanel source
    let headline = textBlockChild panel

    match ToolTip.GetTip(headline) with
    | null -> Assert.Fail("expected a tooltip on the headline TextBlock")
    | tooltip ->
        let text = tooltip.ToString()
        Assert.Contains("from embedded seed", text)
        Assert.Contains(cacheFilePath, text)

[<AvaloniaFact>]
let View_Live_GreenPillAndLiveHeadline () =
    let fetchedAt = DateTimeOffset(2026, 5, 18, 14, 30, 0, TimeSpan.Zero)
    let source = Live fetchedAt

    let panel = renderToStackPanel source
    let pill = ellipseChild panel
    let headline = textBlockChild panel

    // The headline displays the local-time projection of `fetchedAt`
    // — UTC 14:30 on a UTC CI agent reads as 14:30; on Luca's CEST
    // machine it reads 16:30 (CET) or 17:30 (CEST). Derive the
    // expected value from the same conversion the production code
    // uses so the assertion holds on any machine.
    let local = fetchedAt.LocalDateTime
    let expected = sprintf "Live · synced %02d:%02d" local.Hour local.Minute

    Assert.Same(Brushes.Green, pill.Fill)
    Assert.Equal(expected, headline.Text)

[<AvaloniaFact>]
let View_CachedWithFailureReason_DetailSurfacesReasonAndLocalCopy () =
    let source =
        Cached(
            DateTimeOffset(2026, 5, 18, 0, 0, 0, TimeSpan.Zero),
            FromLocalFile,
            Some Unauthorized
        )

    let panel = renderToStackPanel source
    let headline = textBlockChild panel

    match ToolTip.GetTip(headline) with
    | null -> Assert.Fail("expected a tooltip on the headline TextBlock")
    | tooltip ->
        let text = tooltip.ToString()
        Assert.Contains("from local copy", text)
        Assert.Contains("Unauthorized", text)

// --- tests: cold-start hint (phase-7) ---

[<AvaloniaFact>]
let View_Idle_DoesNotRenderColdStartHint () =
    // Steady state: the hint is reserved for the in-flight window so
    // it does not visually clutter the row at rest.
    let source = Live(DateTimeOffset(2026, 5, 18, 14, 30, 0, TimeSpan.Zero))

    let panel = renderToStackPanelWithState source DictionaryStatusRow.Idle

    Assert.Equal(None, tryColdStartHint panel)

[<AvaloniaFact>]
let View_Refreshing_RendersColdStartHintWithDocumentedText () =
    // While a refresh is in flight the row surfaces the worst-case
    // cold-start wait so the technician understands why the network
    // call may take up to ~90 s on a worker that just unloaded.
    let source = Live(DateTimeOffset(2026, 5, 18, 14, 30, 0, TimeSpan.Zero))

    let panel = renderToStackPanelWithState source DictionaryStatusRow.Refreshing

    match tryColdStartHint panel with
    | None ->
        Assert.Fail("expected a TextBlock named \"ColdStartHint\" while Refreshing")
    | Some hint ->
        Assert.Equal(
            "This may take up to a minute if the service has been idle.",
            hint.Text
        )

// --- tests: host-driven render state (#179 catastrophic-init dead-path) ---

/// Materialise `dictionaryView` for a render state and collect every
/// TextBlock in the resulting control tree (the row may be a bare
/// placeholder TextBlock or a StackPanel of headline + button).
let rec private collectTextBlocks (c: Control) : TextBlock list =
    match box c with
    | :? TextBlock as t -> [ t ]
    | :? Panel as p ->
        p.Children |> Seq.collect collectTextBlocks |> List.ofSeq
    | :? ContentControl as cc ->
        match cc.Content with
        | :? Control as inner -> collectTextBlocks inner
        | _ -> []
    | _ -> []

let private textOf (t: TextBlock) : string =
    match t.Text with
    | null -> ""
    | s -> s

let private renderDictionaryView (render: DictionaryStatusRow.DictionaryRender) : Control =
    VirtualDom.create (
        DictionaryStatusRow.dictionaryView
            cacheFilePath
            render
            fixedNow
            DictionaryStatusRow.Idle
            noop
            noop)

[<AvaloniaFact>]
let DictionaryView_Unavailable_ShowsUnavailableCopyNotInitializing () =
    // The catastrophic-init dead-path (#179): InitializeAsync returned
    // NoDictionaryAvailable, so SourceChanged never fired. The row must
    // surface the terminal "Dictionary unavailable" copy instead of
    // sitting on the "Initializing dictionary…" placeholder forever.
    let texts =
        renderDictionaryView (DictionaryStatusRow.Unavailable CacheUnreadable)
        |> collectTextBlocks
        |> List.map textOf

    Assert.Contains(texts, fun t -> t.StartsWith("Dictionary unavailable"))
    Assert.Contains(texts, fun t -> t.Contains("CacheUnreadable"))
    Assert.DoesNotContain("Initializing dictionary…", texts)

[<AvaloniaFact>]
let DictionaryView_Initializing_ShowsInitializingPlaceholder () =
    // The pre-first-event placeholder is unchanged: before any
    // SourceChanged or init result lands, the row reads "Initializing
    // dictionary…".
    let texts =
        renderDictionaryView DictionaryStatusRow.Initializing
        |> collectTextBlocks
        |> List.map textOf

    Assert.Contains("Initializing dictionary…", texts)

[<Fact>]
let RenderForInitResult_NoDictionaryAvailable_MapsToUnavailable () =
    // The heart of the dead-path fix: the App host must turn a discarded
    // NoDictionaryAvailable init result into a terminal Unavailable
    // render state. Returning None here (the old "let! _ = …" discard)
    // is exactly the bug.
    let render =
        DictionaryStatusRow.renderForInitResult (NoDictionaryAvailable CacheUnreadable)

    Assert.Equal(Some(DictionaryStatusRow.Unavailable CacheUnreadable), render)

[<Fact>]
let RenderForInitResult_Updated_ReturnsNone () =
    // The success path is a no-op: SourceChanged already drove the row
    // to Ready inside InitializeAsync, so the awaited Updated result
    // must not re-render anything.
    let dict = { ContentHash = "abc"; PanelTypes = [] }
    let source = Live fixedNow

    let render =
        DictionaryStatusRow.renderForInitResult (Updated(dict, source))

    Assert.Equal(None, render)
