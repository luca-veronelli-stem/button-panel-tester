# Standard: PORTABILITY

> **Stability:** v1.0.0
> **Goal:** every layer defaults to `net10.0` (cross-platform). Windows-only code is confined to opt-in adapter projects.

## Default TFM

- `<TargetFramework>net10.0</TargetFramework>` is the default in `Directory.Build.props`.
- A project moves to `net10.0-windows` only when it directly references Windows APIs (Win32, WPF, WinForms, WMI). Under the layered split this is rare — such code lives in named driver projects.

## Pattern A — TFM-conditional packages

Driver projects multi-target and pull Windows-only packages **only on the Windows TFM**:

```xml
<TargetFrameworks>net10.0;net10.0-windows</TargetFrameworks>

<ItemGroup Condition="'$(TargetFramework)' == 'net10.0-windows'">
  <PackageReference Include="System.IO.Ports" />
  <PackageReference Include="InTheHand.Net.Bluetooth" />
</ItemGroup>
```

Under `net10.0`, the Windows-only package is absent and the driver provides a `NotSupportedException`-throwing stub — but it should not normally be reached, because the composition root only registers Windows drivers when running on Windows.

## Compile-time vs runtime gating

- **Compile-time** — TFM controls which code compiles. WinForms/WPF/Win32-bound code lives in `net10.0-windows` projects (or under `#if WINDOWS` blocks in multi-targeted ones), so a Linux build can't accidentally pull it in.
- **Runtime** — the composition root reads `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)` and registers Windows drivers only when running on Windows.

```fsharp
// <App>.GUI/Composition/Bindings.fs
let registerDrivers (services: IServiceCollection) =
    if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
        services |> Stem.Communication.Drivers.Windows.Ble.register
    else
        // No cross-platform BLE driver yet — fail fast at startup so a missing
        // adapter doesn't surface as a NotSupportedException mid-flow.
        failwith "BLE driver unavailable on this platform"
```

## Cross-platform replacements

| Need | Replace | With |
| --- | --- | --- |
| Excel read/write | `Microsoft.Office.Interop.Excel`, `EPPlus` | `ClosedXML` |
| 2D drawing | `System.Drawing.*` | `SkiaSharp` |
| Configuration store | `Microsoft.Win32.Registry` | `appsettings.json` + `IOptions<T>` |
| Hardcoded paths | `C:\\Users\\…`, `/tmp/…` | `Environment.SpecialFolder` + `Path.Combine` |
| Hardware enumeration | `System.Management` (WMI) | port + driver in `Stem.Communication.Drivers.Windows.*` |
| Serial ports | `System.IO.Ports` directly in `Services` | port + `Stem.Communication.Drivers.<Plat>.Serial` |

These are enforced by `BannedSymbols.txt` for the pure layers (see MODULE_SEPARATION).

## What does NOT cross platforms

After applying this standard, the only intentional Windows-bound code is:

1. `Stem.Communication.Drivers.Windows.*` packages (BLE, serial, USB, …).
2. Any legacy `<App>.GUI.Windows` project that still uses WinForms/WPF — flagged as a migration target by the MIGRATION standard.

Everything else builds and runs on Linux and macOS. CI (see CI standard) enforces this with a `ubuntu-latest` matrix leg.

## Verifying portability

```powershell
dotnet build                                 # cross-platform leg; legacy net10.0-windows projects build via EnableWindowsTargeting
dotnet test  --framework net10.0             # cross-platform tests
```

`dotnet build` is intentionally not filtered with `--framework net10.0`: that flag overrides per-project TFMs and breaks legacy `net10.0-windows` GUI projects (WinForms/WPF) that rely on `EnableWindowsTargeting=true` to build on Linux. The cross-platform contract is enforced by the test leg plus the explicit migration tracker in MIGRATION.

If either fails on a Linux runner, the offending project is leaking platform-specific code into a non-driver layer.

## TFM summary

| Project | TFM | Why |
| --- | --- | --- |
| `Core`, `Abstractions` | `net10.0` | Pure domain — no platform calls |
| `Services`, `Protocol` | `net10.0` | Pure logic — no platform calls |
| `Infrastructure` | `net10.0` | Talks to ports, not platforms directly |
| `GUI` (Avalonia + FuncUI) | `net10.0` | Avalonia is cross-platform |
| `Drivers.Windows.*` | `net10.0;net10.0-windows` | Multi-target — runtime resolves at composition root |
| `Drivers.Linux.*` | `net10.0` | Single TFM; Linux-only restrictions are runtime, not TFM |
| Legacy `GUI.Windows` (WinForms/WPF) | `net10.0-windows` | Until migrated to Avalonia |
