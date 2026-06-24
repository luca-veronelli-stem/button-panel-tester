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

# Build the whole solution in Release.
dotnet build Stem.ButtonPanelTester.slnx -c Release
if ($LASTEXITCODE -ne 0) { $failures += 'dotnet build' }

# Test legs are per-project, mirroring the standards dotnet-ci.yml. Run the
# cross-platform project WITH --framework net10.0 and the Windows-only project
# WITHOUT it (it builds net10.0-windows; --framework net10.0 at that scope
# trips NETSDK1005). Hardware-tagged tests are excluded from both legs.

# Cross-platform leg.
dotnet test tests/ButtonPanelTester.Tests/ButtonPanelTester.Tests.fsproj --framework net10.0 -c Release --filter "Category!=Hardware"
if ($LASTEXITCODE -ne 0) { $failures += 'dotnet test (cross-platform)' }

# Windows-only leg (no --framework; NETSDK1005 guard).
dotnet test tests/ButtonPanelTester.Tests.Windows/ButtonPanelTester.Tests.Windows.fsproj -c Release --filter "Category!=Hardware"
if ($LASTEXITCODE -ne 0) { $failures += 'dotnet test (windows)' }

# Lean formalization. lake build is NOT in CI, so this gate is the only Lean
# check on the branch.
Push-Location lean
lake build
if ($LASTEXITCODE -ne 0) { $failures += 'lake build' }
Pop-Location

# Ticket-specific proof: the button-press observability regression. Fast
# focused signal over the spec-005 button-press suite (extend the filter to
# the new observability tests as they land).
dotnet test tests/ButtonPanelTester.Tests/ButtonPanelTester.Tests.fsproj --framework net10.0 -c Release --filter "FullyQualifiedName~ButtonPress&Category!=Hardware"
if ($LASTEXITCODE -ne 0) { $failures += 'button-press regression' }

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "FAIL: $_" }
    Write-Host "gate.ps1: $($failures.Count) check(s) failed"
    exit 1
}
Write-Host 'gate.ps1: all checks green'
exit 0
