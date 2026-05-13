# Standard: DESIGN_SYSTEM

> **Stability:** v1.5.0
> **Principle:** every archetype A app shares the **same visual contract**: the same theme, the same spacing rhythm, the same icon source, the same localisation mechanism, the same error-and-progress surfaces. Internal STEM tooling reads as one product family even when each app is a separate repo. Brand assets (corporate palette / typography / logo) follow once received from STEM; until then the design system is anchored to neutral Fluent defaults.
> **Applies to:** archetype A.
> **Pairs with:** [`GUI.md`](./GUI.md). `GUI` governs project shape and the MVU paradigm; this standard governs everything the user sees inside that shape.

## Reference

`Avalonia.Themes.Fluent` (theme), `FluentIcons.Avalonia.Fluent` (iconography). Both pinned via the templates' `Directory.Packages.props`. F# strings module for localisation — no external runtime, no `.resx`, no JSON dictionaries.

## Theme

- **Base:** `FluentTheme` from `Avalonia.Themes.Fluent`. No `SimpleTheme`, no third-party theme packs.
- **Mode:** **light mode is the default at first launch.** The Stem brand manual is entirely light-canvas — there is no sanctioned dark palette. Light mode adheres to the brand; dark mode is a **software-only convention** offered for long engineer sessions and low-light production bays, using derived shades that fall outside the brand's sanctioned Cool Gray range (10–60%). The user can toggle; the choice is persisted to settings per [`CONFIGURATION.md`](./CONFIGURATION.md).
- The active mode lives on the top-level `Model` (`type ThemeMode = Light | Dark`) and is applied at `App.fs` startup via `RequestedThemeVariant`.

```fsharp
// in App.fs
override this.Initialize () =
    base.Initialize ()
    this.Styles.Add (FluentTheme ())
    this.RequestedThemeVariant <- ThemeVariant.Light
```

## Palette

The Stem brand manual (`Manuale del brand`) is the print-side source of truth. This section is the software-side mirror: every brand-sanctioned color exposed as a named token, plus a small set of software-derived tokens for UI semantics the brand manual does not cover.

### Brand tokens — per-app `Brand` module

Each archetype A app declares a `Brand` module that exposes the brand-sanctioned colors as `Avalonia.Media.Color` values. **No view ever uses a hex literal** — references go through this module.

```fsharp
module Stem.<App>.GUI.Brand

open Avalonia.Media

// Primary (corporate identity, all divisions)
let BluStem        = Color.Parse "#004682"   // Pantone 2154 C

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
```

### Software-derived semantic tokens

The brand manual does not define a software UI semantic palette. The four tokens below are **software conventions**, distinct from brand-identity colors so they don't collide:

```fsharp
module Stem.<App>.GUI.Brand.Semantic

open Avalonia.Media
open Stem.<App>.GUI.Brand

let Info    = BluStem                          // brand-aligned
let Success = Color.Parse "#16A34A"            // forest green — distinct from VerdeEMS
let Warning = Color.Parse "#D97706"            // orange-amber — distinct from GialloCommercialVehicles
let Error   = RossoAlert                       // brand-aligned (Pantone 185 C)
```

- **Error** reuses `RossoAlert` because the brand manual itself sanctions red for "alerts, warnings, key features" — the alignment is meaningful.
- **Success** picks a forest green visibly distinct from `VerdeEMS` (`#98D801`) so a "saved successfully" toast in an EMS app cannot be mistaken for a division-identity marker. The same reasoning constrains **Warning**.
- **Info** reuses `BluStem` — the primary identity color is the natural carrier for neutral-informational accents.

These four tokens are the ones consumed by the error-and-progress surfaces below. They never appear in chrome; the chrome uses `BluStem` plus the Cool Gray ramp.

### Color usage rules (from the brand manual)

These rules are load-bearing — they constrain what views can compose.

1. **Primary blue can be solid or gradient.** The blu↔white gradient is sanctioned. Software hover / selected / focus states can lean on the `BluStem30/40/60` tints without violating the brand.
2. **Division colors never pair with each other.** Verde EMS + Giallo CV in the same view reads as cross-division marketing and is brand-illegal. Inside any one app, only **one** division's identity color appears — see `Branding` below.
3. **Cool Gray range is 10% to 60% only.** No `#000000`. No `#FFFFFF` for body text (use `Bianco` `#F5F5F5` for canvas). Body text on a light canvas defaults to `Gray60` (`#757982`); on a Blu Stem surface, body text defaults to `Bianco`.
4. **Red is the alert / key-emphasis color.** Sanctioned for warnings, alerts, stickers, key-concept call-outs. Software co-opts this for `Semantic.Error`.

## Division badging — per-app `Branding` module

Each archetype A app declares which Stem division it primarily serves. The division identity color appears **only** in the badge surface — never in chrome (title bar, primary buttons, navigation accent, hover states).

```fsharp
module Stem.<App>.GUI.Branding

open Avalonia.Media
open Stem.<App>.GUI.Brand

type Division =
    | None                 // corporate / cross-division tool
    | EMS
    | CommercialVehicles
    | Marine
    | France               // branch, not a division — uses BluFrance + flag colors

let division : Division = EMS   // edit per app

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
```

**Where the badge appears:**

- A small division mark next to the corporate brand mark in the title bar (e.g. `[Stem] [EMS]`).
- A divider color (`Branding.badgeColor`) on division-tagged rows in lists when the app spans multiple divisions (`stem-dictionaries-manager` is the canonical case).
- The About dialog (corporate brand mark + division badge + version).

**Where the badge does *not* appear:**

- Title bar background (stays `Bianco` light / derived dark in dark mode).
- Primary button fill (`BluStem`).
- Hover / focus / selected states (`BluStem` tints).
- Page-content backgrounds.

This keeps the app reading as a **Stem corporate tool** that happens to support a specific division, rather than a division-only product — matching how the brand manual structures the hierarchy.

## Typography

The brand body font is **Poppins** (Google Fonts, SIL OFL — free to bundle and ship). It is the only font archetype A apps use.

```fsharp
module Stem.<App>.GUI.Typography

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
```

**Bundling:** ship Poppins in `Resources/fonts/Poppins-*.ttf` (Regular, Medium, SemiBold, Bold, Light), referenced via `App.axaml`:

```xml
<Application.Resources>
    <FontFamily x:Key="StemFontFamily">avares://Stem.&lt;App&gt;.GUI/Resources/fonts/#Poppins</FontFamily>
</Application.Resources>
```

The Stem-Regular custom font from the brand manual is **not** used in software — it is reserved for division wordmarks inside the print/marketing brand mark and ships as part of the corporate logo SVG (rendered, not loaded as a font).

## Logo

The corporate brand mark and division marks ship as SVGs under `Resources/branding/`:

```
Resources/branding/
├── stem-mark.svg                  corporate brand mark (simbolo + "Stem")
├── stem-mark-negative.svg         white-on-color variant
├── stem-ems-mark.svg              EMS division mark (corporate + "EMS" in VerdeEMS)
├── stem-commercial-vehicles-mark.svg
├── stem-marine-mark.svg
└── stem-france-mark.svg
```

Per-app, `Branding.division` selects which division mark to render alongside the corporate mark. Brand-mark application rules (when to use positive vs negative vs monochrome white, contrast requirements against photo backgrounds) follow `tavola 21–50` of the brand manual — those rules apply to the splash, About dialog, and any print export the app produces. They do not constrain in-app chrome, which uses palette tokens rather than rendered brand marks.

**Asset source.** The SVGs are not in this standard's repo. They live in Stem's brand-asset library and are dropped into each adopted repo's `Resources/branding/` at app-init time. The rollout script does not copy them — they are per-repo assets, not templates.

## Spacing

A 4-pt grid spans the whole app. Use named constants from a per-app `Spacing` module — no magic numbers in the view DSL.

```fsharp
module Stem.<App>.GUI.Spacing

let xs   = 4.0      // hairline padding, tight stacks
let sm   = 8.0      // standard control padding
let md   = 12.0     // grouped-control spacing
let lg   = 16.0     // section padding
let xl   = 24.0     // page margins
let xxl  = 32.0     // page-section breaks
let xxxl = 56.0     // hero / dialog padding
let huge = 80.0     // full-bleed splash
```

Stack with `StackPanel.spacing Spacing.md`; pad with `Border.padding (Thickness Spacing.lg)`. Never pass a literal `8.0` or `12.0` into a layout primitive — if a value isn't in the scale, propose adding it to `Spacing` rather than inlining.

## Iconography

- **Source:** `FluentIcons.Avalonia.Fluent` (Microsoft's open-source Fluent System Icons, MIT licensed). Provides ~2000 icons in regular and filled variants, paired natively with the Fluent theme.
- **Usage from FuncUI:**

  ```fsharp
  open FluentIcons.Avalonia.Fluent
  open FluentIcons.Common

  SymbolIcon.create [
      SymbolIcon.symbol Symbol.Save
      SymbolIcon.iconVariant IconVariant.Regular
      SymbolIcon.fontSize 20.0
  ]
  ```

- **Sizing:** icons match the surrounding text's `FontSize`. Toolbar icons default to `20`, in-line icons to `16`, hero icons (empty states) to `48`.
- **Filled vs regular:** regular for navigation and idle affordances; filled for selected / active / destructive states. Pick one of the two within a row — never mix.
- **Tinting:** default to `Brand.Gray60` on light canvas and `Bianco` on Blu Stem chrome. Icons paired with a Blu Stem surface use `Brand.BluStem30` (`#B1C9F8`, Pantone 658 C) per the brand manual's sanctioned icon tint.
- **Custom icons:** when the Fluent catalogue genuinely lacks a glyph (device-specific schematics, Stem hardware silhouettes), drop an SVG into `Resources/icons/` and expose it through a typed `Icons` module. Avoid raster formats; SVG only.

## Localisation (i18n)

Every archetype A app ships **Italian** (default at runtime) **and English** translations. No app is single-language. No string ever sits inline in a view — every visible word lives in `Strings.fs`.

### Mechanism — F# strings module

```fsharp
module Stem.<App>.GUI.Strings

type Lang = It | En

let welcome (lang: Lang) =
    match lang with
    | It -> "Benvenuto"
    | En -> "Welcome"

let deviceConnected (lang: Lang) =
    match lang with
    | It -> "Dispositivo collegato"
    | En -> "Device connected"

let devicesFound (count: int) (lang: Lang) =
    match lang with
    | It -> sprintf "%d dispositivi trovati" count
    | En -> sprintf "%d devices found" count
```

The view consumes by passing the current `Lang` from the `Model`:

```fsharp
TextBlock.create [
    TextBlock.text (Strings.welcome model.Lang)
]
```

### Why F# strings module, not `.resx`

- **Compile-time completeness.** Adding a new language is one new DU case; the compiler then refuses to build until every string function handles it. Adding a new string forces an `It` and `En` value at the declaration site. Missing translations are impossible.
- **Refactor-safe.** Renaming a string is a rename across the project — no XML keys to chase, no `Resources.Designer.cs` to regenerate, no runtime `null` from a missing lookup.
- **MVU-native.** Strings are values, not resource-manager lookups. `Update` and `View` consume them the same way they consume any other model field.
- **No external tooling.** Translators receive a `Strings.fs` patch on a PR; the diff reads as prose paired by case. There is no ResX Manager dependency, no Crowdin pipeline, no key-mismatch class of bug.

### Default language at runtime

`Lang.It` is the startup default. The first-run experience offers a language picker in the title bar; the choice is persisted per [`CONFIGURATION.md`](./CONFIGURATION.md). The active value lives on the top-level `Model.Lang`.

### Italian-first content rules

- **Seed data stays Italian.** Variables, protocol dictionaries, and other domain data are *data*, not UI chrome — they ship in whatever language STEM uses internally (typically Italian for legacy dictionaries) and are not translated by the strings module.
- **UI chrome translates.** Every label, button, error message, toast, modal heading, and tab title goes through `Strings.fs`.
- **Logs stay English** per [`LOGGING.md`](./LOGGING.md) and the `COMMENTS.md` English-by-default rule. The strings module is the user-facing surface, not the diagnostic one.

## Error and progress surfaces

Four surfaces, each with a defined role. Pick by the decision tree, not by feel.

| Surface | When to use | Lifetime | Blocks input? |
| --- | --- | --- | --- |
| **Toast** | Non-critical info, success confirmation, recoverable warning | Auto-dismiss after 4–8 s | No |
| **Banner** | Persistent condition the user should be aware of (offline mode, stale data, pending update) | Until condition clears or user dismisses | No |
| **Inline error** | Validation failure attached to a specific control | Until the user corrects the input | No |
| **Modal** | Destructive confirmation, unrecoverable error, mandatory acknowledgement | Until the user acknowledges | Yes |

### Decision tree

1. Does the user need to *do* something before the app can proceed? → **modal**.
2. Is the error tied to one input field? → **inline error** beside that control.
3. Is the condition ongoing (not a one-shot event)? → **banner**.
4. Otherwise → **toast**.

### Payload shape

All four surfaces consume the same `ErrorPayload` record produced upstream per [`ERROR_HANDLING.md`](./ERROR_HANDLING.md):

```fsharp
type Severity = Info | Success | Warning | Error

type ErrorPayload = {
    Severity: Severity
    Title:    Lang -> string
    Body:     Lang -> string
    Action:   (Lang -> string) option   // optional "Retry" / "View details" label
    OnAction: Msg option                 // dispatched if Action is invoked
}
```

The `Title` and `Body` carry localised functions, not pre-rendered strings — the surface renders them with the current `Model.Lang`, so a language switch mid-toast re-renders correctly.

### Semantic colours

Each severity maps to a token from `Brand.Semantic`:

| Severity | Token | Hex | Source |
| --- | --- | --- | --- |
| `Info` | `Brand.Semantic.Info` | `#004682` | brand `BluStem` (Pantone 2154 C) |
| `Success` | `Brand.Semantic.Success` | `#16A34A` | software-derived — distinct from `VerdeEMS` to prevent division-color collision |
| `Warning` | `Brand.Semantic.Warning` | `#D97706` | software-derived — distinct from `GialloCommercialVehicles` to prevent division-color collision |
| `Error` | `Brand.Semantic.Error` | `#E40032` | brand `RossoAlert` (Pantone 185 C — sanctioned alert color) |

Severity is never communicated by color alone — every surface pairs the color with a matching `SymbolIcon` (`Symbol.Info`, `Symbol.CheckmarkCircle`, `Symbol.Warning`, `Symbol.DismissCircle`) so the signal carries on grayscale screens, in colorblind contexts, and on accessibility tooling.

## Loading and progress

- **Inline spinner** for small async ops (single-row save, validation round-trip). Sits on the control itself.
- **Top-of-view progress bar** (`ProgressBar.isIndeterminate true`) for page transitions and content loads.
- **Skeleton loaders** for content-heavy pages (variable tables, log views) — preserves layout while data streams in.
- **Cancel affordance.** When the operation is long-running and cancellable per [`CANCELLATION.md`](./CANCELLATION.md), the surface exposes a visible **Cancel** button (toast action / banner button / modal button). Operations without a Cancel affordance are not allowed to run more than ~2 s.

## Window sizing and DPI

- **Minimum window size:** `1024 × 600`. Engineers run these tools on docked laptops and production-floor workstations; below this size column-dense tables collapse beyond usability.
- **Per-monitor DPI awareness:** on by default via Avalonia. No fixed-pixel positioning — use `Grid` with proportional rows/columns, or `DockPanel`, or `StackPanel` plus the `Spacing` scale.
- **High-DPI assets:** SVG icons (Fluent System Icons + custom) scale natively; raster assets in `Resources/` ship at 1×, 2×, and 3× via Avalonia's `.assets` convention.

## Accessibility floor

Minimum requirements every archetype A app meets:

- **Keyboard navigation:** every interactive control is reachable by Tab; tab order matches visual reading order.
- **Focus indicator:** the Fluent default focus ring stays on. Don't strip it for visual reasons.
- **Contrast:** body text meets WCAG AA against its background in both themes. Verify via Avalonia DevTools' contrast checker during page review.
- **Touch targets:** minimum 44 × 44 px when running on a touchscreen workstation (production floor). Critical actions get explicit hit-test padding.

Production-floor touchscreens are real deployment targets for some apps — design with mouse and finger in mind from the start, not as a retrofit.

## What this means in practice

- **Adding a string:** edit `Strings.fs`, the compiler reminds you to fill `It` and `En`. View consumes via `Strings.<name> model.Lang`.
- **Adding a colour:** use a `Brand.*` token (or `Brand.Semantic.*` for status colour). Never a hex literal in the view DSL.
- **Adding an icon:** import from `FluentIcons.Common.Symbol` and pick. Custom SVG only when the Fluent set genuinely lacks the glyph. Tint defaults to `Brand.Gray60`; on Blu Stem surfaces, tint to `Brand.BluStem30`.
- **Reporting an error:** build an `ErrorPayload` upstream; route to the right surface via the decision tree. Don't `printfn` a user-facing string and call it a day.
- **Choosing a spacing value:** read from `Stem.<App>.GUI.Spacing`. If the value you need is missing, add it to `Spacing` (and consider whether it should land here as a new scale step).
- **Setting a new app's division:** edit `Branding.division` once at app scaffolding. The badge, badge color, and About-dialog branding follow.
- **Pairing colors in a view:** brand-illegal combinations to avoid — two division colors in the same view (e.g. Verde EMS + Giallo CV); pure black or white for body text (use `Gray60` / `Bianco`).
