<#
.SYNOPSIS
    Validates the shared ADR graph and generates its index, navigation and detail-page relationships.

.DESCRIPTION
    Reads canonical core and Community Server ADRs from docs/content/articles/ADRs.
    Generated relationship blocks, index and TOC are written only to the staging tree.
#>
param(
    [string]$AdrDirectory = '',
    [string]$OutputPath = '',
    [string]$StagingAdrDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ([string]::IsNullOrWhiteSpace($AdrDirectory)) {
    $AdrDirectory = Join-Path $repoRoot 'docs/content/articles/ADRs'
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot 'artifacts/docs/content/articles/ADRs/index.md'
}
$AdrDirectory = [System.IO.Path]::GetFullPath($AdrDirectory)
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
if (-not [string]::IsNullOrWhiteSpace($StagingAdrDirectory)) {
    $StagingAdrDirectory = [System.IO.Path]::GetFullPath($StagingAdrDirectory)
}
$adrLinkRoot = if ([string]::IsNullOrWhiteSpace($StagingAdrDirectory)) { $AdrDirectory } else { $StagingAdrDirectory }

function ConvertFrom-YamlScalar([string]$Value, [string]$Path, [string]$Key) {
    $value = $Value.Trim()
    if ($value.Length -eq 0) { return '' }
    if ($value.StartsWith('"') -and $value.EndsWith('"')) {
        try { return [System.Text.Json.JsonSerializer]::Deserialize($value, [string]) }
        catch { throw "Invalid quoted value for '$Key' in '$Path': $value" }
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
    if ([string]::IsNullOrWhiteSpace($contents)) { return @() }
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

function ConvertTo-AdrDate([string]$Value, [string]$Path, [string]$Key) {
    $date = [DateTime]::MinValue
    if (-not [DateTime]::TryParseExact(
            $Value,
            'yyyy-MM-dd',
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::None,
            [ref]$date)) {
        throw "ADR '$Path' has an invalid $Key date '$Value'."
    }
    return $date
}

function Get-AdrFrontMatter([string]$Path, [string]$RelativePath) {
    $lines = [System.IO.File]::ReadAllLines($Path)
    if ($lines.Count -lt 2 -or $lines[0].Trim() -ne '---') {
        throw "ADR '$Path' does not start with YAML front matter."
    }

    $closingLine = -1
    for ($i = 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i].Trim() -eq '---') { $closingLine = $i; break }
    }
    if ($closingLine -lt 0) { throw "ADR '$Path' has no closing YAML front-matter delimiter." }

    $frontMatter = @{}
    for ($i = 1; $i -lt $closingLine; $i++) {
        $line = $lines[$i]
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        if ($line -notmatch '^([A-Za-z][A-Za-z0-9-]*):(?:\s?(.*))$') {
            throw "Unsupported YAML front-matter line in '$Path': $line"
        }
        $key = $Matches[1]
        if ($frontMatter.ContainsKey($key)) { throw "ADR '$Path' declares '$key' more than once." }
        $frontMatter[$key] = $Matches[2].Trim()
    }

    foreach ($requiredKey in @('adr', 'title', 'date', 'status', 'version-added', 'summary', 'areas')) {
        if (-not $frontMatter.ContainsKey($requiredKey) -or [string]::IsNullOrWhiteSpace([string]$frontMatter[$requiredKey])) {
            throw "ADR '$Path' is missing '$requiredKey' front matter."
        }
    }

    $adrId = ConvertFrom-YamlScalar ([string]$frontMatter['adr']) $Path 'adr'
    if ($adrId -notmatch '^(ADR\d{3}|ADR-S\d{3})$') {
        throw "ADR '$Path' has an invalid ADR identifier '$adrId'."
    }
    $title = ConvertFrom-YamlScalar ([string]$frontMatter['title']) $Path 'title'
    $summary = ConvertFrom-YamlScalar ([string]$frontMatter['summary']) $Path 'summary'
    $versionAdded = ConvertFrom-YamlScalar ([string]$frontMatter['version-added']) $Path 'version-added'
    $status = ConvertFrom-YamlScalar ([string]$frontMatter['status']) $Path 'status'
    if ($status -notin @('Accepted', 'Deprecated', 'Superseded', 'Withdrawn')) {
        throw "ADR '$Path' has an invalid status '$status'."
    }

    $areas = @(ConvertFrom-YamlStringList ([string]$frontMatter['areas']) $Path 'areas')
    if ($areas.Count -eq 0) { throw "ADR '$Path' must declare at least one area." }

    $relationships = @{}
    foreach ($key in @('supersedes', 'depends-on', 'extends', 'relates-to', 'superseded-by')) {
        $relationships[$key] = if ($frontMatter.ContainsKey($key)) {
            @(ConvertFrom-YamlStringList ([string]$frontMatter[$key]) $Path $key)
        } else { @() }
    }

    $dateText = ConvertFrom-YamlScalar ([string]$frontMatter['date']) $Path 'date'
    $date = ConvertTo-AdrDate $dateText $Path 'date'
    $lastUpdated = ''
    $lastUpdatedDate = $date
    if ($frontMatter.ContainsKey('last-updated')) {
        $lastUpdated = ConvertFrom-YamlScalar ([string]$frontMatter['last-updated']) $Path 'last-updated'
        $lastUpdatedDate = ConvertTo-AdrDate $lastUpdated $Path 'last-updated'
        if ($lastUpdatedDate -lt $date) { throw "ADR '$Path' has last-updated earlier than its immutable date." }
    }

    $historyRows = [System.Collections.Generic.List[object]]::new()
    $inChanges = $false
    $changePattern = '^\|\s*(\d{4}-\d{2}-\d{2})\s*\|\s*(Accepted|Clarified|Amended|Deprecated|Superseded|Withdrawn)\s*\|\s*(.*?)\s*\|\s*(.*?)\s*\|$'
    for ($i = $closingLine + 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^## Changes$') { $inChanges = $true; continue }
        if ($inChanges -and $lines[$i] -match '^## ') { break }
        if ($inChanges -and $lines[$i] -match $changePattern) {
            $historyDate = $Matches[1]
            $historyRows.Add([pscustomobject]@{
                Date = ConvertTo-AdrDate $historyDate $Path 'Changes'
                DateText = $historyDate
                Type = $Matches[2]
                Change = $Matches[3]
                Reference = $Matches[4]
            })
        }
    }
    if (-not $inChanges -or $historyRows.Count -eq 0) {
        throw "ADR '$Path' must contain a Changes table."
    }
    if ($historyRows[0].Type -ne 'Accepted' -or $historyRows[0].DateText -ne $dateText) {
        throw "ADR '$Path' must begin Changes with its original Accepted decision date."
    }
    $previousDate = $date
    foreach ($row in $historyRows) {
        if ($row.Date -lt $previousDate) { throw "ADR '$Path' Changes rows are not in chronological order." }
        if ([string]::IsNullOrWhiteSpace($row.Change) -or [string]::IsNullOrWhiteSpace($row.Reference)) {
            throw "ADR '$Path' has a Changes row without a description or reference."
        }
        $previousDate = $row.Date
    }
    if ($historyRows.Count -gt 1 -and [string]::IsNullOrWhiteSpace($lastUpdated)) {
        throw "ADR '$Path' requires last-updated for material Changes rows after acceptance."
    }
    if (-not [string]::IsNullOrWhiteSpace($lastUpdated) -and $historyRows[$historyRows.Count - 1].DateText -ne $lastUpdated) {
        throw "ADR '$Path' last-updated must match its most recent Changes row."
    }

    $number = if ($adrId -match '^ADR(\d{3})$') { [int]$Matches[1] } else { [int]$adrId.Substring(5) }
    return [pscustomobject]@{
        adr = $adrId
        title = $title
        summary = $summary
        date = $dateText
        lastUpdated = $lastUpdated
        dateValue = $date
        status = $status
        versionAdded = $versionAdded
        areas = $areas
        relationships = $relationships
        file = $RelativePath.Replace('\', '/')
        number = $number
        namespaceOrder = if ($adrId.StartsWith('ADR-S')) { 1 } else { 0 }
    }
}

function Escape-Html([string]$Value) {
    return [System.Net.WebUtility]::HtmlEncode($Value)
}

function Format-AdrLinks([string[]]$Ids, [string]$BaseDirectory) {
    $links = [System.Collections.Generic.List[string]]::new()
    foreach ($id in $Ids) {
        $target = $adrsById[$id]
        $targetPath = Join-Path $adrLinkRoot $target.file
        $relative = [System.IO.Path]::GetRelativePath($BaseDirectory, $targetPath).Replace('\', '/')
        $link = '<a href="{0}">{1}</a>' -f @((Escape-Html $relative), (Escape-Html $id))
        $links.Add($link)
    }
    return ($links -join ', ')
}

if (-not (Test-Path $AdrDirectory -PathType Container)) { throw "ADR directory not found: $AdrDirectory" }
$adrFiles = @(Get-ChildItem $AdrDirectory -File -Recurse -Filter 'ADR*.md' | Sort-Object FullName)
if ($adrFiles.Count -eq 0) { throw "No ADR files found in $AdrDirectory" }

$adrs = [System.Collections.Generic.List[object]]::new()
foreach ($adrFile in $adrFiles) {
    $relativePath = [System.IO.Path]::GetRelativePath($AdrDirectory, $adrFile.FullName)
    $adrs.Add((Get-AdrFrontMatter $adrFile.FullName $relativePath))
}
$orderedAdrs = @($adrs | Sort-Object namespaceOrder, number)
$adrsById = @{}
$incomingSupersedes = @{}
foreach ($adr in $orderedAdrs) {
    if ($adrsById.ContainsKey($adr.adr)) {
        throw "Found duplicate ADR identifier '$($adr.adr)' in '$($adrsById[$adr.adr].file)' and '$($adr.file)'."
    }
    $adrsById[$adr.adr] = $adr
    $incomingSupersedes[$adr.adr] = [System.Collections.Generic.List[string]]::new()
}

$relationshipKeys = @('supersedes', 'depends-on', 'extends', 'relates-to')
foreach ($adr in $orderedAdrs) {
    $declaredTargets = @{}
    foreach ($key in $relationshipKeys) {
        $targets = @($adr.relationships[$key])
        $withinRelation = @{}
        foreach ($target in $targets) {
            if ($withinRelation.ContainsKey($target)) { throw "ADR '$($adr.adr)' repeats '$target' under '$key'." }
            $withinRelation[$target] = $true
            if ($declaredTargets.ContainsKey($target)) {
                throw "ADR '$($adr.adr)' declares '$target' under more than one relationship type."
            }
            $declaredTargets[$target] = $key
            if ($target -eq $adr.adr) { throw "ADR '$($adr.adr)' cannot refer to itself under '$key'." }
            if (-not $adrsById.ContainsKey($target)) { throw "ADR '$($adr.adr)' references missing ADR '$target'." }
            if ($key -eq 'supersedes') {
                if ($adr.dateValue -le $adrsById[$target].dateValue) {
                    throw "ADR '$($adr.adr)' must be later than the ADR it supersedes ('$target')."
                }
                $incomingSupersedes[$target].Add($adr.adr)
            }
            if ($key -in @('depends-on', 'extends') -and $adrsById[$target].status -in @('Superseded', 'Withdrawn')) {
                throw "ADR '$($adr.adr)' has an unresolved '$key' relationship to retired ADR '$target'."
            }
        }
    }
}

foreach ($adr in $orderedAdrs) {
    $incoming = @($incomingSupersedes[$adr.adr] | Sort-Object -Unique)
    $explicit = @($adr.relationships['superseded-by'] | Sort-Object -Unique)
    if ($explicit.Count -gt 0 -and ($explicit -join ',') -ne ($incoming -join ',')) {
        throw "ADR '$($adr.adr)' superseded-by metadata disagrees with incoming supersedes relationships."
    }
    if ($adr.status -eq 'Superseded' -and $incoming.Count -eq 0) {
        throw "Superseded ADR '$($adr.adr)' has no later superseding decision."
    }
    if ($adr.status -eq 'Accepted' -and $incoming.Count -gt 0) {
        throw "Accepted ADR '$($adr.adr)' is the target of a supersession and must be reviewed."
    }
}

foreach ($start in $orderedAdrs) {
    $visited = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $pending = [System.Collections.Generic.Stack[string]]::new()
    foreach ($target in $start.relationships['supersedes']) { $pending.Push($target) }
    while ($pending.Count -gt 0) {
        $current = $pending.Pop()
        if ($current -eq $start.adr) { throw "ADR supersession cycle contains '$($start.adr)'." }
        if (-not $visited.Add($current)) { continue }
        foreach ($target in $adrsById[$current].relationships['supersedes']) { $pending.Push($target) }
    }
}

$indexDirectory = Split-Path $OutputPath -Parent
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[void][System.IO.Directory]::CreateDirectory($indexDirectory)

function New-AdrTable([object[]]$Records) {
    $rows = [System.Collections.Generic.List[string]]::new()
    foreach ($adr in $Records) {
        $targetPath = Join-Path $adrLinkRoot $adr.file
        $href = [System.IO.Path]::GetRelativePath($indexDirectory, $targetPath).Replace('\', '/')
        $supersedes = Format-AdrLinks @($adr.relationships['supersedes']) $indexDirectory
        $lastUpdated = if ($adr.lastUpdated) { Escape-Html $adr.lastUpdated } else { '' }
        $row = '<tr><td><a href="{0}">{1}</a></td><td>{2}</td><td>{3}</td><td>{4}</td><td>{5}</td><td>{6}</td></tr>' -f @(
            (Escape-Html $href)
            (Escape-Html $adr.adr)
            (Escape-Html $adr.date)
            $lastUpdated
            (Escape-Html $adr.status)
            $supersedes
            (Escape-Html $adr.title)
        )
        $rows.Add($row)
    }
    return @(
        '<div class="table-responsive">'
        '<table class="table table-sm table-striped adr-index-table">'
        '<thead><tr><th>ADR</th><th>Date</th><th>Last updated</th><th>Status</th><th>Supersedes</th><th>Decision</th></tr></thead>'
        '<tbody>'
        ($rows -join "`n")
        '</tbody></table></div>'
    ) -join "`n"
}

$coreAdrs = @($orderedAdrs | Where-Object { $_.namespaceOrder -eq 0 })
$serverAdrs = @($orderedAdrs | Where-Object { $_.namespaceOrder -eq 1 })
$content = @"
---
title: Architecture Decision Records
_description: Recorded LeanCorpus and Community Server architecture decisions and their status.
---

# Architecture Decision Records

This index is generated from the core and Community Server ADR records. Their
original decisions remain canonical in the linked detail pages.

## Core decisions

$(New-AdrTable $coreAdrs)

## Community Server decisions

$(New-AdrTable $serverAdrs)

## Template

New records should follow [the shared template](_template.md). Core identifiers
use the reserved `ADRnnn` sequence; Server identifiers use `ADR-Snnn`.
"@
[System.IO.File]::WriteAllText($OutputPath, $content + [Environment]::NewLine, $utf8NoBom)

$tocLines = [System.Collections.Generic.List[string]]::new()
$tocLines.Add('- name: Architecture Decision Records')
$tocLines.Add('  href: index.md')
$tocLines.Add('  items:')
foreach ($adr in $coreAdrs) {
    $name = "$($adr.adr): $($adr.title)".Replace("'", "''")
    $tocLines.Add("    - name: '$name'")
    $tocLines.Add("      href: $($adr.file)")
}
$tocLines.Add("    - name: 'Community Server decisions'")
$tocLines.Add('      href: index.md#community-server-decisions')
$tocLines.Add('      items:')
foreach ($adr in $serverAdrs) {
    $name = "$($adr.adr): $($adr.title)".Replace("'", "''")
    $tocLines.Add("        - name: '$name'")
    $tocLines.Add("          href: $($adr.file)")
}
$tocLines.Add("    - name: 'Shared ADR template'")
$tocLines.Add('      href: _template.md')
$tocPath = Join-Path $indexDirectory 'toc.yml'
[System.IO.File]::WriteAllLines($tocPath, $tocLines, $utf8NoBom)

if (-not [string]::IsNullOrWhiteSpace($StagingAdrDirectory)) {
    foreach ($adr in $orderedAdrs) {
        $stagedPath = [System.IO.Path]::GetFullPath((Join-Path $StagingAdrDirectory $adr.file))
        if (-not (Test-Path -LiteralPath $stagedPath -PathType Leaf)) {
            throw "Staged ADR is missing: $stagedPath"
        }
        $stagedContent = [System.IO.File]::ReadAllText($stagedPath)
        $stagedContent = [regex]::Replace($stagedContent, '(?ms)^## Relationships\r?\n.*?(?=^## |\z)', '')
        $relationshipLines = [System.Collections.Generic.List[string]]::new()
        $relationshipLines.Add('## Relationships')
        $relationshipLines.Add('')
        $displayRelations = @(
            @{ Key = 'supersedes'; Label = 'Supersedes' },
            @{ Key = 'depends-on'; Label = 'Depends on' },
            @{ Key = 'extends'; Label = 'Extends' }
        )
        foreach ($relation in $displayRelations) {
            $ids = @($adr.relationships[$relation.Key])
            if ($ids.Count -gt 0) {
                $base = Split-Path $stagedPath -Parent
                $relationshipLines.Add("- **$($relation.Label):** $(Format-AdrLinks $ids $base)")
            }
        }

        $relatedIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        foreach ($id in $adr.relationships['relates-to']) { [void]$relatedIds.Add($id) }
        foreach ($candidate in $orderedAdrs) {
            if ($candidate.relationships['relates-to'] -contains $adr.adr) { [void]$relatedIds.Add($candidate.adr) }
        }
        $related = @($relatedIds | Sort-Object)
        if ($related.Count -gt 0) {
            $relationshipLines.Add("- **Relates to:** $(Format-AdrLinks $related (Split-Path $stagedPath -Parent))")
        }
        $replacedBy = @($incomingSupersedes[$adr.adr] | Sort-Object -Unique)
        if ($replacedBy.Count -gt 0) {
            $relationshipLines.Add("- **Superseded by:** $(Format-AdrLinks $replacedBy (Split-Path $stagedPath -Parent))")
        }
        if ($relationshipLines.Count -eq 2) { $relationshipLines.Add('No recorded relationships.') }
        $relationshipBlock = ($relationshipLines -join [Environment]::NewLine) + [Environment]::NewLine + [Environment]::NewLine
        $contextMatch = [regex]::Match($stagedContent, '(?m)^## Context\s*$')
        if (-not $contextMatch.Success) { throw "Staged ADR '$($adr.adr)' has no Context section for generated relationships." }
        $stagedContent = $stagedContent.Insert($contextMatch.Index, $relationshipBlock)
        [System.IO.File]::WriteAllText($stagedPath, $stagedContent, $utf8NoBom)
    }
}

Write-Host "Validated and generated $($orderedAdrs.Count) core and Server ADR records: $OutputPath" -ForegroundColor Green
