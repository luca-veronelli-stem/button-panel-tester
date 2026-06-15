namespace Stem.ButtonPanelTester.GUI.Can

open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
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

    /// Drop the current selection when its panel is no longer on the bus.
    /// The host calls this on every `PanelsOnBusChanged` before re-rendering:
    /// when the selected row prunes out of the map (the "selected row prunes
    /// during interaction" edge case) the selection clears, so the baptism
    /// surface deactivates (`baptizeEnablement` sees `None`) rather than ever
    /// firing a stale send against a panel that has gone silent (FR-002).
    let pruneSelection (panels: PanelsOnBus) (selected: PanelUuid option) : PanelUuid option =
        selected |> Option.filter (fun u -> Map.containsKey u panels)

    /// Render the Panels-on-bus list: one selectable `PanelRow` button per
    /// panel in `PanelUuid` key order (its content is the unchanged `rowView`),
    /// or the FR-006 empty-state explainer (`emptyStateText linkState`) when
    /// the map is empty. The selected row carries a `LightBlue` highlight; a
    /// click invokes `onSelect` with that row's `PanelUuid`. Pure render — the
    /// GUI decides nothing; the host re-invokes it on every
    /// `PanelsOnBusChanged` / `LinkStateChanged` and feeds the selection into
    /// `Baptism.baptizeEnablement` (FR-002).
    let view
        (panels: PanelsOnBus)
        (linkState: CanLinkState)
        (selected: PanelUuid option)
        (onSelect: PanelUuid -> unit)
        : IView =
        if Map.isEmpty panels then
            TextBlock.create [
                TextBlock.name "EmptyState"
                TextBlock.text (emptyStateText linkState)
            ]
            :> IView
        else
            let rowButton (o: PanelObservation) : IView =
                // FuncUI: the selected highlight is added by list-concat (the
                // `variantAttrs` idiom), NOT a `match ... -> ()` inside the attr
                // list (that would be a type error). When unselected the
                // background is left unset.
                let attrs: IAttr<Button> list =
                    [ Button.name "PanelRow"
                      Button.horizontalAlignment HorizontalAlignment.Stretch
                      Button.content (rowView o)
                      Button.onClick (fun _ -> onSelect o.Uuid) ]
                    @ (if selected = Some o.Uuid then
                           [ Button.background Brushes.LightBlue ]
                       else
                           [])

                Button.create attrs :> IView

            StackPanel.create [
                StackPanel.name "PanelsOnBusList"
                StackPanel.orientation Orientation.Vertical
                StackPanel.spacing 4.0
                // Map iteration is by PanelUuid key order -> deterministic row order.
                StackPanel.children [ for kvp in panels -> rowButton kvp.Value ]
            ]
            :> IView
