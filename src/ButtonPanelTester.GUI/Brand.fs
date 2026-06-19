/// Stem brand-identity palette per `docs/Standards/DESIGN_SYSTEM.md`
/// L36-66. Pure constants; views consume via `Brand.<Name>` and the
/// no-hex-literals-in-DSL rule keeps the palette as the single source
/// of truth.
module Stem.ButtonPanelTester.GUI.Brand

open Avalonia.Media
open Avalonia.Styling

// Primary (corporate identity, all divisions). Deviates from L36-66
// pending standards#92 — the standards palette pins `#004682`, but
// every positive brand-mark SVG fills with `#004483` (the agency's
// authoritative Blu Stem for the rendered mark). Aligning the token
// to the SVG fill prevents a visible colour seam between chrome and
// brand mark in the same view. Realign upstream when standards#92
// lands.
let BluStem        = Color.Parse "#004483"   // Pantone 2154 C

// Blu Stem sanctioned tints
let BluStem30      = Color.Parse "#B1C9F8"   // Pantone 658 C — icon tint paired with Blu Stem
let BluStem40      = Color.Parse "#7BA5E9"   // Pantone 659 C — secondary text
let BluStem60      = Color.Parse "#407ED4"   // Pantone 660 C — secondary text

// Cool Gray ramp — sanctioned range is 10% to 60% only.
// Pure black (#000) and pure white (#FFF) for body text are off-brand.
let Gray10         = Color.Parse "#D9DAE4"   // Pantone Cool Gray 1C
let Gray20         = Color.Parse "#C9CAD4"   // Pantone Cool Gray 3C
let Gray30         = Color.Parse "#B2B4BE"   // Pantone Cool Gray 5C
let Gray40         = Color.Parse "#989AA5"   // Pantone Cool Gray 7C
let Gray60         = Color.Parse "#757982"   // Pantone Cool Gray 9C — darkest sanctioned neutral

// Alert — the brand's only sanctioned warm color in the primary palette
let RossoAlert     = Color.Parse "#E40032"   // Pantone 185 C — alerts, warnings, key features

// Division identity colors (used by Branding.badge, not as primary chrome)
let VerdeEMS                  = Color.Parse "#98D801"   // Pantone 375 C
let GialloCommercialVehicles  = Color.Parse "#FFC04A"   // Pantone 136 C
let AzzurroMarine             = Color.Parse "#00B6ED"   // Pantone 306 C

// Stem France branch (used only by France-targeted apps)
let BluFrance      = Color.Parse "#0031A7"   // Pantone 286 C
let Bianco         = Color.Parse "#F5F5F5"

// Theme-aware selected-state highlight (#235, bench finding F2). Two cached
// palette brushes so every render reuses one instance instead of allocating.
// FluentTheme's adaptive Button foreground is dark on light and white on dark,
// so the selection tint flips with the theme to keep selected text legible:
//   * Light theme → the light `BluStem30` tint reads as "selected" against the
//     near-white unselected button while keeping the dark FluentTheme text AA.
//   * Dark theme  → deep `BluStem` gives strong contrast under the white/light
//     FluentTheme text, where the old `LightBlue` washed out (the F2 bug).
let private selectionLight = SolidColorBrush BluStem30 :> IBrush
let private selectionDark = SolidColorBrush BluStem :> IBrush

/// Theme-aware selected-state background brush, drawn from the BluStem palette
/// (DESIGN_SYSTEM colour-usage). The single source for the panel-row and
/// baptism-variant selection highlight — meets WCAG AA against FluentTheme's
/// adaptive foreground in both light and dark themes (#235, F2).
let selectionBackground (theme: ThemeVariant) : IBrush =
    if theme = ThemeVariant.Dark then selectionDark else selectionLight
