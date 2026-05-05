<#
.SYNOPSIS
    Generates DocFX benchmark pages from BDN output files.

.DESCRIPTION
    For each machine directory under bench/, finds the latest completed run,
    copies the BDN-generated markdown tables into a single page per machine
    under docs/benchmarks/, and appends the raw report.json as a collapsible
    block. Also generates a toc.yml covering all machines found.

    Run this before docfx build; docs.ps1 calls it automatically.

.PARAMETER BenchDir
    Path to the bench/ directory. Defaults to ../bench relative to the script.

.PARAMETER OutputDir
    Path to write the generated files. Defaults to ../docs/benchmarks.

.EXAMPLE
    .\scripts\generate-benchmark-docs.ps1
    Generates pages for all machines from their latest completed run.
#>
param(
    [string]$BenchDir  = '',
    [string]$OutputDir = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

if ([string]::IsNullOrEmpty($BenchDir))  { $BenchDir  = Join-Path $repoRoot 'bench' }
if ([string]::IsNullOrEmpty($OutputDir)) { $OutputDir = Join-Path $repoRoot 'docs\benchmarks' }

$BenchDir  = [System.IO.Path]::GetFullPath($BenchDir)
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)

# ── Suite display names ───────────────────────────────────────────────────────

$suiteNames = @{
    'analysis'         = 'Analysis'
    'blockjoin'        = 'Block-Join'
    'boolean'          = 'Boolean queries'
    'compound-index'   = 'Compound file (index)'
    'compound-search'  = 'Compound file (search)'
    'deletion'         = 'Deletion'
    'fuzzy'            = 'Fuzzy queries'
    'index'            = 'Indexing'
    'indexsort-index'  = 'Index-sort (index)'
    'indexsort-search' = 'Index-sort (search)'
    'phrase'           = 'Phrase queries'
    'prefix'           = 'Prefix queries'
    'query'            = 'Term queries'
    'schemajson'       = 'Schema and JSON'
    'suggester'        = 'Suggester'
    'wildcard'         = 'Wildcard queries'
}

# ── Helpers ───────────────────────────────────────────────────────────────────

# Extracts just the GFM table rows from a BDN markdown file, skipping the
# environment code block that BDN prepends.
function Get-TableContent([string]$path) {
    $lines = Get-Content $path -Encoding UTF8
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\|') {
            return ($lines[$i..($lines.Count - 1)] -join "`n").TrimEnd()
        }
    }
    return $null
}

# HTML-escapes a string for embedding in a <pre> block.
function ConvertTo-HtmlEncoded([string]$text) {
    return $text -replace '&', '&amp;' -replace '<', '&lt;' -replace '>', '&gt;'
}

# ── Main ──────────────────────────────────────────────────────────────────────

$machines = @(Get-ChildItem $BenchDir -Directory | Sort-Object Name)

if ($machines.Count -eq 0) {
    Write-Warning "No machine directories found in $BenchDir"
    exit 0
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$tocEntries = [System.Collections.Generic.List[string]]::new()

foreach ($machine in $machines) {
    Write-Host "Processing: $($machine.Name)" -ForegroundColor Cyan

    # Find the latest completed run by scanning for report.json files.
    # Directory names are YYYY-MM-DD/HH-MM so lexicographic descending = newest first.
    $reportFiles = Get-ChildItem $machine.FullName -Recurse -Filter 'report.json' |
                   Sort-Object { $_.FullName } -Descending

    $report     = $null
    $reportPath = $null
    $runDir     = $null

    foreach ($rf in $reportFiles) {
        $candidate = Get-Content $rf.FullName -Raw | ConvertFrom-Json
        if ($candidate.totalBenchmarkCount -gt 0) {
            $report     = $candidate
            $reportPath = $rf.FullName
            $runDir     = $rf.DirectoryName
            break
        }
    }

    if ($null -eq $report) {
        Write-Warning "  No completed run found, skipping."
        continue
    }

    $runDate     = ([datetime]$report.generatedAtUtc).ToUniversalTime().ToString('d MMMM yyyy HH:mm UTC')
    $commitShort = if ($report.commitHash.Length -gt 7) { $report.commitHash.Substring(0, 7) } else { $report.commitHash }

    Write-Host "  Run: $($report.runId)  ($($report.totalBenchmarkCount) benchmarks)" -ForegroundColor DarkGray

    $sb = [System.Text.StringBuilder]::new()

    # Front matter
    [void]$sb.AppendLine('---')
    [void]$sb.AppendLine("title: Benchmarks - $($machine.Name)")
    [void]$sb.AppendLine('---')
    [void]$sb.AppendLine()

    # Page header
    [void]$sb.AppendLine("# Benchmarks: $($machine.Name)")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("**.NET** $($report.dotnetVersion) &nbsp;&middot;&nbsp; **Commit** ``$commitShort`` &nbsp;&middot;&nbsp; $runDate &nbsp;&middot;&nbsp; $($report.totalBenchmarkCount) benchmarks")
    [void]$sb.AppendLine()

    # Suite sections — copy BDN markdown tables directly
    foreach ($suite in $report.suites) {
        $displayName = if ($suiteNames.ContainsKey($suite.suiteName)) { $suiteNames[$suite.suiteName] } else { $suite.suiteName }

        $mdFiles = @(Get-ChildItem (Join-Path $runDir $suite.suiteName) -Recurse -Filter '*-report-github.md' -ErrorAction SilentlyContinue)

        if ($mdFiles.Count -eq 0) {
            Write-Warning "  No markdown file for suite '$($suite.suiteName)', skipping."
            continue
        }

        $tableContent = Get-TableContent $mdFiles[0].FullName

        if ([string]::IsNullOrWhiteSpace($tableContent)) {
            Write-Warning "  No table found in $($mdFiles[0].Name), skipping."
            continue
        }

        [void]$sb.AppendLine("## $displayName")
        [void]$sb.AppendLine()
        [void]$sb.AppendLine($tableContent)
        [void]$sb.AppendLine()
    }

    # Collapsible raw JSON block
    $rawJson      = Get-Content $reportPath -Raw
    $encodedJson  = ConvertTo-HtmlEncoded $rawJson

    [void]$sb.AppendLine('<details>')
    [void]$sb.AppendLine('<summary>Full data (report.json)</summary>')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("<pre><code class=`"lang-json`">$encodedJson</code></pre>")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('</details>')

    # Write output
    $outFile = Join-Path $OutputDir "$($machine.Name).md"
    $sb.ToString() | Set-Content $outFile -Encoding UTF8
    Write-Host "  Written: $outFile" -ForegroundColor Green

    $tocEntries.Add("- name: $($machine.Name)")
    $tocEntries.Add("  href: $($machine.Name).md")
}

# toc.yml
if ($tocEntries.Count -gt 0) {
    $tocEntries | Set-Content (Join-Path $OutputDir 'toc.yml') -Encoding UTF8
    Write-Host "Written: $(Join-Path $OutputDir 'toc.yml')" -ForegroundColor Green
}
