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

dotnet build Stem.ButtonPanelTester.slnx -c Release
if ($LASTEXITCODE -ne 0) { $failures += 'dotnet build' }

dotnet test tests/ButtonPanelTester.Tests/ButtonPanelTester.Tests.fsproj -c Release --no-build
if ($LASTEXITCODE -ne 0) { $failures += 'dotnet test (Tests)' }

dotnet test tests/ButtonPanelTester.Tests.Windows/ButtonPanelTester.Tests.Windows.fsproj -c Release --no-build --filter "Category!=Hardware"
if ($LASTEXITCODE -ne 0) { $failures += 'dotnet test (Tests.Windows, Category!=Hardware)' }

Push-Location lean
lake build
if ($LASTEXITCODE -ne 0) { $failures += 'lake build' }
Pop-Location

# Ticket-specific proof (extended once C3 lands baptism tests):
# dotnet test tests/ButtonPanelTester.Tests/ButtonPanelTester.Tests.fsproj -c Release --no-build --filter "FullyQualifiedName~Baptism"
# if ($LASTEXITCODE -ne 0) { $failures += 'focused baptism filter' }

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "FAIL: $_" }
    Write-Host "gate.ps1: $($failures.Count) check(s) failed"
    exit 1
}
Write-Host 'gate.ps1: all checks green'
exit 0
