<#
.SYNOPSIS
    Generates DocFX benchmark pages from BDN output files — one page per suite.

.DESCRIPTION
    For each machine directory under bench/, scans all completed runs and keeps the
    newest run per suite. Writes one markdown page per suite into docs/benchmarks/
    and generates a toc.yml listing all suites.

    Run this before docfx build; docs.ps1 calls it automatically.

.PARAMETER BenchDir
    Path to the bench/ directory. Defaults to ../bench relative to the script.

.PARAMETER OutputDir
    Path to write the generated files. Defaults to ../docs/benchmarks.

.EXAMPLE
    .\scripts\generate-benchmark-docs.ps1
    Generates per-suite pages from all machines' latest runs.
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
    'aggregation'         = 'Aggregation'
    'analysis'            = 'Analysis'
    'analysis-filters'    = 'Analysis filters'
    'analysis-filters-v2' = 'Analysis filters v2'
    'analysis-parity'     = 'Analysis parity'
    'async-index'         = 'Async index'
    'blockjoin'           = 'Block-Join'
    'blockjoin-index'     = 'Block-Join (index)'
    'blockjoin-search'    = 'Block-Join (search)'
    'boolean'             = 'Boolean queries'
    'collapse-facet'      = 'Collapse and facet'
    'combined'            = 'Combined queries'
    'compound-index'      = 'Compound file (index)'
    'compound-search'     = 'Compound file (search)'
    'deletion'            = 'Deletion'
    'deletion-commit'     = 'Deletion (commit)'
    'deletion-queue'      = 'Deletion (queue)'
    'dismax'              = 'Disjunction max'
    'function-score'      = 'Function score'
    'fuzzy'               = 'Fuzzy queries'
    'geo'                 = 'Geo queries'
    'gutenberg-analysis'  = 'Gutenberg analysis'
    'gutenberg-index'     = 'Gutenberg index'
    'gutenberg-search'    = 'Gutenberg search'
    'highlighter'         = 'Highlighter'
    'hunspell'            = 'Hunspell'
    'index'               = 'Indexing'
    'indexsort-index'     = 'Index-sort (index)'
    'indexsort-search'    = 'Index-sort (search)'
    'kstemmer'            = 'KStemmer'
    'lightenglish'        = 'Light English stemmer'
    'mlt'                 = 'More like this'
    'multiphrase'         = 'Multi-phrase'
    'ngram'               = 'N-gram'
    'parallel'            = 'Parallel indexing'
    'pattern-tokeniser'   = 'Pattern tokeniser'
    'phrase'              = 'Phrase queries'
    'prefix'              = 'Prefix queries'
    'query'               = 'Term queries'
    'query-cache'         = 'Query cache'
    'range'               = 'Range queries'
    'regexp'              = 'Regexp queries'
    'schemajson'          = 'Schema and JSON'
    'searcher-mgr'        = 'Searcher manager'
    'similarity'          = 'Similarity'
    'span'                = 'Span queries'
    'stemmer'             = 'Stemmer'
    'suggester'           = 'Suggester'
    'synonym'             = 'Synonym'
    'terminset'           = 'Term in set'
    'tv-highlighter'      = 'Term-vector highlighter'
    'vq'                  = 'Vector queries'
    'wildcard'            = 'Wildcard queries'
}

# ── Helpers ───────────────────────────────────────────────────────────────────

# Extracts just the GFM table rows from a BDN markdown file, skipping the
# environment code block that BDN prepends.
function Get-TableContent([string]$path) {
    if (-not (Test-Path $path)) { return $null }
    $lines = Get-Content $path -Encoding UTF8
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\|') {
            return ($lines[$i..($lines.Count - 1)] -join "`n").TrimEnd()
        }
    }
    return $null
}

# ── Collect runs ──────────────────────────────────────────────────────────────

$machines = @(Get-ChildItem $BenchDir -Directory | Where-Object { $_.Name -ne 'data' } | Sort-Object Name)

if ($machines.Count -eq 0) {
    Write-Warning "No machine directories found in $BenchDir"
    exit 0
}

# Map: suiteName -> { runDir, report, generatedAtUtc }
$newestPerSuite = @{}

foreach ($machine in $machines) {
    Write-Host "Scanning: $($machine.Name)" -ForegroundColor Cyan

    # Walk all date/time dirs
    $dateDirs = Get-ChildItem $machine.FullName -Directory | Sort-Object Name -Descending

    foreach ($dateDir in $dateDirs) {
        $timeDirs = Get-ChildItem $dateDir.FullName -Directory | Sort-Object Name -Descending

        foreach ($timeDir in $timeDirs) {
            $reportPath = Join-Path $timeDir.FullName 'report.json'
            if (-not (Test-Path $reportPath)) { continue }

            try {
                $report = Get-Content $reportPath -Raw | ConvertFrom-Json
            } catch {
                Write-Warning "  Failed to parse $reportPath, skipping."
                continue
            }

            if ($report.totalBenchmarkCount -le 0) { continue }

            foreach ($suite in $report.suites) {
                $name = $suite.suiteName

                # Keep the newest run for this suite
                if (-not $newestPerSuite.ContainsKey($name)) {
                    $newestPerSuite[$name] = @{
                        RunDir          = $timeDir.FullName
                        Report          = $report
                        GeneratedAtUtc  = $report.generatedAtUtc
                        Machine         = $machine.Name
                    }
                }
            }
        }
    }
}

if ($newestPerSuite.Count -eq 0) {
    Write-Warning "No suites found in any run."
    exit 0
}

Write-Host "Found $($newestPerSuite.Count) suites across all runs." -ForegroundColor Green

# ── Generate pages ────────────────────────────────────────────────────────────

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

# Remove old generated files (keep any hand-written content, but since these
# are all auto-generated, safe to clear *.md that match our suites)
$existingMd = Get-ChildItem $OutputDir -Filter '*.md' -ErrorAction SilentlyContinue
foreach ($f in $existingMd) {
    Remove-Item $f.FullName -Force
}

$tocEntries = [System.Collections.Generic.List[string]]::new()
$pageCount   = 0

# Sort suites by display name for a stable TOC order
$sortedSuites = $newestPerSuite.GetEnumerator() |
    Sort-Object { $suiteNames[$_.Key] ?? $_.Key }

foreach ($entry in $sortedSuites) {
    $suiteName  = $entry.Key
    $data       = $entry.Value
    $report     = $data.Report
    $runDir     = $data.RunDir
    $machine    = $data.Machine

    # File name: prefix with machine only when multiple machines exist
    if ($machines.Count -gt 1) {
        $fileName = "$machine-$suiteName.md"
    } else {
        $fileName = "$suiteName.md"
    }

    $displayName = if ($suiteNames.ContainsKey($suiteName)) { $suiteNames[$suiteName] } else { $suiteName }

    # Find the BDN markdown file for this suite
    $suiteResultDir = Join-Path $runDir $suiteName
    $mdFiles = @(Get-ChildItem $suiteResultDir -Recurse -Filter '*-report-github.md' -ErrorAction SilentlyContinue)

    if ($mdFiles.Count -eq 0) {
        Write-Warning "  No markdown for '$suiteName', skipping."
        continue
    }

    $tableContent = Get-TableContent $mdFiles[0].FullName

    if ([string]::IsNullOrWhiteSpace($tableContent)) {
        Write-Warning "  No table in $($mdFiles[0].Name), skipping."
        continue
    }

    # Build page
    $runDate     = ([datetime]$report.generatedAtUtc).ToUniversalTime().ToString('d MMMM yyyy HH:mm UTC')
    $commitShort = if ($report.commitHash.Length -gt 7) { $report.commitHash.Substring(0, 7) } else { $report.commitHash }
    $docCount    = $report.provenance.effectiveDocCount

    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine('---')
    [void]$sb.AppendLine("title: Benchmarks - $displayName")
    [void]$sb.AppendLine('---')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("# $displayName")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("**.NET** $($report.dotnetVersion) &nbsp;&middot;&nbsp; **Commit** ``$commitShort`` &nbsp;&middot;&nbsp; $runDate &nbsp;&middot;&nbsp; $($docCount.ToString('N0')) docs")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine($tableContent)
    [void]$sb.AppendLine()

    $outPath = Join-Path $OutputDir $fileName
    $sb.ToString() | Set-Content $outPath -Encoding UTF8
    Write-Host "  $fileName" -ForegroundColor Green
    $pageCount++

    $tocEntries.Add("- name: $displayName")
    $tocEntries.Add("  href: $fileName")
}

# ── toc.yml ───────────────────────────────────────────────────────────────────

if ($tocEntries.Count -gt 0) {
    $tocEntries | Set-Content (Join-Path $OutputDir 'toc.yml') -Encoding UTF8
    Write-Host "Written: toc.yml ($pageCount suites)" -ForegroundColor Green
} else {
    Write-Warning "No pages generated."
}

Write-Host "Done. $pageCount benchmark pages written." -ForegroundColor Cyan
