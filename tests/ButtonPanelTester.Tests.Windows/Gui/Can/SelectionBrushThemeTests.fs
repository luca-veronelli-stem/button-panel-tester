module Stem.ButtonPanelTester.Tests.Windows.Gui.Can.SelectionBrushThemeTests

open System
open Avalonia.Controls
open Avalonia.Media
open Avalonia.Styling
open Avalonia.Headless.XUnit
open Avalonia.FuncUI.VirtualDom
open Xunit
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.GUI
open Stem.ButtonPanelTester.GUI.Can

/// Regression guard for #235 (bench finding F2): the selected panel row and the
/// selected baptism variant must paint with the theme-aware `Brand.selectionBackground`
/// brush drawn from the BluStem palette — deep `BluStem` (#004483) in dark theme, the
/// light `BluStem30` (#B1C9F8) tint in light theme — never the hardcoded `Brushes.LightBlue`
/// that washed out under dark-theme white text. Pins both surfaces, both themes; the theme
/// is a `view` parameter so the assertion needs no running app / `ActualThemeVariant`.

let private fixedNow = DateTimeOffset(2026, 6, 9, 14, 30, 0, TimeSpan.Zero)

let private fixedAdapter: AdapterIdentification =
    { ChannelName = "PCAN-USB (1)"
      DeviceId = "0x01"
      BaudrateBps = 250_000 }

let private selectedUuid = PanelUuid(0x177C126Du, 0x7308748Fu, 0x16092104u)

let private oneEntry (machineType: byte) (u0, u1, u2) (now: DateTimeOffset) : PanelsOnBus =
    let frame: WhoIAmFrame =
        { MachineType = MachineTypeByte machineType
          FwType = FwType 0x0004us
          Uuid = PanelUuid(u0, u1, u2) }
    PanelsOnBus.observe now frame PanelsOnBus.empty

let rec private allButtons (c: Control) : Button list =
    match box c with
    | :? Button as b -> [ b ]
    | :? Panel as p -> [ for child in p.Children do yield! allButtons child ]
    | :? ContentControl as cc ->
        match cc.Content with
        | :? Control as inner -> allButtons inner
        | _ -> []
    | _ -> []

let private buttonsNamed (name: string) (root: Control) : Button list =
    allButtons root |> List.filter (fun b -> b.Name = name)

// The single highlighted button of a given name: the one carrying a background.
let private highlighted (name: string) (root: Control) : Button =
    buttonsNamed name root
    |> List.filter (fun b -> not (isNull b.Background))
    |> List.exactlyOne

// Null-safe read of a button's selection colour (the type-test pattern rules
// out the `IBrush | null` background without a downcast nullness warning).
let private colorOf (b: Button) : Color =
    match b.Background with
    | :? SolidColorBrush as scb -> scb.Color
    | other -> failwithf "expected a SolidColorBrush selection background, got %A" other

let private expectedColor (theme: ThemeVariant) : Color =
    (Brand.selectionBackground theme :?> SolidColorBrush).Color

// --- PanelsOnBusView: the selected row paints the theme selection brush ---

let private renderRow (theme: ThemeVariant) : Control =
    let panels = oneEntry 0xFFuy (0x177C126Du, 0x7308748Fu, 0x16092104u) fixedNow
    VirtualDom.create (
        PanelsOnBusView.view panels (Connected(fixedAdapter, fixedNow)) (Some selectedUuid) (fun _ -> ()) theme)

[<AvaloniaTheory>]
[<InlineData(true)>]   // dark  -> BluStem  (#004483)
[<InlineData(false)>]  // light -> BluStem30 (#B1C9F8)
let SelectedRow_PaintsThemeSelectionBrush (dark: bool) =
    let theme = if dark then ThemeVariant.Dark else ThemeVariant.Light
    let button = highlighted "PanelRow" (renderRow theme)

    Assert.Equal(expectedColor theme, colorOf button)
    Assert.Equal((if dark then Brand.BluStem else Brand.BluStem30), colorOf button)
    Assert.False(obj.ReferenceEquals(button.Background, Brushes.LightBlue))

// --- BaptismView: the selected variant option paints the theme selection brush ---

let private renderVariant (theme: ThemeVariant) : Control =
    VirtualDom.create (
        BaptismView.view
            Enabled
            Enabled
            Idle
            (Some EdenXp)
            None
            None
            None
            (fun _ -> ())
            (fun _ -> ())
            (fun () -> ())
            theme)

[<AvaloniaTheory>]
[<InlineData(true)>]
[<InlineData(false)>]
let SelectedVariant_PaintsThemeSelectionBrush (dark: bool) =
    let theme = if dark then ThemeVariant.Dark else ThemeVariant.Light
    let button = highlighted "VariantOption" (renderVariant theme)

    Assert.Equal(expectedColor theme, colorOf button)
    Assert.Equal((if dark then Brand.BluStem else Brand.BluStem30), colorOf button)
    Assert.False(obj.ReferenceEquals(button.Background, Brushes.LightBlue))

// --- the brush is genuinely theme-aware: light and dark differ ---
// AvaloniaFact (not Fact): reading `SolidColorBrush.Color` calls VerifyAccess,
// so it must run on the headless UI thread — exactly where production renders.

[<AvaloniaFact>]
let SelectionBrush_LightAndDarkDiffer () =
    Assert.NotEqual(expectedColor ThemeVariant.Light, expectedColor ThemeVariant.Dark)
    Assert.Equal(Brand.BluStem30, expectedColor ThemeVariant.Light)
    Assert.Equal(Brand.BluStem, expectedColor ThemeVariant.Dark)
