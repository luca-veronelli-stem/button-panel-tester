# Quickstart: feat/001 — Dictionary Fetch and Status Display

**Audience**: a developer joining `button-panel-tester` who needs to run feat/001 locally end-to-end (build, register, refresh, read tests).

**Prereqs**: Windows 10+, .NET 10 SDK, PowerShell 7+, a running `stem-dictionaries-manager` instance (local or shared dev), and a `BootstrapToken` from the `stem-dictionaries-manager` admin endpoint.

This document is a hand-holding walkthrough for the slice. The shipped tool's user-facing behaviour is described in [`spec.md`](./spec.md).

---

## 1. Clone and restore

```powershell
git clone git@github.com:luca-veronelli-stem/button-panel-tester.git
cd button-panel-tester
git checkout feat/001-fetch-dictionary  # while the branch is unmerged
dotnet restore
dotnet build -c Release
```

The first build may take 30–60 s because Avalonia and FuncUI pull a moderate dependency graph.

## 2. Configure the dictionary endpoint

`appsettings.Development.json` controls dev-mode wiring. Both `appsettings.json` (production placeholder) and `appsettings.Development.json` (dev URL) are checked in under `src/ButtonPanelTester.GUI/` — the dev URL `https://localhost:7065` is non-secret, and the per-supplier API key never lives in source (it sits DPAPI-encrypted in `%LOCALAPPDATA%\Stem\ButtonPanelTester\credentials\credential.dpapi`). If the file is missing locally, recreate it at `src/ButtonPanelTester.GUI/appsettings.Development.json`:

```json
{
  "Dictionary": {
    "BaseUrl": "https://localhost:7065",
    "Id": 2
  }
}
```

`Id: 2` matches the seeded "Pulsantiere" dictionary in a fresh `stem-dictionaries-manager` dev seed. Adjust if your local seed assigned a different id.

## 3. Mint a BootstrapToken on the dictionary service side

Bootstrap tokens are minted by an admin-authenticated endpoint on `stem-dictionaries-manager`. From a separate PowerShell session inside the `stem-dictionaries-manager` repo, run the admin helper (or use `gh` / curl against the admin endpoint — exact ceremony depends on the dictionary service's current admin UX).

For development, a static dev key works: `appsettings.json` of `stem-dictionaries-manager` carries a static key under `"ApiKeys": { "ButtonPanelTester": "STEM-BT-DEV-KEY-2026" }`. **You can paste this static key into the registration dialog as if it were a `BootstrapToken`** — the dev-side `ApiKeyMiddleware` accepts it as legacy mode. This shortcut is dev-only.

## 4. Run the GUI

```powershell
dotnet run --project src/ButtonPanelTester.GUI -c Debug
```

You should see:

1. The main window opens.
2. The dictionary status row at the top reads **"Cached · last synced \<seed build date\>"** in orange (the seeded data was extracted to `%LOCALAPPDATA%\Stem\ButtonPanelTester\cache\dictionary.json` on first launch).
3. A modal **Register your tool** dialog appears in front of the main window.
4. Paste the `STEM-BT-DEV-KEY-2026` (or your real `BootstrapToken`) and click **Submit**.
5. The dialog closes. The status row reads **"Live · synced now"** in green within ~1 s.

If step 5 fails, the status row stays orange and the detail affordance shows the failure reason. Check `%LOCALAPPDATA%\Stem\ButtonPanelTester\logs\app.log` for details — the NReco file sink wired in `CompositionRoot.configure` rolls at 5 MB and keeps 3 files (`app.log`, `app.1.log`, `app.2.log`). The `HttpRegistrationClient` `Warning` line records the failure mode (`HTTP 401` vs network timeout vs payload error), and `HttpDictionaryProvider` emits the immediate post-registration fetch outcome on the next line. The LOGGING standard governs the format.

## 5. Verify the on-disk artifacts

```powershell
$root = Join-Path $env:LOCALAPPDATA "Stem\ButtonPanelTester"
Get-ChildItem $root -Recurse | Format-Table FullName, Length, LastWriteTime -AutoSize
```

Expected (per STEM `APP_DATA.md`):
- `cache\dictionary.json` — UTF-8 JSON (canonicalised, no whitespace).
- `cache\dictionary.json.sha256` — 64-char hex + LF.
- `credentials\credential.dpapi` — opaque binary blob.
- `credentials\install.guid` — text Guid (rotated together with `credential.dpapi` by Re-Register).
- `logs\app.log` — NReco rolling log (also `app.1.log`, `app.2.log` after roll).

```powershell
# Validate the sidecar matches the JSON (PowerShell-only; sanity check)
$cache = Join-Path $root "cache"
$expected = (Get-Content "$cache\dictionary.json.sha256" -Raw).Trim()
$actual = (Get-FileHash "$cache\dictionary.json" -Algorithm SHA256).Hash.ToLowerInvariant()
"expected=$expected"
"actual  =$actual"
"match:    $($expected -eq $actual)"
```

`credential.dpapi` cannot be opened — it's DPAPI-encrypted under your user account.

## 6. Trigger a manual refresh

In the running app, click the **Refresh** button in the status row.

- If `stem-dictionaries-manager` is up: status row briefly shows `"Live · synced 14:32 · refreshing…"` (pulsing indicator), then settles to `"Live · synced \<now\>"`.
- If you stop `stem-dictionaries-manager` mid-session and click Refresh: status row settles to `"Cached · last synced 14:32 · refresh failed (server unavailable)"` in orange. The in-memory dictionary is preserved (FR-011); subsequent CAN tests in feat/00N+ would still operate against the current data.

## 7. Run the tests

```powershell
dotnet test -c Release --logger "console;verbosity=normal"
```

Expected layers:

- `tests/ButtonPanelTester.Tests/Unit/` — pure F# logic, no IO.
- `tests/ButtonPanelTester.Tests/Property/` — FsCheck properties: round-trip serialisation, ContentHash determinism, FetchFailureReason exhaustion, DictionarySource transitions.
- `tests/ButtonPanelTester.Tests/Integration/` — `DictionaryService` wired through `InMemory*` fakes; full cache-and-memory-in-lockstep flow without network or filesystem.
- `tests/ButtonPanelTester.Tests/Gui/` — `Avalonia.Headless.XUnit` against `DictionaryStatusRow` (rendering checks) and `RegistrationDialog` (Elmish message-loop checks).

To run a single layer:

```powershell
dotnet test --filter "FullyQualifiedName~Property"
dotnet test --filter "FullyQualifiedName~Gui"
```

## 8. Lean Phase1 verification

```powershell
cd lean
lake build
```

The four modules under `Stem/ButtonPanelTester/Phase1/` must compile with no `sorry`. Any failed proof blocks `/speckit.implement`'s gate per Constitution Principle I.

## 9. Re-registering (if you need to)

If your `BootstrapToken` was rotated and the next refresh returns `Unauthorized`, the status row's detail affordance shows a **"Re-register"** button. Click it to re-open the registration dialog. Submit a fresh token; on success the new credential overwrites `credential.dpapi` atomically.

To force a re-register from the OS side (e.g. for testing):

```powershell
Remove-Item "$env:LOCALAPPDATA\Stem\ButtonPanelTester\credentials\credential.dpapi"
Remove-Item "$env:LOCALAPPDATA\Stem\ButtonPanelTester\credentials\install.guid"
# Restart the GUI
```

Wiping both files (not just `credential.dpapi`) matches what the in-app **Re-Register** button does (#98) — the next `POST /register` carries a fresh `installGuid` and the server treats the machine as a clean install.

## 10. Refreshing the embedded seed (release-time)

Before a release, refresh the seed so first-launch users on a fresh machine see current data:

```powershell
$env:STEM_DICT_KEY = "<a dev API key with access to dictionary id 2>"
.\eng\refresh-seed.ps1
git add src/ButtonPanelTester.GUI/Assets/dictionary.seed.json
git commit -m "chore: refresh dictionary seed for release"
```

The script fetches `GET /api/dictionaries/2/resolved` from the configured URL, normalises the JSON, stamps a `seededAt` timestamp, and writes the file. See the script header for full prereqs.

---

## Troubleshooting

**Status row stays "Cached · last synced \<seed build date\>" after submitting a valid token**: check the network. The registration succeeded (the dialog closed) but the immediate post-registration fetch failed. Click Refresh again or inspect `%LOCALAPPDATA%\Stem.ButtonPanelTester\app.log` — the `HttpDictionaryProvider` `Warning` / `Error` line names the failure (4xx, 5xx, timeout, or network failure) with its template parameters.

**`CryptographicException` on launch**: `credential.dpapi` was written under a different user account or was copied between machines. Delete the file (see step 9); the registration dialog will re-appear.

**`dotnet test` says `Avalonia.Headless` fails to load**: ensure you're running on Windows; `Avalonia.Headless` runs cross-platform but the test harness in this repo currently boots a Windows-specific platform service. If you're on WSL, run from the Windows-side terminal.

**Lean `lake build` fails**: ensure you have the toolchain pinned in `lean/lean-toolchain` installed (`elan toolchain install <version>`). The constitution mandates no `sorry`, so a failing proof is a build break, not a warning.

**Refresh appears to hang for up to a minute**: the dictionary service is hosted on Azure App Service Free tier with Always-On off, so the worker unloads after ~30 min of idle traffic. The first refresh after idle triggers cold-boot (max observed ~90 s in PR #91's diagnostics). The status row's "This may take up to a minute if the service has been idle." hint surfaces during the in-flight window; the client-side timeout is 90 s per [`phases/phase-7.md`](phases/phase-7.md). Subsequent refreshes against the warm worker return in <1 s.

# Compliance

**FR-020 (T063, audited 2026-05-20)**: zero raw machine-name / OS-user / machine-identifier / MAC / SID fields cross the dictionary-fetch wire — `GET /api/dictionaries/{id}/resolved` carries only the configured `Dictionary:Id` (URL), the `X-Api-Key` header injected by `ApiKeyAuthHandler`, an `Accept: application/json` header, and a static `Stem.ButtonPanelTester/<assemblyVersion>` `User-Agent` header; no request body. Per-installation identifiers reach STEM only via the registration descriptor (`POST /register`), which transmits the lowercase SHA-256 hex digest of `osUserId` and `machineId` as permitted by FR-020 — raw values do not cross the supplier↔STEM boundary.
