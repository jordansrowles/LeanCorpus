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
# are all auto-generated, safe to clear *.md and *.json that match our suites)
$existingMd = Get-ChildItem $OutputDir -Filter '*.md' -ErrorAction SilentlyContinue
foreach ($f in $existingMd) {
    Remove-Item $f.FullName -Force
}
$existingJson = Get-ChildItem $OutputDir -Filter '*.json' -ErrorAction SilentlyContinue
foreach ($f in $existingJson) {
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

    # Find and copy the full BDN JSON alongside the page
    $jsonBaseName = [System.IO.Path]::GetFileNameWithoutExtension($fileName)
    $jsonOutName  = "$jsonBaseName.json"
    $jsonOutPath  = Join-Path $OutputDir $jsonOutName
    $jsonFiles    = @(Get-ChildItem $suiteResultDir -Recurse -Filter '*-report-full.json' -ErrorAction SilentlyContinue)
    $hasCharts    = $false

    if ($jsonFiles.Count -gt 0) {
        Copy-Item $jsonFiles[0].FullName $jsonOutPath -Force
        $hasCharts = $true
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
        [void]$sb.AppendLine("<div class=""benchmark-charts"">")
        [void]$sb.AppendLine("<h2>Allocation</h2>")
        [void]$sb.AppendLine("<div style=""max-width:960px""><canvas id=""chart-alloc-$chartId"" style=""max-height:400px""></canvas></div>")
        [void]$sb.AppendLine("<h2>Performance</h2>")
        [void]$sb.AppendLine("<div style=""max-width:960px""><canvas id=""chart-perf-$chartId"" style=""max-height:500px""></canvas></div>")
        [void]$sb.AppendLine("<p><a href=""$jsonOutName"">Full results as JSON</a></p>")
        [void]$sb.AppendLine("</div>")
        [void]$sb.AppendLine("<script src=""benchmark-charts.js""></script>")
    }
    [void]$sb.AppendLine()

    $outPath = Join-Path $OutputDir $fileName
    $sb.ToString() | Set-Content $outPath -Encoding UTF8
    Write-Host "  $fileName" -ForegroundColor Green
    $pageCount++

    $tocEntries.Add("- name: $displayName")
    $tocEntries.Add("  href: $fileName")
}

# ── benchmark-charts.js ───────────────────────────────────────────────────────

$benchmarkChartsJs = @'
(function(){
"use strict";

// Work out which suite page we are on from the canvas element id.
var allocCanvas = document.querySelector("canvas[id^='chart-alloc-']");
if(!allocCanvas)return;
var suite = allocCanvas.id.replace("chart-alloc-","");
var jsonUrl = suite + ".json";

// Load Chart.js then fetch data and render.
var chartJs = document.createElement("script");
chartJs.src = "https://cdn.jsdelivr.net/npm/chart.js@4";
chartJs.onload = function(){ fetch(jsonUrl).then(function(r){return r.json();}).then(render).catch(function(){}); };
document.head.appendChild(chartJs);

function render(full){
  var benchmarks = full.Benchmarks;
  if(!benchmarks||!benchmarks.length)return;

  var colors=["#4e79a7","#f28e2b","#e15759","#76b7b2","#59a14f","#edc948","#b07aa1","#ff9da7","#9c755f","#bab0ac"];

  // Build chart data from the full BDN JSON
  var chartData=[];
  benchmarks.forEach(function(b){
    var label=b.MethodTitle;
    if(b.Parameters)label+=" ["+b.Parameters+"]";
    var iters=[];
    b.Measurements.forEach(function(m){if(m.IterationStage==="Result")iters.push(m.Nanoseconds);});
    chartData.push({
      label:label,
      meanNs:Math.round(b.Statistics.Mean),
      allocBytes:b.Memory.BytesAllocatedPerOperation,
      iterations:iters
    });
  });

  // Allocation — horizontal bar
  new Chart(document.getElementById("chart-alloc-"+suite),{
    type:"bar",
    data:{
      labels:chartData.map(function(x){return x.label;}),
      datasets:[{
        label:"Bytes allocated",
        data:chartData.map(function(x){return x.allocBytes;}),
        backgroundColor:colors[0]
      }]
    },
    options:{
      indexAxis:"y",
      responsive:true,
      maintainAspectRatio:false,
      plugins:{legend:{display:false},tooltip:{callbacks:{label:function(c){return fmtBytes(c.raw);}}}},
      scales:{x:{title:{display:true,text:"Allocated"},ticks:{callback:fmtBytes}}}
    }
  });

  // Performance — bar means with scatter iterations overlay
  var perfLabels=chartData.map(function(x){return x.label;});
  var perfDatasets=[{
    type:"bar",
    label:"Mean",
    data:chartData.map(function(x){return x.meanNs;}),
    backgroundColor:colors.map(function(c){return c+"88";}),
    order:1
  }];
  chartData.forEach(function(m,i){
    perfDatasets.push({
      type:"scatter",
      label:m.label,
      data:m.iterations.map(function(ns){return{x:m.label,y:ns};}),
      backgroundColor:colors[i%colors.length],
      pointRadius:3,
      pointHoverRadius:5,
      showLine:false,
      order:0
    });
  });
  new Chart(document.getElementById("chart-perf-"+suite),{
    data:{labels:perfLabels,datasets:perfDatasets},
    options:{
      responsive:true,
      maintainAspectRatio:false,
      plugins:{tooltip:{callbacks:{label:function(c){var v=c.raw.y||c.raw;return c.dataset.label+": "+fmtNs(v);}}}},
      scales:{y:{title:{display:true,text:"Time"},ticks:{callback:fmtNs}}}
    }
  });

  function fmtBytes(v){if(v>=1e9)return(v/1e9).toFixed(1)+" GB";if(v>=1e6)return(v/1e6).toFixed(1)+" MB";if(v>=1e3)return(v/1e3).toFixed(1)+" KB";return v+" B";}
  function fmtNs(v){if(v>=1e9)return(v/1e9).toFixed(2)+" s";if(v>=1e6)return(v/1e6).toFixed(2)+" ms";if(v>=1e3)return(v/1e3).toFixed(2)+" μs";return v.toFixed(0)+" ns";}
}})();

'@

$benchmarkChartsJs | Set-Content (Join-Path $OutputDir 'benchmark-charts.js') -Encoding UTF8
Write-Host "Written: benchmark-charts.js" -ForegroundColor Green

# ── toc.yml ───────────────────────────────────────────────────────────────────

if ($tocEntries.Count -gt 0) {
    $tocEntries | Set-Content (Join-Path $OutputDir 'toc.yml') -Encoding UTF8
    Write-Host "Written: toc.yml ($pageCount suites)" -ForegroundColor Green
} else {
    Write-Warning "No pages generated."
}

Write-Host "Done. $pageCount benchmark pages written." -ForegroundColor Cyan
