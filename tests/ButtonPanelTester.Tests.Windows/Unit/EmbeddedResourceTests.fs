module Stem.ButtonPanelTester.Tests.Windows.Unit.EmbeddedResourceTests

open System
open Avalonia.Platform
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
