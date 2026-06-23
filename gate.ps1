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

# Build the whole solution in Release (src + tests + GUI).
dotnet build Stem.ButtonPanelTester.slnx -c Release
if ($LASTEXITCODE -ne 0) { $failures += 'dotnet build (slnx, Release)' }

# Test legs are per-project (standards dotnet-ci.yml). Cross-platform *.Tests
# run WITH --framework net10.0; Windows-only *.Tests.Windows run WITHOUT it
# (NETSDK1005 guard: a net10.0-windows-only project has no net10.0 target).

# Cross-platform leg: Core/Services FsCheck + xUnit. No hardware.
dotnet test tests/ButtonPanelTester.Tests/ButtonPanelTester.Tests.fsproj --framework net10.0 -c Release --filter "Category!=Hardware"
if ($LASTEXITCODE -ne 0) { $failures += 'dotnet test ButtonPanelTester.Tests' }

# Windows leg: Infrastructure/GUI/Integration. Hardware excluded, no --framework.
dotnet test tests/ButtonPanelTester.Tests.Windows/ButtonPanelTester.Tests.Windows.fsproj -c Release --filter "Category!=Hardware"
if ($LASTEXITCODE -ne 0) { $failures += 'dotnet test ButtonPanelTester.Tests.Windows' }

# Lean formalization: the Phase 4 theorems must compile with no sorry.
Push-Location lean
lake build
if ($LASTEXITCODE -ne 0) { $failures += 'lake build' }
Pop-Location

# Phase A focused proof: the button-frame codec + press-edge detector suites.
dotnet test tests/ButtonPanelTester.Tests/ButtonPanelTester.Tests.fsproj --framework net10.0 -c Release --filter "Category!=Hardware&(FullyQualifiedName~ButtonStateFrame|FullyQualifiedName~KeyStateBitmap)"
if ($LASTEXITCODE -ne 0) { $failures += 'focused: ButtonStateFrame|KeyStateBitmap' }

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "FAIL: $_" }
    Write-Host "gate.ps1: $($failures.Count) check(s) failed"
    exit 1
}
Write-Host 'gate.ps1: all checks green'
exit 0
