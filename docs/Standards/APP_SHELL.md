# Standard: APP_SHELL

> **Stability:** v1.5.0
> **Principle:** every archetype A app exposes the **same set of canonical views** (Settings, About, language picker, and — for connected apps — a connection-status indicator) and composes them through a **typed Shell record with named slots**. The catalogue is fixed; the geometry is not. The standard names the nouns and the wiring; each app picks its own visual arrangement until enough apps have shipped to extract a shared shell library.
> **Applies to:** archetype A.
> **Pairs with:** [`GUI.md`](./GUI.md) (project layout, MVU paradigm), [`DESIGN_SYSTEM.md`](./DESIGN_SYSTEM.md) (visual contract, palette, typography, branding, i18n).

## Why this exists

Without a shared shell vocabulary, every new STEM tool re-decides "where is Settings, where is About, how does the language switcher render, what does the connection state look like." Operators hop between `stem-button-panel-tester`, `stem-dictionaries-manager`, `stem-device-manager`, and `spark-log-analyzer`; consistency in *what* exists (and how it behaves) builds muscle memory faster than consistency in *where* it sits.

This standard codifies the catalogue. It deliberately does **not** prescribe layout coordinates (logo top-left vs bottom, nav left vs right, hamburger vs menubar) — that's a per-app call until two or three apps converge on the same shape in practice. At that point the convergent layout earns promotion to a shared `Stem.UI.Shell` library and a follow-up `APP_SHELL` minor bump tightens this standard's geometry section.

## What's NOT in scope

- **Most pixel positions, grid coordinates, fixed control sizes.** Outside of Navigation (see below), each app's `View.fs` arranges the Shell slots as it sees fit. A wizard tester can use a single-page layout; a dictionary editor can use a master-detail sidebar; a log analyzer can dedicate most of the window to a table. Same slot set, different geometry.
- **Menu form** — hamburger button vs top menubar vs context menu on the brand mark — is per-app.
- **Animation, transitions, gesture handling.** Those are app-level concerns; the standard stays at the structural layer.
- **A shared shell NuGet.** Deliberately deferred until 1–2 FuncUI apps prove a layout. This standard prescribes the *pattern*; the *library* comes later.

## What IS pinned

- **Navigation is a left sidebar.** The `Navigation` slot renders as a vertical left-edge sidebar listing the app's top-level pages — never top tabs, never a bottom bar, never breadcrumbs alone. Width is per-app (typical ~200–280 px); collapsibility (icon-only when narrow) is per-app; the *position* is fixed so operators hopping between Stem tools find page-switching in the same place every time. In a single-page app the sidebar disappears entirely; it does not collapse to a placeholder.

## Canonical view catalogue

| View | Required | Lives in | Form |
| --- | --- | --- | --- |
| **Settings** page | Always | `Pages/Settings/` | Full page reached via menu; sectioned (Appearance / Language / Notifications / Connection / Logging / Advanced) |
| **About** dialog | Always | `Pages/About/` | Modal dialog (per [`GUI.md`](./GUI.md)'s modal pattern) reached via menu |
| **LanguagePicker** | Always | `Components/LanguagePicker.fs` | Inline component placed in the chrome (typically title bar) |
| **NotificationCenter** | Always | `Components/NotificationCenter.fs` + top-level `Model.NotificationHistory` | Bell-with-count icon in the chrome (`Notifications` slot); opens a panel listing recent toast history |
| **App menu** | Always | per-app, slot-bound | Surface for invoking Settings / About / Help; form is per-app (hamburger / menubar / context menu) |
| **ConnectionStatus** indicator | Connected apps only — see Capabilities | `Components/ConnectionStatus.fs` | Inline component in the chrome; opens a popover or routes to `Settings.Connection` |
| **Help / Documentation** entry | Optional | menu entry pointing to external URL | Plain menu item |

"Always" means the view exists, has the content shape defined below, and is reachable through the menu (or, for components, slotted into the chrome). *Where* in the chrome each component appears is per-app, except `Navigation` which is locked to a left sidebar (see "What IS pinned" above).

**NotificationCenter vs DESIGN_SYSTEM error surfaces.** The four display surfaces from [`DESIGN_SYSTEM.md`](./DESIGN_SYSTEM.md) (toast / banner / inline / modal) are the **current display** of an event — ephemeral by design. `NotificationCenter` is the **history view** of past toast-level notifications, so a user who missed a toast or wants to recheck "what just happened" has somewhere to look. Banners and modals manage their own lifecycle and do not feed the history; only toast-level events do.

## The Shell record

The top-level `View.fs` (per [`GUI.md`](./GUI.md)) implements the Shell pattern by constructing a `ShellSlots<'msg>` record and binding it to its preferred layout primitives (`Grid`, `DockPanel`, `StackPanel`, …). Required slots are non-option; conditional slots are `option`.

```fsharp
module Stem.<App>.GUI.Shell

open Avalonia.FuncUI.DSL

type ShellSlots<'msg> = {
    // -- Always present ---------------------------------------------------
    BrandMark:        IView<'msg>           // corporate Stem mark + division badge (per DESIGN_SYSTEM Branding)
    AppTitle:         IView<'msg>           // app name + active page (or breadcrumbs)
    LanguagePicker:   IView<'msg>           // language switcher
    Notifications:    IView<'msg>           // bell-with-count icon; opens the NotificationCenter panel
    Menu:             IView<'msg>           // invokes Settings / About / Help
    Navigation:       IView<'msg>           // multi-page apps: bound to the LeftSidebar layout primitive (see below)
    Content:          IView<'msg>           // the active page's View output

    // -- Conditional ------------------------------------------------------
    ConnectionStatus: IView<'msg> option    // populated iff Capabilities.connection = Connected
    StatusFooter:     IView<'msg> option    // optional bottom strip (build info, sync state)
}
```

Required slots being non-option means the compiler refuses to construct a Shell that omits them — the catalogue is enforced at the type level, not by documentation alone.

The Shell record is consumed by a per-app layout function. The standard does not pin most of the geometry; **Navigation is the exception** — it must dock to the left edge. A representative archetype-A choice:

```fsharp
// View.fs — per app
let layout (slots: ShellSlots<Msg>) : IView<Msg> =
    DockPanel.create [
        DockPanel.children [
            // Header strip — order of items inside is per-app
            DockPanel.create [
                DockPanel.dock Dock.Top
                DockPanel.children [
                    slots.BrandMark
                    slots.AppTitle
                    slots.LanguagePicker
                    slots.ConnectionStatus |> Option.defaultValue (TextBlock.empty ())
                    slots.Notifications
                    slots.Menu
                ]
            ]
            // Optional footer
            slots.StatusFooter |> Option.iter id

            // Navigation — pinned to the left edge per "What IS pinned" above
            Border.create [
                Border.dock Dock.Left
                Border.width 240.0       // typical 200–280; per-app
                Border.child slots.Navigation
            ]

            // Active page fills the remaining space
            ContentControl.create [
                ContentControl.content slots.Content
            ]
        ]
    ]
```

A different app may freely rearrange the header, footer, and content area — but Navigation always docks left. Single-page apps simply omit the `Border` + `slots.Navigation` block; they don't render a placeholder.

## Capabilities — per-app feature declaration

Whether a `ConnectionStatus` slot is populated, and whether the `Settings.Connection` section is rendered, depends on the app's own declaration. Lives next to `Branding.fs` (from [`DESIGN_SYSTEM.md`](./DESIGN_SYSTEM.md)) at the top of `<App>.GUI/`.

```fsharp
module Stem.<App>.GUI.Capabilities

type Connection =
    | NotConnected           // app reads static data only (e.g. log files, internal DB)
    | Connected              // app maintains an active session with a device or API

let connection : Connection = Connected   // edit per app
```

Adopters today:

| App | Connection | Why |
| --- | --- | --- |
| `stem-button-panel-tester` | `Connected` | active BLE/CAN session with the panel under test |
| `stem-device-manager` | `Connected` | configures and probes embedded devices |
| `stem-dictionaries-manager` | `Connected` | fetches protocol dictionaries from the Stem API |
| `stem-production-tracker` | `NotConnected` | internal SQLite database, no live device link |
| `spark-log-analyzer` | `NotConnected` | reads static log files |

Adding richer states later (multi-target, online/offline degraded modes) is additive on the DU and is a **minor** bump.

## Settings page

A required page reached through the app menu. Sectioned. The Model is a record of sub-records — each section is independently extensible per app.

```fsharp
module Stem.<App>.GUI.Pages.Settings.Model

open Stem.<App>.GUI.Brand           // ThemeMode (Light | Dark)
open Stem.<App>.GUI.Strings         // Lang (It | En)

type LogLevel = Error | Warning | Info | Debug | Trace

type AppearanceSettings   = { Theme: ThemeMode }
type LanguageSettings     = { Lang: Lang }
type NotificationSettings = { ShowHistory: bool; Muted: bool }
type LoggingSettings      = { Verbosity: LogLevel; LogFile: string }

// Per-app shapes (empty record is valid when the app has nothing extra to declare)
type ConnectionSettings  = { /* per-app fields when Capabilities.connection = Connected */ }
type AdvancedSettings    = { /* per-app catch-all */ }

type Model = {
    Appearance:    AppearanceSettings
    Language:      LanguageSettings
    Notifications: NotificationSettings
    Connection:    ConnectionSettings option   // None iff Capabilities.connection = NotConnected
    Logging:       LoggingSettings
    Advanced:      AdvancedSettings
}
```

Section order in the View (top to bottom): **Appearance → Language → Notifications → Connection (when present) → Logging → Advanced**. Each section is rendered as a labeled group with a clear divider. Settings persist to disk per [`CONFIGURATION.md`](./CONFIGURATION.md) — the page reads from / writes to `IOptions<TSettings>` bound at the composition root.

Section content shapes — minimum required:

| Section | Required field | Type | Source |
| --- | --- | --- | --- |
| Appearance | Theme | `ThemeMode` | matches [`DESIGN_SYSTEM.md`](./DESIGN_SYSTEM.md)'s theme toggle |
| Language | Lang | `Lang` | matches [`DESIGN_SYSTEM.md`](./DESIGN_SYSTEM.md)'s i18n DU |
| Notifications | ShowHistory | `bool` | toggles whether the bell badge counts unseen items; `false` mutes the badge but toasts still appear |
| Notifications | Muted | `bool` | global mute — when `true`, no toast displays and the history pauses recording |
| Connection | (per-app) | per-app record | only when `Capabilities.connection = Connected` |
| Logging | Verbosity | `LogLevel` | feeds [`LOGGING.md`](./LOGGING.md)'s `ILogger<T>` minimum level |
| Logging | LogFile | `string` | absolute path; UI exposes a "Reveal in Explorer / Finder" action |
| Advanced | — | per-app | empty record allowed; no required fields |

Notifications and Logging are intentionally separate sections because they control **separate concerns**: notifications govern what the *user* sees in real time (user-facing surface, ephemeral); logging governs what the *forensic record* captures (developer-facing surface, persistent). Per-severity notification tuning and history-retention size go under `Advanced` if an app needs them.

Adding sections beyond the catalogue is not allowed — extend `Advanced` or propose a `minor` bump to add a new canonical section.

## About dialog

A required modal reached through the app menu. Stateless view (no Model / Update — it takes a record and renders).

```fsharp
module Stem.<App>.GUI.Pages.About

open Avalonia.Media

type AboutInfo = {
    AppName:         Lang -> string         // localized
    AppVersion:      string                 // semver from assembly informational version
    StandardVersion: string                 // read from .stem-standard.json at startup
    BuildSha:        string option          // from ThisAssembly.Git.Sha when available
    BuildDate:       System.DateTimeOffset option
    DivisionLabel:   Lang -> string         // from Branding.badgeLabel
    DivisionColor:   Color                  // from Branding.badgeColor
    Copyright:       Lang -> string         // copyright line in current Lang
    FontCredit:      Lang -> string         // SIL OFL attribution for bundled Poppins
}
```

The composition root populates `AboutInfo` once at startup and passes it down. The About view renders in this order: corporate brand mark → division badge (`DivisionLabel` in `DivisionColor`) → app name → app version + standard version pin → optional build SHA + build date → copyright → font credit.

The **font credit is required** because the SIL OFL license under which Poppins ships (per [`DESIGN_SYSTEM.md`](./DESIGN_SYSTEM.md)) requires attribution. Suggested wording:

```fsharp
let fontCredit = function
    | It -> "Carattere tipografico: Poppins di Indian Type Foundry — SIL Open Font License 1.1"
    | En -> "Typeface: Poppins by Indian Type Foundry — SIL Open Font License 1.1"
```

## LanguagePicker component

A required stateless component that takes a current `Lang` and dispatches `LanguageChanged of Lang` on selection. Renders each language label in its own language so the picker is recognizable to a user who can't read the current one.

```fsharp
module Stem.<App>.GUI.Components.LanguagePicker

open Avalonia.FuncUI.DSL
open Stem.<App>.GUI.Strings

let create (current: Lang) (dispatch: Lang -> 'msg) : IView<'msg> =
    ComboBox.create [
        ComboBox.dataItems [ It; En ]
        ComboBox.selectedItem current
        ComboBox.itemTemplate (DataTemplateView<Lang>.create (fun lang ->
            TextBlock.create [
                TextBlock.text (match lang with It -> "Italiano" | En -> "English")
            ] :> _))
        ComboBox.onSelectedItemChanged (fun item ->
            match item with
            | :? Lang as l -> dispatch l
            | _ -> ())
    ]
```

The component is consumed by both the chrome (title-bar placement, typical) and by the Settings page's Language section. Same component, two binding sites.

## ConnectionStatus component (connected apps only)

Required iff `Capabilities.connection = Connected`. A stateless component that takes a `ConnectionState` and renders a `SymbolIcon` + a localized label, colored from `Brand.Semantic`.

```fsharp
module Stem.<App>.GUI.Components.ConnectionStatus

open Avalonia.FuncUI.DSL
open FluentIcons.Common
open Stem.<App>.GUI.Brand
open Stem.<App>.GUI.Strings

type ConnectionState =
    | Disconnected
    | Connecting
    | Connected of deviceLabel: string
    | Error     of message: string

let private icon = function
    | Disconnected         -> Symbol.PlugDisconnected
    | Connecting           -> Symbol.PlugConnectedSettings
    | Connected _          -> Symbol.PlugConnected
    | Error _              -> Symbol.ErrorCircle

let private color = function
    | Disconnected         -> Gray60
    | Connecting           -> Semantic.Info
    | Connected _          -> Semantic.Success
    | Error _              -> Semantic.Error
```

Clicking the indicator either opens a popover (compact apps) or routes to `Settings.Connection` (full apps). Either is acceptable; standard does not pin the choice.

## NotificationCenter component

Required by every archetype A app. Two parts, both stateless:

- **Bell-with-count** — slotted into the chrome (the `Notifications` slot). Shows the number of unseen items as a badge. Clicking opens the panel.
- **Panel** — a popup or side-flyout listing the most recent toast-level notifications, newest first. Each row renders severity icon + title + body + relative timestamp + optional action button.

The notification history lives on the top-level `Model`, not on the component. Dispatching a toast appends to the history and (when not muted) renders an ephemeral toast surface. The two surfaces share the same `Notification` payload.

```fsharp
module Stem.<App>.GUI.Components.NotificationCenter

open Avalonia.Media
open Stem.<App>.GUI.Brand
open Stem.<App>.GUI.Strings

type Notification = {
    Id:        System.Guid
    Timestamp: System.DateTimeOffset
    Severity:  Severity                      // Info | Success | Warning | Error (per DESIGN_SYSTEM)
    Title:     Lang -> string                // localized
    Body:      Lang -> string                // localized
    Action:    NotificationAction option     // optional "Retry", "View details", …
    IsRead:    bool
}

and NotificationAction = {
    Label:    Lang -> string
    OnInvoke: System.Guid -> unit            // dispatches a Msg in the host app, keyed by Notification.Id
}

// Default retention: most recent 50 notifications. Apps may override via Advanced settings.
let defaultRetention : int = 50
```

The host app's `Update` is responsible for:

- **Appending** a `Notification` when a toast is dispatched (in the same place `Cmd.OfAsync.either`'s success/failure messages route).
- **Pruning** to the retention cap.
- **Marking-as-read** when the panel is opened (or per-item when scrolled past, per app).
- **Honoring `Settings.Notifications.Muted`** — when `true`, neither the toast nor the history records the event. The forensic record still goes through `ILogger<T>` per [`LOGGING.md`](./LOGGING.md); muting suppresses the *user-facing* surface only.

**Where the bell goes in the chrome:** per-app (the `Notifications` slot binds it). Typical placement is the right side of the header strip, paired with `ConnectionStatus` and the `Menu` button — matching where users expect to find "things requiring attention" in modern desktop chrome.

**Banner and modal surfaces do not feed the history.** Banners are tied to ongoing conditions (offline mode, stale data) that resolve when the condition clears; modals demand acknowledgement before the user moves on. Both are self-managing and inappropriate for after-the-fact review. Only toast-level events accumulate in the NotificationCenter.

## App menu

Every app exposes a menu with the following entries:

| Entry | Required | Action |
| --- | --- | --- |
| Settings | Always | navigate to `Pages/Settings` |
| About | Always | open the About modal |
| Help / Documentation | Optional | open an external URL (typically the GitHub repo's README) |

The menu's **form** is per-app: a hamburger button in the title bar, a classic menubar, a context menu on the brand mark, a sidebar accordion — all acceptable. The standard fixes the entries, not the chrome.

Apps may add menu entries beyond the catalogue (e.g. "Run diagnostics" in a test rig). Required entries appear in the order **Settings → About → (optional)**; app-specific entries appear before, separator, then the canonical entries.

## File layout — what this standard adds

On top of [`GUI.md`](./GUI.md)'s base layout, this standard prescribes:

```
<App>.GUI/
├── Capabilities.fs                              ← NEW: per-app capability declaration
├── Shell.fs                                     ← NEW: ShellSlots type + layout fn (or fold into View.fs)
├── Components/
│   ├── LanguagePicker.fs                        ← NEW: canonical, always
│   ├── NotificationCenter.fs                    ← NEW: canonical, always (bell + panel)
│   └── ConnectionStatus.fs                      ← NEW: canonical, connected apps only
└── Pages/
    ├── Settings/                                ← NEW: canonical, always
    │   ├── Model.fs
    │   ├── Update.fs
    │   └── View.fs
    └── About/                                   ← NEW: canonical, always
        └── View.fs                              (stateless — no Model/Update)
```

`Shell.fs` may be folded into `View.fs` if the app prefers a flatter layout — both shapes are acceptable as long as the `ShellSlots<'msg>` type is exposed somewhere reachable by the top-level View.

## What this means in practice

- **Adding a new archetype A app:** scaffold `Capabilities.fs`, `Shell.fs`, `Pages/Settings/`, `Pages/About/`, and the required components (`LanguagePicker`, `NotificationCenter`, plus `ConnectionStatus` when connected). Wire them into the top-level `View.fs` as a `ShellSlots<Msg>`. Dock the Navigation sidebar on the left; arrange the rest of the chrome however fits.
- **Toggling an app from non-connected to connected:** flip `Capabilities.connection` to `Connected`, populate the `ConnectionStatus` slot, populate `Connection` in the Settings model. The compiler enforces the rest.
- **Dispatching a toast:** in `Update`, route the event to **both** `ILogger<T>` (per [`LOGGING.md`](./LOGGING.md), structured, English) **and** the notification history (`Notification` record with localized `Title` / `Body`). The two are deliberately separate calls — logging is forensic, notifications are user-facing.
- **Adding an app-specific setting:** extend `AdvancedSettings` (or extend `ConnectionSettings` if it's connection-related, or `NotificationSettings` for per-severity tuning). Adding a *required canonical section* is a `minor` bump on this standard.
- **Adding a menu entry:** append before the canonical entries with a separator. App-specific entries are always app-defined; the standard does not curate them.
- **Designing the layout:** free choice for header / footer / content area; Navigation always docks left. Document the geometry briefly in the app's CLAUDE.md so reviewers know what to expect. When 2+ apps converge on the same layout, raise a `minor` bump proposal to extract it into `Stem.UI.Shell`.
