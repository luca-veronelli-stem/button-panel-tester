module Stem.ButtonPanelTester.Tests.Windows.Gui.Can.PanelsOnBusViewTests

open System
open Avalonia.Controls
open Avalonia.Interactivity
open Avalonia.Media
open Avalonia.Headless.XUnit
open Avalonia.FuncUI.VirtualDom
open Xunit
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.GUI.Can

let private fixedNow = DateTimeOffset(2026, 6, 9, 14, 30, 0, TimeSpan.Zero)

let private fixedAdapter: AdapterIdentification =
    { ChannelName = "PCAN-USB (1)"
      DeviceId = "0x01"
      BaudrateBps = 250_000 }

// Default render: no selection, onSelect is a no-op so the six pre-existing
// content tests stay byte-for-byte on the new 4-arg `view` signature.
let private render (panels: PanelsOnBus) (linkState: CanLinkState) : Control =
    VirtualDom.create (PanelsOnBusView.view panels linkState None (fun _ -> ()))

// Render variant that threads a selection + onSelect callback, mirroring
// `renderStateWith` in CanStatusRowTests.
let private renderWith
    (panels: PanelsOnBus)
    (linkState: CanLinkState)
    (selected: PanelUuid option)
    (onSelect: PanelUuid -> unit)
    : Control =
    VirtualDom.create (PanelsOnBusView.view panels linkState selected onSelect)

// The list nests rows (each wrapped in a `PanelRow` Button whose content is the
// row StackPanel), so collect TextBlocks recursively — descending through both
// `Panel.Children` and `ContentControl.Content`.
let rec private allTextBlocks (c: Control) : TextBlock list =
    match box c with
    | :? TextBlock as t -> [ t ]
    | :? Panel as p -> [ for child in p.Children do yield! allTextBlocks child ]
    | :? ContentControl as cc ->
        match cc.Content with
        | :? Control as inner -> allTextBlocks inner
        | _ -> []
    | _ -> []

let private byName (name: string) (root: Control) : TextBlock list =
    allTextBlocks root |> List.filter (fun t -> t.Name = name)

// Each row is wrapped in a `PanelRow` Button; collect them recursively the
// same way `allTextBlocks` walks the tree.
let rec private allButtons (c: Control) : Button list =
    match box c with
    | :? Button as b -> [ b ]
    | :? Panel as p -> [ for child in p.Children do yield! allButtons child ]
    | :? ContentControl as cc ->
        match cc.Content with
        | :? Control as inner -> allButtons inner
        | _ -> []
    | _ -> []

let private panelRows (root: Control) : Button list =
    allButtons root |> List.filter (fun b -> b.Name = "PanelRow")

let private frame (machineType: byte) (u0, u1, u2) : WhoIAmFrame =
    { MachineType = MachineTypeByte machineType
      FwType = FwType 0x0004us
      Uuid = PanelUuid(u0, u1, u2) }

let private oneEntry (machineType: byte) (uuid) (now: DateTimeOffset) : PanelsOnBus =
    PanelsOnBus.observe now (frame machineType uuid) PanelsOnBus.empty

// (1) empty + Connected -> "link up, nothing announcing" (FR-006)
[<AvaloniaFact>]
let Empty_Connected_RendersLinkUpIdleText () =
    let root = render PanelsOnBus.empty (Connected(fixedAdapter, fixedNow))
    let empty = byName "EmptyState" root |> List.exactlyOne
    Assert.Contains("no panels announcing", empty.Text)

// (2) empty + not-Connected -> "link is down" (FR-006)
[<AvaloniaFact>]
let Empty_NotConnected_RendersLinkDownText () =
    let root = render PanelsOnBus.empty (Disconnected(NoAdapterPresent, fixedNow))
    let empty = byName "EmptyState" root |> List.exactlyOne
    Assert.Contains("link down", empty.Text)

// (3) one virgin panel -> UUID hex + "Virgin" + last-seen (FR-003/004)
[<AvaloniaFact>]
let OneVirginPanel_RendersUuidVariantAndLastSeen () =
    let panels = oneEntry 0xFFuy (0x177C126Du, 0x7308748Fu, 0x16092104u) fixedNow
    let root = render panels (Connected(fixedAdapter, fixedNow))
    Assert.Equal("177C126D-7308748F-16092104", (byName "PanelUuid" root |> List.exactlyOne).Text)
    Assert.Equal("Virgin", (byName "PanelVariant" root |> List.exactlyOne).Text)
    let local = fixedNow.LocalDateTime
    Assert.Equal(sprintf "%02d:%02d" local.Hour local.Minute, (byName "PanelLastSeen" root |> List.exactlyOne).Text)

// (4) virgin/unknown row exposes the raw machineType byte via the detail tooltip (FR-003)
[<AvaloniaFact>]
let OneVirginPanel_VariantTooltipShowsRawByte () =
    let panels = oneEntry 0xFFuy (0x1u, 0x2u, 0x3u) fixedNow
    let root = render panels (Connected(fixedAdapter, fixedNow))
    let variant = byName "PanelVariant" root |> List.exactlyOne
    match ToolTip.GetTip(variant) with
    | null -> Assert.Fail("expected a raw-byte tooltip on the Virgin variant")
    | tip -> Assert.Contains("0xFF", tip.ToString())

// (5) a re-observation of the same UUID renders a SINGLE row with the updated last-seen
[<AvaloniaFact>]
let ReObservation_SameUuid_RendersSingleRowWithUpdatedLastSeen () =
    let uuid = (0x1u, 0x2u, 0x3u)
    let later = fixedNow.AddSeconds 3.0
    let panels =
        PanelsOnBus.empty
        |> PanelsOnBus.observe fixedNow (frame 0xFFuy uuid)
        |> PanelsOnBus.observe later (frame 0xFFuy uuid)
    let root = render panels (Connected(fixedAdapter, fixedNow))
    Assert.Equal(1, (byName "PanelUuid" root).Length)
    let local = later.LocalDateTime
    Assert.Equal(sprintf "%02d:%02d" local.Hour local.Minute, (byName "PanelLastSeen" root |> List.exactlyOne).Text)

// (6) unknown machineType -> "Unknown" label + raw byte in the tooltip (FR-003)
[<AvaloniaFact>]
let UnknownVariant_RendersUnknownLabelWithRawByteTooltip () =
    let panels = oneEntry 0x08uy (0x1u, 0x2u, 0x3u) fixedNow // 0x08 = TopLift-A -> Unknown
    let root = render panels (Connected(fixedAdapter, fixedNow))
    let variant = byName "PanelVariant" root |> List.exactlyOne
    Assert.Equal("Unknown", variant.Text)
    match ToolTip.GetTip(variant) with
    | null -> Assert.Fail("expected a raw-byte tooltip on the Unknown variant")
    | tip -> Assert.Contains("0x08", tip.ToString())

// --- spec-004 E1 (T032/T033): row selection on the Panels-on-bus list ---
//
// The GUI renders enablement and selection; it decides nothing. The selected
// row feeds `Baptism.baptizeEnablement` (FR-002); when the selected row prunes
// from the snapshot the selection clears so the baptism surface deactivates
// (never a stale send).

let private selectedUuid = PanelUuid(0x177C126Du, 0x7308748Fu, 0x16092104u)

// (7) selected row carries the LightBlue highlight; its content shows the UUID
[<AvaloniaFact>]
let SelectedRow_RendersSelectedHighlight () =
    let panels = oneEntry 0xFFuy (0x177C126Du, 0x7308748Fu, 0x16092104u) fixedNow
    let root = renderWith panels (Connected(fixedAdapter, fixedNow)) (Some selectedUuid) (fun _ -> ())

    let highlighted =
        panelRows root
        |> List.filter (fun b -> obj.ReferenceEquals(b.Background, Brushes.LightBlue))

    let button = highlighted |> List.exactlyOne
    Assert.Same(Brushes.LightBlue, button.Background)

    let uuidText =
        allTextBlocks button
        |> List.filter (fun t -> t.Name = "PanelUuid")
        |> List.exactlyOne
    Assert.Equal("177C126D-7308748F-16092104", uuidText.Text)

// (8) with no selection, no row carries the highlight
[<AvaloniaFact>]
let UnselectedRows_HaveNoHighlight () =
    let panels = oneEntry 0xFFuy (0x1u, 0x2u, 0x3u) fixedNow
    let root = renderWith panels (Connected(fixedAdapter, fixedNow)) None (fun _ -> ())

    let highlighted =
        panelRows root
        |> List.filter (fun b -> obj.ReferenceEquals(b.Background, Brushes.LightBlue))

    Assert.Empty(highlighted)

// (9) clicking a row fires onSelect with that row's UUID
[<AvaloniaFact>]
let ClickingARow_FiresOnSelectWithItsUuid () =
    let uuid = (0x1u, 0x2u, 0x3u)
    let panels = oneEntry 0xFFuy uuid fixedNow
    let mutable picked: PanelUuid option = None
    let onSelect u = picked <- Some u

    let root = renderWith panels (Connected(fixedAdapter, fixedNow)) None onSelect

    let row = panelRows root |> List.exactlyOne
    row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))

    let (u0, u1, u2) = uuid
    Assert.Equal(Some(PanelUuid(u0, u1, u2)), picked)

// (10) pruneSelection keeps a still-present selection
[<Fact>]
let PruneSelection_SelectedRowPresent_KeepsSelection () =
    let panels = oneEntry 0xFFuy (0x177C126Du, 0x7308748Fu, 0x16092104u) fixedNow
    Assert.Equal(Some selectedUuid, PanelsOnBusView.pruneSelection panels (Some selectedUuid))

// (11) pruneSelection clears a selection whose row has left the snapshot
[<Fact>]
let PruneSelection_SelectedRowPruned_ClearsSelection () =
    Assert.Equal<PanelUuid option>(
        None,
        PanelsOnBusView.pruneSelection PanelsOnBus.empty (Some selectedUuid)
    )

// (12) once the selected row prunes (selection None, announcing count 0),
// baptizeEnablement deactivates with a non-empty explanation (FR-002).
[<Fact>]
let ClearedSelection_DeactivatesBaptizeWithExplanation () =
    match Baptism.baptizeEnablement (Connected(fixedAdapter, fixedNow)) 0 None with
    | Disabled explanation -> Assert.False(String.IsNullOrEmpty explanation)
    | Enabled -> Assert.Fail("expected Disabled when the selected row pruned (count 0, no selection)")
