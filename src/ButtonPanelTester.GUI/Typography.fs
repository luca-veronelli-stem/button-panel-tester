/// Type scale + Poppins weight handles per
/// `docs/Standards/DESIGN_SYSTEM.md` L155-175. Poppins is the only
/// font any archetype A app uses; the TTFs ship under
/// `Resources/fonts/` and are wired as default at `AppBuilder`
/// time in `Program.fs`.
module Stem.ButtonPanelTester.GUI.Typography

open Avalonia.Media

let fontFamily   = "Poppins"        // exposed as a string for FuncUI

// Weights — every weight ships in Resources/fonts/
let regular      = FontWeight.Regular   // 400 — body text
let medium       = FontWeight.Medium    // 500 — UI labels
let semiBold     = FontWeight.SemiBold  // 600 — buttons, table headers
let bold         = FontWeight.Bold      // 700 — titles, section headers
let light        = FontWeight.Light     // 300 — tertiary text, captions

// Type scale (Avalonia FontSize values)
let body         = 14.0
let bodySmall    = 12.0
let label        = 13.0
let button       = 14.0
let h3           = 16.0    // sub-section
let h2           = 20.0    // section
let h1           = 28.0    // page title
let display      = 40.0    // empty-state hero text
