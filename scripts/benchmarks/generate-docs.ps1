<#
.SYNOPSIS
    Generates DocFX benchmark pages from BDN output files — one page per suite.

.DESCRIPTION
    Scans completed runs under artifacts/benchmark/runs and keeps the
    newest run per suite and writes benchmark pages plus a local index and TOC
    into the documentation staging tree.

    Run this before docfx build; docs.ps1 calls it automatically.

.PARAMETER BenchDir
    Path to the benchmark run directory. Defaults to artifacts/benchmark/runs.

.PARAMETER OutputDir
    Path to write the generated files. Defaults to artifacts/docs/content/benchmarks.

.PARAMETER SkipRunScan
    Create an empty benchmark overview and TOC without scanning retained runs.

.EXAMPLE
    .\scripts\generate-benchmark-docs.ps1
    Generates per-suite pages from all machines' latest runs.
#>
param(
    [string]$BenchDir  = '',
    [string]$OutputDir = '',
    [string]$ChartJsPath = '',
    [switch]$SkipRunScan
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))

if ([string]::IsNullOrEmpty($BenchDir))  { $BenchDir  = Join-Path $repoRoot 'artifacts/benchmark/runs' }
if ([string]::IsNullOrEmpty($OutputDir)) { $OutputDir = Join-Path $repoRoot 'artifacts/docs/content/benchmarks' }
if ([string]::IsNullOrEmpty($ChartJsPath)) {
    $ChartJsPath = Join-Path $repoRoot 'docs/site-config/diagrams/node_modules/chart.js/dist/chart.umd.js'
}

$BenchDir  = [System.IO.Path]::GetFullPath($BenchDir)
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)
$ChartJsPath = [System.IO.Path]::GetFullPath($ChartJsPath)
if (-not (Test-Path -LiteralPath $ChartJsPath -PathType Leaf)) {
    throw "Pinned Chart.js browser bundle not found at '$ChartJsPath'. Restore the site dependencies with npm ci --prefix docs/site-config/diagrams."
}

[void][System.IO.Directory]::CreateDirectory($OutputDir)
foreach ($stagedItem in @(Get-ChildItem $OutputDir -Force -ErrorAction SilentlyContinue)) {
    Remove-Item -LiteralPath $stagedItem.FullName -Recurse -Force
}

function ConvertTo-BenchmarkYamlString([string]$Value) {
    return "'$($Value.Replace("'", "''"))'"
}

function New-BenchmarkCatalog {
    $pages = @(Get-ChildItem -LiteralPath $OutputDir -File -Filter '*.md' |
        Where-Object { $_.Name -notin @('overview.md', 'toc.yml') } |
        Sort-Object Name)
    $markdown = [System.Collections.Generic.List[string]]::new()
    $markdown.Add('# Benchmarks')
    $markdown.Add('')
    if ($pages.Count -eq 0) {
        $markdown.Add('No benchmark reports are available in the retained run artefacts for this build.')
    } else {
        $markdown.Add('Generated reports from the latest successful retained benchmark runs.')
        $markdown.Add('')
        foreach ($page in $pages) {
            $heading = Get-Content -LiteralPath $page.FullName -Encoding UTF8 |
                Where-Object { $_ -match '^#\s+(.+?)\s*$' } |
                Select-Object -First 1
            $title = if ($heading -match '^#\s+(.+?)\s*$') { $Matches[1].Trim() } else { $page.BaseName }
            $link = [Uri]::EscapeDataString($page.Name)
            $markdown.Add("- [$title]($link)")
        }
    }

    $toc = [System.Collections.Generic.List[string]]::new()
    $toc.Add('- name: Benchmarks')
    $toc.Add('  href: overview.md')
    if ($pages.Count -gt 0) {
        $toc.Add('  items:')
        foreach ($page in $pages) {
            $heading = Get-Content -LiteralPath $page.FullName -Encoding UTF8 |
                Where-Object { $_ -match '^#\s+(.+?)\s*$' } |
                Select-Object -First 1
            $title = if ($heading -match '^#\s+(.+?)\s*$') { $Matches[1].Trim() } else { $page.BaseName }
            $toc.Add("    - name: $(ConvertTo-BenchmarkYamlString $title)")
            $toc.Add("      href: $(ConvertTo-BenchmarkYamlString $page.Name)")
        }
    }

    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllLines((Join-Path $OutputDir 'overview.md'), $markdown, $utf8NoBom)
    [System.IO.File]::WriteAllLines((Join-Path $OutputDir 'toc.yml'), $toc, $utf8NoBom)
}

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
    'parallel'            = 'Parallel search'
    'pattern-tokeniser'   = 'Pattern tokeniser'
    'phrase'              = 'Phrase queries'
    'prefix'              = 'Prefix queries'
    'query'               = 'Term queries'
    'query-cache'         = 'Query cache'
    'range'               = 'Range queries'
    'regexp'              = 'Regexp queries'
    'schemajson'          = 'Schema and JSON'
    'searcher-mgr'        = 'Searcher manager'
    'segment-reader-cache' = 'Segment reader cache'
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

# Collect runs.
function Read-JsonDocument([string]$Path) {
    try {
        return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    } catch {
        Write-Warning "  Failed to parse $Path, skipping."
        return $null
    }
}

function Get-DocumentProperty($Document, [string]$Name, $Default = $null) {
    if ($null -eq $Document) { return $Default }
    $property = $Document.PSObject.Properties[$Name]
    if ($null -eq $property) { return $Default }
    return $property.Value
}

function Test-CoreReportShape($Report) {
    $names = @($Report.PSObject.Properties.Name)
    return 'totalBenchmarkCount' -in $names -and 'suites' -in $names -and 'generatedAtUtc' -in $names
}

$newestPerSuite = @{}
$newestProjectEvidence = @{}
$runDirectories = @(
    if (-not $SkipRunScan -and (Test-Path $BenchDir)) {
        Get-ChildItem $BenchDir -Directory
    }
)
if ($runDirectories.Count -eq 0) {
    Write-Warning "No benchmark runs found in $BenchDir"
}

foreach ($runDirectory in $runDirectories) {
    $runReportPath = Join-Path $runDirectory.FullName 'run-report.json'
    if (-not (Test-Path $runReportPath -PathType Leaf)) { continue }

    $runManifestPath = Join-Path $runDirectory.FullName 'run.json'
    if (-not (Test-Path $runManifestPath -PathType Leaf)) { continue }
    $runManifest = Read-JsonDocument $runManifestPath
    $runReport = Read-JsonDocument $runReportPath
    if ($null -eq $runManifest -or $null -eq $runReport) { continue }

    try {
        $completedAtUtc = [DateTimeOffset]::Parse([string](Get-DocumentProperty $runManifest 'completedAtUtc'))
    } catch {
        Write-Warning "  Invalid completion timestamp in $runManifestPath, skipping."
        continue
    }

    $projects = @(Get-DocumentProperty $runReport 'projects' @())
    foreach ($projectKey in @('core', 'text', 'compression')) {
        $project = @($projects | Where-Object {
            (Get-DocumentProperty $_ 'project' '') -ceq $projectKey
        } | Select-Object -First 1)
        if ($project.Count -eq 0 -or (Get-DocumentProperty $project[0] 'status' '') -cne 'Passed') { continue }

        $relativeProjectPath = [string](Get-DocumentProperty $project[0] 'path' $projectKey)
        $projectDirectory = Join-Path $runDirectory.FullName $relativeProjectPath
        if (-not (Test-Path $projectDirectory -PathType Container)) { continue }

        if ($projectKey -eq 'core') {
            $reportPath = Join-Path $projectDirectory 'report.json'
            if (-not (Test-Path $reportPath -PathType Leaf)) { continue }
            $report = Read-JsonDocument $reportPath
            if ($null -eq $report -or -not (Test-CoreReportShape $report) -or [long]$report.totalBenchmarkCount -le 0) { continue }

            $machineName = [string](Get-DocumentProperty (Get-DocumentProperty $report 'provenance') 'machineName' 'unknown')
            foreach ($suite in @($report.suites)) {
                $name = [string](Get-DocumentProperty $suite 'suiteName' '')
                if (-not $name) { continue }
                if ([long](Get-DocumentProperty $suite 'failedBenchmarkCount' 0) -gt 0 -or
                    [long](Get-DocumentProperty $suite 'missingBenchmarkCount' 0) -gt 0) { continue }
                if (-not $newestPerSuite.ContainsKey($name) -or $completedAtUtc -gt $newestPerSuite[$name].CompletedAtUtc) {
                    $newestPerSuite[$name] = @{
                        RunDir = $projectDirectory
                        Report = $report
                        CompletedAtUtc = $completedAtUtc
                        Machine = $machineName
                    }
                }
            }
            continue
        }

        $markdownFiles = @(Get-ChildItem $projectDirectory -Recurse -File -Filter '*-report-github.md' -ErrorAction SilentlyContinue |
            Where-Object { -not [string]::IsNullOrWhiteSpace((Get-TableContent $_.FullName)) })
        if ($markdownFiles.Count -eq 0) { continue }
        if (-not $newestProjectEvidence.ContainsKey($projectKey) -or
            $completedAtUtc -gt $newestProjectEvidence[$projectKey].CompletedAtUtc) {
            $newestProjectEvidence[$projectKey] = @{
                Directory = $projectDirectory
                CompletedAtUtc = $completedAtUtc
            }
        }
    }
}

# Legacy Core reports remain supported only for runs without run-report.json.
foreach ($runDirectory in @($runDirectories | Where-Object { -not (Test-Path (Join-Path $_.FullName 'run-report.json')) })) {
    foreach ($reportFile in @(Get-ChildItem $runDirectory.FullName -Recurse -File -Filter 'report.json' -ErrorAction SilentlyContinue)) {
        $report = Read-JsonDocument $reportFile.FullName
        if ($null -eq $report -or -not (Test-CoreReportShape $report) -or [long]$report.totalBenchmarkCount -le 0) { continue }
        try { $generatedAtUtc = [DateTimeOffset]::Parse([string]$report.generatedAtUtc) } catch { continue }
        $machineName = [string](Get-DocumentProperty (Get-DocumentProperty $report 'provenance') 'machineName' 'unknown')
        foreach ($suite in @($report.suites)) {
            $name = [string](Get-DocumentProperty $suite 'suiteName' '')
            if (-not $name) { continue }
            if (-not $newestPerSuite.ContainsKey($name) -or $generatedAtUtc -gt $newestPerSuite[$name].CompletedAtUtc) {
                $newestPerSuite[$name] = @{
                    RunDir = $reportFile.DirectoryName
                    Report = $report
                    CompletedAtUtc = $generatedAtUtc
                    Machine = $machineName
                }
            }
        }
    }
}

$machines = @($newestPerSuite.Values | ForEach-Object { $_.Machine } | Sort-Object -Unique)
if ($newestPerSuite.Count -eq 0) {
    Write-Warning 'No valid Core benchmark suites found; preserving the published Core pages.'
} else {
    Write-Host "Found $($newestPerSuite.Count) Core suites across all runs." -ForegroundColor Green
}

# Generate pages.
$pageCount = 0

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

    # Find and copy the full BDN JSON alongside the page
    $jsonBaseName = [System.IO.Path]::GetFileNameWithoutExtension($fileName)
    $jsonOutName  = "$jsonBaseName.json"
    $jsonOutPath  = Join-Path $OutputDir $jsonOutName
    $jsonFiles    = @(Get-ChildItem $suiteResultDir -Recurse -Filter '*-report-full.json' -ErrorAction SilentlyContinue)
    $hasCharts    = $false

    if ($jsonFiles.Count -gt 0) {
        Copy-Item $jsonFiles[0].FullName $jsonOutPath -Force
        $hasCharts = $true
    } elseif (Test-Path $jsonOutPath) {
        Remove-Item -LiteralPath $jsonOutPath -Force
    }

    $chartId = $suiteName -replace '[^a-zA-Z0-9]', '-'

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

    if ($hasCharts) {
        [void]$sb.AppendLine("<div class=""benchmark-chart"">")
        [void]$sb.AppendLine("<p style=""margin-bottom:4px""><label>Time scale: <select id=""chart-scale-$chartId""><option value=""log2"" selected>Log2</option><option value=""log10"">Log10</option><option value=""linear"">Linear</option></select></label> <label>Width: <input type=""range"" id=""chart-width-$chartId"" min=""400"" max=""1400"" value=""960"" step=""20"" style=""vertical-align:middle""></label> <label>Height: <input type=""range"" id=""chart-height-$chartId"" min=""200"" max=""900"" value=""500"" step=""20"" style=""vertical-align:middle""></label></p>")
        [void]$sb.AppendLine("<div id=""chart-wrap-$chartId"" style=""max-width:960px""><canvas id=""chart-bench-$chartId"" style=""height:500px""></canvas></div>")
        [void]$sb.AppendLine("<p><a href=""$jsonOutName"">Full results as JSON</a></p>")
        [void]$sb.AppendLine("</div>")
        [void]$sb.AppendLine("<script src=""benchmark-charts.js""></script>")
    }
    [void]$sb.AppendLine()

    $outPath = Join-Path $OutputDir $fileName
    $sb.ToString() | Set-Content $outPath -Encoding UTF8
    Write-Host "  $fileName" -ForegroundColor Green
    $pageCount++

}

# Rowles.Text and compression use their own BenchmarkDotNet executables.
foreach ($projectKey in @('text', 'compression')) {
    if (-not $newestProjectEvidence.ContainsKey($projectKey)) { continue }
    $projectDirectory = $newestProjectEvidence[$projectKey].Directory

    $title = if ($projectKey -eq 'text') { 'Rowles.Text benchmarks' } else { 'Compression benchmarks' }
    $builder = [System.Text.StringBuilder]::new()
    [void]$builder.AppendLine('---')
    [void]$builder.AppendLine("title: $title")
    [void]$builder.AppendLine('---')
    [void]$builder.AppendLine()
    [void]$builder.AppendLine("# $title")
    [void]$builder.AppendLine()
    foreach ($markdownFile in @(Get-ChildItem $projectDirectory -Recurse -File -Filter '*-report-github.md' | Sort-Object Name)) {
        $table = Get-TableContent $markdownFile.FullName
        if (-not $table) { continue }
        [void]$builder.AppendLine("## $($markdownFile.BaseName -replace '-report-github$', '')")
        [void]$builder.AppendLine()
        [void]$builder.AppendLine($table)
        [void]$builder.AppendLine()
    }
    $builder.ToString() | Set-Content (Join-Path $OutputDir "$projectKey.md") -Encoding UTF8
    Write-Host "  $projectKey.md" -ForegroundColor Green
    $pageCount++
}

# ── benchmark-charts.js ───────────────────────────────────────────────────────

$benchmarkChartsJs = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot '../../docs/site-config/benchmark-charts.js'))
$benchmarkChartsJs | Set-Content (Join-Path $OutputDir 'benchmark-charts.js') -Encoding UTF8
Write-Host "Written: benchmark-charts.js" -ForegroundColor Green
Copy-Item -LiteralPath $ChartJsPath -Destination (Join-Path $OutputDir 'chart.umd.js') -Force
Write-Host 'Copied pinned Chart.js browser bundle.' -ForegroundColor Green

New-BenchmarkCatalog
Write-Host 'Written: overview.md and toc.yml' -ForegroundColor Green

if ($pageCount -eq 0) {
    Write-Warning "No pages generated."
}

Write-Host "Done. $pageCount benchmark pages written." -ForegroundColor Cyan
