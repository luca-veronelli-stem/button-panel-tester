# Standard: CI

> **Stability:** v1.0.0
> **Platform:** GitHub Actions is CI of record. Bitbucket Pipelines runs a build-only stub for visibility.

## Workflows shipped per repo

From `v1.4.0`, the workflows the rollout writes into an adopted repo are **caller stubs**: each `.github/workflows/*.yml` in the adopted repo owns only the consumer-side surface (triggers, concurrency, permissions, per-repo inputs) and delegates the job body to a reusable workflow shipped from this `standards` repo via `uses: luca-veronelli-stem/standards/.github/workflows/<workflow>.yml@vX.Y.Z`. The reusable workflows themselves live at `.github/workflows/` *in this repo* and are referenced by tag. Bumping a GHA pin (`actions/setup-dotnet`, `dorny/test-reporter`, etc.) in a reusable workflow propagates to adopted repos on their next run, with no per-repo PR.

| Workflow | Stub (in adopted repo) | Reusable body (this repo) | Trigger |
| --- | --- | --- | --- |
| **CI** | `.github/workflows/ci.yml` | `.github/workflows/dotnet-ci.yml` | push, PR, manual dispatch, weekly schedule |
| **Mirror to Bitbucket** | `.github/workflows/mirror-bitbucket.yml` | `.github/workflows/mirror-bitbucket.yml` | push to `main`, tag push (`v*.*.*`) |
| **Release** (archetype A) | `.github/workflows/release.yml` | `.github/workflows/release-archetype-a.yml` | tag `v*.*.*` |
| **Release** (archetype B) | `.github/workflows/release.yml` | `.github/workflows/release-archetype-b.yml` | tag `v*.*.*` |

The stubs live under `shared/templates/.github/workflows/` (common: `ci.yml`, `mirror-bitbucket.yml`) and `shared/templates/archetypes/{A,B}/.github/workflows/release.yml` (archetype overlays) and are copied into each repo by the rollout script (see REPO_STRUCTURE). The rollout substitutes `v1.17.1` into the `uses:` pin at bump time, so each adopted repo references the exact tag it is pinned to. Migrating an existing repo across this shape change is covered in MIGRATION.md → "Rollout phase for v1.4.0".

## ci.yml — invariants

Triggers, concurrency, and permissions live in the per-repo stub (`.github/workflows/ci.yml`); the matrix, caching, and steps live in the reusable body (`luca-veronelli-stem/standards/.github/workflows/dotnet-ci.yml`):

- **Triggers (stub):** `push` to `main`, `pull_request` against `main`, `workflow_dispatch`, weekly `schedule` cron (catches dependency drift on idle repos).
- **Concurrency (stub):** `concurrency.group = ci-${{ github.ref }}`, `cancel-in-progress: true` — newer pushes cancel older runs on the same branch.
- **Matrix (reusable):** `os: [ubuntu-latest, windows-latest]`. The Linux leg enforces portability; the Windows leg validates Windows-only drivers and any legacy `GUI.Windows` projects.
- **Caching (reusable):** `~/.nuget/packages/` keyed on `Directory.Packages.props`; Lean `~/.elan/` keyed on `**/lean-toolchain` (Linux leg only — the toolchain is identical across OSes, so there is no value in doubling the cache + build on Windows). The recursive `**/` pattern handles both supported layouts: workspace-root (`./lean-toolchain`) and the sub-directory layout STEM apps use (`lean/lean-toolchain`). Both cache steps carry `continue-on-error: true` — a cache restore is an optimization, never a correctness gate, so a transient cache-service error degrades to a cold restore instead of skipping Restore/Build/Test. Without it a failed cache step's implicit `success()` cascades to every downstream step lacking a status function, redding `main` with no real test failure (`dorny/test-reporter` then fails "No test report files were found" because no `.trx` was produced). See "Cache restore is non-fatal" below and #123.
- **Steps (reusable):** checkout → setup-dotnet (from `global.json`) → restore → format check → build (Release) → test (Release). The Linux leg additionally runs `lake build` (working directory `./` or `./lean`, whichever holds `lean-toolchain`) when a Lean track is present — that gate enforces constitution Principle I (no `sorry`, no custom axioms) on every adopter PR.

## Runner-image policy (float on `*-latest`)

Every `runs-on:` and matrix value in the shipped workflows targets the floating aliases (`ubuntu-latest`, `windows-latest`) **on purpose**. The alternative — pinning dated images (`windows-2025`, `windows-2025-vs2026`, …) — was evaluated and rejected when GitHub announced the `windows-latest` → `windows-2025-vs2026` redirect (June 15, 2026; [#134](https://github.com/luca-veronelli-stem/standards/issues/134)):

- **A dated image is not a frozen toolchain.** GitHub rolls the software on dated images continuously — the `Set up job` log shows e.g. `Image: windows-2025`, `Version: 20260525.149.1`, and that version advances weekly under the same label. Pinning fixes only the OS/VS generation. This is unlike the action tag-pinning posture (the `setup-dotnet` pin in `dotnet-ci.yml`), where a tag resolves to exact action code: a pinned image keeps drifting underneath, so the determinism gain is marginal.
- **The toolchain CI correctness depends on is pinned elsewhere.** The .NET SDK comes from `global.json` — BUILD_CONFIG allows patch roll-forward precisely so a fresh runner image cannot break the build — and every action is tag-pinned. The runner image only has to provide an OS that can host them.
- **Pinning renames the required status checks.** Check contexts are named after the matrix value (`build (windows-latest)` — see "Branch protection" below), and adopted repos' branch protection requires them by that exact name. Pinning changes the context name, which means a branch-protection update in **every** adopted repo (a MIGRATION-standard rollout) — and again at every dated-image retirement, since GitHub deprecates dated images on its own schedule. That is a recurring fleet-wide migration treadmill bought for the marginal determinism above.

**Accepted risk:** an image swap lands with no repo-side diff to bisect against. Bounded by two facts. First, the adopter stub's weekly scheduled CI run catches image-induced breakage within a week even on idle repos. Second, every run's `Set up job` log names the image and version that served it (`Runner Image` → `Image` / `Version`), so triaging across a swap is a one-line comparison between the last green and the first red run — the "repo-side diff" is replaced by the log line.

When GitHub announces an alias redirect, treat it as a calendar event, not drift: verify a green run on the new image around the flip date. The standards repo's own `ci.yml` carries `workflow_dispatch` for exactly this. For the June 2026 redirect, this repo's full static-checks job was run green on `windows-2025-vs2026` directly, before the flip ([#134](https://github.com/luca-veronelli-stem/standards/issues/134)).

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

## Cache restore is non-fatal

A cache restore is an optimization, not a correctness gate. Both `actions/cache@v5` steps (NuGet, and the Linux-leg Lean toolchain) carry `continue-on-error: true`:

```yaml
- name: Cache NuGet packages
  uses: actions/cache@v5
  continue-on-error: true
  with:
    path: ~/.nuget/packages
    key: ${{ runner.os }}-nuget-${{ hashFiles('**/Directory.Packages.props') }}
    restore-keys: |
      ${{ runner.os }}-nuget-
```

Why it is load-bearing: a GitHub Actions step `if:` that contains no status check function (`success()`, `failure()`, `always()`, `cancelled()`) carries an **implicit `success()`** — `if: runner.os == 'Windows'` is really `success() && runner.os == 'Windows'`. So when a cache step errors transiently (a cache-service hiccup, not a code fault), its conclusion is `failure`, `success()` flips to false, and every downstream step lacking a status function — Restore, Build, Test — is **skipped**. No `.trx` is produced, and `dorny/test-reporter` then fails with "No test report files were found", redding `main` even though no test failed. `continue-on-error: true` makes the failed step's *conclusion* `success` (its `outcome` still records the real `failure` for anyone inspecting it), so `success()` stays true and Build/Test run against a cold cache instead of being skipped. The Lean cache step gets the same guard because it, too, sits before Restore/Build/Test on the Linux leg, so a Lean-cache flake would skip them just the same. First incidents: `button-panel-tester` run `26628708854` (2026-05-29) and PR #178 (2026-06-03), both cleared by a no-op re-run. See #123.

## Test reporting

`dorny/test-reporter@v3` consumes the TRX output from `dotnet test --logger trx` and surfaces failed tests in the PR check. The Windows leg runs the solution in one go; the Linux leg enumerates test projects and skips Windows-only / Linux-only ones by name (`*.Tests.Windows.*`, `*.Tests.Linux.*`) — see TESTING for the convention:

```yaml
- name: Test (cross-platform leg)
  id: test_xplat
  if: runner.os == 'Linux'
  shell: bash
  run: |
    set -euo pipefail
    shopt -s globstar nullglob
    for proj in tests/**/*.Tests.fsproj tests/**/*.Tests.csproj; do
      case "$proj" in
        *.Tests.Windows.*|*.Tests.Linux.*) continue ;;
      esac
      dotnet test "$proj" --framework net10.0 --configuration Release --no-build --filter "${{ inputs.category-filter }}" --logger "trx;LogFileName=test-results.trx"
    done

- name: Test (full leg)
  id: test_full
  if: runner.os == 'Windows'
  run: dotnet test --configuration Release --no-build --filter "${{ inputs.category-filter }}" --logger "trx;LogFileName=test-results.trx"

- name: Test report
  uses: dorny/test-reporter@v3
  if: ${{ always() && (steps.test_xplat.outcome != 'skipped' || steps.test_full.outcome != 'skipped') }}
  with:
    name: Tests (${{ matrix.os }})
    path: '**/test-results.trx'
    reporter: dotnet-trx
    use-actions-summary: 'false'
```

Why the Linux leg loops: vstest cannot filter Windows-only-TFM assemblies via `--framework net10.0` at solution scope — given a `<App>.Tests.Windows` project that only targets `net10.0-windows`, the runner tries to load a `net10.0` output that does not exist and exits non-zero. The naming convention (TESTING) lets the workflow exclude those projects at the project layer instead.

The `Test report` step keys off the test step's own `outcome` (via the `id: test_xplat` / `id: test_full` handles), not the mere presence of a `.trx`. It runs whenever the OS-relevant test step actually executed — success or failure, so genuine test failures are still reported — and skips only when **both** test steps were skipped, i.e. an upstream failure (e.g. Build) aborted the run before any test ran. That keeps a genuinely skipped/aborted run distinguishable from "tests ran and reported nothing", so a real upstream red is no longer masked by the reporter's own "No test report files were found" failure (#123). `always()` rather than `!cancelled()` preserves the prior on-cancellation behaviour; a status check function is still present, so the implicit `success()` is disabled and a *failing* Test step still produces its report. `use-actions-summary: 'false'` opts back into the legacy Check Run sink — v3's default writes to `$GITHUB_STEP_SUMMARY` instead, which silently drops the per-OS Tests gate at PR level.

## Hardware-test exclusion

Tests that require physical hardware (CAN adapters, BLE radios, PCAN, serial dongles) are annotated with the xUnit `Category=Hardware` trait so they can be excluded on hosted runners that lack the device:

```fsharp
[<Fact; Trait("Category", "Hardware")>]
let ``connects to a PEAK USB adapter`` () = ...
```

```csharp
[Fact, Trait("Category", "Hardware")]
public void ConnectsToBleRadio() { ... }
```

The local pre-push gate (per the `workflow` rule) passes `--filter "Category!=Hardware"`. From `v1.10.0`, the reusable `dotnet-ci.yml` accepts a matching input so the CI gate matches the local one without per-repo workaround:

```yaml
# .github/workflows/dotnet-ci.yml (reusable)
on:
  workflow_call:
    inputs:
      category-filter:
        type: string
        required: false
        default: "Category!=Hardware"

# ...

      - name: Test (cross-platform leg)
        # ...
        run: |
          # ...
          dotnet test "$proj" ... --filter "${{ inputs.category-filter }}" ...

      - name: Test (full leg)
        if: runner.os == 'Windows'
        run: dotnet test ... --filter "${{ inputs.category-filter }}" ...
```

Adopter stubs need no change to pick up the default — the empty `with:` block keeps inheriting `Category!=Hardware`:

```yaml
# .github/workflows/ci.yml (adopter caller stub, unchanged)
jobs:
  build:
    uses: luca-veronelli-stem/standards/.github/workflows/dotnet-ci.yml@v1.10.0
```

Adopters running their own hardware-equipped runner — or that want to include hardware tests under a different gate — override the input from the stub:

```yaml
jobs:
  build:
    uses: luca-veronelli-stem/standards/.github/workflows/dotnet-ci.yml@v1.10.0
    with:
      category-filter: ""   # include everything

  hardware:
    # Self-hosted job that runs hardware tests in isolation.
    runs-on: [self-hosted, hardware]
    steps:
      - uses: actions/checkout@v6
      # ...
      - run: dotnet test --filter "Category=Hardware"
```

Why the default is backward-compatible: `Category!=Hardware` matches every test that does **not** carry the trait, so existing suites without any `Category` annotation stay green. The first hardware-traited test an adopter writes is excluded silently — no `Skip = "...#NNN"` workaround needed in source.

The bookkeeping rule: once a test gets `Trait("Category", "Hardware")`, never substitute `Skip = "..."` for the same exclusion intent — the filter is the contract, and `Skip` overrides the filter (so the test stays skipped even on a developer's bench where the hardware is plugged in).

## Release workflow — archetype A

Triggered on `v*.*.*` tag push. Steps:

1. Checkout, setup-dotnet from `global.json`.
2. `dotnet publish src/<App>.GUI -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`.
3. `Compress-Archive` to `<app>-<version>-win-x64.zip`.
4. `softprops/action-gh-release@v3` — creates a GitHub Release with the zip attached and the matching CHANGELOG entry as body.
5. **Bitbucket Downloads upload** (optional, gated) — when the caller passes a `bitbucket-repo` slug, `POST` the same zip to that repo's Bitbucket **Downloads** section so the firmware team can fetch released builds without GitHub access. GitHub Releases stay primary; this is additive.

Why archetype A needs a release workflow: the desktop app's distributable is a self-contained zip. Without it, "release" means "open the IDE and copy bin/Release somewhere" — fragile and unreproducible.

`IncludeNativeLibrariesForSelfExtract=true` packs the native binaries (Avalonia/Skia, Plugin.BLE, PCAN, `System.IO.Ports`, …) into the `.exe` bundle's self-extract section so the published artifact is a true single `.exe` — draggable, USB-launchable, no companion `runtimes/win-x64/native/` directory required to run. Pairs with `PublishSingleFile=true` (which alone only bundles the managed DLLs and leaves native deps as siblings). The flag trades default behaviour for shape: on first launch per user per release, the bundle extracts to `%LOCALAPPDATA%\.net\<App>\<content-hash>\` and subsequent launches reuse the extracted copy — invisible in steady state.

The escape hatch for hardened environments where the default extraction path is blocked (EDR products quarantining freshly-materialized `.dll` files, read-only `%LOCALAPPDATA%`) is the `DOTNET_BUNDLE_EXTRACT_BASE_DIR` environment variable: set it to an app-writable path before launch, and the bundle extracts there instead. Rare in supplier-workshop / bench-tech target environments, documented here because adopters debugging a launch failure in a customer site will want one entry point that names the variable.

### Bitbucket Downloads upload (`v1.17.0`)

From `v1.17.0` the reusable workflow can also publish the release zip to a repo's Bitbucket **Downloads** section, so the firmware team can download released builds straight from Bitbucket without a GitHub account. It is **additive** — the GitHub Release is still the primary publish target — and **opt-in per repo**:

- The reusable workflow gains an optional `bitbucket-repo` input (default `''`) and an optional `BITBUCKET_API_TOKEN` secret. The upload step is gated `if: inputs.bitbucket-repo != ''`, so a caller that passes no slug — a hand-rolled stub, or a future archetype A repo with no Bitbucket mirror — skips it cleanly.
- The archetype A caller stub passes `bitbucket-repo: stem-fw/<repo>` and forwards the `BITBUCKET_API_TOKEN` secret. Because the stub **hard-codes** the slug, the gate is always satisfied for a re-rolled adopter — the step is not silently dormant. Re-rolling the stub to `v1.17.0` and provisioning the token are therefore one coupled rollout step: until the secret is set, the `curl -sSf` upload fails the release with `401 Unauthorized` (a missing/empty Bearer token). MIGRATION.md → "Rollout phase for v1.17.0" makes the two-part bump explicit, same as how adopting the mirror couples the stub with `BITBUCKET_SSH_KEY`.

The upload is a single `curl` against the Bitbucket Downloads REST API — the GitHub-Release-asset equivalent:

```bash
curl -sSf -H "Authorization: Bearer $TOKEN" \
  "https://api.bitbucket.org/2.0/repositories/<workspace>/<repo>/downloads" \
  -F files=@"$ZIP"
```

Three load-bearing points:

- **Auth is a Bitbucket repository access token**, passed as `Authorization: Bearer`, stored in the `BITBUCKET_API_TOKEN` Actions secret. It needs `repository:write` scope and is created per repo at *Repository settings → Access tokens*. **Not** a workspace or project access token — those are a Bitbucket **Premium** feature and STEM is not on Premium; repository access tokens are available on every plan and use the same Bearer auth. **Not** an app password either (Atlassian is removing those mid-2026). This is an **HTTPS REST** credential, entirely separate from the **SSH** `BITBUCKET_SSH_KEY` the git mirror uses (`dual-remote` rule) — different transport, different secret, different lifecycle. Provisioning it is a manual per-repo step, same as `BITBUCKET_SSH_KEY`; the rollout script does not set Actions secrets.
- **Idempotent on tag re-runs.** Re-POSTing a file with the same name **replaces** the existing download rather than erroring or duplicating, so re-running a release tag (or a `workflow_dispatch` with the same tag) overwrites the Downloads entry in place.
- **`shell: bash` on purpose.** This workflow's steps default to `pwsh` on `windows-latest` (see the Publish step's backtick continuations), so the upload step pins `shell: bash` to let the `\` line continuations and `curl -F files=@` work as written. `curl` ships on the GitHub Windows runner image.

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

From v1.14.0 the stub fires on **both** branch pushes to `main` and version-tag pushes (`on.push.tags: ['v*.*.*']`), and the reusable body branches on `github.ref_type`:

- **Branch push to `main`** — `git push --follow-tags bitbucket HEAD:refs/heads/main`, mirroring the commit *and* every annotated tag reachable from `main` that the mirror is missing.
- **Tag push** — `git push bitbucket "$REF:$REF"`, mirroring only the pushed tag ref. `bitbucket/main` is deliberately never updated from the detached tag checkout (pushing `HEAD:refs/heads/main` there would force the mirror's `main` back to the tagged commit).

**Annotated vs lightweight.** `--follow-tags` carries **annotated** tags only. The release convention is annotated by construction — `softprops/action-gh-release` and `git tag -a` both create annotated tags — so `v*.*.*` release tags reach the mirror on the next `main` push even without a separate tag-push event. Lightweight tags are intentionally not followed; a repo that deliberately mirrors lightweight tags must switch the `main` path to `--tags` (which pushes *all* tags, reachable or not — use only when that is the intent). The explicit tag-push path mirrors whichever `v*.*.*` ref was pushed regardless of kind.

First-run backfill: the first `main` push after a repo adopts v1.14.0 pushes every annotated tag reachable from `main` that the mirror lacks. Lightweight or unreachable tags need a one-time manual `git push git@bitbucket.org:stem-fw/<repo>.git --tags`.

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
