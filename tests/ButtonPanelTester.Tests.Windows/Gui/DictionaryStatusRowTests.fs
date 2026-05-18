module Stem.ButtonPanelTester.Tests.Windows.Gui.DictionaryStatusRowTests

open System
open Avalonia.Controls
open Avalonia.Controls.Shapes
open Avalonia.Media
open Avalonia.Headless.XUnit
open Avalonia.FuncUI.VirtualDom
open Xunit
open Stem.ButtonPanelTester.Core.Dictionary
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

let private renderToStackPanel (source: DictionarySource) : StackPanel =
    let materialized =
        VirtualDom.create (DictionaryStatusRow.view cacheFilePath source)

    materialized :?> StackPanel

let private ellipseChild (panel: StackPanel) : Ellipse =
    panel.Children
    |> Seq.choose (fun c ->
        match box c with
        | :? Ellipse as e -> Some e
        | _ -> None)
    |> Seq.exactlyOne

let private textBlockChild (panel: StackPanel) : TextBlock =
    panel.Children
    |> Seq.choose (fun c ->
        match box c with
        | :? TextBlock as t -> Some t
        | _ -> None)
    |> Seq.exactlyOne

// --- tests ---

[<AvaloniaFact>]
let View_CachedFromEmbeddedSeed_OrangePillAndCachedHeadline () =
    let seedFetchedAt = DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero)
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

    Assert.Same(Brushes.Green, pill.Fill)
    Assert.Equal("Live · synced 14:30", headline.Text)

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
