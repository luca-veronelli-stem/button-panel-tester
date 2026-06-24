#Requires -Version 7
$ErrorActionPreference = 'Stop'

# Exit-code discipline: EAP 'Stop' governs PowerShell errors only -- native
# commands (git, dotnet, lake, ...) never trigger it, and $LASTEXITCODE is
# last-command-wins. Capture the exit code after EVERY native check, report
# failures via Write-Host (Write-Error under EAP Stop throws and aborts the
# caller's compound statement), and end with an explicit exit so callers --
# including GitHub Actions' `shell: pwsh`, which appends `exit $LASTEXITCODE`
# to every run: step -- see the gate's verdict, not the last command's.

$failures = @()

# Universal: catch whitespace errors in the diff
git diff --check
if ($LASTEXITCODE -ne 0) { $failures += 'git diff --check' }

# Build the whole solution in Release (src + tests + GUI). This compiles the new
# Category=Hardware suite too (env-gated; excluded from the test legs below).
dotnet build Stem.ButtonPanelTester.slnx -c Release
if ($LASTEXITCODE -ne 0) { $failures += 'dotnet build (slnx, Release)' }

# Test legs are per-project (standards dotnet-ci.yml). Cross-platform *.Tests
# run WITH --framework net10.0; Windows-only *.Tests.Windows run WITHOUT it
# (NETSDK1005 guard: a net10.0-windows-only project has no net10.0 target).
# Category!=Hardware excludes the bench suite (it needs a real PEAK adapter +
# panel; validated at the rig per quickstart, NOT in CI).

# Cross-platform leg: Core/Services FsCheck + xUnit + integration. No hardware.
dotnet test tests/ButtonPanelTester.Tests/ButtonPanelTester.Tests.fsproj --framework net10.0 -c Release --filter "Category!=Hardware"
if ($LASTEXITCODE -ne 0) { $failures += 'dotnet test ButtonPanelTester.Tests' }

# Windows leg: Infrastructure/GUI/Integration + the hardware suite (excluded by
# the filter). Hardware excluded, no --framework.
dotnet test tests/ButtonPanelTester.Tests.Windows/ButtonPanelTester.Tests.Windows.fsproj -c Release --filter "Category!=Hardware"
if ($LASTEXITCODE -ne 0) { $failures += 'dotnet test ButtonPanelTester.Tests.Windows' }

# Lean formalization (T041): every Phase 4 theorem compiles with no sorry.
Push-Location lean
lake build
if ($LASTEXITCODE -ne 0) { $failures += 'lake build' }
Pop-Location

# Phase G focused proof: the full button-press regression still green after the
# polish/docs edits (the hardware suite itself is bench-validated, not gated).
dotnet test tests/ButtonPanelTester.Tests/ButtonPanelTester.Tests.fsproj --framework net10.0 -c Release --filter "Category!=Hardware&FullyQualifiedName~ButtonPress"
if ($LASTEXITCODE -ne 0) { $failures += 'focused: ButtonPress regression' }

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "FAIL: $_" }
    Write-Host "gate.ps1: $($failures.Count) check(s) failed"
    exit 1
}
Write-Host 'gate.ps1: all checks green'
exit 0
