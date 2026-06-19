# Standard: MIGRATION

> **Stability:** v1.0.0
> **Goal:** moving an existing repo to a new Standard version is a tracked, gradual process — not a big-bang rewrite.

## Where adoption is tracked

- **`<standards>/state/repos.md`** — single source of truth for which repo is on which Standard version.
- **`<repo>/CLAUDE.md`** — per-repo declaration of `**Standard version:**` and `**Archetype:**`.

When the two disagree, `state/repos.md` is canonical for "what should be"; the per-repo `CLAUDE.md` is canonical for "what is right now".

## Rollout phases for v1.0.0

The standards landed in v1.0.0 — the *standards-definition* design session. The rollout order, by repo:

| Phase | Repo | Why this order |
| --- | --- | --- |
| 1 | `llm-settings` itself (now `standards`) | Define the standards (this PR). Self-referential — archetype C. |
| 2 | `stem-device-manager` | Most active repo; already has the most CI/standards scaffolding. Lowest delta. |
| 3 | `stem-communication` | Library archetype; rename + sub-package split happens here. Validates archetype B. |
| 4 | `stem-button-panel-tester` | Small, low-risk archetype A. Pilot for the rollout script. |
| 5 | `spark-log-analyzer` | Analyzer tool, archetype A. |
| 6 | `stem-production-tracker` | Larger archetype A; benefits from the script being battle-tested. |
| 7 | `stem-dictionaries-manager` | Archetype A with a class library inside. Last because it's the least typical. |

Each phase opens a single PR per repo titled `chore: adopt v1.0.0 standards`.

## Rollout phase for v1.2.0 — docs standards

`v1.2.0` adds eight content standards (`EVENTARGS`, `VISIBILITY`, `LOGGING`, `THREAD_SAFETY`, `CANCELLATION`, `COMMENTS`, `ERROR_HANDLING`, `CONFIGURATION`) and three doc templates (`STANDARD_TEMPLATE.md`, `README_TEMPLATE.md`, `API_SURFACE.md`). It is a minor bump — non-breaking — so adoption is opt-in per repo and can happen in any order.

Per-repo adoption PR (`chore: bump standards to v1.2.0`):

1. **Before running the script:** run a one-time export of any open `<Component>/ISSUES.md` and root-level `ISSUES_TRACKER.md` entries to GitHub Issues with appropriate labels (`feat`/`fix`/`chore`/…). The rollout deletes those files; their content has to land in the tracker first or it's lost.
2. **Salvage non-derivable content from per-component `README.md` files** *before* deleting them. The rollout script does **not** regenerate per-component READMEs (its ownership ends at the top-level `README.md` and `CLAUDE.md`), and adopters routinely delete the pre-v1 ones rather than rewrite them in place — see `stem-button-panel-tester` commit `f0e69f0` (2026-05-05) and the `feat/legacy-docs-snapshot` branch in this repo. Before the deletion, scan each `<Component>/README.md` for content that doesn't live anywhere else in the repo:
   - cross-system context (consumer matrices, deploy runbooks);
   - domain semantics not present in code (state machines, business rules referenced as `BR-*` or similar);
   - external system references (third-party APIs, hardware addresses, magic numbers).

   Move that content to a better home: XML doc on the relevant types, a top-level `docs/Domain.md` or `docs/Deploy.md`, or `lean/` for invariants that warrant Lean formalization. *Then* delete; the snapshot branch is the rollback. Adding a fresh `README.md` from `shared/templates/docs/README_TEMPLATE.md` is opt-in per component — only do it where the component actually earns its keep (a stub README with no non-derivable content is worse than no README).
3. Re-run `eng/apply-repo-standard.ps1 -StandardVersion v1.2.0`. The script regenerates `docs/Standards/` with the new content standards alongside the v1.0 ones and removes the on-disk `ISSUES.md` / `ISSUES_TRACKER.md` files (their content is now in the GitHub tracker). Per-component `README.md` files are not touched — keep, delete, or regenerate by hand per the previous step.
4. Bump the per-repo `CLAUDE.md` `**Standard version:**` line to `v1.2.0`.
5. Update `state/repos.md` to reflect the bump.
6. Single-commit PR.

## Rollout phase for v1.8.0 — single-`.exe` archetype A artifact

`v1.8.0` adds `-p:IncludeNativeLibrariesForSelfExtract=true` to the archetype A reusable release workflow's `dotnet publish` invocation. After this bump, the artifact attached to the GitHub Release for an archetype A adopter is a single self-extracting `.exe` (no sibling `runtimes/win-x64/native/` directory) — draggable, USB-launchable, robust to user-driven moves between folders. Backward-compatible at the caller-stub surface: every input on the reusable (`app`, `repo`, `tag`) keeps the same shape, so a v1.7.x adopter does not need to edit its stub. The only observable change is the shape inside the published zip: one `.exe`, no native sibling DLLs. Closes [#106](https://github.com/luca-veronelli-stem/standards/issues/106).

Per-repo adoption PR (`chore: bump standards to v1.8.0`):

1. Re-run `eng/apply-repo-standard.ps1 -StandardVersion v1.8.0`. The diff is the single `@v1.7.x → @v1.8.0` pin bump in `.github/workflows/release.yml` (the archetype A release stub) — same shape v1.4.0+ adopters see on every patch bump. Hand-customised release workflows hit the local-edit guard and need `-Force` or hand-merge per the v1.4.0 pitfalls.
2. Bump the per-repo `CLAUDE.md` `**Standard version:**` line to `v1.8.0`.
3. Update `state/repos.md` to reflect the bump.
4. Single-commit PR.

No source-code action required. On the adopter's next release tag (`v*.*.*`), the next published artifact's shape changes — flag a one-line note in the adopter's CHANGELOG so anyone debugging a launch on a customer site can correlate.

First-launch extraction note: the bundle extracts native deps to `%LOCALAPPDATA%\.net\<App>\<content-hash>\` on first launch per user per release; subsequent launches reuse the extracted copy. For hardened customer environments where the default path is blocked (EDR quarantine of fresh `.dll` materialization, read-only `%LOCALAPPDATA%`), the escape hatch is the `DOTNET_BUNDLE_EXTRACT_BASE_DIR` environment variable — set it to an app-writable path before launch and the bundle extracts there instead (documented in CI.md alongside the flag).

## Rollout phase for v1.9.0 — `APP_DATA` standard

`v1.9.0` adds [`shared/standards/APP_DATA.md`](./APP_DATA.md), codifying `<LocalApplicationData>\Stem\<AppName>\` as the per-user on-disk root for every STEM desktop app's logs, caches, DPAPI credentials, SQLite databases, and any future per-user configuration overrides. Replaces four divergent conventions surveyed across the stack on 2026-05-22:

| Legacy convention | Repos | Used for |
| --- | --- | --- |
| `AppContext.BaseDirectory\logs\` | `stem-device-manager` (pre-v0.4.3) | logs — broken under Program Files / single-file publish (root cause of v0.4.1 silent-Excel-fallback) |
| `%LocalAppData%\Stem.ButtonPanel.Tester\` (dotted, flat) | `stem-button-panel-tester-fsharp-core` | DPAPI credentials + JSON dictionary cache |
| `%LocalAppData%\Stem.ButtonPanelTester\` (single-segment, flat) | `button-panel-tester` (greenfield) | NReco rolling `app.log` + dictionary cache |
| `%AppData%\STEM\<AppName>\` (Roaming, two-segment, ALL-CAPS) | `stem-dictionaries-manager`, `stem-production-tracker` | SQLite databases |

All four collapse to a single shape: `<LocalApplicationData>\Stem\<AppName>\` (PascalCase `Stem`, single-token PascalCase `<AppName>`, no `Stem.` prefix on the inner segment), with mandatory `logs\`/`cache\`/`credentials\`/`db\` sub-folders once a second data type lands. The convention is cross-platform — `Environment.SpecialFolder.LocalApplicationData` resolves to the OS-appropriate root and the segment names are platform-agnostic.

The standard ships a small inline path-resolution helper (`StemAppData`, ~15 LOC) that's permanent in each adopter, and a separate transient migration helper (`StemAppDataMigration` + a `.appdata-version` schema marker file) that an adopter with an existing installed base wires in on the v1.9.0-aligned release and **deletes one or two release cycles later** once the installed base has rolled over. The marker file left on disk after deletion is inert; no future code reads it. The companion future-work ticket [#110](https://github.com/luca-veronelli-stem/standards/issues/110) tracks extracting the path-resolution helper into a `Stem.AppData` NuGet once a second consumer earns its keep — until then, copy-paste is fine.

`LOGGING.md` gains a "Where logs land on disk" subsection cross-referencing `APP_DATA.md`. `CONFIGURATION.md` gains a one-line callout that `appsettings.Production.json` location (next to exe vs `Stem\<App>\`) is a deferred decision. No other standards change. No template changes, no rollout-script behaviour changes beyond the `$standardPurpose` registry entry for `APP_DATA` — adding a standard is the one-file change codified in v1.5.1 ([#71](https://github.com/luca-veronelli-stem/standards/issues/71)).

Per-repo adoption PR (`chore: bump standards to v1.9.0` — separate from the source code change that actually relocates paths):

1. Re-run `eng/apply-repo-standard.ps1 -StandardVersion v1.9.0`. The diff is the new `docs/Standards/APP_DATA.md` inline copy, refreshed `LOGGING.md` + `CONFIGURATION.md` + `docs/Standards/README.md` index, and the standard-version stamp in `CLAUDE.md` / top-level `README.md`. No template or workflow churn.
2. Bump the per-repo `CLAUDE.md` `**Standard version:**` line to `v1.9.0`.
3. Update `state/repos.md` to reflect the bump.
4. Single-commit PR.

**Per-repo path-relocation work** (a separate PR each adopter cuts when it actually moves the source code):

1. Add `StemAppData` (~15 LOC, forever) to the composition root. Point every write site at `StemAppData.GetLogsDir()` / `GetCacheDir()` / `GetCredentialsDir()` / `GetDbDir()`.
2. If the adopter has an installed base on a legacy root, **also** add `StemAppDataMigration` (transient) and wire `MigrateOnce(legacyRoot)` into the composition root before any logger or cache opens a file. The legacy root depends on the adopter:
   - `stem-device-manager` — `Path.Combine(AppContext.BaseDirectory, "logs")`
   - `stem-button-panel-tester-fsharp-core` — `Path.Combine(localAppData, "Stem.ButtonPanel.Tester")`
   - `button-panel-tester` — `Path.Combine(localAppData, "Stem.ButtonPanelTester")`
   - `stem-dictionaries-manager`, `stem-production-tracker` — `Path.Combine(appData /* Roaming */, "STEM", appName)`
3. Greenfield apps (no installed base) skip step 2 entirely — there is nothing to migrate.
4. After one or two release cycles on the v1.9.0-aligned release, delete `StemAppDataMigration.cs` and its call site. `StemAppData` stays.

`stem-device-manager` v0.4.3 (worktree at `C:\Users\LucaV\Source\Repos\stem-device-manager-v0.4.3`, branch `fix/0.4.3-diagnostics`, paused awaiting this standard) is the first reference adopter. Adoption may bundle the standards bump and the path-relocation work into one PR or split them — that's a v0.4.3-session decision, not a v1.9.0-PR decision.

## Rollout phase for v1.16.0 — `CLIENT_REGISTRATION` standard

`v1.16.0` adds [`shared/standards/CLIENT_REGISTRATION.md`](./CLIENT_REGISTRATION.md), a Content standard codifying the bootstrap-token credential-provisioning contract: an unauthenticated `POST /register` exchanges a technician-supplied bootstrap token for a per-installation credential; the credential is stored encrypted-at-rest behind an `ICredentialStore` port (DPAPI `CurrentUser` on Windows) and replayed as an `X-Api-Key` header by a `DelegatingHandler`; machine/user identifiers are hashed (lowercase SHA-256 hex) before crossing an organizational boundary; and registration failure is a closed, `Result`-typed `RegistrationError` taxonomy (`400 DescriptorRejected | 401 TokenInvalid | 409 TokenAlreadyUsed | 410 TokenExpired | 423 TokenRevoked | 5xx ServerError | network/timeout`) that is surfaced, never thrown for expected failures, never swallowed. `button-panel-tester` (`specs/001-fetch-dictionary`) is the reference implementation the standard captures; [stem-device-manager#94](https://github.com/luca-veronelli-stem/stem-device-manager/issues/94) (a gitignored-`appsettings.Production.json` stopgap) and the planned `telemetry-manager` CLI host can cite it instead of re-deriving the contract. Closes [#138](https://github.com/luca-veronelli-stem/standards/issues/138).

It is a **minor** bump — a new standard is additive (per "Choosing the bump level", `v1.5.0`/`v1.9.0` set the precedent), nothing previously compliant becomes non-compliant on re-roll, and adding a standard is the one-file registry change codified in `v1.5.1` ([#71](https://github.com/luca-veronelli-stem/standards/issues/71)): the `$standardPurpose` entry in `eng/apply-repo-standard.ps1` plus the `.md` file, the README counts/table/F#-coverage rows, and this section. No template or rollout-script behaviour change beyond the registry entry; the dynamic standards-count assertion in `eng/tests/Apply-RepoStandard.Tests.ps1` derives its expected value from `shared/standards/*.md`, so it absorbs the new file without edit.

**Scope note:** the standard covers registration + credential storage + the authenticated-call handler only. The observable seed→cache→live *resource* fallback (degraded-mode-as-visible-state) that `button-panel-tester` pairs with registration is an adjacent concern with a single consumer so far, deliberately left out — it has not yet cleared the cross-repo bar and would get its own standard when a second consumer earns it.

Per-repo adoption PR (`chore: bump standards to v1.16.0`):

1. Re-run `eng/apply-repo-standard.ps1 -StandardVersion v1.16.0`. The diff is the new `docs/Standards/CLIENT_REGISTRATION.md` inline copy, the refreshed `docs/Standards/README.md` index, and the version stamps the rollout refreshes (`CLAUDE.md`'s `**Standard version:**` line, the top-level `README.md`). No template, workflow, or source-code churn.
2. Bump the per-repo `CLAUDE.md` `**Standard version:**` line to `v1.16.0`.
3. Update `state/repos.md` to reflect the bump.
4. Single-commit PR.

No source-code action required at adoption time — the standard documents a contract, it does not introduce an analyzer or a banned symbol. `button-panel-tester` already implements the contract end-to-end, so its bump is a pure documentation pin; a new consumer (`telemetry-manager`, `stem-device-manager` #94) implements the three adapters per the standard and cites it, rather than re-deriving the wire shape, error taxonomy, privacy posture, and storage format.

## Rollout phase for v1.16.1 — root README "Creation Date" full date

`v1.16.1` fixes the root `README.md` footer's **Creation Date** field, which rendered only a year because the rollout captured `(Get-Date).Year` at bootstrap. [`shared/templates/README.md.template`](../templates/README.md.template) now carries a `2026` placeholder; `eng/apply-repo-standard.ps1` captures the full bootstrap date (`yyyy-MM-dd`) into a new `creationDate` field in `.stem-standard.json` and persists it, so a re-roll preserves the original date instead of re-stamping the current one. A legacy config that predates the field (carries only `year`, no `creationDate`) keeps rendering the **year** — the placeholder coalesces to `$cfg.year` when `creationDate` is absent, with no fabricated month/day (option **c**). Patch bump — a rendering bug fix with no contract change, same shape as `v1.15.1`'s `README.md.template` hard-break fix; nothing previously compliant changes on re-roll. There is no urgency to roll this out on its own — it can ride with the next substantive bump. Closes [#141](https://github.com/luca-veronelli-stem/standards/issues/141).

Per-repo adoption PR (`chore: bump standards to v1.16.1`):

1. Re-run `eng/apply-repo-standard.ps1 -StandardVersion v1.16.1`. For a repo whose `.stem-standard.json` already carries a `creationDate` (bootstrapped after this release), the diff is the re-rendered top-level `README.md` footer (full `YYYY-MM-DD` Creation Date — version-stamped templates always iterate, see #87) plus the version stamps the rollout refreshes. An older adopter whose config predates the field sees the footer **year** unchanged (the option-c coalesce), so for them the only footer effect is the version stamp. No workflow or source-code change.
2. Bump the per-repo `CLAUDE.md` `**Standard version:**` line to `v1.16.1`.
3. Update `state/repos.md` to reflect the bump.
4. Single-commit PR.

No source-code action required at adoption time — the change is a template rendering fix.

## Rollout phase for v1.10.0 — `category-filter` input on the reusable `dotnet-ci.yml`

`v1.10.0` adds a `category-filter` input on `on.workflow_call.inputs` in `.github/workflows/dotnet-ci.yml`, threaded through both `dotnet test` invocations (Linux per-project loop + Windows full leg), with a default of `Category!=Hardware`. Closes [#113](https://github.com/luca-veronelli-stem/standards/issues/113).

The motivation is local-vs-CI gate drift: the local pre-push gate (per `workflow` rule + `CI.md`) already filters out xUnit `[<Trait("Category", "Hardware")>]` tests via `--filter "Category!=Hardware"`, but the reusable CI workflow ran `dotnet test` with no filter, so hardware tests executed on hosted runners and failed for lack of the device. The first downstream incident was [`button-panel-tester` PR #122](https://github.com/luca-veronelli-stem/button-panel-tester/pull/122) (`T043`), which shipped a short-term `[<Fact(Skip = "...#112")>]` workaround — fragile because `Skip` overrides the filter even on a developer's bench where the hardware is plugged in.

Backward-compatible at both surfaces:
- Existing test suites with no `Category` trait match the negation and stay green.
- Adopter caller stubs need **no** edit — the empty `with:` block still inherits the default filter once the `@vX.Y.Z` pin is bumped.

Per-repo adoption PR (`chore: bump standards to v1.10.0`):

1. Re-run `eng/apply-repo-standard.ps1 -StandardVersion v1.10.0`. The diff is the single `@v1.9.x → @v1.10.0` pin bump in `.github/workflows/ci.yml` (the CI caller stub). No other workflow churn; no standard-content churn beyond the `CI.md` "Hardware-test exclusion" section already inlined under `docs/Standards/`.
2. Bump the per-repo `CLAUDE.md` `**Standard version:**` line to `v1.10.0`.
3. Update `state/repos.md` to reflect the bump.
4. Single-commit PR.

No source-code action required at adoption time. Adopters that already have a `Skip = "...#NNN"` workaround on hardware-traited tests should remove it in a follow-up PR — the trait filter now covers exclusion on CI, and `Skip` would defeat the trait filter on developer benches. `button-panel-tester` `PcanLifecycleTests.fs` (the two `[<Fact>]`s touched in `76cacef`) is the first such cleanup.

Adopters running their own self-hosted hardware-equipped runner override the input from their caller stub (`with: category-filter: ""` to include hardware tests, or a custom filter for a dedicated hardware job — example in `CI.md` -> "Hardware-test exclusion").

## Rollout phase for v1.11.0 — Dependabot grouping split + minor/patch restriction

`v1.11.0` reshapes `shared/templates/.github/dependabot.yml`: the single `avalonia` group splits into `avalonia-runtime` (`Avalonia` + `Avalonia.*`, excluding `Avalonia.FuncUI*`) and `avalonia-funcui` (`Avalonia.FuncUI*`), and every NuGet group (`avalonia-runtime`, `avalonia-funcui`, `testing`, `microsoft-extensions`) gains `update-types: [minor, patch]`. Closes [#114](https://github.com/luca-veronelli-stem/standards/issues/114).

The motivation is grouped-PR risk bundling. With one `Avalonia*` group and no `update-types` filter, Dependabot could pack a safe patch and a breaking major into one PR — when [`button-panel-tester` PR #123](https://github.com/luca-veronelli-stem/button-panel-tester/pull/123) (2026-05-25) bundled FuncUI `1.5.1 → 1.6.0`, Avalonia `11.3.7 → 12.0.3`, and `Avalonia.Diagnostics` patches, the whole PR turned red on FuncUI/Avalonia incompatibility and the patches that could have merged alone were blocked. Splitting FuncUI out (it has its own cadence and its own Avalonia compat dependency) and capping each group at minor/patch keeps majors as standalone, separately-reviewable PRs.

Per-repo adoption PR (`chore: bump standards to v1.11.0`):

1. Re-run `eng/apply-repo-standard.ps1 -StandardVersion v1.11.0`. The diff is the regenerated `.github/dependabot.yml` (the rollout overwrites it from the template). No source-code change, no other template churn.
2. Bump the per-repo `CLAUDE.md` `**Standard version:**` line to `v1.11.0`.
3. Update `state/repos.md` to reflect the bump.
4. Single-commit PR.

No source-code action required at adoption time. The first observable effect lands on the repo's next weekly Dependabot run: a major Avalonia (or any other group member's major) arrives as its own PR instead of poisoning a grouped bundle, and FuncUI bumps arrive separately from Avalonia-runtime bumps. To decline a specific major, comment `@dependabot ignore this major version` on the standalone PR (a per-repo decision — the template intentionally ships no `ignore` entries). Adopters whose `.github/dependabot.yml` has been hand-customised hit the local-edit guard and need `-Force` or a hand-merge (per the Pitfalls section).

## Rollout phase for v1.15.1 — runner-image policy (float on `*-latest`) + README hard-break fix

`v1.15.1` adds a **Runner-image policy (float on `*-latest`)** section to [`CI.md`](./CI.md), deciding — ahead of GitHub's `windows-latest` → `windows-2025-vs2026` redirect (June 15, 2026) — that the action tag-pinning determinism posture does **not** extend to runner images: every `runs-on:`/matrix value stays on the floating aliases. Pinning was rejected because a dated image is not a frozen toolchain (GitHub rolls its software weekly under the same label), the toolchain that matters is already pinned (`global.json` SDK, tag-pinned actions), and pinning renames the required status-check contexts (`build (windows-latest)`) — a branch-protection update in every adopted repo, repeated at every dated-image retirement. Refs [#134](https://github.com/luca-veronelli-stem/standards/issues/134). It also releases the `README.md.template` hard-break fix (PR [#130](https://github.com/luca-veronelli-stem/standards/pull/130)) that had been sitting on `main` unreleased. Patch bump — the policy section documents the existing posture as deliberate (a clarification, same shape as `v1.14.2`'s doc-only CI.md additions) and the template change is a rendering bug fix; nothing previously compliant changes on re-roll, and CI's stability marker stays at `v1.0.0`. There is no urgency to roll this out on its own — it can ride with the next substantive bump.

Per-repo adoption PR (`chore: bump standards to v1.15.1`):

1. Re-run `eng/apply-repo-standard.ps1 -StandardVersion v1.15.1`. The diff is the refreshed `docs/Standards/CI.md` inline copy, the re-rendered top-level `README.md` (version-stamped templates always iterate — see #87 — so it picks up the hard-break fix after `Bench tool to test STEM button-panel hardware over CAN`), plus the version stamps the rollout refreshes. No workflow or rollout-script change.
2. Bump the per-repo `CLAUDE.md` `**Standard version:**` line to `v1.15.1`.
3. Update `state/repos.md` to reflect the bump.
4. Single-commit PR.

No action is required for the June 15 redirect itself: floating means adopted repos ride the alias flip automatically, branch-protection contexts (`build (windows-latest)`) keep their names, and the stub's weekly scheduled CI run is the breakage detector. If a repo's first post-redirect run goes red, compare the `Set up job` log's `Runner Image` lines between the last green and the first red run (per the CI.md section) before suspecting repo-side changes.

## Rollout phase for v1.15.0 — unattended-only test suites

`v1.15.0` adds an **Unattended-only test suites** principle to [`TESTING.md`](./TESTING.md): the `tests/` project holds only tests that run to completion with no human intervention. A human-in-the-loop test (press a button, unplug a cable, observe a screen) must not sit in the suite as a `Skip`-by-default case — resolve it by automating the human away with a fixture, demoting it to a runbook manual step, or (the one exception) keeping it as an **attended, env-gated** `[<ManualHardwareFact>]` that is dormant in unattended runs yet runnable on demand with no source edit. The `[<HardwareFact>]` / `[<ManualHardwareFact>]` env-gate attributes are the reference implementation, originating in [`button-panel-tester#142`](https://github.com/luca-veronelli-stem/button-panel-tester/issues/142). Closes [#126](https://github.com/luca-veronelli-stem/standards/issues/126). It is a minor bump — additive guidance to an existing standard, nothing previously compliant becomes non-compliant on re-roll (standards docs are advisory, not analyzer-enforced — see "Choosing the bump level") — so adoption is opt-in per repo and can happen in any order. TESTING's stability marker stays at `v1.0.0`.

Per-repo adoption PR (`chore: bump standards to v1.15.0`):

1. Re-run `eng/apply-repo-standard.ps1 -StandardVersion v1.15.0`. The diff is the refreshed `docs/Standards/TESTING.md` inline copy plus the version stamps the rollout refreshes (`docs/Standards/README.md` index, `CLAUDE.md`'s `**Standard version:**` line, the top-level `README.md`). No template, workflow, or rollout-script change; the standards count is unchanged (TESTING already exists), so the dynamic count assertion in `eng/tests/Apply-RepoStandard.Tests.ps1` is unaffected.
2. Bump the per-repo `CLAUDE.md` `**Standard version:**` line to `v1.15.0`.
3. Update `state/repos.md` to reflect the bump.
4. Single-commit PR.

No source-code action required at adoption time. Adopters carrying a `[<Fact(Skip = "Manual …")>]` human-in-the-loop test should resolve it per the new principle in a follow-up PR — same shape as the v1.10.0 note about removing `Skip = "...#NNN"` hardware workarounds. `button-panel-tester` is the first such cleanup: the `PhysicalUnplug…` deletion (its logic already covered by a fake-driven unit test) plus the `PhysicalReplug…` re-gate to `[<ManualHardwareFact>]` that motivated this principle (see [#142](https://github.com/luca-veronelli-stem/button-panel-tester/issues/142)).

## Rollout phase for v1.14.2 — cache-restore resilience

`v1.14.2` fixes the reusable `dotnet-ci.yml` so a transient `actions/cache` restore flake can no longer skip Restore/Build/Test and red `main`: both `actions/cache@v5` steps gain `continue-on-error: true`, and the `Test report` step keys off the test step's own `outcome` rather than the mere presence of a `.trx` (see CI.md → "Cache restore is non-fatal" and "Test reporting", and [#123](https://github.com/luca-veronelli-stem/standards/issues/123)). The fix lives entirely in the reusable body, so there is no source-code or stub-shape change — a patch that restores intended behaviour.

Per-repo adoption PR (`chore: bump standards to v1.14.2`):

1. Re-run `eng/apply-repo-standard.ps1 -StandardVersion v1.14.2`. The only diff is the `uses: …/dotnet-ci.yml@…` pin in the `.github/workflows/ci.yml` stub bumping to `@v1.14.2`. No source-code change. A hand-customised `ci.yml` stub hits the local-edit guard and needs `-Force` or a hand-merge (per the Pitfalls section).
2. Bump the per-repo `CLAUDE.md` `**Standard version:**` line to `v1.14.2`.
3. Update `state/repos.md` to reflect the bump.
4. Single-commit PR.

The fix is silent in steady state — it only changes behaviour the next time the runner's cache service hiccups, where the pre-fix workflow would have skipped Build/Test and failed `dorny/test-reporter` with "No test report files were found". Consumer side tracked by [`button-panel-tester#162`](https://github.com/luca-veronelli-stem/button-panel-tester/issues/162).

## Rollout phase for v1.14.0 — mirror-bitbucket tag mirroring

`v1.14.0` fixes the reusable `mirror-bitbucket.yml` so version tags reach the Bitbucket mirror. Pre-fix it triggered only on `main` branch pushes and pushed a single explicit refspec (`git push bitbucket HEAD:refs/heads/main`), which carries no tags — release/version tags never reached the mirror. The caller stub gains `on.push.tags: ['v*.*.*']`, and the reusable body branches on `github.ref_type`: a `main` push runs `git push --follow-tags bitbucket HEAD:refs/heads/main` (commit + reachable annotated tags), and a tag push runs `git push bitbucket "$REF:$REF"` (only the pushed tag, never touching `bitbucket/main`). Closes [#122](https://github.com/luca-veronelli-stem/standards/issues/122). It is a minor bump — backward-compatible at the caller surface: the new trigger is additive and nothing previously mirrored stops mirroring — so adoption is opt-in per repo and can happen in any order.

Per-repo adoption PR (`chore: bump standards to v1.14.0`):

1. Re-run `eng/apply-repo-standard.ps1 -StandardVersion v1.14.0`. The diff is the regenerated `.github/workflows/mirror-bitbucket.yml` stub — the new `on.push.tags` trigger plus the `uses:` pin bump to `@v1.14.0`. Hand-customised mirror stubs hit the local-edit guard and need `-Force` or a hand-merge (per the Pitfalls section). No source-code change.
2. Bump the per-repo `CLAUDE.md` `**Standard version:**` line to `v1.14.0`.
3. Update `state/repos.md` to reflect the bump.
4. Single-commit PR.

First observable effect after the bump: the repo's next push to `main` backfills every annotated tag reachable from `main` that the mirror is missing (`--follow-tags`), and subsequent `v*.*.*` tag pushes mirror immediately via the tag-push trigger. Lightweight or unreachable tags are not carried by `--follow-tags` — a repo that needs them mirrored runs a one-time `git push git@bitbucket.org:stem-fw/<repo>.git --tags` (see CI.md → "Mirror workflow"). Skip the whole bump for personal-account repos with no Bitbucket mirror (`standards`, `llm-settings`).

End-to-end verification (the [#122](https://github.com/luca-veronelli-stem/standards/issues/122) acceptance check, deferred to the first adopter): after `v1.14.0` is tagged and `button-panel-tester` is bumped to it, push a `v*.*.*` tag to `button-panel-tester` on GitHub and confirm the same tag appears on `bitbucket/button-panel-tester`.

## Rollout phase for v1.5.1 — F# runtime restoration, greenfield scaffold, `lean/`-vs-`specs/` clarification

`v1.5.1` ships three first-adopter gap fixes uncovered while bootstrapping `button-panel-tester` against `v1.5.0`:

1. Restores the `FSharp.Core` `PackageVersion` line in `shared/templates/Directory.Packages.props` (dropped in `v1.5.0`). Without it, an F# `<PackageReference Include="FSharp.Core" />` is rejected by Central Package Management and `FSharp.Core.dll` doesn't flow into the bin of a project-referencing test consumer — xunit's reflection discoverer then fails with `Could not load file or assembly 'FSharp.Core'`.
2. Emits a minimal archetype-A greenfield scaffold on bootstrap: `Stem.<App>.slnx` + `src/<App>.Core/{<App>.Core.fsproj,Placeholder.fs}` + `tests/<App>.Tests/{<App>.Tests.fsproj,PlaceholderTests.fs}`. Before this, the bootstrap PR had no compilable source for `dotnet-ci.yml` to target and CI failed at `MSBUILD : error MSB1003: Specify a project or solution file`. After this, the first PR is CI-green without any hand-rolled follow-up.
3. Clarifies that the Lean 4 workspace lives at `lean/` and `specs/` is the spec-kit feature root. Earlier wording in `REPO_STRUCTURE.md` (v1.5.0 and prior) said `specs/` was the Lean workspace, which collided with spec-kit's `specs/NNN-feature-name/` directories the moment a repo did SDD and Lean together. Doc-only — no rollout-script behaviour changes. Adopters that already keep Lean code under `specs/` are not forced to move it on this bump (the script does not touch either folder); the next time the Lean tree is rearranged for any reason, move it to `lean/`. `button-panel-tester` PR #5 records the deviation paragraph that this clarification removes — once v1.5.1 lands, that paragraph can come out and the constitution bumps to v1.0.1 (PATCH — wording only).

The scaffold files are bootstrap-only: once seeded, the rollout never recreates or clobbers them. An adopter who writes real code over `Placeholder.fs` (or deletes it) keeps their content through future bumps.

**For a brand-new repo (greenfield bootstrap):**

1. Run `eng/apply-repo-standard.ps1 -StandardVersion v1.5.1 -Archetype A -App <YourApp> ...`. The rollout writes the toolchain files, the standards inline copies, the Poppins fonts overlay, **and** the `Core` + `Tests` + `.slnx` scaffold.
2. `dotnet restore && dotnet build && dotnet test` works out of the box on the bootstrap branch — no hand-rolled `.fsproj` required.
3. Open the bootstrap PR. CI is green on the first push.

**For an existing v1.5.0 adopter bumping to v1.5.1:**

1. Re-run `eng/apply-repo-standard.ps1 -StandardVersion v1.5.1`. The diff is one new `<PackageVersion Include="FSharp.Core" ... />` line in `Directory.Packages.props`. The scaffold files are skipped under the bootstrap-only rule (they're meant for greenfields; existing repos have already grown their own structure). If `Directory.Packages.props` was hand-customised since the previous bump, the local-edit guard fires — apply the recipe in "Pitfalls" if so.
2. Bump the per-repo `CLAUDE.md` `**Standard version:**` line to `v1.5.1`.
3. Update `state/repos.md` to reflect the bump.
4. Single-commit PR.

Adopters with no F# code can skip the bump until they have other reasons to move (e.g. v1.6.x). The runtime fix is only load-bearing for repos that consume `FSharp.Core` via `<PackageReference>`, and the scaffold only fires on greenfield bootstraps.

## Rollout phase for v1.4.0 — reusable workflows

`v1.4.0` migrates the four shipped workflow templates (`.github/workflows/ci.yml`, `mirror-bitbucket.yml`, archetype A/B `release.yml`) from full copies to thin caller stubs that delegate the job body via `uses: luca-veronelli-stem/standards/.github/workflows/<workflow>.yml@v1.4.0`. After this bump, GHA-pin updates in the called workflows propagate to adopted repos on the next run — no per-repo PR for routine bumps. It is a minor bump — non-breaking from the consumer side as long as triggers and per-repo inputs survive — so adoption is opt-in per repo and can happen in any order.

The `standards` repo is public, so cross-repo reusable-workflow calls resolve without any "Actions access" prerequisite on the workflow-source repo.

Per-repo adoption PR (`chore: bump standards to v1.4.0`):

1. Re-run `eng/apply-repo-standard.ps1 -StandardVersion v1.4.0`. Two outcomes per workflow file:
   - **Untouched workflow** (matches the previous template hash in `.stem-standard.lock`) — silently overwritten with the stub. Diff shows the ~80 → ~25 line shrink and the new `uses:` pin.
   - **Hand-customised workflow** (extra steps, pinned action versions different from the template, custom matrix) — the local-edit guard skips it with `(local edit; pass -Force to overwrite)`. Decide deliberately:
     - If the customisation is something the reusable workflow already handles (or could, via a small input), prefer migrating to the stub: `-Force` to take the template, then re-add only the still-needed customisations on top of the stub. Open an issue here if a missing input would have made the customisation unnecessary.
     - If the customisation is genuinely repo-specific and not worth pushing upstream (rare), keep the full workflow. Hand-merge any GHA-pin bumps that landed in the reusable. Future bumps continue to skip with the local-edit warning — no further surgery.
2. Verify CI is green on the bump PR. `dotnet-ci.yml@v1.4.0` is referenced by tag, so it must exist when the PR builds — if the bump lands before the tag is cut, the workflow run fails with `unable to find workflow at <ref>`. The standards-repo cuts the tag immediately after merging the v1.4.0 PR here; sequence the adopted-repo bumps *after* the tag exists.
3. Bump the per-repo `CLAUDE.md` `**Standard version:**` line to `v1.4.0`.
4. Update `state/repos.md` to reflect the bump.
5. Single-commit PR.

After the bump, routine GHA pin updates are handled centrally: the `standards` repo bumps the reusable workflow, cuts a new patch (`v1.4.x`), and adopted repos pick it up on their next bump (or immediately if their stub references `@v1.4` instead of `@v1.4.0` — a per-repo decision; the rollout writes `@v1.4.0`-style exact pins by default). Workflow-shape changes (new triggers, new per-repo inputs) still need a per-repo PR to refresh the stub.

## What a v1 adoption PR contains

For an archetype A repo:

1. **Repo restructure** — move existing project folders under `src/`; tests under `tests/`; rename folders to PascalCase if needed; rename solution file to `.slnx`.
2. **Toolchain files at root** — `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `.editorconfig`, `.gitignore`, `.gitattributes`, generated by the rollout script.
3. **`.github/`** — workflows (`ci.yml`, `mirror-bitbucket.yml`, `release.yml`), issue templates, `pull_request_template.md`, `CODEOWNERS`, `dependabot.yml`.
4. **`docs/Standards/`** — inline copies of the sixteen standards.
5. **`bitbucket-pipelines.yml`** — build-only stub.
6. **`eng/install-hooks.{ps1,sh}`** — Husky.NET hook installer.
7. **`CLAUDE.md`** — stamped with `**Archetype:** A` and `**Standard version:** vX.Y.Z` (the value passed to `-StandardVersion`).

Adopt deliberately: don't squash language migration (C# → F#) into the same PR. The first adoption PR brings only **structural** changes — files move, configs land, builds stay green. Language migration is a separate, repo-by-repo, phase-gated effort tracked here.

## Per-repo migration log

Append a section per repo as adoption progresses:

```markdown
## stem-device-manager — v1.0.0 adoption

- [x] Phase 1: structural — PR #N — landed YYYY-MM-DD.
- [ ] Phase 2: F# migration of `<App>.Core` — issue #M — target YYYY-Q.
- [ ] Phase 3: F# migration of `<App>.Services`.
- [ ] Phase 4: Avalonia migration of `<App>.GUI.Windows` → `<App>.GUI`.
```

The migration log is **inside this standard** in this repo, so a single file shows the cross-repo state. The repo-side `CHANGELOG.md` records the structural change as a separate entry.

## Choosing the bump level

The major/minor/patch definitions at the top of the `CHANGELOG.md` and in the README "Versioning" section are deliberately terse. This section is the decision procedure behind them, framed around the one thing that actually matters downstream: **the adopter contract**.

**Decision rule.** Picture an adopter who bumps the `**Standard version:**` pin in their `CLAUDE.md` and re-runs `apply-repo-standard.ps1` (the *bump-and-reroll*). Ask one question:

> After the re-roll, is previously-compliant code or config *forced* to change — or does the build break — with no further opt-in on the adopter's side?

- **Yes → major.** The change reaches into the adopter's tree and breaks something that was fine before.
- **No → minor or patch.** The adopter picks up new guidance, a new file, or a new opt-in capability, but nothing they already had stops working.

This is why the minor number can climb indefinitely. SemVer here is **event-driven**, not calendar- or magnitude-driven: a high minor (`v1.11.0`, `v1.12.0`) does not mean "surely it's time for a major." Major is reserved for *forced migration*, and most standards work is additive. The confusion this section exists to prevent surfaced during the [#94](https://github.com/luca-veronelli-stem/standards/issues/94) review, where a high minor read as "overdue for a major" when in fact nothing breaking had ever shipped. There has never been a `v2.0.0` — every minor/patch example below is a real release; the major triggers are illustrative of what *would* force the jump.

### Major — forced adopter churn

A change is major when the bump-and-reroll forces source/config churn or breaks the build with no opt-in. Triggers:

- **Tightening or reversing an existing rule** so that code which was compliant becomes a violation — turning a "prefer" into a "must," or flipping a documented allowance into a ban. A clean adopter now fails its own gate after the re-roll.
- **Adding a `BannedSymbols.txt` entry adopters consume.** `BannedApiAnalyzers` is wired in at solution level (see [`BUILD_CONFIG.md`](./BUILD_CONFIG.md) / [`MODULE_SEPARATION.md`](./MODULE_SEPARATION.md)); shipping a new banned symbol that an adopter currently calls turns their next build red with no code change on their side.
- **Changing an enforced default** — moving the target framework `net10.0 → net11.0`, or changing an archetype's default visibility posture (the archetype B "demote non-kit types to `internal`" rule in [`VISIBILITY.md`](./VISIBILITY.md)). The re-roll rewrites a toolchain file and the adopter's existing code must adapt.
- **Renaming or removing a standard or template.** The inline `docs/Standards/<NAME>.md` an adopter references disappears or moves; cross-links and any constitution/spec text pointing at it break.
- **Changing the `docs/Standards/` layout or the `.stem-standard.lock` format.** Both are consumed mechanically — the rollout script reads/writes the lock, adopters grep the inline tree. A format change forces a coordinated migration rather than a silent re-roll.
- **Redefining an archetype's required project shape.** Changing the projects/folders an archetype *must* have (per [`REPO_STRUCTURE.md`](./REPO_STRUCTURE.md)) forces existing adopters of that archetype to restructure.

When a real major lands, follow the "Major version bumps in `standards`" procedure below — there is no forced upgrade; a repo may pin at `v1.x` indefinitely.

### Minor — additive, nothing breaks

The adopter gains something; nothing they had stops working. The re-roll adds files or refreshes guidance, and an adopter who ignores the new capability is unaffected.

- **New standard → minor.** `v1.5.0` (the `GUI` + `DESIGN_SYSTEM` + `APP_SHELL` trio) and `v1.9.0` (`APP_DATA`) each added a standard without touching the existing contracts.
- **Additive guidance to an existing standard → minor.** `v1.12.0` ([#94](https://github.com/luca-veronelli-stem/standards/issues/94), F# shape coverage) added F#-specific notes across the content standards — nothing previously compliant became non-compliant, which is exactly what made it a minor rather than a major. This ticket ([#119](https://github.com/luca-veronelli-stem/standards/issues/119)) is the same shape: it documents the contract, it does not change it.
- **Backward-compatible workflow change → minor.** `v1.10.0` added the `category-filter` CI input (default `Category!=Hardware`; an empty `with:` block still inherits it, so caller stubs need no edit) and `v1.8.0` added the self-extracting-`.exe` publish flag — both change behavior only for adopters who opt in or re-tag, and existing stubs keep working untouched.

### Patch — no contract change

Bug fixes that restore intended behavior, plus typos, clarifications, and internal refactors — no documented contract moves.

- The `v1.5.1` `FSharp.Core` CPM restoration ([#74](https://github.com/luca-veronelli-stem/standards/issues/74)) re-added a dropped package entry so F# test discovery worked again.
- The `v1.4.0` `xunit 2.9.4 → 2.9.3` pin fix ([#64](https://github.com/luca-veronelli-stem/standards/issues/64)) corrected a version that never existed on nuget.org.
- The `v1.2.1` template-placeholder cleanup replaced render-visible hints that shipped unfilled into adopted repos.

## Major version bumps in `standards`

When `standards` releases a major (`v2.0.0`), the procedure is:

1. The `[Unreleased]` section in this repo's `CHANGELOG.md` lists every breaking change.
2. This standard's "Per-repo migration log" gets a new column for the v2 phases.
3. A repo can choose to **pin** at v1.x (do nothing) or **migrate** (open a PR). Pinning is fine; it just means the repo's `**Standard version:**` stays at `v1.x.y` and Claude treats that as the contract.
4. `state/repos.md` shows pinned vs migrated repos at a glance.

There's no forced upgrade. Major bumps are infrequent enough that lag is acceptable.

## Minor and patch bumps

A minor (`v1.1.0`) adds a new standard or template without breaking anything. A repo bumps by:

1. Re-running `eng/apply-repo-standard.ps1 -Version v1.1.0`.
2. Reviewing the diff (the script writes new files / refreshes existing ones).
3. Bumping `**Standard version:**` in `CLAUDE.md`.
4. Updating `state/repos.md`.
5. Single-commit PR titled `chore: bump standards to v1.1.0`.

Patches (`v1.0.1`) bump the same way but usually produce zero or near-zero diff in the work repo (typo fixes in standards docs).

## Rollback

If a bump regresses a repo, revert the PR and bump `**Standard version:**` back. `state/repos.md` shows the lag until fixed forward in `standards`.

## Keeping the templates current

Two ecosystems pin versions inside `shared/templates/`. Drift on either side replays as a wave of Dependabot PRs against every newly-adopted repo, so they're worth catching here first.

- **GitHub Actions.** This repo's `.github/dependabot.yml` watches the repo's own workflows weekly and groups minor/patch bumps. **When merging a GHA Dependabot PR, mirror the same bump into the matching template files** — `shared/templates/.github/workflows/*.yml` and `shared/templates/archetypes/{A,B}/.github/workflows/release.yml` — in the same PR or a follow-up. Without that mirror the templates go stale, and consumer repos rebump on their next standards adoption.
- **NuGet.** This repo has no `.csproj`/`.fsproj`, so Dependabot can't watch `shared/templates/Directory.Packages.props`. Refresh it manually before each release cut: bootstrap a throwaway repo, run `dotnet outdated`, fold any patch/minor bumps back into the template, re-run the rollout to pick them up. Skip preview tags unless intentional.

## Anti-patterns

- **Squashing language migration into the structural PR.** Two reviews, two PRs.
- **Editing the inline copies in `docs/Standards/` directly.** They're regenerated by the script — edits will be overwritten. Edit upstream in this repo's `shared/standards/`.
- **Skipping the version stamp.** A repo without `**Standard version:**` in `CLAUDE.md` is unaudited; treat as "no standard adopted".
- **Hardcoding the Standard version in `.specify/memory/constitution.md`** (or any other speckit artefact). The rollout script's ownership ends at `docs/Standards/` + `CLAUDE.md`/`README.md` templates and does not rewrite `.specify/`, so a literal version pinned in the constitution silently goes stale on the next bump. Reference the version indirectly via the `**Standard version:**` line in the repo's top-level `CLAUDE.md` — that line is the contract anchor and the single source of truth.

  ✅ "The repo MUST follow the STEM standards verbatim, at the **Standard version** pinned in `CLAUDE.md`, as inlined under `docs/Standards/`."

  ❌ "The repo MUST follow STEM v1.2.1 standards verbatim."

  Illustrative version literals in narrative text (e.g. "v1.2.1 → v1.3.0 added X") are fine — they're examples, not contracts.

## Pitfalls

- **Upgrading from a pre-`v1.3.1` lockfile.** Lockfiles written by the rollout script before `v1.3.1` had two gaps: standards files were keyed by bare filename instead of `docs/Standards/<NAME>.md`, and a number of common-template files (e.g. `CLAUDE.md`, `README.md`, `Directory.Packages.props`, `.github/workflows/*.yml`) were not always recorded. From `v1.3.1` onward, the script treats a missing lock entry on a file that exists on disk as locally-modified, so the **first** post-fix run on a pre-`v1.3.1` repo will skip those files with a `(local edit; pass -Force to overwrite)` warning. Inspect the diff manually before deciding which recipe applies:

  - **Minor local divergence** (e.g. only standards files, or untouched templates) — re-run with `-Force` to seed the missing lock entries. Subsequent bumps work normally.
  - **Substantive local divergence** — a blanket `-Force` would clobber repo-specific content back to template skeletons (the `stem-button-panel-tester` v1.2.1 → v1.3.1 bump hit this on `Directory.Packages.props`, both `.github/workflows/*.yml`, `CLAUDE.md`, and `README.md`). The seed-then-restore recipe:

    1. Re-run `apply-repo-standard.ps1 -Force` to write template content and seed the lock with template hashes.
    2. Immediately `git checkout HEAD -- <customised files>` to restore their pre-bump content (the lock keeps the template hashes).
    3. Hand-bump version stamps where needed (`CLAUDE.md`'s `**Standard version:**` line; `README.md`'s `Standard version: vX.Y.Z` reference).

    Result: `disk != lock` is now the desired permanent state. Future bumps auto-skip those files with the standard `(local edit; pass -Force to overwrite)` warning — no further manual lock surgery. Worked example: [`stem-button-panel-tester` PR #46](https://github.com/luca-veronelli-stem/stem-button-panel-tester/pull/46) shows the actual diff shape.
