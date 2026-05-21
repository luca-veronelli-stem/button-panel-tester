module Stem.ButtonPanelTester.Tests.Windows.Unit.EmbeddedResourceTests

open System
open Avalonia.Platform
open Avalonia.Svg.Skia
open Avalonia.Headless.XUnit
open Xunit
open Stem.ButtonPanelTester.GUI.Dictionary

/// Smoke tests for the `<AvaloniaResource>` globs in
/// `ButtonPanelTester.GUI.fsproj`. A typo or stale glob would cause
/// the app to load but fail at first asset resolution (font fallback,
/// missing brand mark). Asserting reachability via `avares://` in CI
/// fails fast instead.
///
/// `open Stem.ButtonPanelTester.GUI.Dictionary` forces the GUI
/// assembly into the AppDomain so the `avares://ButtonPanelTester.GUI/`
/// scheme resolves.

let private guiAvares (relativePath: string) : Uri =
    Uri($"avares://ButtonPanelTester.GUI/{relativePath}")

let private assertReachable (uri: Uri) : unit =
    use stream = AssetLoader.Open(uri)
    Assert.NotNull(stream)
    Assert.True(stream.Length > 0L, $"Expected non-empty stream for {uri}")

[<AvaloniaFact>]
let ``Resources_PoppinsRegularTtf_IsEmbeddedAndReachable`` () =
    assertReachable (guiAvares "Resources/fonts/Poppins-Regular.ttf")

[<AvaloniaFact>]
let ``Resources_BrandMarkSvg_IsEmbeddedAndReachable`` () =
    assertReachable (guiAvares "Resources/branding/brand-marks/positive/stem-ems.svg")

[<AvaloniaFact>]
let ``Resources_BrandMarkPng_IsEmbeddedAndReachable`` () =
    assertReachable (guiAvares "Resources/branding/brand-marks/positive/stem-ems.png")

[<AvaloniaFact>]
let ``Resources_AppIconIco_IsEmbeddedAndReachable`` () =
    assertReachable (guiAvares "Resources/branding/app-icons/stem-app-icon-positive.ico")

/// Catches a package-wiring regression distinct from raw resource
/// embedding: `Svg.Controls.Skia.Avalonia` could be removed from
/// `Directory.Packages.props`, or the runtime SVG parser could fail
/// on a malformed asset, without breaking the byte-reachability
/// asserted above. This fact pins the contract that the corporate
/// brand-mark SVG round-trips through `SvgSource.Load` to a
/// renderable `SvgImage` with non-zero intrinsic dimensions — the
/// minimum needed for any `Image.Source` consumer (e.g. the
/// brand-mark header rendered by `MainWindow.Chrome`).
[<AvaloniaFact>]
let ``Resources_BrandMarkSvg_RendersAsSvgImage`` () =
    let uri = guiAvares "Resources/branding/brand-marks/positive/stem-corporate.svg"
    let source : SvgSource =
        match SvgSource.Load(uri.OriginalString, null) with
        | null -> failwithf "SvgSource.Load returned null for %O" uri
        | s -> s
    Assert.NotNull(source.Picture)
    let svgImage = SvgImage(Source = source)
    Assert.NotNull(svgImage.Source)
    Assert.True(
        svgImage.Size.Width > 0.0,
        $"SvgImage.Size.Width expected > 0, got {svgImage.Size.Width}")
    Assert.True(
        svgImage.Size.Height > 0.0,
        $"SvgImage.Size.Height expected > 0, got {svgImage.Size.Height}")
