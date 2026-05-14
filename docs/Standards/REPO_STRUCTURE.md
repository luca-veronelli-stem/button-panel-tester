# Standard: REPO_STRUCTURE

> **Stability:** v1.0.0 — load-bearing. Changes are major.
> **Applies to:** archetypes A, B, C. Archetype D triggers a `new-archetype` design session before adoption.

## Root layout

```
<repo>/
├── src/                       Project sources. PascalCase folders inside.
├── tests/                     Test projects. PascalCase folders inside.
├── specs/                     Spec-Driven Development (spec-kit) feature folders: NNN-feature-name/.
├── lean/                      Lean 4 workspace (lakefile.lean + lean-toolchain + namespace folders).
├── docs/                      Markdown documentation.
├── eng/                       Build/release scripts (PowerShell + Bash).
├── .github/                   Workflows, issue templates, CODEOWNERS.
├── .vscode/                   Recommended extensions; no per-user settings.
├── Stem.<App>.slnx            Solution file (modern .slnx, not .sln).
├── Directory.Build.props      MSBuild defaults (TFM, nullability, warnings).
├── Directory.Packages.props   Central package management (CPM).
├── global.json                .NET SDK pin.
├── .editorconfig              Style for C#/F#/JSON/YAML/MD.
├── .gitignore, .gitattributes
├── README.md, CHANGELOG.md, LICENSE, CLAUDE.md
└── bitbucket-pipelines.yml    Build-only stub; CI of record is GitHub Actions.
```

`specs/` and `lean/` are independent siblings — neither implies the other. Repos without a Lean formalization track may omit `lean/` entirely; repos without SDD may omit `specs/`.

## Naming rules

- Folders at the repo root are **lowercase** (`src/`, `tests/`, `specs/`, `lean/`, …).
- Project folders inside `src/` and `tests/` are **PascalCase** and match the project name (`Stem.Communication.Abstractions/`, `<App>.GUI/`).
- Each project has its own folder; the `.fsproj` / `.csproj` filename matches the folder name.
- Project namespace prefix is `Stem.<App>.<Layer>` (archetype A) or `Stem.<Lib>.<Layer>` (archetype B).

## Per-archetype layout

### Archetype A — Desktop App

```
src/
├── <App>.Core/                net10.0  F#  Domain types + ports
├── <App>.Services/            net10.0  F#  Use cases (depends on Core)
├── <App>.Infrastructure/      net10.0  F#  Adapters (depends on Services)
└── <App>.GUI/                 net10.0  F#  Avalonia + FuncUI (depends on Services)
tests/
└── <App>.Tests/               net10.0  F#  xUnit + FsCheck + Avalonia.Headless
lean/                          Optional — present when the repo formalizes invariants in Lean 4.
├── lakefile.lean
├── lean-toolchain
└── Stem/<App>/Phase<N>/       Lean module folders mirror the F# namespace (Stem.<App>...).
```

Split `<App>.Tests` into per-project test assemblies only when the C# surface is substantial enough to need its own xUnit fixtures (see TESTING).

If the app exposes a class library consumed by other apps (e.g. `stem-dictionaries-manager`'s API), keep that library under `src/` like any other layer (`<App>.Api/`). Don't split it into a sibling library repo unless versioning/release cadence diverges from the GUI's.

### Archetype B — Library

Hexagonal layout (see MODULE_SEPARATION for layer rules).

```
src/
├── Stem.<Lib>.Abstractions/         net10.0     F#  Interfaces, types, no logic
├── Stem.<Lib>.Protocol/             net10.0     F#  Pure logic over Abstractions
├── Stem.<Lib>.Drivers.<Plat>.<Bus>/ multi-TFM   F#  Adapter per platform/bus
└── Stem.<Lib>.DependencyInjection/  net10.0     C#  Optional MEDI extensions
tests/
└── Stem.<Lib>.Tests/                net10.0     F#  xUnit + FsCheck
```

### Archetype C — Meta/Config

Repos like `standards` (this one) and `llm-settings`. No `src/`, `tests/`, `specs/`, `lean/`. Layout depends on the meta-config's purpose — see each repo's README. `standards` has `shared/standards/` + `shared/templates/` + `eng/` + `state/`; `llm-settings` has `claude/` + `shared/skills/` + `shared/mcp/`.

### Archetype D — New (placeholder)

Run a `new-archetype` design session before adopting any standard. Don't force-fit.

## Standards reference inside the repo

`docs/Standards/` contains **inline copies** of the standards files from this repo's `shared/standards/`, pinned to the repo's current `**Standard version:**`. The rollout script (`eng/apply-repo-standard.ps1`) is the only writer — it copies the standards into `docs/Standards/` and regenerates a short `docs/Standards/README.md` index that points back to the `standards` repo as the upstream source of truth.

### Rationale — why inline copies, not symlinks or hyperlinks

`llm-settings` itself uses symlinks at install time (e.g. `~/.claude/skills/` → `llm-settings/shared/skills/`), but those live outside any tracked git tree and only need to resolve on one machine.

A symlink **inside** a work repo's tracked tree (`docs/Standards/` → `<standards>/shared/standards/`) is a different problem: git stores the relative target as a tiny text file that has to resolve **everywhere the repo is read**.

| Where the work repo is read | In-repo symlink | Hyperlink to standards | Inline copy |
| --- | --- | --- | --- |
| Local machine, both repos checked out | ✅ | ✅ | ✅ |
| GitHub Actions runner | ❌ — sibling repo not cloned | ✅ | ✅ |
| Bitbucket-only colleague's clone | ❌ — no GitHub access | ✅ (public, tag-pinned URL) | ✅ |
| Drift risk if `standards` evolves | none | none (tag-pinned) | bounded by Standard version stamp + `state/repos.md` |
| Greppable / readable inside the work repo | external — points outside the tree | external — needs a browser round-trip | ✅ |

Symlinks fail the dual-remote constraint: a Bitbucket-only colleague has no path to the GitHub-hosted target. Hyperlinks now resolve for that case — `standards` is public — but they push the standards content outside the work repo's tree, so any repo-local read (grep, on-Bitbucket inline review, offline browse) has to round-trip through a browser. Inline copies keep the content inside the work repo and pin it to an explicit Standard version stamp, which is the property `state/repos.md` actually tracks.

### Why we still use symlinks for `~/.claude/`

The install-time symlinks are local-only, never committed, and serve a single user on a single machine — exactly the case where symlinks are the right tool. The principle is consistent: **use the simplest mechanism that works for every reader of the artifact.**

## What lives where — cheat sheet

| Need to add | Goes in |
| --- | --- |
| Domain type or interface | `<App>.Core/` (A) or `<Lib>.Abstractions/` (B) |
| Use case / orchestration | `<App>.Services/` (A) or `<Lib>.Protocol/` (B) |
| EF Core / file IO / HTTP | `<App>.Infrastructure/` (A) or `<Lib>.Drivers.<Plat>.<Bus>/` (B) |
| Avalonia view / view-model | `<App>.GUI/` |
| MEDI registration extensions | `<App>.GUI/Composition/` (A) or `<Lib>.DependencyInjection/` (B, optional) |
| xUnit test | `tests/<App>.Tests/` |
| Spec-Driven Development (spec-kit) feature folder | `specs/NNN-feature-name/` |
| Lean 4 spec | `lean/Stem/<App>/Phase<N>/` (archetype A) or `lean/Stem/<Lib>/Phase<N>/` (archetype B) |
| Build/release script | `eng/` |
| Standard or convention doc | upstream in this repo's `shared/standards/`; cite from `docs/Standards.md` |

## Solution file (.slnx)

Modern XML format. The rollout script generates a baseline `Stem.<App>.slnx` containing the projects under `src/` and `tests/`. Edit by hand or via Rider; Visual Studio 2022+ also supports `.slnx`.
