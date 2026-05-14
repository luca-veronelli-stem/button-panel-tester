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
