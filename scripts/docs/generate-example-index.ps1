<#!
.SYNOPSIS
    Generates the examples catalogue from example.yml files under src/examples.
#>
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$examplesRoot = Join-Path $repoRoot 'src/examples'
$outputPath = Join-Path $repoRoot 'docs/examples/index.md'

function Get-ExampleRecord([string]$Path) {
    $values = @{}
    foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
        if ([string]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith('#')) { continue }
        if ($line -notmatch '^([^:#][^:]*):\s*(.+)$') { throw "Unsupported example metadata in '$Path': $line" }
        $values[$Matches[1].Trim()] = $Matches[2].Trim().Trim('"')
    }

    foreach ($key in @('name', 'summary', 'packages', 'run')) {
        if (-not $values.ContainsKey($key) -or [string]::IsNullOrWhiteSpace($values[$key])) {
            throw "Example metadata '$Path' is missing '$key'."
        }
    }

    $projectDirectory = Split-Path $Path -Parent
    $projects = @(Get-ChildItem $projectDirectory -Filter '*.csproj' -File)
    if ($projects.Count -ne 1) { throw "Example '$projectDirectory' must contain exactly one project file." }

    return [pscustomobject]@{
        name = $values['name']; summary = $values['summary']; packages = $values['packages']; run = $values['run']; project = $projects[0].FullName
    }
}

$records = @(Get-ChildItem $examplesRoot -Recurse -Filter example.yml -File | ForEach-Object { Get-ExampleRecord $_.FullName } | Sort-Object name)
if ($records.Count -eq 0) { throw "No example.yml records found under '$examplesRoot'." }

$rows = foreach ($record in $records) {
    '| {0} | {1} | {2} | `{3}` |' -f $record.name, $record.summary, $record.packages, $record.run
}

$content = @"
---
title: Examples
_description: Runnable LeanCorpus and Rowles.Text examples.
---

# Examples

These projects are small, runnable applications. Start with the first index
guide for the shortest path, then use an example when you need an end-to-end
shape.

| Example | What it demonstrates | Packages | Run |
| --- | --- | --- | --- |
$($rows -join "`n")
"@

[void][System.IO.Directory]::CreateDirectory((Split-Path $outputPath -Parent))
[System.IO.File]::WriteAllText($outputPath, $content + [System.Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
Write-Host "Generated $($records.Count) example rows: $outputPath" -ForegroundColor Green
