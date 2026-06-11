# Changelog

All notable changes to ButtonPanelTester follow [Semantic Versioning](https://semver.org/) and are recorded here in [Keep a Changelog](https://keepachangelog.com/) format.

## [Unreleased]

### Added

- Living roadmap at [`docs/ROADMAP.md`](docs/ROADMAP.md): shipped state, the committed next spec (004 — baptism workflow), the provisional order through spec-008, the v1.0 definition of done, the debt ledger, and the per-release maintenance protocol. Renumbers the frozen `bpt-rollout` briefing after the [#151](https://github.com/luca-veronelli-stem/button-panel-tester/issues/151) split and folds in the `CORRECTIONS.md` audit where it changes a spec's shape ([#210](https://github.com/luca-veronelli-stem/button-panel-tester/issues/210)).

## [0.3.0] - 2026-06-10

Ships spec 003 (passive CAN panel discovery — the Panels-on-bus list) plus the structured discovery logging that landed with it.

### Added

- **Passive CAN panel discovery** ([spec 003](specs/003-panel-discovery/spec.md)): a **Panels-on-bus list** on the main window that listens for STEM auto-address `WHO_I_AM` broadcasts while the CAN link is Connected and shows one row per panel — the UUID, the decoded variant (marketing name / virgin / unknown, with the raw machine-type byte on a detail tooltip for the latter two), and the last-seen time. Rows coalesce by UUID, prune after 15 s of silence, and clear immediately when the link leaves Connected; an empty-state line distinguishes "link down" from "link up, nothing announcing". The feature is pure observation — it transmits no CAN frame. The receive path starts the vendored read loop on connect and reassembles the segmented multi-frame `WHO_I_AM` SP_APP message before decoding ([#201](https://github.com/luca-veronelli-stem/button-panel-tester/issues/201)).
- Structured domain-event logging in `PanelDiscoveryService`: `Information` when a panel first appears on the bus (UUID + decoded variant), and `Debug` on a coalescing re-broadcast, a TTL prune, and the link-loss clear — so support can trace "the panel isn't showing up" from the log without a debugger. Adds `Trace` drop-axis diagnostics in `WhoIAmReassemblyObserver` (wrong-id / incomplete / wrong-command / wrong-length) for a "why was this frame dropped" trail, off by default. Archetype-A required `ILogger`, templates + named parameters ([#204](https://github.com/luca-veronelli-stem/button-panel-tester/issues/204)).

## [0.2.0] - 2026-06-04

Ships spec 002 (CAN-link lifecycle) plus dictionary-status hardening and the STEM standards `v1.9.0 → v1.15.0` bump that landed since v0.1.0.

### Added

- **CAN-link lifecycle** ([spec 002](specs/002-can-link-lifecycle/spec.md)): a persistent CAN status row on the main window reporting the live link state over a four-family FSM (`Initializing | Connected | Disconnected | Error`) — colour-coded chip, human-readable headline, detail tooltip, and a manual **Reconnect** control. Opens the configured PEAK PCAN-USB adapter at 250 kbps and surfaces bench realities: no adapter present, mid-session unplug, driver missing, bus-off, transient PEAK faults. Runs over a vendored `ButtonPanelTester.Infrastructure.Protocol` C# stack (frozen copy of `stem-device-manager`'s CAN + raw-frame layer; documented stopgap per Constitution Principle VI).
- Per-transition structured logging in `CanLinkService`: one `ILogger` entry per `CanLinkState` transition carrying `State` / `Severity` / `Detail` / `Since` fields ([#148](https://github.com/luca-veronelli-stem/button-panel-tester/issues/148)).
- User-friendly PEAK status translation: adapter-busy and bus-off statuses render a jargon-free cause + remediation suggestion instead of the raw PEAK `GetErrorText`; cold-start poll-exhaust is classified as `Disconnected(NoAdapterPresent)` rather than a runtime `Error` ([#150](https://github.com/luca-veronelli-stem/button-panel-tester/issues/150), [#136](https://github.com/luca-veronelli-stem/button-panel-tester/issues/136), [#139](https://github.com/luca-veronelli-stem/button-panel-tester/issues/139)).
- Driver-download link on the missing-PEAK-driver `Error · Fatal` status, pointing at the PEAK downloads page ([#143](https://github.com/luca-veronelli-stem/button-panel-tester/issues/143)).

### Changed

- Bumped STEM standards from v1.9.0 to v1.15.0.

### Fixed

- Dictionary status row no longer loses its last-confirmed-live timestamp across restart when a refresh returns byte-identical content ([#191](https://github.com/luca-veronelli-stem/button-panel-tester/issues/191)).
- A catastrophic dictionary-init failure now surfaces as a terminal status row instead of leaving the row without a signal in the situation that most needs one ([#179](https://github.com/luca-veronelli-stem/button-panel-tester/issues/179)).

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
