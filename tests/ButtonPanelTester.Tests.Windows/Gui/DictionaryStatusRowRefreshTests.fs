module Stem.ButtonPanelTester.Tests.Windows.Gui.DictionaryStatusRowRefreshTests

open System
open Avalonia.Controls
open Avalonia.Controls.Shapes
open Avalonia.Headless.XUnit
open Avalonia.FuncUI.VirtualDom
open Xunit
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.GUI.Dictionary

/// `Avalonia.Headless.XUnit` tests for the Phase 5 surface on
/// `DictionaryStatusRow.view` per `phase-5.md` §T056. Covers:
///   - Clicking Refresh raises the expected callback and the
///     in-flight UX renders.
///   - On `Refreshing` resolving Live, the row settles green.
///   - On `Failed Unauthorized`, the row settles orange and the
///     Re-register button is present.
///   - When `Cached(_, FromEmbeddedSeed, _)` and seed `seededAt` is
///     older than 90 days, the stale-glyph element is rendered.
///
/// Lives in `Tests.Windows` (per #76) — `Avalonia.Headless.XUnit`
/// binds to the `net10.0-windows` GUI project.

// --- helpers ---

let private cacheFilePath =
    @"C:\Users\test\AppData\Local\Stem.ButtonPanelTester\dictionary.json"

let private fixedNow =
    DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero)

let private noop () = ()

let private render
    (source: DictionarySource)
    (now: DateTimeOffset)
    (refreshState: DictionaryStatusRow.RefreshState)
    (onRefresh: unit -> unit)
    (onReregister: unit -> unit)
    : StackPanel =
    let view =
        DictionaryStatusRow.view
            cacheFilePath
            source
            now
            refreshState
            onRefresh
            onReregister

    (VirtualDom.create view) :?> StackPanel

let private findControl<'T when 'T : not struct> (panel: StackPanel) (name: string) : 'T option =
    panel.Children
    |> Seq.tryPick (fun c ->
        match box c with
        | :? 'T as t ->
            match box t with
            | :? Avalonia.StyledElement as se when se.Name = name -> Some t
            | _ -> None
        | _ -> None)

let private buttonText (b: Button) : string =
    match b.Content with
    | null -> ""
    | :? string as s -> s
    | other ->
        match other.ToString() with
        | null -> ""
        | s -> s

// --- T056.1: Refresh button click + in-flight UX ---

[<AvaloniaFact>]
let RefreshClick_RaisesCallbackAndShowsInFlightUx () =
    // First render (Idle) — capture the Refresh button and click it.
    // The click handler flips refreshState (the test owns the
    // mutable cell) and re-renders, then the test asserts on the
    // post-click control tree.
    let mutable refreshCount = 0
    let mutable refreshState = DictionaryStatusRow.Idle
    let cachedSource =
        Cached(
            DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero),
            FromLocalFile,
            None
        )

    let onRefresh () =
        refreshCount <- refreshCount + 1
        refreshState <- DictionaryStatusRow.Refreshing

    let idlePanel = render cachedSource fixedNow refreshState onRefresh noop

    let refreshButton =
        findControl<Button> idlePanel "RefreshButton"
        |> Option.defaultWith (fun () ->
            Assert.Fail("idle render is missing RefreshButton")
            Unchecked.defaultof<_>)

    Assert.Equal("Refresh", buttonText refreshButton)
    Assert.True(refreshButton.IsEnabled)

    // Drive the click handler directly — the headless harness
    // doesn't propagate ButtonBase.PerformClick through hit-testing
    // without a render pass; invoking the handler covers the same
    // surface (the callback wired in `view`).
    refreshButton.Command <- null
    refreshButton.RaiseEvent(
        new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent))

    Assert.Equal(1, refreshCount)
    Assert.Equal(DictionaryStatusRow.Refreshing, refreshState)

    let inflightPanel =
        render cachedSource fixedNow refreshState onRefresh noop

    let pill =
        findControl<Ellipse> inflightPanel "IndicatorPill"
        |> Option.defaultWith (fun () ->
            Assert.Fail("in-flight render is missing IndicatorPill")
            Unchecked.defaultof<_>)
    Assert.True(pill.Opacity < 1.0)

    let inflightButton =
        findControl<Button> inflightPanel "RefreshButton"
        |> Option.defaultWith (fun () ->
            Assert.Fail("in-flight render is missing RefreshButton")
            Unchecked.defaultof<_>)
    Assert.Equal("⟳", buttonText inflightButton)
    Assert.False(inflightButton.IsEnabled)

    let headline =
        inflightPanel.Children
        |> Seq.choose (fun c ->
            match box c with
            | :? Avalonia.Controls.TextBlock as t when t.Name = "Headline" -> Some t
            | _ -> None)
        |> Seq.exactlyOne
    Assert.Contains("refreshing…", headline.Text)

// --- T056.2: in-flight task resolves Live → row settles green ---

[<AvaloniaFact>]
let RefreshResolved_Live_SettlesGreen () =
    // After kickoffRefresh resolves Live, App.fs flips refreshState
    // back to Idle and re-renders with the new source. The test
    // simulates the final render and asserts on the green pill +
    // "Live" headline.
    let liveSource = Live (DateTimeOffset(2026, 5, 19, 12, 30, 0, TimeSpan.Zero))
    let panel = render liveSource fixedNow DictionaryStatusRow.Idle noop noop

    let pill =
        findControl<Ellipse> panel "IndicatorPill"
        |> Option.defaultWith (fun () ->
            Assert.Fail("missing IndicatorPill")
            Unchecked.defaultof<_>)

    Assert.Same(Avalonia.Media.Brushes.Green, pill.Fill)
    Assert.Equal(1.0, pill.Opacity)

    let headline =
        panel.Children
        |> Seq.choose (fun c ->
            match box c with
            | :? Avalonia.Controls.TextBlock as t when t.Name = "Headline" -> Some t
            | _ -> None)
        |> Seq.exactlyOne
    Assert.StartsWith("Live · synced", headline.Text)

// --- T056.3: Unauthorized → orange row + Re-register button ---

[<AvaloniaFact>]
let RefreshResolved_Unauthorized_SettlesOrangeWithReregisterButton () =
    let cachedFetchedAt = DateTimeOffset(2026, 5, 10, 9, 0, 0, TimeSpan.Zero)
    let unauthorized =
        Cached(cachedFetchedAt, FromLocalFile, Some Unauthorized)

    let panel = render unauthorized fixedNow DictionaryStatusRow.Idle noop noop

    let pill =
        findControl<Ellipse> panel "IndicatorPill"
        |> Option.defaultWith (fun () ->
            Assert.Fail("missing IndicatorPill")
            Unchecked.defaultof<_>)
    Assert.Same(Avalonia.Media.Brushes.Orange, pill.Fill)

    let reregister =
        findControl<Button> panel "ReregisterButton"
        |> Option.defaultWith (fun () ->
            Assert.Fail("expected Re-register button on Unauthorized")
            Unchecked.defaultof<_>)
    Assert.Equal("Re-register", buttonText reregister)
    Assert.True(reregister.IsEnabled)

[<AvaloniaFact>]
let ReregisterClick_RaisesCallback () =
    let mutable reregisterCount = 0
    let unauthorized =
        Cached(
            DateTimeOffset(2026, 5, 10, 9, 0, 0, TimeSpan.Zero),
            FromLocalFile,
            Some Unauthorized
        )

    let onReregister () = reregisterCount <- reregisterCount + 1

    let panel = render unauthorized fixedNow DictionaryStatusRow.Idle noop onReregister

    let reregister =
        findControl<Button> panel "ReregisterButton"
        |> Option.defaultWith (fun () ->
            Assert.Fail("expected Re-register button on Unauthorized")
            Unchecked.defaultof<_>)

    reregister.RaiseEvent(
        new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent))

    Assert.Equal(1, reregisterCount)

[<AvaloniaFact>]
let NoUnauthorized_NoReregisterButton () =
    // Cached with no failure → no Re-register. Same for Live.
    let cached =
        Cached(
            DateTimeOffset(2026, 5, 10, 9, 0, 0, TimeSpan.Zero),
            FromLocalFile,
            None
        )

    let panel = render cached fixedNow DictionaryStatusRow.Idle noop noop

    let reregister = findControl<Button> panel "ReregisterButton"
    Assert.True(reregister.IsNone)

// --- T056.4: stale seed → glyph rendered ---

[<AvaloniaFact>]
let StaleEmbeddedSeed_GlyphRendered () =
    // 91 days ago → should render the stale glyph.
    let oldSeededAt = fixedNow.AddDays(-91.0)
    let source = Cached(oldSeededAt, FromEmbeddedSeed, None)

    let panel = render source fixedNow DictionaryStatusRow.Idle noop noop

    let stale =
        panel.Children
        |> Seq.choose (fun c ->
            match box c with
            | :? Avalonia.Controls.TextBlock as t when t.Name = "StaleSeedGlyph" -> Some t
            | _ -> None)
        |> Seq.tryHead

    match stale with
    | Some glyph ->
        Assert.Equal("⚠", glyph.Text)
        match Avalonia.Controls.ToolTip.GetTip(glyph) with
        | null -> Assert.Fail("expected a tooltip on the stale glyph")
        | tt ->
            let text = tt.ToString()
            Assert.Contains("Last refreshed by STEM", text)
            Assert.Contains("Refresh when network is available", text)
    | None ->
        Assert.Fail("expected a StaleSeedGlyph TextBlock for 91-day-old seed")

[<AvaloniaFact>]
let FreshEmbeddedSeed_NoGlyph () =
    // 30 days ago — within the 90-day threshold, no glyph.
    let recentSeededAt = fixedNow.AddDays(-30.0)
    let source = Cached(recentSeededAt, FromEmbeddedSeed, None)

    let panel = render source fixedNow DictionaryStatusRow.Idle noop noop

    let stale =
        panel.Children
        |> Seq.choose (fun c ->
            match box c with
            | :? Avalonia.Controls.TextBlock as t when t.Name = "StaleSeedGlyph" -> Some t
            | _ -> None)
        |> Seq.tryHead

    Assert.True(stale.IsNone)

[<AvaloniaFact>]
let CachedLocalFile_NoStaleGlyph_EvenIfOld () =
    // FromLocalFile means a live fetch landed at some point; the
    // stale-glyph is exclusive to FromEmbeddedSeed.
    let oldFetchedAt = fixedNow.AddDays(-365.0)
    let source = Cached(oldFetchedAt, FromLocalFile, None)

    let panel = render source fixedNow DictionaryStatusRow.Idle noop noop

    let stale =
        panel.Children
        |> Seq.choose (fun c ->
            match box c with
            | :? Avalonia.Controls.TextBlock as t when t.Name = "StaleSeedGlyph" -> Some t
            | _ -> None)
        |> Seq.tryHead

    Assert.True(stale.IsNone)
