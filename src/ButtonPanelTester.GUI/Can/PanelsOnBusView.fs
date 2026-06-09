namespace Stem.ButtonPanelTester.GUI.Can

open Avalonia.Controls
open Avalonia.Layout
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Stem.ButtonPanelTester.Core.Can

/// FuncUI view for the Panels-on-bus list (spec-003 T020, FR-003/004/006). Pure render:
/// the host (App.fs) passes the latest PanelsOnBus snapshot + the latest CanLinkState
/// (for the empty-state explainer) and re-renders on PanelsOnBusChanged / LinkStateChanged.
[<RequireQualifiedAccess>]
module PanelsOnBusView =

    /// Marketing name / "Virgin" / "Unknown" (FR-003). The raw byte rides the row's
    /// detail tooltip for Virgin/Unknown (see rowView).
    let variantLabel (identity: VariantIdentity) : string =
        match identity with
        | Marketing EdenXp -> "Eden XP"
        | Marketing OptimusXp -> "Optimus XP"
        | Marketing R3LXp -> "R-3L XP"
        | Marketing EdenBs8 -> "Eden BS8"
        | Virgin -> "Virgin"
        | Unknown _ -> "Unknown"

    /// UUID hex triple, e.g. "177C126D-7308748F-16092104".
    let uuidText (PanelUuid(u0, u1, u2)) : string = sprintf "%08X-%08X-%08X" u0 u1 u2

    let private rawByteText (MachineTypeByte raw) : string = sprintf "0x%02X" raw

    /// HH:MM local, mirroring CanStatusRow's timestamp format (FR-004).
    let private lastSeenText (ts: System.DateTimeOffset) : string =
        let local = ts.LocalDateTime
        sprintf "%02d:%02d" local.Hour local.Minute

    /// FR-006: distinguish "link up, nothing announcing" from "link is down".
    let emptyStateText (linkState: CanLinkState) : string =
        match linkState with
        | Connected _ -> "CAN link up - no panels announcing yet"
        | _ -> "CAN link down - connect a panel to discover it"

    /// Raw-byte detail affordance for Virgin/Unknown only (FR-003); Marketing needs none.
    let private variantTooltip (o: PanelObservation) : string option =
        match o.VariantIdentity with
        | Virgin
        | Unknown _ -> Some(sprintf "machineType %s" (rawByteText o.VariantByte))
        | Marketing _ -> None

    let private rowView (o: PanelObservation) : IView =
        // FuncUI: a conditional attribute is added by list-concat, NOT a `match ... -> ()`
        // inside the attr list (that would be a type error).
        let variantAttrs: IAttr<TextBlock> list =
            [ TextBlock.name "PanelVariant"
              TextBlock.text (variantLabel o.VariantIdentity)
              TextBlock.verticalAlignment VerticalAlignment.Center ]
            @ (match variantTooltip o with
               | Some tip -> [ ToolTip.tip tip ]
               | None -> [])

        StackPanel.create [
            StackPanel.orientation Orientation.Horizontal
            StackPanel.spacing 8.0
            StackPanel.children [
                TextBlock.create [
                    TextBlock.name "PanelUuid"
                    TextBlock.text (uuidText o.Uuid)
                    TextBlock.verticalAlignment VerticalAlignment.Center
                ]
                TextBlock.create variantAttrs
                TextBlock.create [
                    TextBlock.name "PanelLastSeen"
                    TextBlock.text (lastSeenText o.LastSeen)
                    TextBlock.verticalAlignment VerticalAlignment.Center
                ]
            ]
        ]
        :> IView

    let view (panels: PanelsOnBus) (linkState: CanLinkState) : IView =
        if Map.isEmpty panels then
            TextBlock.create [
                TextBlock.name "EmptyState"
                TextBlock.text (emptyStateText linkState)
            ]
            :> IView
        else
            StackPanel.create [
                StackPanel.name "PanelsOnBusList"
                StackPanel.orientation Orientation.Vertical
                StackPanel.spacing 4.0
                // Map iteration is by PanelUuid key order -> deterministic row order.
                StackPanel.children [ for kvp in panels -> rowView kvp.Value ]
            ]
            :> IView
