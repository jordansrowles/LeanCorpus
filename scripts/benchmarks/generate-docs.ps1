<#
.SYNOPSIS
    Generates DocFX benchmark pages from BDN output files — one page per suite.

.DESCRIPTION
    Scans completed runs under artifacts/benchmark/runs and keeps the
    newest run per suite and writes one markdown page per suite into docs/benchmarks/.

    Run this before docfx build; docs.ps1 calls it automatically.

.PARAMETER BenchDir
    Path to the benchmark run directory. Defaults to artifacts/benchmark/runs.

.PARAMETER OutputDir
    Path to write the generated files. Defaults to ../docs/benchmarks.

.EXAMPLE
    .\scripts\generate-benchmark-docs.ps1
    Generates per-suite pages from all machines' latest runs.
#>
param(
    [string]$BenchDir  = '',
    [string]$OutputDir = '',
    [string]$PublishedDir = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))

if ([string]::IsNullOrEmpty($BenchDir))  { $BenchDir  = Join-Path $repoRoot 'artifacts/benchmark/runs' }
if ([string]::IsNullOrEmpty($OutputDir)) { $OutputDir = Join-Path $repoRoot 'artifacts\docs\generated\benchmarks' }
if ([string]::IsNullOrEmpty($PublishedDir)) { $PublishedDir = Join-Path $repoRoot 'docs/benchmarks' }

$BenchDir  = [System.IO.Path]::GetFullPath($BenchDir)
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)
$PublishedDir = [System.IO.Path]::GetFullPath($PublishedDir)

# Preserve the last published benchmark documentation until a newer run exists.
# This keeps a clean checkout buildable while moving generated output out of docs/.
[void][System.IO.Directory]::CreateDirectory($OutputDir)
foreach ($stagedItem in @(Get-ChildItem $OutputDir -Force -ErrorAction SilentlyContinue)) {
    Remove-Item -LiteralPath $stagedItem.FullName -Recurse -Force
}
foreach ($publishedFile in @(Get-ChildItem $PublishedDir -File -ErrorAction SilentlyContinue)) {
    Copy-Item -LiteralPath $publishedFile.FullName -Destination (Join-Path $OutputDir $publishedFile.Name) -Force
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
$runDirectories = if (Test-Path $BenchDir) { @(Get-ChildItem $BenchDir -Directory) } else { @() }
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

$benchmarkChartsJs = @'
(function(){
"use strict";

var canvas = document.querySelector("canvas[id^='chart-bench-']");
if(!canvas)return;
var suite = canvas.id.replace("chart-bench-","");
var jsonUrl = suite + ".json";
var status = document.createElement("p");
status.className = "benchmark-chart-status";
status.textContent = "Loading chart…";
canvas.before(status);

var chartJs = document.createElement("script");
chartJs.src = "https://cdn.jsdelivr.net/npm/chart.js@4";
chartJs.onload = function(){ fetch(jsonUrl).then(function(r){if(!r.ok)throw new Error("Benchmark data request failed");return r.json();}).then(render).catch(showError); };
chartJs.onerror = showError;
document.head.appendChild(chartJs);

function render(full){
  var benchmarks = full.Benchmarks;
  if(!benchmarks||!benchmarks.length){showError();return;}
  status.remove();

  var colors=["#4e79a7","#f28e2b","#e15759","#76b7b2","#59a14f","#edc948","#b07aa1","#ff9da7","#9c755f","#bab0ac"];

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

  var labels=chartData.map(function(x){return x.label;});

  var datasets=[];

  datasets.push({
    type:"bar",
    label:"Allocated",
    data:chartData.map(function(x){return x.allocBytes;}),
    backgroundColor:colors[0]+"cc",
    yAxisID:"y",
    order:1
  });

  datasets.push({
    type:"line",
    label:"Mean time",
    data:chartData.map(function(x){return x.meanNs;}),
    borderColor:"#e15759",
    backgroundColor:"#e1575933",
    yAxisID:"y1",
    pointRadius:4,
    pointHoverRadius:6,
    order:0
  });

  chartData.forEach(function(m,i){
    datasets.push({
      type:"scatter",
      label:m.label,
      data:m.iterations.map(function(ns){return{x:m.label,y:ns};}),
      backgroundColor:colors[i%colors.length]+"55",
      yAxisID:"y1",
      pointRadius:2,
      pointHoverRadius:4,
      showLine:false,
      order:2
    });
  });

  var chart = new Chart(canvas,{
    data:{labels:labels,datasets:datasets},
    options:{
      responsive:true,
      maintainAspectRatio:false,
      interaction:{mode:"index",intersect:false},
      plugins:{
        legend:{labels:{generateLabels:function(chart){var d=chart.data.datasets;return[{text:d[0].label,fillStyle:d[0].backgroundColor,strokeStyle:d[0].borderColor,hidden:!chart.isDatasetVisible(0),datasetIndex:0},{text:d[1].label,fillStyle:d[1].backgroundColor||d[1].borderColor,strokeStyle:d[1].borderColor,hidden:!chart.isDatasetVisible(1),datasetIndex:1,pointStyle:"circle",pointStyleWidth:8}];}}},
        tooltip:{callbacks:{label:function(c){var v=c.raw.y||c.raw;if(c.dataset.yAxisID==="y")return fmtBytes(v);return fmtNs(v);}}}
      },
      scales:{
        y:{
          type:"linear",
          position:"left",
          title:{display:true,text:"Allocated"},
          ticks:{callback:fmtBytes},
          grid:{drawOnChartArea:false}
        },
        y1:makeScale("log2")
      }
    }
  });

  // Scale switcher
  var sel = document.getElementById("chart-scale-"+suite);
  if(sel){
    sel.addEventListener("change",function(){
      chart.options.scales.y1 = makeScale(this.value);
      chart.update();
    });
  }

  // Width / height sliders
  var wrap = document.getElementById("chart-wrap-"+suite);
  var widthSlider = document.getElementById("chart-width-"+suite);
  var heightSlider = document.getElementById("chart-height-"+suite);
  if(widthSlider && wrap){
    widthSlider.addEventListener("input",function(){
      wrap.style.maxWidth = this.value + "px";
      chart.resize();
    });
  }
  if(heightSlider){
    heightSlider.addEventListener("input",function(){
      canvas.style.height = this.value + "px";
      chart.resize();
    });
  }

  function makeScale(mode){
    var base = {
      type:"logarithmic",
      position:"right",
      title:{display:true,text:"Time (log\u2082)"},
      ticks:{callback:fmtNs}
    };
    if(mode==="log10"){
      base.title.text = "Time (log\u2081\u2080)";
    } else if(mode==="linear"){
      base.type = "linear";
      base.title.text = "Time (linear)";
    } else {
      // log2 — afterBuildTicks to show powers of 2
      base.afterBuildTicks = function(axis){
        var min = axis.min, max = axis.max;
        var ticks = [];
        var v = Math.pow(2, Math.floor(Math.log2(min||1)));
        while(v <= max){ ticks.push({value:v}); v *= 2; }
        axis.ticks = ticks;
      };
    }
    return base;
  }

  function fmtBytes(v){if(v>=1e9)return(v/1e9).toFixed(1)+" GB";if(v>=1e6)return(v/1e6).toFixed(1)+" MB";if(v>=1e3)return(v/1e3).toFixed(1)+" KB";return v+" B";}
  function fmtNs(v){if(v>=1e9)return(v/1e9).toFixed(2)+" s";if(v>=1e6)return(v/1e6).toFixed(2)+" ms";if(v>=1e3)return(v/1e3).toFixed(2)+" μs";return v.toFixed(0)+" ns";}
}

function showError(){
  status.textContent = "The interactive chart could not be loaded. The benchmark table and JSON results are still available."
}
})();
'@

$benchmarkChartsJs | Set-Content (Join-Path $OutputDir 'benchmark-charts.js') -Encoding UTF8
Write-Host "Written: benchmark-charts.js" -ForegroundColor Green

if ($pageCount -eq 0) {
    Write-Warning "No pages generated."
}

Write-Host "Done. $pageCount benchmark pages written." -ForegroundColor Cyan
