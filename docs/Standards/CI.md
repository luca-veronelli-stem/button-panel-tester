# Standard: CI

> **Stability:** v1.0.0
> **Platform:** GitHub Actions is CI of record. Bitbucket Pipelines runs a build-only stub for visibility.

## Workflows shipped per repo

From `v1.4.0`, the workflows the rollout writes into an adopted repo are **caller stubs**: each `.github/workflows/*.yml` in the adopted repo owns only the consumer-side surface (triggers, concurrency, permissions, per-repo inputs) and delegates the job body to a reusable workflow shipped from this `standards` repo via `uses: luca-veronelli-stem/standards/.github/workflows/<workflow>.yml@vX.Y.Z`. The reusable workflows themselves live at `.github/workflows/` *in this repo* and are referenced by tag. Bumping a GHA pin (`actions/setup-dotnet`, `dorny/test-reporter`, etc.) in a reusable workflow propagates to adopted repos on their next run, with no per-repo PR.

| Workflow | Stub (in adopted repo) | Reusable body (this repo) | Trigger |
| --- | --- | --- | --- |
| **CI** | `.github/workflows/ci.yml` | `.github/workflows/dotnet-ci.yml` | push, PR, manual dispatch, weekly schedule |
| **Mirror to Bitbucket** | `.github/workflows/mirror-bitbucket.yml` | `.github/workflows/mirror-bitbucket.yml` | push to `main` |
| **Release** (archetype A) | `.github/workflows/release.yml` | `.github/workflows/release-archetype-a.yml` | tag `v*.*.*` |
| **Release** (archetype B) | `.github/workflows/release.yml` | `.github/workflows/release-archetype-b.yml` | tag `v*.*.*` |

The stubs live under `shared/templates/.github/workflows/` (common: `ci.yml`, `mirror-bitbucket.yml`) and `shared/templates/archetypes/{A,B}/.github/workflows/release.yml` (archetype overlays) and are copied into each repo by the rollout script (see REPO_STRUCTURE). The rollout substitutes `v1.6.0` into the `uses:` pin at bump time, so each adopted repo references the exact tag it is pinned to. Migrating an existing repo across this shape change is covered in MIGRATION.md → "Rollout phase for v1.4.0".

## ci.yml — invariants

Triggers, concurrency, and permissions live in the per-repo stub (`.github/workflows/ci.yml`); the matrix, caching, and steps live in the reusable body (`luca-veronelli-stem/standards/.github/workflows/dotnet-ci.yml`):

- **Triggers (stub):** `push` to `main`, `pull_request` against `main`, `workflow_dispatch`, weekly `schedule` cron (catches dependency drift on idle repos).
- **Concurrency (stub):** `concurrency.group = ci-${{ github.ref }}`, `cancel-in-progress: true` — newer pushes cancel older runs on the same branch.
- **Matrix (reusable):** `os: [ubuntu-latest, windows-latest]`. The Linux leg enforces portability; the Windows leg validates Windows-only drivers and any legacy `GUI.Windows` projects.
- **Caching (reusable):** `~/.nuget/packages/` keyed on `Directory.Packages.props`; Lean `~/.elan/` keyed on `**/lean-toolchain` (Linux leg only — the toolchain is identical across OSes, so there is no value in doubling the cache + build on Windows). The recursive `**/` pattern handles both supported layouts: workspace-root (`./lean-toolchain`) and the sub-directory layout STEM apps use (`lean/lean-toolchain`).
- **Steps (reusable):** checkout → setup-dotnet (from `global.json`) → restore → format check → build (Release) → test (Release). The Linux leg additionally runs `lake build` (working directory `./` or `./lean`, whichever holds `lean-toolchain`) when a Lean track is present — that gate enforces constitution Principle I (no `sorry`, no custom axioms) on every adopter PR.

## Format check is a hard gate (whitespace-only in CI)

```yaml
- name: Verify formatting (whitespace)
  run: dotnet format whitespace --verify-no-changes --no-restore
```

Whitespace-only on purpose. The full `dotnet format --verify-no-changes` fails on the GitHub-hosted runners with `CS0246` when C# files reference types from F# projects, even after a successful `dotnet build` — Roslyn's `MSBuildWorkspace` doesn't fully resolve cross-language refs during the analyzer phase on those images. The same command passes locally on the same SDK (10.0.203). Analyzer/style enforcement still happens via the build's `TreatWarningsAsErrors` (BUILD_CONFIG), so the whitespace check is sufficient at the CI formatting gate.

Pure-C# or pure-F# repos don't strictly need the workaround, but the template uses `whitespace` everywhere for uniformity — analyzer enforcement still happens via build.

Husky.NET pre-commit (BUILD_CONFIG) keeps running the full `dotnet format --verify-no-changes` locally, where the cross-language gap doesn't manifest. The CI step is the whitespace backstop.

Revisit when .NET SDK 11 / Roslyn ship: if `MSBuildWorkspace` gains full cross-language resolution on the hosted runners, restore the full check here.

## TFM-conditional matrix legs

The Linux leg builds only `net10.0`. The Windows leg builds both `net10.0` and `net10.0-windows`. Driver projects' `net10.0-windows` TFM is therefore covered exactly once, on the right runner.

```yaml
- name: Build (Linux)
  if: runner.os == 'Linux'
  run: dotnet build --framework net10.0 --configuration Release

- name: Build (Windows)
  if: runner.os == 'Windows'
  run: dotnet build --configuration Release   # picks up both TFMs
```

## Test reporting

`dorny/test-reporter@v3` consumes the TRX output from `dotnet test --logger trx` and surfaces failed tests in the PR check. The Windows leg runs the solution in one go; the Linux leg enumerates test projects and skips Windows-only / Linux-only ones by name (`*.Tests.Windows.*`, `*.Tests.Linux.*`) — see TESTING for the convention:

```yaml
- name: Test (cross-platform leg)
  if: runner.os == 'Linux'
  shell: bash
  run: |
    set -euo pipefail
    shopt -s globstar nullglob
    for proj in tests/**/*.Tests.fsproj tests/**/*.Tests.csproj; do
      case "$proj" in
        *.Tests.Windows.*|*.Tests.Linux.*) continue ;;
      esac
      dotnet test "$proj" --framework net10.0 --configuration Release --no-build --logger "trx;LogFileName=test-results.trx"
    done

- name: Test (full leg)
  if: runner.os == 'Windows'
  run: dotnet test --configuration Release --no-build --logger "trx;LogFileName=test-results.trx"

- name: Test report
  uses: dorny/test-reporter@v3
  if: always()
  with:
    name: Tests (${{ matrix.os }})
    path: '**/test-results.trx'
    reporter: dotnet-trx
    use-actions-summary: 'false'
```

Why the Linux leg loops: vstest cannot filter Windows-only-TFM assemblies via `--framework net10.0` at solution scope — given a `<App>.Tests.Windows` project that only targets `net10.0-windows`, the runner tries to load a `net10.0` output that does not exist and exits non-zero. The naming convention (TESTING) lets the workflow exclude those projects at the project layer instead.

`if: always()` so failed tests still produce a report. `use-actions-summary: 'false'` opts back into the legacy Check Run sink — v3's default writes to `$GITHUB_STEP_SUMMARY` instead, which silently drops the per-OS Tests gate at PR level.

## Release workflow — archetype A

Triggered on `v*.*.*` tag push. Steps:

1. Checkout, setup-dotnet from `global.json`.
2. `dotnet publish src/<App>.GUI -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`.
3. `Compress-Archive` to `<app>-<version>-win-x64.zip`.
4. `softprops/action-gh-release@v3` — creates a GitHub Release with the zip attached and the matching CHANGELOG entry as body.

Why archetype A needs a release workflow: the desktop app's distributable is a self-contained zip. Without it, "release" means "open the IDE and copy bin/Release somewhere" — fragile and unreproducible.

## Release workflow — archetype B

Triggered on `v*.*.*` tag push. Steps:

1. Checkout, setup-dotnet.
2. `dotnet pack -c Release -o ./packages -p:Version=$VERSION`.
3. `dotnet nuget push` to GitHub Packages.
4. `softprops/action-gh-release@v3` — GitHub Release with the matching CHANGELOG entry.

## Release workflow — archetype C

`standards` (this repo). No release workflow — versioning is the git tag (see CHANGELOG.md at this repo's root). Tag is created manually after merging the relevant PR. The agent-config sibling `llm-settings` follows the same archetype but is unversioned (HEAD-only).

## Mirror workflow

Defined in `dual-remote.md` rule. From v1.4.0 the rollout writes a stub that calls `luca-veronelli-stem/standards/.github/workflows/mirror-bitbucket.yml@<version>` and supplies the per-repo Bitbucket slug as input plus the `BITBUCKET_SSH_KEY` secret. Skip for personal-account repos with no Bitbucket mirror (e.g. `standards`, `llm-settings`).

## Bitbucket Pipelines stub

`bitbucket-pipelines.yml` runs a minimal build to keep the team's Bitbucket green. Single step, `dotnet build`, no tests. Sync the .NET SDK version whenever `global.json` changes.

```yaml
image: mcr.microsoft.com/dotnet/sdk:10.0
pipelines:
  default:
    - step:
        name: Build
        script:
          - dotnet build --configuration Release
```

## Branch protection (set once per repo)

After the first green CI run on `main`:

- Require status check `CI / build (ubuntu-latest)` and `CI / build (windows-latest)` before merge.
- Require linear history (no merge commits).
- Require branches to be up-to-date with `main` before merging.
- Allow squash merges only.
- `delete_branch_on_merge: true` (also set via `gh api -X PATCH /repos/{owner}/{repo}`).

## Dependabot

`dependabot.yml` shipped per repo: weekly updates for `nuget` and `github-actions`. Major bumps open separate PRs.

## What CI does NOT do

- **Coverage reporting** — not enforced in v1 (see TESTING).
- **Lint of markdown / YAML** — left to local tooling for now.
- **Deploy** — STEM apps don't auto-deploy. The release workflow's artifact is the deploy bundle.
