# Standard: MODULE_SEPARATION

> **Stability:** v1.0.0
> **Principles:** onion (archetype A) and hexagonal (archetype B). Inner layers know nothing of outer layers. Banned APIs enforce the line at compile time.

## Archetype A — Onion

```
GUI ──► Services ──► Core
        ▲              ▲
        Infrastructure (depends on Services for use-cases, on Core for types)
```

| Layer | Depends on | Owns |
| --- | --- | --- |
| `Core` | nothing | Domain types, ports (interfaces), pure invariants |
| `Services` | Core | Use cases, orchestration of ports |
| `Infrastructure` | Core, Services | Adapters: EF Core, file IO, HTTP, hardware drivers |
| `GUI` | Core, Services | Views, view-models, composition root |

`GUI` references `Infrastructure` only in the **composition root** (`Program.fs` / startup), where it wires concrete adapters into the ports defined in `Core`. Views and view-models depend on ports, not adapters.

## Archetype B — Hexagonal

```
            Drivers.<Plat>.<Bus>      DependencyInjection (optional)
                    │                           │
                    ▼                           ▼
              Abstractions ◄──── Protocol
```

| Layer | Depends on | Owns |
| --- | --- | --- |
| `Abstractions` | nothing | Interfaces, DTOs, error types — no logic |
| `Protocol` | Abstractions | Pure logic: encoding, decoding, state machines |
| `Drivers.<Plat>.<Bus>` | Abstractions | Platform-specific adapter (e.g. `Drivers.Windows.Ble`) |
| `DependencyInjection` | all of the above | Optional `IServiceCollection` extension methods |

A consumer takes a NuGet dependency on `Stem.<Lib>.Abstractions` for the port surface, on one or more `Stem.<Lib>.Drivers.*` for adapters, and optionally on `Stem.<Lib>.DependencyInjection` for wiring.

## Banned APIs

To enforce that platform-specific code doesn't leak into pure layers, `Core`, `Services`, `Abstractions`, and `Protocol` projects load `BannedSymbols.txt` via `Microsoft.CodeAnalysis.BannedApiAnalyzers`. The list lives in `shared/templates/BannedSymbols.txt` and bans:

- `System.Drawing.*` — use `SkiaSharp`.
- `Microsoft.Win32.Registry.*` — use `appsettings.json` + `IOptions<T>`.
- `System.Management.*` (WMI) — use `Stem.Communication.Drivers.Windows.*` if you really need it.
- `System.IO.Ports.SerialPort` — use the `Stem.Communication.Abstractions` port and a driver implementation.
- Hardcoded paths (`C:\\…`, `/tmp/…`) — use `Path.Combine` + `Environment.SpecialFolder`.

Adapter / Driver projects do **not** load the banned list — they're the layer where platform reality lives.

## Composition root

The composition root is the single place that knows about every adapter:

- **Archetype A:** `<App>.GUI/Composition/Bindings.fs` (or equivalent).
- **Archetype B:** `Stem.<Lib>.DependencyInjection` is the composition root **for the consumer**.

Runtime platform dispatch (e.g. *use Windows BLE driver on Windows, fallback or error elsewhere*) happens in the composition root via `RuntimeInformation.IsOSPlatform(...)` — see PORTABILITY.

## What this means in practice

- Editing `Core` and need `System.IO`? Stop and ask whether the operation belongs in `Infrastructure`.
- Adding a new adapter: it lives in `Infrastructure` (A) or a `Drivers.*` package (B). Never in `Services` / `Protocol`.
- A use case wants to render an Excel file? Define a port in `Core` (e.g. `IReportExporter`), implement in `Infrastructure` with `ClosedXML`. The use case stays pure.
