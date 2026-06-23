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
#
# Test legs are per-project, mirroring the standards dotnet-ci.yml (#82).
# Never pass --framework net10.0 at solution scope: it overrides each
# project's TFM, so a net10.0-windows-only test project fails NETSDK1005
# (its build has no net10.0 target to load). Run cross-platform test
# projects WITH --framework net10.0 and Windows-only ones WITHOUT it (they
# use their own net10.0-windows TFM). The <App>.Tests.<Platform> naming is
# load-bearing here (standards TESTING.md).
#
# # Cross-platform leg: *.Tests projects, minus *.Tests.Windows / *.Tests.Linux.
# foreach ($p in Get-ChildItem tests -Recurse -File -Include '*.Tests*.csproj','*.Tests*.fsproj' |
#         Where-Object { $_.Name -notmatch '\.Tests\.(Windows|Linux)\.' }) {
#     dotnet test $p.FullName --framework net10.0 -c Release
#     if ($LASTEXITCODE -ne 0) { $failures += "dotnet test $($p.Name)" }
# }
#
# # Windows-only leg: *.Tests.Windows projects, no --framework (NETSDK1005 guard).
# # No matches (no Windows-only tests) -> loop never runs, same as before.
# foreach ($p in Get-ChildItem tests -Recurse -File -Include '*.Tests*.csproj','*.Tests*.fsproj' |
#         Where-Object { $_.Name -match '\.Tests\.Windows\.' }) {
#     dotnet test $p.FullName -c Release
#     if ($LASTEXITCODE -ne 0) { $failures += "dotnet test $($p.Name)" }
# }
#
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
