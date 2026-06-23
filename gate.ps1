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

# STEM .NET default - uncomment + adjust per repo:
# dotnet build -c Release
# if ($LASTEXITCODE -ne 0) { $failures += 'dotnet build' }
# dotnet test Tests/Tests.csproj --framework net10.0
# if ($LASTEXITCODE -ne 0) { $failures += 'dotnet test' }
# dotnet format --verify-no-changes
# if ($LASTEXITCODE -ne 0) { $failures += 'dotnet format' }

# Lean formalization (for repos with specs/ Lean projects):
# lake build
# if ($LASTEXITCODE -ne 0) { $failures += 'lake build' }

# Ticket-specific proof (extend per ticket, same capture pattern):
# dotnet test Tests/Tests.csproj --filter FullyQualifiedName~<focused-pattern>
# if ($LASTEXITCODE -ne 0) { $failures += '<focused-pattern>' }

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "FAIL: $_" }
    Write-Host "gate.ps1: $($failures.Count) check(s) failed"
    exit 1
}
Write-Host 'gate.ps1: all checks green'
exit 0
