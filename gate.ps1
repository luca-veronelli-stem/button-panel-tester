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

# Whole-solution Release build (CI parity).
dotnet build Stem.ButtonPanelTester.slnx -c Release
if ($LASTEXITCODE -ne 0) { $failures += 'dotnet build' }

# Cross-platform test project (net10.0).
dotnet test tests/ButtonPanelTester.Tests/ButtonPanelTester.Tests.fsproj -c Release --no-build
if ($LASTEXITCODE -ne 0) { $failures += 'dotnet test (cross-platform)' }

# Windows test project, excluding the hardware-gated bench suite (CI default).
dotnet test tests/ButtonPanelTester.Tests.Windows/ButtonPanelTester.Tests.Windows.fsproj -c Release --no-build --filter "Category!=Hardware"
if ($LASTEXITCODE -ne 0) { $failures += 'dotnet test (windows, Category!=Hardware)' }

# Focused baptism fast-signal: the spec-004 logic suites (always present on
# this branch, so the gate stays green from the gate-add commit onward).
dotnet test tests/ButtonPanelTester.Tests/ButtonPanelTester.Tests.fsproj -c Release --no-build --filter "FullyQualifiedName~Baptism"
if ($LASTEXITCODE -ne 0) { $failures += 'baptism focused filter' }

# Lean Phase 3 formalization.
Push-Location lean
lake build
if ($LASTEXITCODE -ne 0) { $failures += 'lake build' }
Pop-Location

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "FAIL: $_" }
    Write-Host "gate.ps1: $($failures.Count) check(s) failed"
    exit 1
}
Write-Host 'gate.ps1: all checks green'
exit 0
