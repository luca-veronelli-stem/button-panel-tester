# Standard: APP_DATA

> **Stability:** v1.9.0
> **Principle:** every STEM desktop app writes its per-user runtime data under one fixed root — `<LocalApplicationData>\Stem\<AppName>\`. Logs, caches, DPAPI credentials, SQLite databases, and any future per-user configuration overrides live under this root and nowhere else. No `AppContext.BaseDirectory`, no `%AppData%` Roaming, no `%ProgramData%`, no hardcoded `C:\…` paths.

## Reference

[`Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)`](https://learn.microsoft.com/dotnet/api/system.environment.getfolderpath). Available in `System` since .NET 1.0; cross-platform from Mono / .NET Core onward. Resolves to:

| OS | Path |
| --- | --- |
| Windows | `%LocalAppData%` (typically `C:\Users\<user>\AppData\Local`) |
| Linux | `$XDG_DATA_HOME` if set, else `~/.local/share` |
| macOS | `~/Library/Application Support` |

The convention applies on every OS — the segment names `Stem\<AppName>\` are platform-agnostic, only the leading separator and resolved root differ.

## Required

### Root

```csharp
var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
var appRoot      = Path.Combine(localAppData, "Stem", appName);
Directory.CreateDirectory(appRoot);
```

`appName` is the **single PascalCase token** identifying the app on disk — no `Stem.` prefix (the parent segment already carries that), no separators, no version suffix. Examples: `DeviceManager`, `ButtonPanelTester`, `DictionariesManager`, `ProductionTracker`, `SparkLogAnalyzer`. The on-disk name maps onto the repo name (`stem-device-manager` → `Stem\DeviceManager\`, `button-panel-tester` → `Stem\ButtonPanelTester\`).

### Folder shape

```
<LocalApplicationData>\
└── Stem\                       PascalCase, single token. Matches namespace prefix Stem.*.
    └── <AppName>\              Single PascalCase token, no Stem. prefix.
        ├── logs\               Log files. Owned by APP_DATA; shape owned by LOGGING.md.
        ├── cache\              JSON / Excel / dictionary caches — anything safely re-fetchable.
        ├── credentials\        DPAPI-encrypted credential blobs.
        └── db\                 SQLite databases.
```

The `Stem` company segment is PascalCase (not `STEM`, not `stem`) — matches the namespace prefix used in code (`Stem.Communication`, `Stem.ButtonPanelTester.GUI`), aligns with Microsoft's canonical `CompanyName\AppName` guidance for `LocalApplicationData`, and reads naturally on disk.

Sub-purpose folders are mandatory as soon as a second data type lands. A single-data app may put one file directly at the per-app root (`Stem\ProductionTracker\data.db`) — but the moment a log file, cache, or second database joins it, move the existing file into the appropriate sub-folder. Don't grow a flat directory of mixed concerns.

## Forbidden

| Alternative | Why not |
| --- | --- |
| `AppContext.BaseDirectory` | Fails under Program Files / read-only install locations. Loses per-user separation on shared bench machines. Under single-file publish, the directory the `.exe` was launched from may not be writable, and on a thumb-drive launch the path varies per session. Documented failure mode in `stem-device-manager` v0.4.1 → v0.4.2: a technician downloading only the `.exe` (no sibling `appsettings.json`) saw the app silently fall back to embedded Excel data with no log line, because the would-be log directory was unwritable and the would-be `appsettings.json` lookup hit `BaseDirectory` first. |
| `Environment.SpecialFolder.ApplicationData` (`%AppData%` Roaming) | Roaming profile sync (via Active Directory) was designed for tiny user-preference XML — not for log files, SQLite databases, or DPAPI blobs. Wastes profile size, mishandles laptop-to-desktop session moves (binary blobs replicate across machines that can't decrypt the user's DPAPI scope), slows logon. The two repos currently here (`stem-dictionaries-manager`, `stem-production-tracker`) migrate to Local on their next bump. |
| `%ProgramData%` (`Environment.SpecialFolder.CommonApplicationData`) | Shared across all users on a machine. Loses per-tech session separation (techs sharing a workstation see each other's logs and credentials). Frequently requires elevated permissions to write to. |
| Hardcoded absolute paths (`C:\Users\…`, `C:\Stem\…`) | Obvious portability anti-pattern; breaks under non-default user-profile relocation and on non-Windows. |

The forbidden list is enforced socially via review and `LOGGING.md`'s cross-reference, not by an analyzer — there is no `BannedSymbols.txt` entry for `AppContext.BaseDirectory` because legitimate uses exist (locating an `.exe`-relative resource for `<AvaloniaResource>`, reading a bundled binary at launch). Reviewer-side check: any **write** keyed off `AppContext.BaseDirectory` is a smell.

## Path-resolution helper (optional, forever)

The path convention is the standard; a helper is just `Path.Combine` sugar. Most apps will land something like this in the composition root, but a one-line `Path.Combine(localAppData, "Stem", appName, "logs")` at the call site is equally fine.

```csharp
namespace Stem.<App>;

internal static class StemAppData
{
    private const string CompanySegment = "Stem";
    private const string AppSegment     = "<App>";

    public static string GetAppRoot()        => EnsureDir(Path.Combine(LocalRoot, CompanySegment, AppSegment));
    public static string GetLogsDir()        => EnsureDir(Path.Combine(GetAppRoot(), "logs"));
    public static string GetCacheDir()       => EnsureDir(Path.Combine(GetAppRoot(), "cache"));
    public static string GetCredentialsDir() => EnsureDir(Path.Combine(GetAppRoot(), "credentials"));
    public static string GetDbDir()          => EnsureDir(Path.Combine(GetAppRoot(), "db"));

    private static string LocalRoot =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static string EnsureDir(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
```

That's the entire forever surface. ~15 LOC, no marker file, no migration state. The companion follow-up [#110](https://github.com/luca-veronelli-stem/standards/issues/110) tracks extracting this into a `Stem.AppData` NuGet once a second consumer earns its keep — until then, copy-paste is fine.

## Migrating an existing installed base (transient)

Adopters with **already-installed users** writing to one of the legacy roots (`AppContext.BaseDirectory\logs\`, `%LocalAppData%\Stem.ButtonPanel.Tester\`, `%LocalAppData%\Stem.ButtonPanelTester\`, `%AppData%\STEM\<App>\`) need a one-shot first-launch migration so a tech doesn't lose their cached state, credentials, or log history on the v1.9.0 cutover. This section describes the pattern; both the helper class and the on-disk marker it writes are **removable once the adopter's installed base has rolled over** — typically one or two release cycles after the cutover, when the adopter is confident no first-launch under the old layout will surface again.

### Schema-marker file (transient)

The migration helper writes a `.appdata-version` file at the per-app root containing a single ASCII integer — keeps the parsing surface minimal so a botched read can't itself break startup:

```text
1
```

The marker is **migration bookkeeping**, not a permanent part of the layout. Its only job is to make `MigrateOnce` idempotent across launches. Once the migration code is deleted, the file on disk becomes inert and can be ignored or cleaned up opportunistically; it does not become a forever fixture of the convention.

### Migration helper (transient)

```csharp
namespace Stem.<App>;

internal static class StemAppDataMigration
{
    private const int SchemaVersion = 1;

    /// <summary>
    /// Idempotent first-launch migration from a legacy root to <see cref="StemAppData.GetAppRoot"/>.
    /// Safe to call on every startup; safe to call twice in a row; safe to interrupt and retry.
    /// Delete this class (and the call site) once the installed base has rolled over.
    /// </summary>
    public static void MigrateOnce(string legacyRoot)
    {
        var newRoot = StemAppData.GetAppRoot();
        var marker  = Path.Combine(newRoot, ".appdata-version");
        var current = File.Exists(marker) && int.TryParse(File.ReadAllText(marker), out var v) ? v : 0;
        if (current >= SchemaVersion) return;

        if (Directory.Exists(legacyRoot) && !PathEquals(legacyRoot, newRoot))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(legacyRoot))
            {
                var target = Path.Combine(newRoot, Path.GetFileName(entry));
                if (!File.Exists(target) && !Directory.Exists(target))
                    Directory.Move(entry, target);
            }
        }
        File.WriteAllText(marker, SchemaVersion.ToString());
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                      Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
                      StringComparison.OrdinalIgnoreCase);
}
```

Wire it in at the composition root before any logger or cache opens a file under the new root:

```csharp
// Program.cs, before host build
var legacyRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "Stem.ButtonPanelTester");   // whichever legacy shape this adopter previously used
StemAppDataMigration.MigrateOnce(legacyRoot);
```

### Removal after cutover

Once the installed base has rolled over — one or two release cycles after the v1.9.0-aligned release is the usual cadence — delete `StemAppDataMigration.cs` and its call site. The `.appdata-version` files left on disk are harmless; no future code reads them. `StemAppData` (the path-resolution helper) stays.

If a **second** layout migration is ever needed (unlikely — the convention is meant to be stable), reintroduce a fresh `StemAppDataMigration` with a bumped `SchemaVersion`. The marker convention recurs naturally then; nothing about v1.9.0 forces a forever marker on disk in the meantime.

## Archetype B (libraries)

A library never assumes its consumer's data root. If a library needs to persist anything (cache, credentials), it takes the target directory as a constructor parameter from the consuming app:

```csharp
public sealed class CredentialStore(string credentialsDir, ILogger<CredentialStore>? logger = null)
{
    // credentialsDir comes from StemAppData.GetCredentialsDir() in the composition root.
}
```

This standard governs **where the app writes**; a library writes wherever the app tells it to.

## Cross-platform note

The convention applies on Linux and macOS without modification — `Environment.SpecialFolder.LocalApplicationData` resolves to the platform-appropriate root, `Path.Combine` handles separator differences, and the segment names `Stem` + `<AppName>` are case-preserved by every filesystem STEM apps target (NTFS on Windows, ext4 / APFS / Btrfs in case-sensitive mode on Linux/macOS).

```
Windows:  C:\Users\<user>\AppData\Local\Stem\DeviceManager\logs\
Linux:    /home/<user>/.local/share/Stem/DeviceManager/logs/
macOS:    /Users/<user>/Library/Application Support/Stem/DeviceManager/logs/
```

Pairs with [`PORTABILITY.md`](./PORTABILITY.md): `PORTABILITY` is upstream (how code is shaped — TFM defaults, banned Win32 APIs), `APP_DATA` is downstream (where the cross-platform code writes its data).

## F#

Same `Environment.SpecialFolder.LocalApplicationData` API; idiomatic F# wraps the helper as a module instead of a static class:

```fsharp
module Stem.<App>.StemAppData

open System
open System.IO

[<Literal>]
let private CompanySegment = "Stem"

[<Literal>]
let private AppSegment = "<App>"

let private localRoot () =
    Environment.GetFolderPath Environment.SpecialFolder.LocalApplicationData

let private ensureDir path =
    Directory.CreateDirectory path |> ignore
    path

let appRoot ()        = Path.Combine(localRoot (), CompanySegment, AppSegment) |> ensureDir
let logsDir ()        = Path.Combine(appRoot (), "logs")        |> ensureDir
let cacheDir ()       = Path.Combine(appRoot (), "cache")       |> ensureDir
let credentialsDir () = Path.Combine(appRoot (), "credentials") |> ensureDir
let dbDir ()          = Path.Combine(appRoot (), "db")          |> ensureDir
```

The `button-panel-tester` greenfield established the F# adopter pattern at [`CompositionRoot.fs:71-73`](https://github.com/luca-veronelli-stem/button-panel-tester/blob/main/src/ButtonPanelTester.GUI/Composition/CompositionRoot.fs#L71-L73); the only delta against v1.9.0 is the folder shape (`Stem\ButtonPanelTester\` two-segment, instead of `Stem.ButtonPanelTester\` flat). The transient migration helper from the previous section translates to F# the same way — a small `StemAppDataMigration` module deleted once the installed base has rolled over.

## Cross-references

- [`LOGGING.md`](./LOGGING.md) — log filename pattern and provider choice (NReco rolling vs custom per-process). `APP_DATA` owns directory location (`<appRoot>\logs\`); `LOGGING` owns log-specific shape.
- [`CONFIGURATION.md`](./CONFIGURATION.md) — `appsettings.Production.json` location (next to exe vs `Stem\<App>\appsettings.json`) is a deferred decision. Apps continue to ship `appsettings.json` next to the `.exe` for v1.9.0; revisit if a real per-user override use case appears.
- [`PORTABILITY.md`](./PORTABILITY.md) — upstream (cross-platform code shape) vs downstream (where data lives).

## What this means in practice

- **Greenfield archetype A app:** drop the `StemAppData` path-resolution helper into the composition root, point every write site at `StemAppData.GetLogsDir()` / `GetCacheDir()` / `GetCredentialsDir()` / `GetDbDir()`. No migration helper, no marker file — there is nothing to migrate from.
- **Existing adopter with an installed base on a legacy root:** add `StemAppData` (forever) **and** `StemAppDataMigration` (transient) at the same time. Wire `StemAppDataMigration.MigrateOnce(legacyRoot)` into the composition root before any logger or cache opens a file. Ship one or two release cycles, then delete `StemAppDataMigration` and its call site; the `.appdata-version` files left behind become inert.
- **Reviewing:** flag any write keyed off `AppContext.BaseDirectory`, any `Environment.SpecialFolder.ApplicationData` (Roaming), any `%ProgramData%`, any hardcoded `C:\` path, any flat `Stem.<App>\` folder shape (dot instead of separator), any ALL-CAPS `STEM\` segment. Also flag a `StemAppDataMigration` class that has been in-tree for longer than its adopter's typical install-base rollover window — that's dead code that should have been deleted.
- **Adoption:** non-breaking for libraries; opt-in for archetype A apps on their next bump. `stem-device-manager` v0.4.3 is the first reference adopter (paused on this standard); `button-panel-tester` realigns its existing flat `Stem.ButtonPanelTester\` to the two-segment shape on its next bump; `stem-dictionaries-manager` and `stem-production-tracker` migrate from Roaming `STEM\` to Local `Stem\` on their next bump. Each carries the transient migration helper through one or two release cycles, then deletes it.
