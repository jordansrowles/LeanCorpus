<#
.SYNOPSIS
    Generates the Architecture Decision Records index from ADR front matter.

.DESCRIPTION
    Reads ADR Markdown files under docs/articles/ADRs and replaces index.md
    with a static HTML table. Individual ADR pages remain the source of truth
    for their title, date, status and classification metadata.

.PARAMETER AdrDirectory
    Directory containing ADR Markdown files.

.PARAMETER OutputPath
    Path of the generated ADR index.
#>
param(
    [string]$AdrDirectory = '',
    [string]$OutputPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($AdrDirectory)) {
    $AdrDirectory = Join-Path $repoRoot 'docs/articles/ADRs'
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $AdrDirectory 'index.md'
}

$AdrDirectory = [System.IO.Path]::GetFullPath($AdrDirectory)
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)

function ConvertFrom-YamlScalar([string]$Value, [string]$Path, [string]$Key) {
    $value = $Value.Trim()
    if ($value.Length -eq 0) {
        return ''
    }

    if ($value.StartsWith('"') -and $value.EndsWith('"')) {
        try {
            return [System.Text.Json.JsonSerializer]::Deserialize($value, [string])
        } catch {
            throw "Invalid quoted value for '$Key' in '$Path': $value"
        }
    }

    if ($value.StartsWith("'") -and $value.EndsWith("'")) {
        return $value.Substring(1, $value.Length - 2).Replace("''", "'")
    }

    return $value
}

function ConvertFrom-YamlStringList([string]$Value, [string]$Path, [string]$Key) {
    $value = $Value.Trim()
    if (-not $value.StartsWith('[') -or -not $value.EndsWith(']')) {
        throw "'$Key' in '$Path' must be an inline YAML list."
    }

    $contents = $value.Substring(1, $value.Length - 2).Trim()
    if ([string]::IsNullOrWhiteSpace($contents)) {
        return @()
    }

    return @(
        foreach ($item in $contents.Split(',')) {
            $parsedItem = ConvertFrom-YamlScalar $item $Path $Key
            if ([string]::IsNullOrWhiteSpace($parsedItem)) {
                throw "'$Key' in '$Path' contains an empty item."
            }
            $parsedItem
        }
    )
}

function Get-AdrFrontMatter([string]$Path) {
    $lines = [System.IO.File]::ReadAllLines($Path)
    if ($lines.Count -lt 2 -or $lines[0].Trim() -ne '---') {
        throw "ADR '$Path' does not start with YAML front matter."
    }

    $closingLine = -1
    for ($i = 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i].Trim() -eq '---') {
            $closingLine = $i
            break
        }
    }
    if ($closingLine -lt 0) {
        throw "ADR '$Path' has no closing YAML front-matter delimiter."
    }

    $frontMatter = @{}
    for ($i = 1; $i -lt $closingLine; $i++) {
        $line = $lines[$i]
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }
        if ($line -notmatch '^([^:#][^:]*):(?:\s?(.*))$') {
            throw "Unsupported YAML front-matter line in '$Path': $line"
        }

        $key = $Matches[1].Trim()
        $frontMatter[$key] = ConvertFrom-YamlScalar $Matches[2] $Path $key
    }

    foreach ($requiredKey in @('adr', 'title', 'date', 'status', 'version-added', 'summary', 'areas')) {
        if (-not $frontMatter.ContainsKey($requiredKey) -or [string]::IsNullOrWhiteSpace($frontMatter[$requiredKey])) {
            throw "ADR '$Path' is missing '$requiredKey' front matter."
        }
    }

    if ($frontMatter['adr'] -notmatch '^ADR\d{3}$') {
        throw "ADR '$Path' has an invalid ADR identifier '$($frontMatter['adr'])'."
    }

    if ($frontMatter['status'] -notin @('Accepted', 'Deprecated')) {
        throw "ADR '$Path' has an invalid status '$($frontMatter['status'])'."
    }

    $frontMatter['areas'] = ConvertFrom-YamlStringList $frontMatter['areas'] $Path 'areas'
    if ($frontMatter['areas'].Count -eq 0) {
        throw "ADR '$Path' must declare at least one area."
    }

    $date = [DateTime]::MinValue
    if (-not [DateTime]::TryParseExact(
            $frontMatter['date'],
            'yyyy-MM-dd',
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::None,
            [ref]$date)) {
        throw "ADR '$Path' has an invalid date '$($frontMatter['date'])'."
    }

    return $frontMatter
}

function Escape-Html([string]$Value) {
    return [System.Net.WebUtility]::HtmlEncode($Value)
}

if (-not (Test-Path $AdrDirectory -PathType Container)) {
    throw "ADR directory not found: $AdrDirectory"
}

$adrFiles = @(Get-ChildItem $AdrDirectory -File -Filter 'ADR*.md' | Sort-Object Name)
if ($adrFiles.Count -eq 0) {
    throw "No ADR files found in $AdrDirectory"
}

$adrs = @(
    foreach ($adrFile in $adrFiles) {
        $frontMatter = Get-AdrFrontMatter $adrFile.FullName
        $status = $frontMatter['status']
        if ($frontMatter.ContainsKey('superseded-by') -and -not [string]::IsNullOrWhiteSpace($frontMatter['superseded-by'])) {
            $status = 'Superseded'
        }

        [pscustomobject]@{
            number = [int]$frontMatter['adr'].Substring(3)
            adr = $frontMatter['adr']
            display = $frontMatter['adr'].Substring(3)
            title = $frontMatter['title']
            date = $frontMatter['date']
            status = $status
            file = $adrFile.Name
            supersedes = if ($frontMatter.ContainsKey('supersedes')) { $frontMatter['supersedes'] } else { '' }
            supersededBy = if ($frontMatter.ContainsKey('superseded-by')) { $frontMatter['superseded-by'] } else { '' }
        }
    }
)

$orderedAdrs = @($adrs | Sort-Object number)
$adrsById = @{}
foreach ($adr in $orderedAdrs) {
    $adrsById[$adr.adr] = $adr
}

foreach ($adr in $orderedAdrs) {
    foreach ($reference in @($adr.supersedes, $adr.supersededBy)) {
        if (-not [string]::IsNullOrWhiteSpace($reference) -and -not $adrsById.ContainsKey($reference)) {
            throw "ADR '$($adr.adr)' references missing ADR '$reference'."
        }
    }
}

$rows = @(
    foreach ($adr in $orderedAdrs) {
        $previous = if (-not [string]::IsNullOrWhiteSpace($adr.supersedes)) { $adrsById[$adr.supersedes] } else { $null }
        $next = if (-not [string]::IsNullOrWhiteSpace($adr.supersededBy)) { $adrsById[$adr.supersededBy] } else { $null }
        $previousLink = if ($null -ne $previous) { '<a href="{0}">{1}</a>' -f (Escape-Html $previous.file), (Escape-Html $previous.display) } else { '' }
        $nextLink = if ($null -ne $next) { '<a href="{0}">{1}</a>' -f (Escape-Html $next.file), (Escape-Html $next.display) } else { '' }

        '<tr><td><a href="{0}">{1}</a></td><td>{2}</td><td>{3}</td><td>{4}</td><td><a href="{0}">{5}</a></td><td>{6}</td></tr>' -f (Escape-Html $adr.file), (Escape-Html $adr.display), (Escape-Html $adr.date), (Escape-Html $adr.status), $previousLink, (Escape-Html $adr.title), $nextLink
    }
)

$content = @"
---
title: Architecture Decision Records
_description: Recorded LeanCorpus architecture decisions and their status.
---

# Architecture Decision Records

<div class="table-responsive">
<table class="table table-sm table-striped adr-index-table">
<thead>
<tr><th>ADR</th><th>Date</th><th>Status</th><th>Previous</th><th>Decision</th><th>Next</th></tr>
</thead>
<tbody>
$($rows -join "`n")
</tbody>
</table>
</div>

## Template

New ADRs should follow [the template](_template.md) using the next available
``ADRnnn`` prefix.

## Reasons for an ADR

Create an ADR when the decision is costly to reverse, trade-off heavy,
cross-cutting or non-obvious. Major changes to index structure, storage
formats, analysis pipelines, concurrency, merging, scoring or query parsing
also need one.
"@

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($OutputPath, $content + [System.Environment]::NewLine, $utf8NoBom)

Write-Host "Generated $($adrs.Count) ADR rows: $OutputPath" -ForegroundColor Green
