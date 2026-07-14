#!/usr/bin/env pwsh
<#
.SYNOPSIS
    LeanCorpus devops — test, benchmark, and docs in one script.

.DESCRIPTION
    Single entry point for all devops tasks.
      devops test      [-Suite <name>] [-Framework <tfm>] [-Aot] [-Filter <expr>] [-List]
      devops benchmark [-Suite <name>] [-Strat <name>] [-DocCount <n>] [-Framework <tfm>] [-PrepareData] [-CorpusOnly] [-List] [-Dry]
      devops docs      [-SkipBenchmarks] [-SkipCoverage] [-Serve]
      devops docs      -Coverage [-Clean] [-IncludePerformance] [-GenerateReport] [-Framework <tfm>]

.EXAMPLE
    devops test

.EXAMPLE
    devops test -Suite integration -Framework net11.0

.EXAMPLE
    devops test -Aot

.EXAMPLE
    devops benchmark -List

.EXAMPLE
    devops benchmark -Suite query -Strat fast

.EXAMPLE
    devops docs -Serve

.EXAMPLE
    devops docs -Coverage -Clean -GenerateReport
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('test', 'benchmark', 'docs')]
    [string]$Command,

    # ── Shared ──
    [ValidateSet('net10.0', 'net11.0')]
    [string]$Framework = 'net10.0',

    # ── Test ──
    [string]$Suite = 'all',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$Aot,
    [string]$RuntimeIdentifier,
    [string]$Filter,

    # ── Benchmark ──
    [string]$Strat = 'default',
    [int]$DocCount = 0,
    [switch]$PrepareData,
    [int]$BookCount = 200,
    [switch]$CorpusOnly,
    [switch]$Dry,
    [switch]$GcDump,
    [switch]$Controlled,
    [string]$SourceCommit = '',
    [string]$SourceRef = '',
    [string]$SourceManifest = '',

    # ── Docs ──
    [switch]$Coverage,
    [switch]$SkipBenchmarks,
    [switch]$SkipCoverage,
    [switch]$Serve,
    [switch]$Clean,
    [switch]$IncludePerformance,
    [switch]$GenerateReport,

    # ── General ──
    [switch]$List
)

$ErrorActionPreference = 'Stop'

$scriptsDir = $PSScriptRoot
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptsDir '..'))

# ═══════════════════════════════════════════════════════════════════════════════
#  HELP
# ═══════════════════════════════════════════════════════════════════════════════

if (-not $Command) {
    Write-Host ''
    Write-Host '  LeanCorpus devops'
    Write-Host '  ================'
    Write-Host ''
    Write-Host '  Usage:'
    Write-Host '    devops test      [-Suite <name>] [-Framework <tfm>] [-Aot] [-Filter <expr>] [-List]  (default: all)'
    Write-Host '    devops benchmark [-Suite <name>] [-Strat <name>] [-DocCount <n>] [-Framework <tfm>] [-PrepareData] [-CorpusOnly] [-List] [-Dry]  (default: all)'
    Write-Host '    devops docs      [-SkipBenchmarks] [-SkipCoverage] [-Serve]'
    Write-Host '    devops docs      -Coverage [-Clean] [-IncludePerformance] [-GenerateReport] [-Framework <tfm>]'
    Write-Host ''
    Write-Host '  Examples:'
    Write-Host '    devops test'
    Write-Host '    devops test -Suite integration -Framework net11.0'
    Write-Host '    devops test -Aot'
    Write-Host '    devops test -List'
    Write-Host '    devops benchmark -List'
    Write-Host '    devops benchmark -Suite query -Strat fast'
    Write-Host '    devops docs'
    Write-Host '    devops docs -Serve'
    Write-Host '    devops docs -Coverage -Clean -GenerateReport'
    Write-Host ''
    exit 0
}

# ═══════════════════════════════════════════════════════════════════════════════
#  TEST
# ═══════════════════════════════════════════════════════════════════════════════

if ($Command -eq 'test') {
    $testSuites = [ordered]@{
        unit               = @{ Name = 'Unit';         Project = 'src\devops\Rowles.LeanCorpus.Tests.Unit\Rowles.LeanCorpus.Tests.Unit.csproj' }
        integration        = @{ Name = 'Integration';  Project = 'src\devops\Rowles.LeanCorpus.Tests.Integration\Rowles.LeanCorpus.Tests.Integration.csproj' }
        chaos              = @{ Name = 'Chaos';         Project = 'src\devops\Rowles.LeanCorpus.Tests.Chaos\Rowles.LeanCorpus.Tests.Chaos.csproj' }
        sourcegen          = @{ Name = 'SourceGen';     Project = 'src\devops\Rowles.LeanCorpus.Tests.SourceGen\Rowles.LeanCorpus.Tests.SourceGen.csproj' }
        compressionparity  = @{ Name = 'CompressionParity'; Project = 'src\devops\Rowles.LeanCorpus.Tests.CompressionParity\Rowles.LeanCorpus.Tests.CompressionParity.csproj' }
    }

    if ($List) {
        Write-Host ''
        Write-Host '  Available test suites (-Suite):'
        Write-Host ''
        foreach ($key in $testSuites.Keys) {
            Write-Host "    $($key.PadRight(18)) $($testSuites[$key].Name)"
        }
        Write-Host '    all                 All test suites'
        Write-Host ''
        exit 0
    }

    # ── AOT smoke ──
    if ($Aot) {
        if (-not $RuntimeIdentifier) {
            if ($IsLinux)        { $RuntimeIdentifier = 'linux-x64' }
            elseif ($IsMacOS)    { $RuntimeIdentifier = 'osx-x64' }
            else                 { $RuntimeIdentifier = 'win-x64' }
        }
        $aotProject = Join-Path $repoRoot 'src\devops\Rowles.LeanCorpus.Tests.AOTSmoke\Rowles.LeanCorpus.Example.NativeAot.csproj'
        Write-Host "Publishing NativeAOT example ($RuntimeIdentifier)..." -ForegroundColor Cyan
        dotnet publish $aotProject -c Release -r $RuntimeIdentifier --self-contained true -p:PublishAot=true
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        $publishDir = Join-Path $repoRoot "src\devops\Rowles.LeanCorpus.Tests.AOTSmoke\bin\Release\net10.0\$RuntimeIdentifier\publish"
        $exe = if ($RuntimeIdentifier.StartsWith('win-', [StringComparison]::OrdinalIgnoreCase)) {
            Join-Path $publishDir 'Rowles.LeanCorpus.Tests.AOTSmoke.exe'
        } else { Join-Path $publishDir 'Rowles.LeanCorpus.Example.NativeAot' }
        Write-Host "Running NativeAOT executable..." -ForegroundColor Cyan
        & $exe
        if ($LASTEXITCODE -ne 0) { Write-Error "NativeAOT executable failed."; exit $LASTEXITCODE }
        Write-Host 'NativeAOT smoke test passed.' -ForegroundColor Green
        exit 0
    }

    # ── dotnet test ──
    $toRun = if ($Suite -eq 'all') { [string[]]($testSuites.Keys) } else { @($Suite) }
    $testArgs = @('--configuration', $Configuration, '--framework', $Framework, '--no-restore')
    if ($Filter) { $testArgs += @('--filter', $Filter) }

    Write-Host "Test runner — $($toRun.Count) suite(s)" -ForegroundColor Cyan
    Write-Host "  Framework:     $Framework"
    Write-Host "  Configuration: $Configuration"
    if ($Filter) { Write-Host "  Filter:        $Filter" }
    Write-Host ''

    foreach ($key in $toRun) {
        $ts = $testSuites[$key]
        $projectPath = Join-Path $repoRoot $ts.Project
        Write-Host "  $($ts.Name)..." -ForegroundColor DarkGray
        dotnet test $projectPath @testArgs
        if ($LASTEXITCODE -ne 0) {
            Write-Error "$($ts.Name) tests failed with exit code $LASTEXITCODE."
            exit $LASTEXITCODE
        }
        Write-Host "  $($ts.Name) — passed" -ForegroundColor Green
    }
    Write-Host ''
    Write-Host 'All test suites passed.' -ForegroundColor Green
    exit 0
}

# ═══════════════════════════════════════════════════════════════════════════════
#  BENCHMARK
# ═══════════════════════════════════════════════════════════════════════════════

if ($Command -eq 'benchmark') {
    $suiteDescriptions = [ordered]@{
        all                  = 'All primary benchmark suites'
        'all-with-explicit'  = 'All primary plus all explicit-only suites'
        index                = 'IndexingBenchmarks'
        query                = 'TermQueryBenchmarks'
        analysis             = 'AnalysisBenchmarks'
        'analysis-filters-v2'= 'NewTokenFilterBenchmarks'
        'pattern-tokeniser'  = 'PatternTokeniserBenchmarks'
        boolean              = 'BooleanQueryBenchmarks'
        phrase               = 'PhraseQueryBenchmarks'
        prefix               = 'PrefixQueryBenchmarks'
        fuzzy                = 'FuzzyQueryBenchmarks'
        wildcard             = 'WildcardQueryBenchmarks'
        deletion             = 'DeletionQueue/Commit'
        'deletion-queue'     = 'DeletionQueueBenchmarks'
        'deletion-commit'    = 'DeletionCommitBenchmarks'
        suggester            = 'SuggesterBenchmarks'
        schemajson           = 'SchemaAndJsonBenchmarks'
        indexsort            = 'IndexSortIndex/Search'
        blockjoin            = 'BlockJoinIndex/Search'
        'blockjoin-index'    = 'BlockJoinIndexBenchmarks'
        'blockjoin-search'   = 'BlockJoinSearchBenchmarks'
        range                = 'RangeQueryBenchmarks'
        regexp               = 'RegexpQueryBenchmarks'
        dismax               = 'DisjunctionMaxQueryBenchmarks'
        multiphrase          = 'MultiPhraseQueryBenchmarks'
        span                 = 'SpanQueryBenchmarks'
        mlt                  = 'MoreLikeThisBenchmarks'
        highlighter          = 'HighlighterBenchmarks'
        'searcher-mgr'       = 'SearcherManagerBenchmarks'
        combined             = 'CombinedFieldsQueryBenchmarks'
        terminset            = 'TermInSetQueryBenchmarks'
        aggregation          = 'AggregationBenchmarks'
        'query-cache'        = 'QueryCacheBenchmarks'
        parallel             = 'ParallelSearchBenchmarks'
        'function-score'     = 'FunctionScoreQueryBenchmarks'
        geo                  = 'GeoQueryBenchmarks'
        'collapse-facet'     = 'CollapseAndFacetBenchmarks'
        similarity           = 'SimilarityBenchmarks'
        stemmer              = 'StemmerParityBenchmarks'
        kstemmer             = 'KStemmerParityBenchmarks'
        lightenglish         = 'LightEnglishStemmerBenchmarks'
        hunspell             = 'HunspellBenchmarks'
        ngram                = 'NGramTokeniserBenchmarks'
        synonym              = 'SynonymBenchmarks'
        'async-index'        = 'AsyncIndexingBenchmarks'
        'gutenberg-analysis' = 'GutenbergAnalysis'
        'gutenberg-index'    = 'GutenbergIndex'
        'gutenberg-search'   = 'GutenbergSearch'
        tokenbudget          = 'TokenBudgetBenchmarks'
        diagnostics          = 'DiagnosticsBenchmarks'
        'packed-int-codec'   = 'PackedIntCodecBenchmarks'
        'numeric-aggregator' = 'NumericAggregatorSimdBenchmarks'
        'index-writer'       = 'IndexWriterContentionBenchmarks'
        'concurrent-write'   = 'ConcurrentVsSequentialBenchmarks'
        merge                = 'MergeBenchmarks'
        flush                = 'FlushBenchmarks'
        'docvalues-read'     = 'DocValuesReadBenchmarks'
        bkd                  = 'BKDTreeBenchmarks'
        'fst-lookup'         = 'FstLookupBenchmarks'
        'mmap-io'            = 'MMapDirectoryIOBenchmarks'
        hnsw                 = 'HnswSearchBenchmarks'
        vq                   = 'VectorQuantisationBenchmarks'
        'tv-highlighter'     = 'TermVectorHighlighterBenchmarks'
        'analysis-parity'    = 'AnalyserParityBenchmarks'
        'analysis-filters'   = 'TokenFilterBenchmarks'
        explicit             = 'All explicit-only suites'
    }

    $stratDescriptions = [ordered]@{
        'default'       = '20K docs, --job short (development baseline)'
        'fast'          = '500 docs, --job dry (minimal smoke-test)'
        'quick-compare' = '1000 docs, --job short (quick comparison)'
        'intense'       = '10K docs, default BDN job'
        'stress'        = '50K docs, default BDN job'
        'exhaustive'    = '100K docs, default BDN job (official reference)'
    }

    if ($List) {
        Write-Host ''
        Write-Host '  Available benchmark suites (-Suite):'
        Write-Host ''
        foreach ($name in $suiteDescriptions.Keys) {
            Write-Host "    $($name.PadRight(22)) $($suiteDescriptions[$name])"
        }
        Write-Host ''
        Write-Host '  Available strategies (-Strat):'
        Write-Host ''
        foreach ($name in $stratDescriptions.Keys) {
            Write-Host "    $($name.PadRight(16)) $($stratDescriptions[$name])"
        }
        Write-Host ''
        exit 0
    }

    # Strategy presets
    $stratDocCount = 0
    $stratJobArgs = @()
    switch ($Strat) {
        'default'       { $stratJobArgs = @('--job', 'short') }
        'fast'          { $stratDocCount = 500;   $stratJobArgs = @('--job', 'dry') }
        'quick-compare' { $stratDocCount = 1000;  $stratJobArgs = @('--job', 'short') }
        'intense'       { $stratDocCount = 10000; $stratJobArgs = @('--job', 'default') }
        'stress'        { $stratDocCount = 50000; $stratJobArgs = @('--job', 'default') }
        'exhaustive'    { $stratDocCount = 100000;$stratJobArgs = @('--job', 'default') }
    }

    if ($Controlled) {
        if ($DocCount -le 0 -and $stratDocCount -le 0) { $stratDocCount = 1000 }
        if ($stratJobArgs.Count -eq 0) { $stratJobArgs = @('--job', 'short') }
        $CorpusOnly = $true
    }

    $effectiveDocCount = 0
    if ($DocCount -gt 0)       { $effectiveDocCount = $DocCount }
    elseif ($stratDocCount -gt 0) { $effectiveDocCount = $stratDocCount }

    $projectPath = Join-Path $repoRoot 'src\devops\Rowles.LeanCorpus.Benchmarks\Rowles.LeanCorpus.Benchmarks.csproj'

    # Prepare data
    if ($PrepareData) {
        $dataDir = Join-Path $repoRoot 'bench\data'
        $gutenbergDir = Join-Path $dataDir 'gutenberg-ebooks'
        $newsDir = Join-Path $dataDir '20newsgroups'
        $reutersDir = Join-Path $dataDir 'reuters21578'
        $gutenbergCount = if (Test-Path $gutenbergDir) { (Get-ChildItem $gutenbergDir -Filter '*.txt' -ErrorAction SilentlyContinue).Count } else { 0 }
        if ($gutenbergCount -lt $BookCount) {
            Write-Host "Preparing Gutenberg data (BookCount=$BookCount)..." -ForegroundColor Cyan
            & (Join-Path $scriptsDir 'download-gutenberg.ps1') -BookCount $BookCount
        } else { Write-Host "Gutenberg data present ($gutenbergCount books), skipping download." -ForegroundColor DarkGray }
        $newsCount = if (Test-Path $newsDir) { (Get-ChildItem $newsDir -File -Recurse -ErrorAction SilentlyContinue).Count } else { 0 }
        $reutersCount = if (Test-Path $reutersDir) { (Get-ChildItem $reutersDir -Filter '*.sgm' -File -ErrorAction SilentlyContinue).Count } else { 0 }
        if ($newsCount -eq 0 -or $reutersCount -eq 0) {
            Write-Host 'Preparing news data...' -ForegroundColor Cyan
            & (Join-Path $scriptsDir 'download-news.ps1')
        } else { Write-Host "News data present ($newsCount posts, $reutersCount Reuters files), skipping download." -ForegroundColor DarkGray }
        Write-Host ''
    }

    # Build BDN args
    $runArgs = @('--suite', $Suite)
    if ($effectiveDocCount -gt 0) {
        $runArgs += @('--doccount', $effectiveDocCount.ToString())
        $env:BENCH_DOC_COUNT = $effectiveDocCount.ToString()
    }
    if ($CorpusOnly) { $runArgs += '--corpus-only' }
    if ($SourceCommit)   { $env:BENCH_SOURCE_COMMIT   = $SourceCommit }
    if ($SourceRef)      { $env:BENCH_SOURCE_REF      = $SourceRef }
    if ($SourceManifest) { $env:BENCH_SOURCE_MANIFEST = [System.IO.Path]::GetFullPath($SourceManifest) }

    Write-Host "Suite:      $Suite"
    Write-Host "Strat:      $Strat"
    Write-Host "Framework:  $Framework"
    if ($Controlled) { Write-Host 'Mode:       controlled' }
    if ($CorpusOnly) { Write-Host 'CorpusOnly: enabled' }
    if ($effectiveDocCount -gt 0) { Write-Host "Docs:       $effectiveDocCount" }
    if ($stratJobArgs) { Write-Host "Job:        $($stratJobArgs -join ' ')" }

    if ($Dry) {
        Write-Host ''
        Write-Host 'Dry run — command that would execute:'
        Write-Host "  dotnet run -c Release --framework $Framework --project `"$projectPath`" -- $($runArgs -join ' ') $($stratJobArgs -join ' ')"
        Write-Host ''
        exit 0
    }

    if ($GcDump) {
        $runArgs += '--gcdump'
        if (-not (Get-Command dotnet-gcdump -ErrorAction SilentlyContinue)) { dotnet tool install -g dotnet-gcdump }
    }

    Write-Host ''
    dotnet run -c Release --framework $Framework --project $projectPath -- @runArgs @stratJobArgs
    exit $LASTEXITCODE
}

# ═══════════════════════════════════════════════════════════════════════════════
#  DOCS
# ═══════════════════════════════════════════════════════════════════════════════

if ($Command -eq 'docs') {
    $docsDir   = Join-Path $repoRoot 'docs'
    $docfxJson = Join-Path $docsDir 'docfx.json'
    $apiDir    = Join-Path $docsDir 'api'
    $siteDir   = Join-Path $docsDir 'site'

    # ── Coverage mode ──
    if ($Coverage) {
        $testProjects = @(
            Get-ChildItem (Join-Path $repoRoot 'src\devops') -Filter '*.csproj' -Recurse |
                Where-Object { $_.Directory.Name -like 'Rowles.LeanCorpus.Tests.*' -and $_.Directory.Name -ne 'Rowles.LeanCorpus.Tests.Shared' } |
                Sort-Object FullName | ForEach-Object { $_.FullName }
        )
        $resultsDir = Join-Path $repoRoot 'coverage-results'
        if ($Clean -and (Test-Path $resultsDir)) { Remove-Item $resultsDir -Recurse -Force }
        if (-not (Test-Path $resultsDir)) { New-Item -ItemType Directory -Path $resultsDir | Out-Null }

        Write-Host 'Running tests with coverage collection...' -ForegroundColor Cyan
        Write-Host "  Framework: $Framework"
        if (-not $IncludePerformance) { Write-Host '  Filter:    Coverage!=Skip' }
        Write-Host ''

        foreach ($tp in $testProjects) {
            $projName = [System.IO.Path]::GetFileNameWithoutExtension($tp)
            Write-Host "  $projName..." -ForegroundColor DarkGray
            $covArgs = @('test', $tp, '--configuration', 'Release', '--framework', $Framework, '--collect', 'XPlat Code Coverage', '--results-directory', $resultsDir)
            if (-not $IncludePerformance) { $covArgs += @('--filter', 'Coverage!=Skip') }
            dotnet @covArgs
            if ($LASTEXITCODE -ne 0) { Write-Error "Tests failed."; exit $LASTEXITCODE }
        }

        $xmlFiles = @(Get-ChildItem $resultsDir -Filter 'coverage.cobertura.xml' -Recurse)
        Write-Host ''
        Write-Host "Coverage data written to: $resultsDir" -ForegroundColor Green
        Write-Host "  Found $($xmlFiles.Count) coverage file(s)."

        if ($GenerateReport) {
            if (-not (Get-Command reportgenerator -ErrorAction SilentlyContinue)) { dotnet tool install -g dotnet-reportgenerator-globaltool }
            $reportOut = Join-Path $repoRoot 'docs\coverage'
            $reportPaths = ($xmlFiles | ForEach-Object { $_.FullName }) -join ';'
            Write-Host 'Generating coverage report...' -ForegroundColor Cyan
            reportgenerator "-reports:$reportPaths" "-targetdir:$reportOut" '-reporttypes:Html' '-title:LeanCorpus Coverage'
            if ($LASTEXITCODE -eq 0) { Write-Host "Coverage report written to: $reportOut" -ForegroundColor Green }
        }
        exit 0
    }

    # ── Docs build mode ──
    if (-not (Get-Command docfx -ErrorAction SilentlyContinue)) { dotnet tool install -g docfx }
    if (-not $SkipBenchmarks) {
        Write-Host 'Generating benchmark pages...' -ForegroundColor Cyan
        & (Join-Path $scriptsDir 'generate-benchmark-docs.ps1')
    }
    if (-not $SkipCoverage) {
        $xmlFiles = @(Get-ChildItem (Join-Path $repoRoot 'coverage-results') -Filter 'coverage.cobertura.xml' -Recurse -ErrorAction SilentlyContinue)
        if ($xmlFiles.Count -gt 0) {
            if (-not (Get-Command reportgenerator -ErrorAction SilentlyContinue)) { dotnet tool install -g dotnet-reportgenerator-globaltool }
            $reportOut = Join-Path $docsDir 'coverage'
            $reportPaths = ($xmlFiles | ForEach-Object { $_.FullName }) -join ';'
            Write-Host 'Generating coverage report...' -ForegroundColor Cyan
            reportgenerator "-reports:$reportPaths" "-targetdir:$reportOut" '-reporttypes:Html' '-title:LeanCorpus Coverage'
        }
    }

    if (Test-Path $apiDir) {
        Get-ChildItem $apiDir -Filter '*.yml' -File | Remove-Item -Force
        $tp = Join-Path $apiDir 'toc.yml'; if (Test-Path $tp) { Remove-Item $tp -Force }
    } else { New-Item -ItemType Directory -Path $apiDir | Out-Null }

    Write-Host 'Generating API metadata...' -ForegroundColor Cyan
    docfx metadata $docfxJson
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    if ($Serve) {
        Write-Host 'Building and serving docs on http://0.0.0.0:8080...' -ForegroundColor Cyan
        docfx build $docfxJson
        docfx serve $siteDir --hostname 0.0.0.0 -p 8080
    } else {
        Write-Host 'Building documentation site...' -ForegroundColor Cyan
        docfx build $docfxJson
        Write-Host "Site written to: $siteDir" -ForegroundColor Green
    }
    exit 0
}
