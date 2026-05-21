/// Per-app division identity per `docs/Standards/DESIGN_SYSTEM.md`
/// L103-133. ButtonPanelTester is a cross-division engineering tool —
/// `division = Division.None` selects the corporate brand mark and
/// the plain `Stem` badge label, with no division-tinted chrome.
module Stem.ButtonPanelTester.GUI.Branding

open Avalonia.Media
open Stem.ButtonPanelTester.GUI.Brand

type Division =
    | None                 // corporate / cross-division tool
    | EMS
    | CommercialVehicles
    | Marine
    | France               // branch, not a division — uses BluFrance + flag colors

let division : Division = Division.None

let badgeColor : Color =
    match division with
    | None               -> BluStem
    | EMS                -> VerdeEMS
    | CommercialVehicles -> GialloCommercialVehicles
    | Marine             -> AzzurroMarine
    | France             -> BluFrance

let badgeLabel : string =
    match division with
    | None               -> "Stem"
    | EMS                -> "Stem EMS"
    | CommercialVehicles -> "Stem Commercial Vehicles"
    | Marine             -> "Stem Marine"
    | France             -> "Stem France"
