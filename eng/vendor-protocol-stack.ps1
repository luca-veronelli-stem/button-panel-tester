#requires -Version 5.1
<#
.SYNOPSIS
    Vendors the stem-device-manager CAN + protocol stack into
    src/ButtonPanelTester.Infrastructure.Protocol/.

.DESCRIPTION
    One-shot helper for spec-002 (CAN link and panel discovery).
    Re-runnable: overwrites previously-vendored files.

    Steps:
      1. Resolve the prior upstream HEAD (so we can restore it).
      2. Check out the requested SHA in the upstream working tree.
      3. Copy each manifest entry to the local vendor folder.
      4. Restore upstream HEAD.
      5. Write VENDOR.md with the pinned SHA + manifest.
      6. Compute VENDOR.sha256 over every vendored file (the contract's
         pre-commit hash check compares against this sidecar).
      7. Scaffold docs/STOPGAP_VENDORED_PROTOCOL_STACK.md if absent.

    Re-vendoring procedure (when upstream lands a fix or the spec
    needs new files): edit `$ManifestEntries` below, re-run the
    script, regenerate VENDOR.sha256.

    Contract: specs/002-can-link-and-panel-discovery/contracts/vendor-manifest.md

.PARAMETER StemDeviceManagerPath
    Absolute path to a local clone of luca-veronelli-stem/stem-device-manager.

.PARAMETER CommitSha
    40-character commit SHA in the upstream repo to vendor from.

.EXAMPLE
    .\eng\vendor-protocol-stack.ps1 `
        -StemDeviceManagerPath C:\Users\veron\Source\Repos\Stem\stem-device-manager `
        -CommitSha 4700c2db65c858f53b4796971a174508b99bce0a
#>
[CmdletBinding(DefaultParameterSetName = 'Vendor')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Vendor')]
    [string] $StemDeviceManagerPath,

    [Parameter(Mandatory = $true, ParameterSetName = 'Vendor')]
    [ValidatePattern('^[0-9a-fA-F]{7,40}$')]
    [string] $CommitSha,

    # Recompute VENDOR.sha256 over the current vendor tree without
    # re-copying from upstream. Use after a local modification recorded
    # in VENDOR.md (manifest "Local modifications" table) — without it
    # the contract's pre-commit hash check would fail.
    [Parameter(Mandatory = $true, ParameterSetName = 'RehashOnly')]
    [switch] $RehashOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$vendorRoot = Join-Path $repoRoot 'src\ButtonPanelTester.Infrastructure.Protocol'
$stopgapDoc = Join-Path $repoRoot 'docs\STOPGAP_VENDORED_PROTOCOL_STACK.md'

function Write-VendorSha256 {
    param([string] $Root)

    $sha256File = Join-Path $Root 'VENDOR.sha256'
    $entries = Get-ChildItem -Path $Root -Recurse -File |
        Where-Object {
            ($_.Name -notin @('VENDOR.md', 'VENDOR.sha256')) -and
            ($_.FullName -notmatch '\\(bin|obj)\\')
        } |
        Sort-Object { $_.FullName.Substring($Root.Length + 1) } |
        ForEach-Object {
            $rel = $_.FullName.Substring($Root.Length + 1).Replace('\', '/')
            $hash = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            "$hash  $rel"
        }
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $content = ($entries -join "`n") + "`n"
    [System.IO.File]::WriteAllText($sha256File, $content, $utf8NoBom)
    Write-Host "Wrote: $sha256File ($($entries.Count) entries)" -ForegroundColor Green
}

if ($RehashOnly) {
    if (-not (Test-Path $vendorRoot)) {
        throw "Vendor root missing: $vendorRoot"
    }
    Write-VendorSha256 -Root $vendorRoot
    return
}

if (-not (Test-Path $StemDeviceManagerPath)) {
    throw "StemDeviceManagerPath not found: $StemDeviceManagerPath"
}

# Manifest — single source of truth for what crosses from upstream.
# Each entry: @{ Source = '<upstream relative path>'; Dest = '<local relative path under vendorRoot>' }
# Keep aligned with VENDOR.md's manifest table.
$ManifestEntries = @(
    # Core/Interfaces — port contracts.
    @{ Source = 'Core/Interfaces/ICommunicationPort.cs';   Dest = 'Core/Interfaces/ICommunicationPort.cs' }
    @{ Source = 'Core/Interfaces/IDeviceVariantConfig.cs'; Dest = 'Core/Interfaces/IDeviceVariantConfig.cs' }
    @{ Source = 'Core/Interfaces/IPacketDecoder.cs';       Dest = 'Core/Interfaces/IPacketDecoder.cs' }
    @{ Source = 'Core/Interfaces/IProtocolService.cs';     Dest = 'Core/Interfaces/IProtocolService.cs' }

    # Core/Models — protocol DTOs.
    @{ Source = 'Core/Models/AppLayerDecodedEvent.cs';   Dest = 'Core/Models/AppLayerDecodedEvent.cs' }
    @{ Source = 'Core/Models/ChannelKind.cs';            Dest = 'Core/Models/ChannelKind.cs' }
    @{ Source = 'Core/Models/Command.cs';                Dest = 'Core/Models/Command.cs' }
    @{ Source = 'Core/Models/ConnectionState.cs';        Dest = 'Core/Models/ConnectionState.cs' }
    @{ Source = 'Core/Models/DeviceVariant.cs';          Dest = 'Core/Models/DeviceVariant.cs' }
    @{ Source = 'Core/Models/DeviceVariantConfig.cs';    Dest = 'Core/Models/DeviceVariantConfig.cs' }
    @{ Source = 'Core/Models/DictionaryData.cs';         Dest = 'Core/Models/DictionaryData.cs' }
    @{ Source = 'Core/Models/ImmutableArrayEquality.cs'; Dest = 'Core/Models/ImmutableArrayEquality.cs' }
    @{ Source = 'Core/Models/ProtocolAddress.cs';        Dest = 'Core/Models/ProtocolAddress.cs' }
    @{ Source = 'Core/Models/RawPacket.cs';              Dest = 'Core/Models/RawPacket.cs' }
    @{ Source = 'Core/Models/SmartBootDeviceEntry.cs';   Dest = 'Core/Models/SmartBootDeviceEntry.cs' }
    @{ Source = 'Core/Models/TelemetryDataPoint.cs';     Dest = 'Core/Models/TelemetryDataPoint.cs' }
    @{ Source = 'Core/Models/Variable.cs';               Dest = 'Core/Models/Variable.cs' }

    # Services/Protocol — flattened to Services/ in the vendor layout.
    @{ Source = 'Services/Protocol/DictionarySnapshot.cs'; Dest = 'Services/DictionarySnapshot.cs' }
    @{ Source = 'Services/Protocol/NetInfo.cs';            Dest = 'Services/NetInfo.cs' }
    @{ Source = 'Services/Protocol/PacketDecoder.cs';      Dest = 'Services/PacketDecoder.cs' }
    @{ Source = 'Services/Protocol/PacketReassembler.cs';  Dest = 'Services/PacketReassembler.cs' }
    @{ Source = 'Services/Protocol/ProtocolService.cs';    Dest = 'Services/ProtocolService.cs' }

    # Infrastructure.Protocol/Hardware — flattened to Hardware/.
    @{ Source = 'Infrastructure.Protocol/Hardware/CanPort.cs';    Dest = 'Hardware/CanPort.cs' }
    @{ Source = 'Infrastructure.Protocol/Hardware/IPcanDriver.cs'; Dest = 'Hardware/IPcanDriver.cs' }
    @{ Source = 'Infrastructure.Protocol/Hardware/PCANManager.cs'; Dest = 'Hardware/PCANManager.cs' }
)

Write-Host "Vendor target: $vendorRoot" -ForegroundColor Cyan
Write-Host "Upstream     : $StemDeviceManagerPath @ $CommitSha" -ForegroundColor Cyan

# 1+2. Save upstream HEAD and check out the requested SHA.
Push-Location $StemDeviceManagerPath
try {
    $priorBranch = (& git rev-parse --abbrev-ref HEAD 2>$null).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "git rev-parse failed in $StemDeviceManagerPath"
    }
    $priorRef = if ($priorBranch -eq 'HEAD') { (& git rev-parse HEAD).Trim() } else { $priorBranch }
    Write-Host "Upstream HEAD was: $priorRef" -ForegroundColor Yellow

    & git checkout --quiet $CommitSha
    if ($LASTEXITCODE -ne 0) {
        throw "git checkout $CommitSha failed in $StemDeviceManagerPath"
    }
} finally {
    Pop-Location
}

try {
    # 3. Copy each manifest entry, normalising line endings to LF so the
    #    on-disk bytes match what git stores (the repo's .gitattributes
    #    pins .cs to `eol=lf`). Without this the SHA bounces on every
    #    Windows checkout when git rewrites the working tree.
    foreach ($entry in $ManifestEntries) {
        $src = Join-Path $StemDeviceManagerPath ($entry.Source -replace '/', '\')
        if (-not (Test-Path $src)) {
            throw "Manifest entry missing in upstream @ ${CommitSha}: $($entry.Source)"
        }
        $dst = Join-Path $vendorRoot ($entry.Dest -replace '/', '\')
        $dstDir = Split-Path -Parent $dst
        if (-not (Test-Path $dstDir)) {
            New-Item -ItemType Directory -Force -Path $dstDir | Out-Null
        }

        # Byte-level CRLF -> LF rewrite. Safe on binary files (no `r`n pair
        # → no-op) but we only manifest text source anyway.
        $bytes = [System.IO.File]::ReadAllBytes($src)
        $normalised = [System.Collections.Generic.List[byte]]::new($bytes.Length)
        for ($i = 0; $i -lt $bytes.Length; $i++) {
            if ($bytes[$i] -eq 0x0D -and ($i + 1) -lt $bytes.Length -and $bytes[$i + 1] -eq 0x0A) {
                continue
            }
            $normalised.Add($bytes[$i])
        }
        [System.IO.File]::WriteAllBytes($dst, $normalised.ToArray())
        Write-Verbose "Copied: $($entry.Source) -> $($entry.Dest)"
    }
    Write-Host ("Copied {0} files." -f $ManifestEntries.Count) -ForegroundColor Green
} finally {
    # 4. Restore upstream HEAD.
    Push-Location $StemDeviceManagerPath
    try { & git checkout --quiet $priorRef | Out-Null } finally { Pop-Location }
}

# 5. Write VENDOR.md.
$vendorMd = Join-Path $vendorRoot 'VENDOR.md'
$today = (Get-Date -Format 'yyyy-MM-dd')

$manifestRows = $ManifestEntries | ForEach-Object {
    $localFull = Join-Path $vendorRoot ($_.Dest -replace '/', '\')
    $loc = if (Test-Path $localFull) {
        (Get-Content $localFull | Measure-Object -Line).Lines
    } else { 0 }
    "| $($_.Source) | $($_.Dest) | $loc | $today |"
}

$manifestTable = @(
    '| Upstream path | Local path | LOC | Last verified |'
    '|---------------|------------|-----|---------------|'
) + $manifestRows | Out-String

$vendorMdContent = @"
# VENDOR.md — Infrastructure.Protocol

**Upstream**: ``git@github.com:luca-veronelli-stem/stem-device-manager.git``
**Pinned SHA**: ``$CommitSha``
**Vendored on**: $today
**Vendored by**: spec-002 PR-A (issue #113) via ``eng/vendor-protocol-stack.ps1``

## Manifest

$manifestTable

## Local modifications

| File | Lines | Why | Upstream PR |
|------|-------|-----|-------------|
| _none yet — populated by T005_ |  |  |  |

## Removal path

Replace this vendored copy with the ``Stem.Communication`` NuGet once
``stem-device-manager``'s Phase 5 migration completes. Tracking:
https://github.com/luca-veronelli-stem/button-panel-tester/issues/111

## Re-vendoring procedure

See ``specs/002-can-link-and-panel-discovery/contracts/vendor-manifest.md``
section "Re-vendoring procedure". Edit ``$ManifestEntries`` in
``eng/vendor-protocol-stack.ps1`` (the ``ManifestEntries`` array),
re-run the script, then commit the regenerated ``VENDOR.sha256``.
"@

Set-Content -Path $vendorMd -Value $vendorMdContent -Encoding UTF8 -NoNewline
Write-Host "Wrote: $vendorMd" -ForegroundColor Green

# 6. Compute VENDOR.sha256 over every file in the vendor root EXCEPT
#    VENDOR.md / VENDOR.sha256 themselves (per contract rule 4) and the
#    transient build outputs under bin/ and obj/ (which are regenerated
#    on every `dotnet build` and would otherwise make the hash bounce
#    on every developer machine). Shared with -RehashOnly so the same
#    hashing rules apply after a local modification.
Write-VendorSha256 -Root $vendorRoot

# 7. Stopgap waiver doc (scaffold once; manual edits preserved).
if (-not (Test-Path $stopgapDoc)) {
    $docsDir = Split-Path -Parent $stopgapDoc
    if (-not (Test-Path $docsDir)) {
        New-Item -ItemType Directory -Force -Path $docsDir | Out-Null
    }

    $stopgapContent = @"
# Stopgap: vendored ``Infrastructure.Protocol`` C# stack

Per Constitution Principle VI (Stopgap Discipline).

## Violated principle

STEM **LANGUAGE** standard (F# default).

## Rationale

No F#-native CAN stack exists. The legacy ``stem-communication`` library
has 84 open issues (recorded in ``docs/Context/bpt-rollout/CORRECTIONS.md``
section C4). Re-implementing in F# would discard roughly two years of
upstream production hardening in ``stem-device-manager``'s
``Infrastructure.Protocol`` for no near-term return.

## Removal path

Replace with the ``Stem.Communication`` NuGet once
``stem-device-manager`` Phase 5 validates the package in production.

## Tracking issue

https://github.com/luca-veronelli-stem/button-panel-tester/issues/111

## Vendor manifest

See ``src/ButtonPanelTester.Infrastructure.Protocol/VENDOR.md``.
"@

    Set-Content -Path $stopgapDoc -Value $stopgapContent -Encoding UTF8 -NoNewline
    Write-Host "Wrote: $stopgapDoc" -ForegroundColor Green
} else {
    Write-Host "Stopgap doc already exists, leaving unchanged: $stopgapDoc" -ForegroundColor Yellow
}

Write-Host ''
Write-Host 'Vendoring complete. Next steps:' -ForegroundColor Cyan
Write-Host '  1. Inspect the diff (git status / git diff).' -ForegroundColor Cyan
Write-Host '  2. dotnet build -c Release (vendored C# must compile).' -ForegroundColor Cyan
Write-Host '  3. Apply T005 local modification if required.' -ForegroundColor Cyan
Write-Host '  4. Commit (single atomic vendor commit).' -ForegroundColor Cyan
