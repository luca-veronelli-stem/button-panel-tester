#Requires -Version 7.0
<#
.SYNOPSIS
    Refresh the embedded dictionary seed bundled with ButtonPanelTester.

.DESCRIPTION
    Operator script that calls the dictionary service's `resolved`
    endpoint with the operator-supplied API key, normalises the
    response into the cache-envelope shape used by
    `JsonFileDictionaryCache`, and writes the result to
    `src/ButtonPanelTester.GUI/Assets/dictionary.seed.json`. The
    output is committed as part of a release build so first-launch
    on a freshly-provisioned machine renders a meaningful status
    row before any live fetch lands.

    The script wraps the wire response (one panel-type object) in a
    `panelTypes: [...]` list, stamps a top-level
    `seededAt: <ISO 8601 UTC>` marker, and leaves `contentHash` as
    the all-zero placeholder so the first live fetch always
    overwrites the cache (no skip-write collision against the
    seed). `fetchedAt` is null in the seed envelope, which signals
    `Origin = FromEmbeddedSeed` to the cache reader.

.PARAMETER BaseUrl
    Production dictionary-service base URL. Defaults to the value in
    `src/ButtonPanelTester.GUI/appsettings.json`
    (`https://app-dictionaries-manager-prod.azurewebsites.net`).
    Pass a different URL when refreshing against a staging instance.

.PARAMETER DictionaryId
    Numeric dictionary id. Defaults to the value in
    `src/ButtonPanelTester.GUI/appsettings.json` (`2`, the
    "Pulsantiere" dictionary).

.PARAMETER OutPath
    Output path for the seed JSON. Defaults to
    `src/ButtonPanelTester.GUI/Assets/dictionary.seed.json` relative
    to the repository root.

.NOTES
    Prerequisites (per `specs/001-fetch-dictionary/research.md`
    "Open follow-ups"):

      - PowerShell 7+ (the script uses `Invoke-RestMethod -SkipHttpErrorCheck`
        and pipeline-chain operators).
      - `$env:STEM_BPT_SEED_REFRESH_KEY` set to the value of
        the `ApiKeys:ButtonPanelTesterSeedRefresh` entry on the
        target dictionary service. On Azure App Service that lives
        in `Configuration > Application settings >
        ApiKeys__ButtonPanelTesterSeedRefresh`; for a local
        `stem-dictionaries-manager` run, the entry sits under
        `ApiKeys:ButtonPanelTesterSeedRefresh` in `appsettings.json`.
        The operator supplies the key per invocation; there is no
        secret-management infrastructure in v1, and the key never
        lands in source.
      - Network access to `BaseUrl`.

    No retries, no exponential backoff — this is a one-shot release
    tool. On any failure the script prints a diagnostic and exits
    non-zero; the existing seed file is left untouched.

    See task T058 in
    `specs/001-fetch-dictionary/tasks.md`.

.EXAMPLE
    $env:STEM_BPT_SEED_REFRESH_KEY = 'tk_...'; .\eng\refresh-seed.ps1

.EXAMPLE
    $env:STEM_BPT_SEED_REFRESH_KEY = 'tk_...'; .\eng\refresh-seed.ps1 -BaseUrl 'https://localhost:7065' -DictionaryId 2
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = 'https://app-dictionaries-manager-prod.azurewebsites.net',
    [int]    $DictionaryId = 2,
    [string] $OutPath
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $OutPath) {
    $OutPath = Join-Path $repoRoot 'src/ButtonPanelTester.GUI/Assets/dictionary.seed.json'
}

$apiKey = $env:STEM_BPT_SEED_REFRESH_KEY
if ([string]::IsNullOrWhiteSpace($apiKey)) {
    Write-Error '$env:STEM_BPT_SEED_REFRESH_KEY is not set. Export the value of the ApiKeys:ButtonPanelTesterSeedRefresh entry from the target dictionary service before running this script.'
    exit 1
}

$uri = '{0}/api/dictionaries/{1}/resolved' -f $BaseUrl.TrimEnd('/'), $DictionaryId
Write-Host "GET $uri" -ForegroundColor Cyan

$headers = @{
    'X-Api-Key' = $apiKey
    'Accept'    = 'application/json'
}

$response = Invoke-RestMethod `
    -Method Get `
    -Uri $uri `
    -Headers $headers `
    -SkipHttpErrorCheck `
    -StatusCodeVariable status `
    -TimeoutSec 120

if ($status -ne 200) {
    Write-Error "Dictionary service returned HTTP $status. Body: $($response | ConvertTo-Json -Depth 5 -Compress)"
    exit 1
}

if (-not $response.variables -or $response.variables.Count -eq 0) {
    Write-Error 'Response had no variables. Refusing to overwrite the seed with an empty payload.'
    exit 1
}

$now = [DateTimeOffset]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz")

# Envelope matches CacheFile in JsonFileDictionaryCache.fs. The
# `contentHash` placeholder is intentional: it guarantees the first
# live fetch overwrites the cache (the real hash will never match
# all-zeros), avoiding the cache-format.md "skip-write optimisation"
# colliding with seed-extraction.
$envelope = [ordered]@{
    schemaVersion = 1
    contentHash   = '0000000000000000000000000000000000000000000000000000000000000000'
    fetchedAt     = $null
    seededAt      = $now
    panelTypes    = @($response)
}

$json = $envelope | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText($OutPath, $json + "`n", [System.Text.UTF8Encoding]::new($false))

Write-Host "Wrote $OutPath" -ForegroundColor Green
Write-Host "  panelTypes:   $($envelope.panelTypes.Count)"
Write-Host "  variables:    $($response.variables.Count)"
Write-Host "  seededAt:     $now"
