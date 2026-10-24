Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$generatorPath = Join-Path $PSScriptRoot '../../docs/generate-adr-index.ps1'
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) "leancorpus-adr-index-smoke-$([Guid]::NewGuid().ToString('N'))"

function Write-SmokeAdr {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$Status,
        [Parameter(Mandatory = $true)][string]$Title,
        [string]$Date = '2026-10-08',
        [string[]]$Areas = @('storage', 'search'),
        [string[]]$Relationships = @()
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('---')
    $lines.Add("adr: $Id")
    $lines.Add("title: `"$Title`"")
    $lines.Add("date: $Date")
    $lines.Add("status: $Status")
    $lines.Add('version-added: 4.0.0')
    $lines.Add('summary: "Smoke fixture."')
    $lines.Add("areas: [$($Areas -join ', ')]")
    foreach ($relationship in $Relationships) { $lines.Add($relationship) }
    $lines.Add('---')
    $lines.Add('')
    $lines.Add("# ${Id}: $Title")
    $lines.Add('')
    $lines.Add('## Context')
    $lines.Add('')
    $lines.Add('Fixture context.')
    $lines.Add('')
    $lines.Add('## Changes')
    $lines.Add('')
    $lines.Add('| Date | Type | Change | Reference |')
    $lines.Add('| --- | --- | --- | --- |')
    $lines.Add("| $Date | Accepted | Original decision accepted. | ``smoke`` |")
    [System.IO.Directory]::CreateDirectory((Split-Path $Path -Parent)) | Out-Null
    [System.IO.File]::WriteAllLines($Path, $lines, [System.Text.UTF8Encoding]::new($false))
}

function Assert-Smoke {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "ADR index smoke assertion failed: $Message" }
}

function Invoke-SmokeGenerator([string]$SourceDirectory, [string]$StagingDirectory) {
    $indexPath = Join-Path $StagingDirectory 'index.md'
    & $generatorPath -AdrDirectory $SourceDirectory -OutputPath $indexPath -StagingAdrDirectory $StagingDirectory
}

try {
    $sourceDirectory = Join-Path $temporaryRoot 'Source/ADRs'
    $stagingDirectory = Join-Path $temporaryRoot 'Staging/ADRs'
    [void][System.IO.Directory]::CreateDirectory((Join-Path $sourceDirectory 'Server'))
    [void][System.IO.Directory]::CreateDirectory((Join-Path $stagingDirectory 'Server'))

    Write-SmokeAdr -Path (Join-Path $sourceDirectory 'ADR001-retired.md') `
        -Id 'ADR001' -Status 'Superseded' -Title 'Retired decision' -Date '2026-10-08'
    Write-SmokeAdr -Path (Join-Path $sourceDirectory 'ADR002-current.md') `
        -Id 'ADR002' -Status 'Accepted' -Title 'Current decision' -Date '2026-10-09' `
        -Areas @('storage') -Relationships @('supersedes: [ADR001]')
    Write-SmokeAdr -Path (Join-Path $sourceDirectory 'ADR003-withdrawn.md') `
        -Id 'ADR003' -Status 'Withdrawn' -Title 'Withdrawn decision'
    Write-SmokeAdr -Path (Join-Path $sourceDirectory 'Server/ADR-S001-server.md') `
        -Id 'ADR-S001' -Status 'Accepted' -Title 'Server decision' -Date '2026-10-10' `
        -Relationships @('relates-to: [ADR002]')
    Copy-Item -Path (Join-Path $sourceDirectory '*') -Destination $stagingDirectory -Recurse -Force

    Invoke-SmokeGenerator $sourceDirectory $stagingDirectory
    $index = [System.IO.File]::ReadAllText((Join-Path $stagingDirectory 'index.md'))
    $toc = [System.IO.File]::ReadAllText((Join-Path $stagingDirectory 'toc.yml'))
    $retired = [System.IO.File]::ReadAllText((Join-Path $stagingDirectory 'ADR001-retired.md'))
    $server = [System.IO.File]::ReadAllText((Join-Path $stagingDirectory 'Server/ADR-S001-server.md'))
    Assert-Smoke ($index.Contains('<th>Last updated</th>')) 'The index omits last-updated metadata.'
    Assert-Smoke ($index.Contains('<td>Withdrawn</td>')) 'Withdrawn status is not rendered.'
    Assert-Smoke ($index.Contains('ADR-S001')) 'The shared index omits Server ADRs.'
    Assert-Smoke ($toc.Contains('href: Server/ADR-S001-server.md')) 'The generated TOC omits the prefixed Server ADR.'
    Assert-Smoke ($retired.Contains('Superseded by:') -and $retired.Contains('ADR002-current.md')) `
        'The incoming supersession relationship was not derived for the retired ADR.'
    Assert-Smoke ($server.Contains('Relates to:') -and $server.Contains('../ADR002-current.md')) `
        'The cross-namespace relationship was not linked from the Server ADR.'
    $current = [System.IO.File]::ReadAllText((Join-Path $stagingDirectory 'ADR002-current.md'))
    Assert-Smoke ($current.Contains('Server/ADR-S001-server.md')) `
        'The reverse relates-to edge was not rendered on the core ADR.'

    Write-SmokeAdr -Path (Join-Path $sourceDirectory 'ADR002-duplicate.md') `
        -Id 'ADR002' -Status 'Accepted' -Title 'Duplicate identifier' -Date '2026-10-09'
    $duplicateFailure = ''
    try { Invoke-SmokeGenerator $sourceDirectory $stagingDirectory | Out-Null }
    catch { $duplicateFailure = $_.Exception.Message }
    Assert-Smoke ($duplicateFailure.Contains('duplicate ADR identifier')) `
        "Duplicate identifier was not rejected clearly; received '$duplicateFailure'."

    Remove-Item -LiteralPath (Join-Path $sourceDirectory 'ADR002-duplicate.md') -Force
    Write-SmokeAdr -Path (Join-Path $sourceDirectory 'ADR004-broken-edge.md') `
        -Id 'ADR004' -Status 'Accepted' -Title 'Broken relationship' -Date '2026-10-11' `
        -Relationships @('depends-on: [ADR099]')
    $referenceFailure = ''
    try { Invoke-SmokeGenerator $sourceDirectory $stagingDirectory | Out-Null }
    catch { $referenceFailure = $_.Exception.Message }
    Assert-Smoke ($referenceFailure.Contains("references missing ADR 'ADR099'")) `
        "A missing relationship target was not rejected; received '$referenceFailure'."

    Write-Host 'ADR graph smoke passed (9 assertions).'
} finally {
    if (Test-Path $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
