# Phase 1 — Setup (Shared Infrastructure)

This document scopes the first phase-PR for spec 001 (dictionary fetch and
status display). It is the durable anchor for the Phase 1 work driven by the
`resolve-ticket-supervised` protocol.

## Scope

Pure infrastructure scaffolding. No behaviour change, no Lean theorems, no
behaviour tests. The goal is for the solution to restore and build green and
for `dotnet test` to still pass against the existing `PlaceholderTests` after
all Phase 1 work is merged.

## Tasks (T001..T009)

Mapped one-to-one to the task list in [`../tasks.md`](../tasks.md) lines
39..47 and to GitHub issues #8..#16:

- T001 — issue [#8](https://github.com/luca-veronelli-stem/button-panel-tester/issues/8):
  add `System.Security.Cryptography.ProtectedData` 10.0.7 to
  `Directory.Packages.props`.
- T002 — issue [#9](https://github.com/luca-veronelli-stem/button-panel-tester/issues/9):
  create `src/ButtonPanelTester.Services/ButtonPanelTester.Services.fsproj`.
- T003 — issue [#10](https://github.com/luca-veronelli-stem/button-panel-tester/issues/10):
  create `src/ButtonPanelTester.Infrastructure/ButtonPanelTester.Infrastructure.fsproj`.
- T004 — issue [#11](https://github.com/luca-veronelli-stem/button-panel-tester/issues/11):
  create `src/ButtonPanelTester.GUI/ButtonPanelTester.GUI.fsproj`.
- T005 — issue [#12](https://github.com/luca-veronelli-stem/button-panel-tester/issues/12):
  update `Stem.ButtonPanelTester.slnx` to add the three new projects.
- T006 — issue [#13](https://github.com/luca-veronelli-stem/button-panel-tester/issues/13):
  update `tests/ButtonPanelTester.Tests/ButtonPanelTester.Tests.fsproj`.
- T007 — issue [#14](https://github.com/luca-veronelli-stem/button-panel-tester/issues/14):
  create Lean 4 workspace skeleton (`lean/lakefile.toml`, `lean/lean-toolchain`).
- T008 — issue [#15](https://github.com/luca-veronelli-stem/button-panel-tester/issues/15):
  create test partitioning folders with `.gitkeep` placeholders.
- T009 — issue [#16](https://github.com/luca-veronelli-stem/button-panel-tester/issues/16):
  create `appsettings.json` + `appsettings.Development.json` for the GUI.

## Exit state

- Solution restores and builds green in Release configuration.
- `dotnet test` still passes (only `PlaceholderTests`).
- No behaviour change introduced.

## Protocol customization

The `resolve-ticket` lifecycle for Phase 1 starts at `WorkRequired`. The
spec, plan, tasks, research, data model, contracts, and quickstart artifacts
for spec 001 already exist on `main`, ratified against constitution v1.0.1
(commit `c7f2d1d`). Re-running speckit-specify / speckit-plan / speckit-tasks
per phase-PR would rewrite shared feature artifacts six times and is
forbidden. The reviewer's per-commit consistency check is: the durable diff
matches the named T-task in `../tasks.md` and respects constitution
Principles I–VI.
