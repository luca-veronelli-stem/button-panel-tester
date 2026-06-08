#Requires -Version 7
$ErrorActionPreference = 'Stop'

# Mechanical commit/test gate for the spec-003 panel-discovery PR (issue #201).
# Every bisect-safe slice must pass this before it is returned and accepted.
# Dropped in the final 'chore: drop gate.ps1' commit before the PR is marked ready.

# Universal: catch whitespace errors in the diff.
git diff --check
if ($LASTEXITCODE -ne 0) { throw 'git diff --check found whitespace errors' }

# Build the whole solution (Release).
dotnet build Stem.ButtonPanelTester.slnx -c Release

# Test both projects. The Windows project excludes the bench-only hardware E2E
# (Category=Hardware runs only on a BPT_HARDWARE=1 rig, never in this gate / CI).
dotnet test tests/ButtonPanelTester.Tests/ButtonPanelTester.Tests.fsproj -c Release
dotnet test tests/ButtonPanelTester.Tests.Windows/ButtonPanelTester.Tests.Windows.fsproj -c Release --filter "Category!=Hardware"

# Lean formalization: the Phase-2 discovery theorems must compile with no sorry.
Push-Location lean
try { lake build } finally { Pop-Location }
