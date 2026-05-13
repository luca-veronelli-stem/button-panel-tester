# Standard: GUI

> **Stability:** v1.5.0
> **Principle:** every new archetype A app is **Avalonia + FuncUI + Elmish-MVU in F#**. The GUI is a thin shell: pure `Update` over an immutable `Model`, declarative `View` returning a `Msg`-typed tree, side effects expressed as `Cmd<Msg>` that calls Services through Core ports. Legacy WinForms/WPF projects are tolerated only inside an active migration window.
> **Applies to:** archetype A.

## Reference

`Avalonia` 11.x, `Avalonia.FuncUI` (Elmish MVU bindings), `Avalonia.Themes.Fluent`, `Avalonia.Headless.XUnit`. All four pinned via the templates' `Directory.Packages.props`. F# default per [`LANGUAGE.md`](./LANGUAGE.md); composition rules per [`MODULE_SEPARATION.md`](./MODULE_SEPARATION.md).

## Project layout inside `<App>.GUI/`

```
<App>.GUI/
├── Program.fs                  entry point + Avalonia.AppBuilder
├── App.fs                      Application class + theme + StyleInclude
├── Model.fs                    top-level Model + Msg + init
├── Update.fs                   pure Update fn + Cmd plumbing
├── View.fs                     top-level View fn (composes Pages/)
├── Strings.fs                  i18n (see DESIGN_SYSTEM)
├── Composition/
│   └── Bindings.fs             composition root — wires Infrastructure
├── Pages/
│   └── <PageName>/             one folder per top-level page
│       ├── Model.fs            page-scoped Model + Msg
│       ├── Update.fs           page-scoped Update
│       └── View.fs             page-scoped View
├── Components/                 stateless view fragments
│   └── <ComponentName>.fs
└── Resources/
    ├── icons/                  SVGs not covered by Fluent System Icons
    └── styles/                 *.axaml resource dictionaries
```

`.fsproj` file order is load-bearing: declared types must precede their first use. Within a page, the order is **Model → Update → View**. Within the GUI project, the order is **Strings → Components → Pages → Composition → App → Program**.

## The MVU triple

Each page (and the top-level shell) is a `Model` + `Msg` + `Update` + `View` quadruple. `Update` is **pure** — `Model -> Msg -> Model * Cmd<Msg>` — no IO, no time, no random. Effects are returned as `Cmd<Msg>` and executed by the runtime.

```fsharp
type Model = { DeviceList: Device list; Busy: bool; Error: string option }

type Msg =
    | LoadRequested
    | LoadSucceeded of Device list
    | LoadFailed of string

let init () = { DeviceList = []; Busy = false; Error = None }, Cmd.ofMsg LoadRequested

let update (deps: AppDeps) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | LoadRequested ->
        { model with Busy = true; Error = None },
        Cmd.OfAsync.either deps.DeviceCatalog.ListAsync () LoadSucceeded (fun ex -> LoadFailed ex.Message)
    | LoadSucceeded devices -> { model with Busy = false; DeviceList = devices }, Cmd.none
    | LoadFailed err -> { model with Busy = false; Error = Some err }, Cmd.none
```

`deps: AppDeps` is the record of ports that `Composition/Bindings.fs` hands to the runtime once at startup. The `Update` function never reaches outside its arguments.

## Composition root

`Composition/Bindings.fs` is the single place that knows about every adapter. It constructs the `AppDeps` record, wiring `Infrastructure` adapters into the ports defined in `Core`. Platform dispatch (Windows BLE driver vs Linux serial-only, etc.) happens here via `RuntimeInformation.IsOSPlatform(...)` — see [`PORTABILITY.md`](./PORTABILITY.md).

```fsharp
module Stem.<App>.GUI.Composition.Bindings

open System.Runtime.InteropServices

let buildDeps (cfg: AppConfiguration) (logger: ILoggerFactory) : AppDeps =
    let comm =
        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
            Stem.Communication.Drivers.Windows.Ble.Driver(cfg.Ble, logger.CreateLogger _) :> ICommunication
        else
            Stem.Communication.Drivers.Generic.Serial.Driver(cfg.Serial, logger.CreateLogger _) :> ICommunication
    { Comm = comm
      DeviceCatalog = Stem.<App>.Infrastructure.DeviceCatalog.create comm
      Clock = SystemClock.Instance }
```

**Manual DI only.** No `Microsoft.Extensions.DependencyInjection` container inside the GUI. The `AppDeps` record is the registration surface; `Bindings.fs` is the resolver. (Background services that genuinely need a hosted-service model use `Microsoft.Extensions.Hosting` in `Program.fs` and surface their port through `AppDeps`.)

## Long-running operations + cancellation

Async work runs through `Cmd.OfAsync.either` (or `.perform` / `.attempt` per Elmish), never inline in `Update`. The cancellation rules from [`CANCELLATION.md`](./CANCELLATION.md) apply:

- Each long-running operation stores its `CancellationTokenSource` in the `Model` (or a non-serialized side-table keyed by operation id).
- A `CancelRequested` `Msg` calls `cts.Cancel()` and dispatches the matching `*Cancelled` message on the receiving end.
- The view exposes a visible **Cancel** affordance whenever an operation is in flight (see DESIGN_SYSTEM's progress section).
- `OperationCanceledException` is mapped to a dedicated `Msg` case (`LoadCancelled`), never silently swallowed or surfaced as a generic error.

## Error handling in views

Errors cross the MVU boundary as **data**, not exceptions. The `Update` function receives `Msg.*Failed of ErrorPayload`; the `View` chooses the surface (toast / banner / modal) per [`DESIGN_SYSTEM.md`](./DESIGN_SYSTEM.md). Exceptions in adapters are caught at the `Cmd.OfAsync.either` boundary and translated to a typed `Msg`. See [`ERROR_HANDLING.md`](./ERROR_HANDLING.md) for the Try-pattern / Result / exception decision tree applied to the underlying Services.

## Logging

`ILogger<T>` is **required** in archetype A per [`LOGGING.md`](./LOGGING.md). The factory is built in `Program.fs`, passed into `Bindings.buildDeps`, and threaded through every adapter. The view itself does not log — view-level events that matter are first emitted as `Msg`, handled by `Update`, and logged from the matching Cmd/Service.

## Testing

The single F# tests project from [`TESTING.md`](./TESTING.md) covers the GUI in three layers:

| Test kind | Tool | Scope |
| --- | --- | --- |
| `Update` properties | xUnit + FsCheck | `Model -> Msg -> Model * Cmd` invariants; `Update` is pure so FsCheck is free |
| View smoke | xUnit + `Avalonia.Headless` | mount each top-level `View`, assert no crash, snapshot a representative `Msg` flow |
| Composition wiring | xUnit | `Bindings.buildDeps` returns a fully-populated `AppDeps` for every supported platform |

Categorise Avalonia.Headless tests `Category=GUI` so CI can filter them on Linux legs when an `axaml` resource references a Windows-only font. Update-function tests stay platform-agnostic.

## Multi-window / dialogs

- **Modal dialogs** are allowed for blocking confirmations, destructive ops, and unrecoverable errors that demand acknowledgement. Implement as an `IsModalOpen: ModalState option` field on the top-level `Model`; the view renders a modal overlay when populated. No `Window.ShowDialog()` from inside a `View`.
- **Modeless secondary windows** are discouraged. Internal tooling fits in one main window with paged navigation; a second OS-level window earns its keep only when the user must compare two views side-by-side (e.g. live device output next to a log replay).
- **Splash / about** windows live as transient pages, not separate `Window` instances.

## Migration carve-out

Existing C# WinForms or WPF projects named `<App>.GUI.Windows` (or similar) are allowed to remain in service while their replacement is built — same pattern as [`LANGUAGE.md`](./LANGUAGE.md)'s GUI carve-out. Rules during the window:

- Each repo's top-level `CLAUDE.md` declares an **Avalonia migration phase** (`Phase 4` is the convention used by adopted repos).
- New features land in the FuncUI surface (`<App>.GUI/`) once it exists, even if it's behind a feature flag. Don't extend `<App>.GUI.Windows`.
- Do not mix WinForms/WPF and Avalonia inside one project. The replacement is a sibling project, swapped at the entry point.
- The legacy project is deleted when no shipping feature still routes through it.

## What this means in practice

- **Adding a page:** new folder under `Pages/<PageName>/` with `Model.fs` / `Update.fs` / `View.fs`. Wire the page's `Model` into the top-level `Model`, route its `Msg` through the top-level `Msg`, and add the page to the shell's `View`.
- **Adding a component:** stateless `View` function under `Components/<Name>.fs`. Takes its props as arguments; returns `IView<'msg>`. No internal state — if it needs state, it's a page.
- **Adding a long-running op:** define the port in `Core`, implement the adapter in `Infrastructure`, expose through `AppDeps`. In `Update`, return `Cmd.OfAsync.either` with explicit `*Started` / `*Succeeded` / `*Failed` / `*Cancelled` messages.
- **Adding a dialog:** add a `ModalState` case, render it from the top-level `View`, dispatch its result as a `Msg`. The page that opened it stays mounted underneath.
- **Editing legacy `<App>.GUI.Windows`:** allowed for bug fixes and security patches only. New features go through the Avalonia surface.
