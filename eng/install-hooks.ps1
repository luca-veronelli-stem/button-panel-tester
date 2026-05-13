#requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent

Push-Location $repoRoot
try {
    Write-Host 'Installing Husky.NET pre-commit hook...' -ForegroundColor Cyan

    if (-not (Test-Path '.config/dotnet-tools.json')) {
        dotnet new tool-manifest | Out-Null
    }

    $tools = & dotnet tool list --local 2>$null
    if (-not ($tools | Select-String '^Husky\b')) {
        dotnet tool install Husky | Out-Null
    }

    dotnet tool restore | Out-Null

    if (-not (Test-Path '.husky/pre-commit')) {
        dotnet husky install
        New-Item -ItemType Directory -Force -Path '.husky' | Out-Null
        @'
#!/usr/bin/env sh
. "$(dirname -- "$0")/_/husky.sh"

dotnet format --verify-no-changes
'@ | Set-Content -Path '.husky/pre-commit' -Encoding utf8
    }

    Write-Host "Done. Pre-commit will run 'dotnet format --verify-no-changes'." -ForegroundColor Green
}
finally {
    Pop-Location
}
