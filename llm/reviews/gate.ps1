#!/usr/bin/env pwsh
# Local quality gate for the resolve-ticket-supervised protocol.
# Worker runs this before every handoff. Fail-fast.

$ErrorActionPreference = 'Stop'

Write-Host '== gate: dotnet build -c Release =='
dotnet build -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host '== gate: dotnet test -c Release --no-build =='
dotnet test -c Release --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host '== gate: dotnet format --verify-no-changes =='
dotnet format --verify-no-changes
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host '== gate: PASS =='
