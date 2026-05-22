# Changelog

All notable changes to ButtonPanelTester follow [Semantic Versioning](https://semver.org/) and are recorded here in [Keep a Changelog](https://keepachangelog.com/) format.

## [0.1.0] - 2026-05-22

First tagged release. Ships spec 001 (dictionary fetch + status row + registration ceremony + manual refresh) plus the post-spec hardening that landed before the cut: standards `v1.5.3 → v1.6.0` bump, Stem brand-mark library, and the Re-Register fix for admin-revoked installations.

### Added

- Dictionary fetch with status row, registration ceremony, and manual refresh ([spec 001](specs/001-fetch-dictionary/spec.md)).
- Stem brand-mark library under `src/ButtonPanelTester.GUI/Resources/branding/` (52 SVG/PNG assets across `app-icons/`, `brand-marks/{positive,negative,mono-white}`, `symbols/{positive,negative,mono-white}`), embedded via `<AvaloniaResource>` and reachable at runtime under `avares://ButtonPanelTester.GUI/Resources/branding/...`.
- `<AvaloniaResource>` wiring for `Resources/fonts/*.ttf`. Closes the pre-existing v1.5.0 dead-bytes gap where the Poppins TTFs shipped on disk but were never embedded in the binary.
- `Tests.Windows/Unit/EmbeddedResourceTests.fs` — smoke tests asserting at least one font, brand-mark SVG, and brand-mark PNG are reachable via `avares://`. Catches glob typos in CI.

### Changed

- Bumped STEM standards from v1.5.3 to v1.6.0 ([#88](https://github.com/luca-veronelli-stem/button-panel-tester/issues/88)).
- `.gitattributes` now pins `*.svg` and `*.pdf` as binary (prevents autocrlf line-ending damage on Windows).

### Fixed

- Re-Register against an admin-revoked Installation no longer silently fails ([#98](https://github.com/luca-veronelli-stem/button-panel-tester/issues/98)). The Re-Register flow now wipes `credential.dpapi` and rotates `install.guid` before opening the registration dialog, so the next `POST /register` carries a fresh `installGuid` and the server treats the machine as a clean install. Companion server-side ticket: `stem-dictionaries-manager` [#85](https://github.com/luca-veronelli-stem/stem-dictionaries-manager/issues/85) (distinct status-code mapping for `ExistingInstallationRevoked`).
