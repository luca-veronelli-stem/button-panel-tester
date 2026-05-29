# Changelog

All notable changes to ButtonPanelTester follow [Semantic Versioning](https://semver.org/) and are recorded here in [Keep a Changelog](https://keepachangelog.com/) format.

## [Unreleased]

### Added

- **CAN-link lifecycle** ([spec 002](specs/002-can-link-lifecycle/spec.md)): a persistent CAN status row on the main window reporting the live link state over a four-family FSM (`Initializing | Connected | Disconnected | Error`) — colour-coded chip, human-readable headline, detail tooltip, and a manual **Reconnect** control. Opens the configured PEAK PCAN-USB adapter at 250 kbps and surfaces bench realities: no adapter present, mid-session unplug, driver missing, bus-off, transient PEAK faults. Runs over a vendored `ButtonPanelTester.Infrastructure.Protocol` C# stack (frozen copy of `stem-device-manager`'s CAN + raw-frame layer; documented stopgap per Constitution Principle VI).
- Per-transition structured logging in `CanLinkService`: one `ILogger` entry per `CanLinkState` transition carrying `State` / `Severity` / `Detail` / `Since` fields ([#148](https://github.com/luca-veronelli-stem/button-panel-tester/issues/148)).
- User-friendly PEAK status translation: adapter-busy and bus-off statuses render a jargon-free cause + remediation suggestion instead of the raw PEAK `GetErrorText`; cold-start poll-exhaust is classified as `Disconnected(NoAdapterPresent)` rather than a runtime `Error` ([#150](https://github.com/luca-veronelli-stem/button-panel-tester/issues/150), [#136](https://github.com/luca-veronelli-stem/button-panel-tester/issues/136), [#139](https://github.com/luca-veronelli-stem/button-panel-tester/issues/139)).
- Driver-download link on the missing-PEAK-driver `Error · Fatal` status, pointing at the PEAK downloads page ([#143](https://github.com/luca-veronelli-stem/button-panel-tester/issues/143)).

### Notes

- Mid-session unplug (`Disconnected · adapter unplugged mid-session`, distinct from `no PEAK adapter found`) and implicit hot-plug recovery are pinned by regression and bench tests ([#117](https://github.com/luca-veronelli-stem/button-panel-tester/issues/117), [#132](https://github.com/luca-veronelli-stem/button-panel-tester/issues/132)).

## [0.1.0] - 2026-05-22

First tagged release. Ships spec 001 (dictionary fetch + status row + registration ceremony + manual refresh) plus the post-spec hardening that landed before the cut: standards `v1.5.3 → v1.9.0` bumps, APP_DATA path relocation for the greenfield STEM-wide layout, Stem brand-mark library, and the Re-Register fix for admin-revoked installations.

### Added

- Dictionary fetch with status row, registration ceremony, and manual refresh ([spec 001](specs/001-fetch-dictionary/spec.md)).
- Stem brand-mark library under `src/ButtonPanelTester.GUI/Resources/branding/` (52 SVG/PNG assets across `app-icons/`, `brand-marks/{positive,negative,mono-white}`, `symbols/{positive,negative,mono-white}`), embedded via `<AvaloniaResource>` and reachable at runtime under `avares://ButtonPanelTester.GUI/Resources/branding/...`.
- `<AvaloniaResource>` wiring for `Resources/fonts/*.ttf`. Closes the pre-existing v1.5.0 dead-bytes gap where the Poppins TTFs shipped on disk but were never embedded in the binary.
- `Tests.Windows/Unit/EmbeddedResourceTests.fs` — smoke tests asserting at least one font, brand-mark SVG, and brand-mark PNG are reachable via `avares://`. Catches glob typos in CI.

### Changed

- Bumped STEM standards from v1.5.3 to v1.6.0 ([#88](https://github.com/luca-veronelli-stem/button-panel-tester/issues/88)), then v1.6.0 to v1.9.0 ([#106](https://github.com/luca-veronelli-stem/button-panel-tester/pull/106)).
- `.gitattributes` now pins `*.svg` and `*.pdf` as binary (prevents autocrlf line-ending damage on Windows).
- Per-user app data relocated from `%LOCALAPPDATA%\Stem.ButtonPanelTester\` (flat) to `%LOCALAPPDATA%\Stem\ButtonPanelTester\` per STEM `APP_DATA.md` (v1.9.0), with mandatory `logs/`, `cache/`, `credentials/` sub-folders. NReco rolling log lives under `logs/app.log`; JSON dictionary cache under `cache/dictionary.json` (+ `.sha256` sidecar); DPAPI `credential.dpapi` and the paired `install.guid` under `credentials/`. Path resolution centralised in a new `Stem.ButtonPanelTester.GUI.Composition.StemAppData` module (~15 LOC, `Path.Combine` sugar over `Environment.SpecialFolder.LocalApplicationData`). Greenfield — no transient migration helper; the two existing dev installs lose their throwaway test state on first launch under the new root.

### Fixed

- Re-Register against an admin-revoked Installation no longer silently fails ([#98](https://github.com/luca-veronelli-stem/button-panel-tester/issues/98)). The Re-Register flow now wipes `credential.dpapi` and rotates `install.guid` before opening the registration dialog, so the next `POST /register` carries a fresh `installGuid` and the server treats the machine as a clean install. Companion server-side ticket: `stem-dictionaries-manager` [#85](https://github.com/luca-veronelli-stem/stem-dictionaries-manager/issues/85) (distinct status-code mapping for `ExistingInstallationRevoked`).
