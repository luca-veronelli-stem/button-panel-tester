# Changelog

All notable changes to ButtonPanelTester follow [Semantic Versioning](https://semver.org/) and are recorded here in [Keep a Changelog](https://keepachangelog.com/) format.

## [Unreleased]

### Added

- **Button-press test (input side)** ([spec 005](specs/005-button-press-test/spec.md)) — the tool's **first input-side test**: prompt a technician through each active button on a baptized panel, observe the CAN `VAR_WRITE` button-state frame, and score each prompt **Pass / Missed / Unexpected / Skipped** with a per-button grid and an "all active passed" aggregate. The session is a pure, Lean-formalized FSM (Phase 4, no `sorry`) driven **receive-only** over the existing CAN observation seam: a per-variant button schema (OPTIMUS-XP authoritative, the other three provisional), a per-button 10 s countdown, press-edge detection (pressed = bit `0`; a press is the `1 → 0` edge, firmware-pinned), Retry / Skip / Re-run recovery, and link-loss / panel-loss surfaced as distinct interruptions that never report all-passed. The test is unavailable, with an explanation, unless a baptized panel is selected on a Connected link. CI greens without hardware; the live OPTIMUS-XP bench run is the done gate ([#255](https://github.com/luca-veronelli-stem/button-panel-tester/issues/255), per-phase children A–G; bench validation tracked at [#253](https://github.com/luca-veronelli-stem/button-panel-tester/issues/253)).

### Fixed

- Re-key button-press observability to the **button-state heartbeat (directed CAN ID)**, not WHO_I_AM discovery — fixes the bench-surfaced defect that a baptized panel, silent on WHO_I_AM (`AAS_STAND_BY`), was invisible to the test. The observer now accepts a frame iff its directed CAN ID's machineType (bits 23–16) decodes to a known `Marketing` variant (dropping the broadcast id and the tool SRID), emits the variant alongside the frame, and reassembles per source CAN ID; the service/GUI key observability, variant, and panel-loss off button-state-frame recency (bench-tunable thresholds), auto-targeting the single heartbeating panel and dropping the `IPanelDiscoveryService` dependency from the button-press path ([#270](https://github.com/luca-veronelli-stem/button-panel-tester/issues/270)).
- Recalibrate the button-press recency thresholds to the panel's **dual-rate heartbeat** and score the **first press after power-up**. The firmware refreshes button-state at ~188 ms only while its latched bitmap is non-zero; a cold, never-touched panel refreshes at ~12.5 s (`TEMPO_CAN_LENTO`) — the earlier "~182 ms idle" reading was the post-boot fast ramp. `observableWindow`/`panelLostThreshold` go 2 s/3 s → **15 s/20 s** (firmware-derived, above the slow branch), and the hardware suite's first-heartbeat wait follows. Because a cold panel's bitmap boots all-zero and bits latch, a button's **first press is never transmitted**; the press-edge detector now arms per position (Lean-proven, Phase 4) and scores an unarmed position on its release transition, so the first press of every button scores instead of reading `Missed` ([#293](https://github.com/luca-veronelli-stem/button-panel-tester/issues/293)).

## [0.4.0] - 2026-06-19

Ships spec 004 — the **baptism workflow** (the tool's first CAN-transmit feature: claim a virgin panel as one of four BoardVariants, or reset a claimed panel to virgin, via the three-step auto-address master sequence) — plus the v0.4.0 ride-along fixes: configurable log levels, a quieter WHO_I_AM reassembly trace, a claim-write race hardening, and dark-theme selection contrast.

### Added

- **Baptism workflow** ([spec 004](specs/004-baptism-workflow/spec.md)) — the tool's **first CAN-transmit feature**: from the Panels-on-bus list, claim the single virgin panel on the bus as one of four BoardVariants (EDEN-XP, OPTIMUS-XP, R-3L XP, EDEN-BS8) via the three-step auto-address master sequence (`WHO_ARE_YOU(reset=1)` → `WHO_I_AM` capture → `SET_ADDRESS`), or reset a claimed panel back to virgin. A baptize is reported **Succeeded only on confirmed adoption** — the panel acknowledges the address assignment (the `0x25` ACK) **and** is confirmed silent on the broadcast — never on bare write-completion (the F1/F6 confirmation-model rework); a claim that does not take surfaces the deterministic `ClaimNotAdopted` outcome and guides the operator into the Reset → re-baptize recovery. Baptize/Reset are disabled with an explanation unless exactly one panel announces, and the tool remembers nothing about panels it baptized. The state machine is Lean-formalized (Phase 3, no `sorry`) and the TX path sits behind a Core port with a virtual adapter, so CI greens without hardware ([#212](https://github.com/luca-veronelli-stem/button-panel-tester/issues/212): children [#213](https://github.com/luca-veronelli-stem/button-panel-tester/issues/213)–[#219](https://github.com/luca-veronelli-stem/button-panel-tester/issues/219), [#232](https://github.com/luca-veronelli-stem/button-panel-tester/issues/232)).
- Living roadmap at [`docs/ROADMAP.md`](docs/ROADMAP.md): shipped state, the committed next spec (004 — baptism workflow), the provisional order through spec-008, the v1.0 definition of done, the debt ledger, and the per-release maintenance protocol. Renumbers the frozen `bpt-rollout` briefing after the [#151](https://github.com/luca-veronelli-stem/button-panel-tester/issues/151) split and folds in the `CORRECTIONS.md` audit where it changes a spec's shape ([#210](https://github.com/luca-veronelli-stem/button-panel-tester/issues/210)).

### Changed

- `WhoIAmReassemblyObserver` no longer emits a per-fragment `Trace` line for the normal `reason=incomplete` buffering of a progressing WHO_I_AM reassembly — that path is now silent. It was ~4 lines per WHO_I_AM (every ~4 s per panel) whose `reason=incomplete` read like a drop and buried the genuine drop-axis diagnostics. The real drop traces (wrong-id / wrong-command / wrong-length) and the [#204](https://github.com/luca-veronelli-stem/button-panel-tester/issues/204) `Info`/`Debug` discovery logs are unchanged; reassembly/parse/observe behaviour is untouched ([#208](https://github.com/luca-veronelli-stem/button-panel-tester/issues/208)).

### Fixed

- Log levels are now bound from configuration: the `AddLogging` builder reads the `Logging` config section and `Program.fs` adds environment-variable configuration, so operators can raise verbosity (e.g. the #204 discovery `Debug`/`Trace` diagnostics) per deployment via `appsettings.json` `Logging:LogLevel` keys or `Logging__LogLevel__*` env vars without a rebuild. Quiet-by-default is unchanged (no `Logging` section → `Information`, `Microsoft`/`System.Net.Http` `Warning`) ([#207](https://github.com/luca-veronelli-stem/button-panel-tester/issues/207)).
- Closed a residual TOCTOU window in `BaptismService.BaptizeAsync`: a CAN link-down landing between the entry guard and the out-of-lock claim write could leak one stray `WHO_ARE_YOU` frame (the returned outcome was already the correct `LinkLost`). The claim write is now gated on an under-lock fire-time re-validation, so a dropped link transmits nothing; the lock is still never held across the send ([#231](https://github.com/luca-veronelli-stem/button-panel-tester/issues/231)).
- Dark-theme legibility: the selected panel row (Panels-on-bus) and selected baptism variant/button now use a theme-aware `BluStem` selection brush meeting WCAG AA in both light and dark themes, replacing a hardcoded `LightBlue` highlight that washed out under dark-theme white text ([#235](https://github.com/luca-veronelli-stem/button-panel-tester/issues/235)).

### Notes

- **Known limitation — SC-004 (rapid four-variant re-baptize cycle) is firmware-limited, not a tool defect.** Single claim, reset, and operator-paced re-typing across all four variants are bench-validated on real silicon (OPTIMUS-XP panel); the confirmed-adoption model correctly catches a half-baptized panel. Under *rapid* automated claim→reset→claim cycling, today's panel firmware (`pac5524-tastiera`) intermittently drops commands / confirms adoption late — the tool transmits and detects correctly throughout. The automated `FullCycle_FourVariants_ZeroResidualState` hardware E2E (SC-004, strict 4/4) validates once the firmware fix lands; tracked at [#237](https://github.com/luca-veronelli-stem/button-panel-tester/issues/237). See [`specs/004-baptism-workflow/spec.md`](specs/004-baptism-workflow/spec.md) SC-004.

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
