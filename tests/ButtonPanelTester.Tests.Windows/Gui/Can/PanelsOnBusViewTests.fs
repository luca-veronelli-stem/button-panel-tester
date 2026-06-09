module Stem.ButtonPanelTester.Tests.Windows.Gui.Can.PanelsOnBusViewTests

open System
open Avalonia.Controls
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

let private render (panels: PanelsOnBus) (linkState: CanLinkState) : Control =
    VirtualDom.create (PanelsOnBusView.view panels linkState)

// The list nests rows, so collect TextBlocks recursively (not just direct children).
let rec private allTextBlocks (c: Control) : TextBlock list =
    match box c with
    | :? TextBlock as t -> [ t ]
    | :? Panel as p -> [ for child in p.Children do yield! allTextBlocks child ]
    | _ -> []

let private byName (name: string) (root: Control) : TextBlock list =
    allTextBlocks root |> List.filter (fun t -> t.Name = name)

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
