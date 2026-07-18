#!/usr/bin/env pwsh
<#
.SYNOPSIS
    LeanCorpus devops — build, test, benchmark, docs in one command.

.DESCRIPTION
    Single entry point for all devops tasks.
      devops build     [-Configuration <c>] [-Framework <tfm>]
      devops test      [-Suite <name>] [-Framework <tfm>] [-Configuration <c>] [-Filter <expr>] [-Verbosity <level>] [-List]
      devops aot       [-RuntimeIdentifier <rid>]
      devops coverage  [-Framework <tfm>] [-Configuration <c>] [-Clean] [-IncludePerformance] [-GenerateReport]
      devops benchmark [-Suite <name>] [-Strat <name>] [-DocCount <n>] [-Framework <tfm>]
                       [-PrepareData] [-BookCount <n>] [-CorpusOnly] [-List] [-Dry] [-GcDump] [-Controlled]
                       [-SourceCommit <s>] [-SourceRef <s>] [-SourceManifest <p>]
                       [-- BenchmarkDotNet arguments]
      devops data      gutenberg|news|wikipedia [options]
      devops docs      [build|metadata|serve] [-SkipBenchmarks] [-SkipCoverage]
      devops benchmarks docs [options]
      devops benchmark remote [options]

.EXAMPLE
    devops build
    devops test -Suite integration -Framework net11.0
    devops aot
    devops coverage -Clean -GenerateReport
    devops benchmark -Suite query -Strat fast
    devops benchmark -Suite mlt -Strat exhaustive -- --filter '*SingleSegment*'
    devops docs serve
    devops docs metadata
    devops data wikipedia -Language en -ArticleCount 5000
#>

$Command = if ($args.Count -gt 0) { $args[0] } else { '' }
$RemainingArgs = [string[]]@(if ($args.Count -gt 1) { $args[1..($args.Count - 1)] } else { })


$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ''))
$ScriptsDir = Join-Path $RepoRoot 'scripts'

# Simple argument helpers — more reliable than hash-table based parsing
function Get-Arg([string]$Name, $Default = $null) {
    for ($i = 0; $i -lt $RemainingArgs.Count - 1; $i++) {
        if ($RemainingArgs[$i] -eq "-$Name") { return $RemainingArgs[$i + 1] }
    }
    return $Default
}

function Has-Flag([string]$Name) {
    return ($RemainingArgs -contains "-$Name") -or ($RemainingArgs -contains "--$Name")
}

# ═════════════════════════════════════════════════════════════════════════════
#  HELP
# ═════════════════════════════════════════════════════════════════════════════

if (-not $Command -or $Command -eq '--help' -or $Command -eq '-Help' -or $Command -eq '-h') {
    Write-Host ''
    Write-Host '  LeanCorpus devops'
    Write-Host '  ================'
    Write-Host ''
    Write-Host '  Commands:'
    Write-Host ''
    Write-Host '    build                Build the solution (Release, net10.0 by default)'
    Write-Host '      -Configuration      Debug or Release (default: Release)'
    Write-Host '      -Framework          net10.0 or net11.0 (default: net10.0)'
    Write-Host ''
    Write-Host '    test                 Run test suites'
    Write-Host '      -Suite              unit, integration, chaos, sourcegen, compressionparity,'
    Write-Host '                          architecture, or all (default: all)'
    Write-Host '      -Framework          net10.0 or net11.0 (default: net10.0)'
    Write-Host '      -Configuration      Debug or Release (default: Release)'
    Write-Host '      -Filter             xUnit filter expression (e.g. FullyQualifiedName~Writer)'
    Write-Host '      -Verbosity          Test output detail: quiet, minimal, normal, detailed'
    Write-Host '      -List               List available suites and exit'
    Write-Host ''
    Write-Host '    aot                  Run NativeAOT smoke tests for both frameworks'
    Write-Host '      -RuntimeIdentifier  linux-x64, osx-x64, win-x64 (auto-detected if omitted)'
    Write-Host ''
    Write-Host '    coverage             Run tests with code coverage collection'
    Write-Host '      -Framework          net10.0 or net11.0 (default: net10.0)'
    Write-Host '      -Configuration      Debug or Release (default: Release)'
    Write-Host '      -Clean              Remove previous coverage results before running'
    Write-Host '      -IncludePerformance  Include tests marked Coverage=Skip'
    Write-Host '      -GenerateReport     Generate HTML coverage report via ReportGenerator'
    Write-Host ''
    Write-Host '    benchmark            Run BenchmarkDotNet suites'
    Write-Host '      -Suite              Suite name (default: all). Use -List to see all suites'
    Write-Host '      -Strat              Strategy preset: fast (500 docs, dry), default (20K, short),'
    Write-Host '                          quick-compare (1K, short), intense (10K), stress (50K),'
    Write-Host '                          exhaustive (100K) (default: default)'
    Write-Host '      -DocCount           Override document count for the run'
    Write-Host '      -Framework          net10.0 or net11.0 (default: net10.0)'
    Write-Host '      -PrepareData        Download benchmark data if not already present'
    Write-Host '      -BookCount          Gutenberg books to fetch with -PrepareData (default: 200)'
    Write-Host '      -CorpusOnly         Skip Lucene.NET comparison; LeanCorpus methods only'
    Write-Host '      -Controlled         Deterministic preset: 1K docs, short job, corpus-only'
    Write-Host '      -Dry                Print the dotnet command without executing'
    Write-Host '      -GcDump             Collect GC heap dumps (requires dotnet-gcdump)'
    Write-Host '      -List               List available suites and strategies and exit'
    Write-Host '      -SourceCommit       Git commit hash for provenance (when .git unavailable)'
    Write-Host '      -SourceRef          Git ref for provenance'
    Write-Host '      -SourceManifest     Path to source manifest for provenance'
    Write-Host '      -- <args>           Arguments passed through to BenchmarkDotNet'
    Write-Host '        benchmark remote  Run benchmarks on a remote host via SSH/tmux'
    Write-Host ''
    Write-Host '    data                 Download benchmark datasets'
    Write-Host '      gutenberg           Project Gutenberg ebooks'
    Write-Host '        -BookCount        Number of books to download (default: 200)'
    Write-Host '      news                20 Newsgroups dataset'
    Write-Host '      wikipedia           Wikipedia article dump'
    Write-Host ''
    Write-Host '    docs                 Build the documentation site'
    Write-Host '      build               Full build: API metadata + static site (default)'
    Write-Host '      metadata            API YAML metadata only, no site build'
    Write-Host '      serve               Build and serve on http://0.0.0.0:8080'
    Write-Host '      -SkipBenchmarks     Skip regenerating benchmark pages'
    Write-Host '      -SkipCoverage       Skip regenerating coverage report'
    Write-Host ''
    Write-Host '    benchmarks           Benchmark documentation'
    Write-Host '      docs                Generate benchmark result pages'
    Write-Host ''
    Write-Host '  Examples:'
    Write-Host '    devops build'
    Write-Host '    devops test -Suite integration -Framework net11.0'
    Write-Host '    devops test -Suite unit -Verbosity detailed'
    Write-Host '    devops aot'
    Write-Host '    devops coverage -Clean -GenerateReport'
    Write-Host '    devops benchmark -List'
    Write-Host '    devops benchmark -Suite query -Strat fast'
    Write-Host '    devops benchmark -Suite mlt -Strat exhaustive -- --filter *SingleSegment*'
    Write-Host '    devops docs serve'
    Write-Host '    devops docs metadata'
    Write-Host '    devops data gutenberg -BookCount 500'
    Write-Host ''
    exit 0
}

# ═════════════════════════════════════════════════════════════════════════════
#  ARGUMENT PARSER
# ═════════════════════════════════════════════════════════════════════════════

function Parse-Args {
    param([string[]]$Args)
    $parsed = @{}
    for ($i = 0; $i -lt $Args.Count; $i++) {
        $arg = $Args[$i]
        if ($arg -eq '--') {
            $parsed['PassThrough'] = $Args[($i + 1)..($Args.Count - 1)]
            break
        }
        if ($arg.StartsWith('-')) {
            $name = $arg.TrimStart('-')
            if ($i + 1 -lt $Args.Count -and -not $Args[$i + 1].StartsWith('-')) {
                $parsed[$name] = $Args[$i + 1]
                $i++
            } else {
                $parsed[$name] = $true
            }
        } else {
            if (-not $parsed.ContainsKey('SubCommand')) {
                $parsed['SubCommand'] = $arg
            } else {
                if (-not $parsed.ContainsKey('Positional')) {
                    $parsed['Positional'] = @()
                }
                $parsed['Positional'] += $arg
            }
        }
    }
    return $parsed
}

function Get-ParsedValue {
    param($Parsed, [string]$Name, $Default = $null)
    foreach ($key in @($Name, $Name.ToLower(), $Name.ToUpper())) {
        if ($Parsed.ContainsKey($key)) { return $Parsed[$key] }
    }
    return $Default
}

# ═════════════════════════════════════════════════════════════════════════════
#  SHARED HELPERS
# ═════════════════════════════════════════════════════════════════════════════

function Get-AotProjectPath {
    Join-Path $RepoRoot 'src\devops\Rowles.LeanCorpus.Tests.AOTSmoke\Rowles.LeanCorpus.Tests.AOTSmoke.csproj'
}

function Get-BenchProjectPath {
    Join-Path $RepoRoot 'src\devops\Rowles.LeanCorpus.Benchmarks\Rowles.LeanCorpus.Benchmarks.csproj'
}

function Get-DefaultFramework {
    'net10.0'
}

function Get-TestSuites {
    [ordered]@{
        unit               = @{ Name = 'Unit';         Project = 'src\devops\Rowles.LeanCorpus.Tests.Unit\Rowles.LeanCorpus.Tests.Unit.csproj' }
        integration        = @{ Name = 'Integration';  Project = 'src\devops\Rowles.LeanCorpus.Tests.Integration\Rowles.LeanCorpus.Tests.Integration.csproj' }
        chaos              = @{ Name = 'Chaos';         Project = 'src\devops\Rowles.LeanCorpus.Tests.Chaos\Rowles.LeanCorpus.Tests.Chaos.csproj' }
        sourcegen          = @{ Name = 'SourceGen';     Project = 'src\devops\Rowles.LeanCorpus.Tests.SourceGen\Rowles.LeanCorpus.Tests.SourceGen.csproj' }
        compressionparity  = @{ Name = 'CompressionParity'; Project = 'src\devops\Rowles.LeanCorpus.Tests.CompressionParity\Rowles.LeanCorpus.Tests.CompressionParity.csproj' }
        architecture       = @{ Name = 'Architecture';  Project = 'src\devops\Rowles.LeanCorpus.Tests.Architecture\Rowles.LeanCorpus.Tests.Architecture.csproj' }
    }
}

function Get-SuiteMap {
    [ordered]@{
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
        'indexsort-index'   = 'IndexSortIndexBenchmarks'
        'indexsort-search'  = 'IndexSortSearchBenchmarks'
        blockjoin            = 'BlockJoinIndex/Search'
        'blockjoin-index'    = 'BlockJoinIndexBenchmarks'
        'blockjoin-search'   = 'BlockJoinSearchBenchmarks'
        range                = 'RangeQueryBenchmarks'
        regexp               = 'RegexpQueryBenchmarks'
        dismax               = 'DisjunctionMaxQueryBenchmarks'
        multiphrase          = 'MultiPhraseQueryBenchmarks'
        span                 = 'SpanQueryBenchmarks'
        mlt                  = 'MoreLikeThisBenchmarks + MoreLikeThisSingleSegmentBenchmarks'
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
}

function Get-StratMap {
    [ordered]@{
        'default'       = '20K docs, --job short (development baseline)'
        'fast'          = '500 docs, --job dry (minimal smoke-test)'
        'quick-compare' = '1000 docs, --job short (quick comparison)'
        'intense'       = '10K docs, default BDN job'
        'stress'        = '50K docs, default BDN job'
        'exhaustive'    = '100K docs, default BDN job (official reference)'
    }
}

function Resolve-Strat {
    param([string]$Strat)
    $docCount = 0
    $jobArgs = @()
    switch ($Strat) {
        'default'       { $jobArgs = @('--job', 'short') }
        'fast'          { $docCount = 500;   $jobArgs = @('--job', 'dry') }
        'quick-compare' { $docCount = 1000;  $jobArgs = @('--job', 'short') }
        'intense'       { $docCount = 10000; $jobArgs = @('--job', 'default') }
        'stress'        { $docCount = 50000; $jobArgs = @('--job', 'default') }
        'exhaustive'    { $docCount = 100000;$jobArgs = @('--job', 'default') }
    }
    return @{ DocCount = $docCount; JobArgs = $jobArgs }
}

# ═════════════════════════════════════════════════════════════════════════════
#  BUILD
# ═════════════════════════════════════════════════════════════════════════════

if ($Command -eq 'build') {
    $parsed = Parse-Args $RemainingArgs
    $Configuration = Get-ParsedValue $parsed 'Configuration' 'Release'
    $Framework = Get-ParsedValue $parsed 'Framework' (Get-DefaultFramework)
    $Project = Get-ParsedValue $parsed 'Project' ''

    $slnPath = Join-Path $RepoRoot 'Rowles.LeanCorpus.slnx'
    $buildArgs = @('build', $slnPath, '-c', $Configuration)

    if ($Project) {
        $buildArgs = @('build', (Join-Path $RepoRoot $Project), '-c', $Configuration)
    }

    Write-Host "Building LeanCorpus..." -ForegroundColor Cyan
    Write-Host "  Configuration: $Configuration"
    Write-Host "  Framework:     $Framework"
    if ($Project) { Write-Host "  Project:       $Project" }
    Write-Host ''

    dotnet @buildArgs -f $Framework -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Host 'Build succeeded.' -ForegroundColor Green
    exit 0
}

# ═════════════════════════════════════════════════════════════════════════════
#  TEST
# ═════════════════════════════════════════════════════════════════════════════

if ($Command -eq 'test') {
    $parsed = Parse-Args $RemainingArgs
    $testSuites = Get-TestSuites
    $Suite = Get-ParsedValue $parsed 'Suite' 'all'
    $Framework = Get-ParsedValue $parsed 'Framework' (Get-DefaultFramework)
    $Configuration = Get-ParsedValue $parsed 'Configuration' 'Release'
    $Filter = Get-ParsedValue $parsed 'Filter' ''
    $List = ($RemainingArgs -contains '-List') -or ($RemainingArgs -contains '--List')
    $TestVerbosity = Get-Arg 'Verbosity' ''

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

    $toRun = if ($Suite -eq 'all') { [string[]]($testSuites.Keys) } else { @($Suite) }
    $testArgs = @('--configuration', $Configuration, '--framework', $Framework, '--no-restore')
    if ($Filter) { $testArgs += @('--filter', $Filter) }

    if ($TestVerbosity) {
        $testArgs += @('--logger', "console;verbosity=$TestVerbosity")
        Write-Host "  Verbosity:     $TestVerbosity"
    }
    Write-Host "Test runner — $($toRun.Count) suite(s)" -ForegroundColor Cyan
    Write-Host "  Framework:     $Framework"
    Write-Host "  Configuration: $Configuration"
    if ($Filter) { Write-Host "  Filter:        $Filter" }
    Write-Host ''

    $failed = @()
    foreach ($key in $toRun) {
        $ts = $testSuites[$key]
        $projectPath = Join-Path $RepoRoot $ts.Project
        Write-Host "  $($ts.Name)..." -ForegroundColor DarkGray
        dotnet test $projectPath @testArgs
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  $($ts.Name) — FAILED" -ForegroundColor Red
            $failed += $ts.Name
        } else {
            Write-Host "  $($ts.Name) — passed" -ForegroundColor Green
        }
    }
    Write-Host ''
    if ($failed.Count -gt 0) {
        Write-Error "Failed suites: $($failed -join ', ')"
        exit 1
    }
    Write-Host 'All test suites passed.' -ForegroundColor Green
    exit 0
}

# ═════════════════════════════════════════════════════════════════════════════
#  AOT
# ═════════════════════════════════════════════════════════════════════════════

if ($Command -eq 'aot') {
    $parsed = Parse-Args $RemainingArgs
    $RuntimeIdentifier = Get-ParsedValue $parsed 'RuntimeIdentifier' ''

    if (-not $RuntimeIdentifier) {
        $RuntimeIdentifier = if ($IsLinux) { 'linux-x64' } elseif ($IsMacOS) { 'osx-x64' } else { 'win-x64' }
    }

    # Use writable NuGet cache locations (default HTTP cache may be read-only in CI)
    if (-not $env:NUGET_HTTP_CACHE_PATH) {
        $env:NUGET_HTTP_CACHE_PATH = Join-Path ([System.IO.Path]::GetTempPath()) 'nuget-http-cache'
    }
    if (-not $env:NUGET_PACKAGES) {
        $env:NUGET_PACKAGES = Join-Path ([System.IO.Path]::GetTempPath()) 'nuget-packages'
    }

    $project = Get-AotProjectPath

    $failed = @()
    foreach ($tfm in @('net10.0', 'net11.0')) {
        Write-Host "Publishing AOT smoke tests for $tfm ($RuntimeIdentifier)..." -ForegroundColor Cyan
        dotnet publish $project -c Release -r $RuntimeIdentifier --self-contained true -f $tfm
        if ($LASTEXITCODE -ne 0) {
            Write-Error "dotnet publish failed for $tfm with exit code $LASTEXITCODE."
            $failed += $tfm
            continue
        }

        $publishDir = Join-Path $RepoRoot "src\devops\Rowles.LeanCorpus.Tests.AOTSmoke\bin\Release\$tfm\$RuntimeIdentifier\publish"
        $exe = if ($RuntimeIdentifier.StartsWith('win-', [StringComparison]::OrdinalIgnoreCase)) {
            Join-Path $publishDir 'Rowles.LeanCorpus.Tests.AOTSmoke.exe'
        } else {
            Join-Path $publishDir 'Rowles.LeanCorpus.Tests.AOTSmoke'
        }

        Write-Host "Running AOT smoke tests for $tfm..." -ForegroundColor Cyan
        & $exe
        if ($LASTEXITCODE -ne 0) {
            Write-Host "AOT smoke tests FAILED for $tfm (exit code $LASTEXITCODE)." -ForegroundColor Red
            $failed += $tfm
        } else {
            Write-Host "AOT smoke tests passed for $tfm." -ForegroundColor Green
        }
    }

    if ($failed.Count -gt 0) {
        Write-Error "AOT smoke tests failed for: $($failed -join ', ')"
        exit 1
    }
    Write-Host 'All AOT smoke tests passed.' -ForegroundColor Green
    exit 0
}

# ═════════════════════════════════════════════════════════════════════════════
#  COVERAGE
# ═════════════════════════════════════════════════════════════════════════════

if ($Command -eq 'coverage') {
    $parsed = Parse-Args $RemainingArgs
    $Framework = Get-ParsedValue $parsed 'Framework' (Get-DefaultFramework)
    $Configuration = Get-ParsedValue $parsed 'Configuration' 'Release'
    $Clean = Get-ParsedValue $parsed 'Clean' $false
    $IncludePerformance = Get-ParsedValue $parsed 'IncludePerformance' $false
    $GenerateReport = Get-ParsedValue $parsed 'GenerateReport' $false

    $testProjectsRoot = Join-Path $RepoRoot 'src\devops'
    $testProjects = @(
        Get-ChildItem $testProjectsRoot -Filter '*.csproj' -Recurse |
            Where-Object {
                $dirName = $_.Directory.Name
                $dirName -like 'Rowles.LeanCorpus.Tests.*' -and
                $dirName -ne 'Rowles.LeanCorpus.Tests.Shared' -and
                $dirName -ne 'Rowles.LeanCorpus.Tests.AOTSmoke' -and
                $dirName -ne 'Rowles.LeanCorpus.Benchmarks'
            } |
            Sort-Object FullName | ForEach-Object { $_.FullName }
    )

    if ($testProjects.Count -eq 0) {
        Write-Error 'No test projects found.'
        exit 1
    }

    $resultsDir = Join-Path $RepoRoot 'coverage-results'
    if ($Clean -and (Test-Path $resultsDir)) {
        Remove-Item $resultsDir -Recurse -Force
    }
    if (-not (Test-Path $resultsDir)) {
        New-Item -ItemType Directory -Path $resultsDir | Out-Null
    }

    Write-Host 'Running tests with coverage collection...' -ForegroundColor Cyan
    Write-Host "  Framework:     $Framework"
    Write-Host "  Configuration: $Configuration"
    Write-Host "  Output:        $resultsDir"
    if (-not $IncludePerformance) {
        Write-Host '  Filter:        Coverage!=Skip'
    }
    Write-Host ''

    foreach ($tp in $testProjects) {
        $projName = [System.IO.Path]::GetFileNameWithoutExtension($tp)
        Write-Host "  $projName..." -ForegroundColor DarkGray
        $covArgs = @('test', $tp, '--configuration', $Configuration, '--framework', $Framework,
            '--collect', 'XPlat Code Coverage', '--results-directory', $resultsDir)
        if (-not $IncludePerformance) {
            $covArgs += @('--filter', 'Coverage!=Skip')
        }
        dotnet @covArgs
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Tests failed for $projName."
            exit $LASTEXITCODE
        }
    }

    $xmlFiles = @(Get-ChildItem $resultsDir -Filter 'coverage.cobertura.xml' -Recurse)
    Write-Host ''
    Write-Host "Coverage data written to: $resultsDir" -ForegroundColor Green
    Write-Host "  Found $($xmlFiles.Count) coverage file(s)."

    if ($GenerateReport) {
        if (-not (Get-Command reportgenerator -ErrorAction SilentlyContinue)) {
            dotnet tool install -g dotnet-reportgenerator-globaltool
        }
        $reportOut = Join-Path $RepoRoot 'docs\coverage'
        $reportPaths = ($xmlFiles | ForEach-Object { $_.FullName }) -join ';'
        Write-Host 'Generating coverage report...' -ForegroundColor Cyan
        reportgenerator "-reports:$reportPaths" "-targetdir:$reportOut" '-reporttypes:Html' '-title:LeanCorpus Coverage'
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Coverage report written to: $reportOut" -ForegroundColor Green
        }
    }
    exit 0
}

# ═════════════════════════════════════════════════════════════════════════════
#  BENCHMARK
# ═════════════════════════════════════════════════════════════════════════════

if ($Command -eq 'benchmark') {
    # ── remote ──
    $subCmd = if ($RemainingArgs.Count -gt 0 -and -not $RemainingArgs[0].StartsWith('-')) { $RemainingArgs[0] } else { 'run' }
    if ($subCmd -eq 'remote') {
        $remoteScript = Join-Path $ScriptsDir 'send-for-bench.ps1'
        & $remoteScript @($RemainingArgs | Select-Object -Skip 1)
        exit $LASTEXITCODE
    }

    # ── Run / List / Help ──
    $suiteMap = Get-SuiteMap
    $stratMap = Get-StratMap

    $Suite = Get-Arg 'Suite' 'all'
    $Strat = Get-Arg 'Strat' 'default'
    $Framework = Get-Arg 'Framework' (Get-DefaultFramework)
    $DocCount = [int](Get-Arg 'DocCount' '0')
    $BookCount = [int](Get-Arg 'BookCount' '200')
    $SourceCommit = Get-Arg 'SourceCommit' ''
    $SourceRef = Get-Arg 'SourceRef' ''
    $SourceManifest = Get-Arg 'SourceManifest' ''
    $PrepareData = Has-Flag 'PrepareData'
    $CorpusOnly = Has-Flag 'CorpusOnly'
    $List = Has-Flag 'List'
    $Dry = Has-Flag 'Dry'
    $GcDump = Has-Flag 'GcDump'
    $Controlled = Has-Flag 'Controlled'

    if ($List) {
        Write-Host ''
        Write-Host '  Available benchmark suites (-Suite):'
        Write-Host ''
        foreach ($name in $suiteMap.Keys) {
            Write-Host ("    {0,-22} {1}" -f $name, $suiteMap[$name])
        }
        Write-Host ''
        Write-Host '  Available strategies (-Strat):'
        Write-Host ''
        foreach ($name in $stratMap.Keys) {
            Write-Host ("    {0,-16} {1}" -f $name, $stratMap[$name])
        }
        Write-Host ''
        exit 0
    }

    $stratCfg = Resolve-Strat $Strat
    $stratDocCount = $stratCfg.DocCount
    $stratJobArgs = $stratCfg.JobArgs

    if ($Controlled) {
        if ($DocCount -le 0 -and $stratDocCount -le 0) { $stratDocCount = 1000 }
        if ($stratJobArgs.Count -eq 0) { $stratJobArgs = @('--job', 'short') }
        $CorpusOnly = $true
    }
    $effectiveDocCount = 0
    if ($DocCount -gt 0)       { $effectiveDocCount = $DocCount }
    elseif ($stratDocCount -gt 0) { $effectiveDocCount = $stratDocCount }

    $projectPath = Get-BenchProjectPath

    # Prepare data
    if ($PrepareData) {
        $dataDir = Join-Path $RepoRoot 'bench\data'
        $gutenbergDir = Join-Path $dataDir 'gutenberg-ebooks'
        $newsDir = Join-Path $dataDir '20newsgroups'
        $reutersDir = Join-Path $dataDir 'reuters21578'
        $gutenbergCount = if (Test-Path $gutenbergDir) {
            (Get-ChildItem $gutenbergDir -Filter '*.txt' -ErrorAction SilentlyContinue).Count
        } else { 0 }
        if ($gutenbergCount -lt $BookCount) {
            Write-Host "Preparing Gutenberg data (BookCount=$BookCount)..." -ForegroundColor Cyan
            & (Join-Path $ScriptsDir 'download-gutenberg.ps1') -BookCount $BookCount
        } else {
            Write-Host "Gutenberg data present ($gutenbergCount books), skipping download." -ForegroundColor DarkGray
        }
        $newsCount = if (Test-Path $newsDir) {
            (Get-ChildItem $newsDir -File -Recurse -ErrorAction SilentlyContinue).Count
        } else { 0 }
        $reutersCount = if (Test-Path $reutersDir) {
            (Get-ChildItem $reutersDir -Filter '*.sgm' -File -ErrorAction SilentlyContinue).Count
        } else { 0 }
        if ($newsCount -eq 0 -or $reutersCount -eq 0) {
            Write-Host 'Preparing news data...' -ForegroundColor Cyan
            & (Join-Path $ScriptsDir 'download-news.ps1')
        } else {
            Write-Host "News data present ($newsCount posts, $reutersCount Reuters files), skipping download." -ForegroundColor DarkGray
        }
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

    # Merge pass-through BDN args (after --)
    $allExtraArgs = @()
    $dashIndex = [Array]::IndexOf($RemainingArgs, '--')
    if ($dashIndex -ge 0 -and $dashIndex -lt $RemainingArgs.Count - 1) {
        $allExtraArgs = $RemainingArgs[($dashIndex + 1)..($RemainingArgs.Count - 1)]
    }

    Write-Host "Suite:      $Suite"
    Write-Host "Strat:      $Strat"
    Write-Host "Framework:  $Framework"
    if ($Controlled)     { Write-Host 'Mode:       controlled' }
    if ($CorpusOnly)     { Write-Host 'CorpusOnly: enabled' }
    if ($effectiveDocCount -gt 0) { Write-Host "Docs:       $effectiveDocCount" }
    if ($stratJobArgs)   { Write-Host "Job:        $($stratJobArgs -join ' ')" }
    if ($allExtraArgs)   { Write-Host "BDN args:   $($allExtraArgs -join ' ')" }

    if ($Dry) {
        Write-Host ''
        Write-Host 'Dry run — command that would execute:'
        Write-Host "  dotnet run -c Release --framework $Framework --project `"$projectPath`" -- $($runArgs -join ' ') $($stratJobArgs -join ' ') $($allExtraArgs -join ' ')"
        Write-Host ''
        exit 0
    }

    if ($GcDump) {
        $runArgs += '--gcdump'
        if (-not (Get-Command dotnet-gcdump -ErrorAction SilentlyContinue)) {
            dotnet tool install -g dotnet-gcdump
        }
    }

    Write-Host ''
    dotnet run -c Release --framework $Framework --project $projectPath -- @runArgs @stratJobArgs @allExtraArgs
    exit $LASTEXITCODE
}

# ═════════════════════════════════════════════════════════════════════════════
#  DATA
# ═════════════════════════════════════════════════════════════════════════════

if ($Command -eq 'data') {
    $parsed = Parse-Args $RemainingArgs
    $dataset = Get-ParsedValue $parsed 'SubCommand' ''
    $passThrough = if ($parsed.ContainsKey('PassThrough')) { $parsed['PassThrough'] } else { @() }

    $valid = @('gutenberg', 'news', 'wikipedia')
    if ($dataset -notin $valid) {
        Write-Error "Unknown dataset '$dataset'. Valid: $($valid -join ', ')"
        exit 1
    }

    $scriptName = "download-$dataset.ps1"
    $scriptPath = Join-Path $ScriptsDir $scriptName
    if (-not (Test-Path $scriptPath)) {
        Write-Error "Script not found: $scriptPath"
        exit 1
    }

    Write-Host "Downloading $dataset data..." -ForegroundColor Cyan
    & $scriptPath @passThrough
    exit $LASTEXITCODE
}

# ═════════════════════════════════════════════════════════════════════════════
#  DOCS
# ═════════════════════════════════════════════════════════════════════════════

if ($Command -eq 'docs') {
    $parsed = Parse-Args $RemainingArgs
    $subCmd = Get-ParsedValue $parsed 'SubCommand' 'build'
    $SkipBenchmarks = Get-ParsedValue $parsed 'SkipBenchmarks' $false
    $SkipCoverage = Get-ParsedValue $parsed 'SkipCoverage' $false

    $docsDir  = Join-Path $RepoRoot 'docs'
    $docfxJson = Join-Path $docsDir 'docfx.json'
    $apiDir   = Join-Path $docsDir 'api'
    $siteDir  = Join-Path $docsDir 'site'

    if (-not (Get-Command docfx -ErrorAction SilentlyContinue)) {
        dotnet tool install -g docfx
    }

    # ── metadata ──
    if ($subCmd -eq 'metadata') {
        Clear-ApiMetadata $docsDir
        Write-Host 'Generating API metadata...' -ForegroundColor Cyan
        docfx metadata $docfxJson
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        Remove-ExternalInheritedMembers $docsDir
        Write-Host 'API metadata written.' -ForegroundColor Green
        exit 0
    }

    # ── serve ──
    if ($subCmd -eq 'serve') {
        Clear-ApiMetadata $docsDir
        Write-Host 'Generating API metadata...' -ForegroundColor Cyan
        docfx metadata $docfxJson
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        Remove-ExternalInheritedMembers $docsDir
        Write-Host 'Building and serving docs on http://0.0.0.0:8080...' -ForegroundColor Cyan
        docfx build $docfxJson
        docfx serve $siteDir --hostname 0.0.0.0 -p 8080
        exit 0
    }

    # ── build (default) ──
    if (-not $SkipBenchmarks) {
        Write-Host 'Generating benchmark pages...' -ForegroundColor Cyan
        & (Join-Path $ScriptsDir 'generate-benchmark-docs.ps1')
    }
    if (-not $SkipCoverage) {
        $xmlFiles = @(Get-ChildItem (Join-Path $RepoRoot 'coverage-results') -Filter 'coverage.cobertura.xml' -Recurse -ErrorAction SilentlyContinue)
        if ($xmlFiles.Count -gt 0) {
            if (-not (Get-Command reportgenerator -ErrorAction SilentlyContinue)) {
                dotnet tool install -g dotnet-reportgenerator-globaltool
            }
            $reportOut = Join-Path $docsDir 'coverage'
            $reportPaths = ($xmlFiles | ForEach-Object { $_.FullName }) -join ';'
            Write-Host 'Generating coverage report...' -ForegroundColor Cyan
            reportgenerator "-reports:$reportPaths" "-targetdir:$reportOut" '-reporttypes:Html' '-title:LeanCorpus Coverage'
        }
    }

    Clear-ApiMetadata $docsDir
    Write-Host 'Generating API metadata...' -ForegroundColor Cyan
    docfx metadata $docfxJson
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Remove-ExternalInheritedMembers $docsDir

    Write-Host 'Building documentation site...' -ForegroundColor Cyan
    docfx build $docfxJson
    Write-Host "Site written to: $siteDir" -ForegroundColor Green
    exit 0
}

# ═════════════════════════════════════════════════════════════════════════════
#  BENCHMARKS DOCS
# ═════════════════════════════════════════════════════════════════════════════

if ($Command -eq 'benchmarks') {
    $parsed = Parse-Args $RemainingArgs
    $subCmd = Get-ParsedValue $parsed 'SubCommand' ''
    if ($subCmd -ne 'docs') {
        Write-Error "Unknown benchmarks subcommand: '$subCmd'. Expected: docs"
        exit 1
    }
    $scriptPath = Join-Path $ScriptsDir 'generate-benchmark-docs.ps1'
    & $scriptPath
    exit $LASTEXITCODE
}

# ── Unknown command ──
Write-Error "Unknown command '$Command'. Run 'devops --help' for usage."
exit 1

# ═════════════════════════════════════════════════════════════════════════════
#  DOCS HELPERS (from docs.ps1)
# ═════════════════════════════════════════════════════════════════════════════

function Clear-ApiMetadata {
    param([string]$DocsDir)
    $apiDir = Join-Path $DocsDir 'api'
    if (-not (Test-Path $apiDir)) {
        New-Item -ItemType Directory -Path $apiDir | Out-Null
        return
    }
    Get-ChildItem $apiDir -Filter '*.yml' -File | Remove-Item -Force
    $tocPath = Join-Path $apiDir 'toc.yml'
    if (Test-Path $tocPath) { Remove-Item $tocPath -Force }
}

function Set-GeneratedContent {
    param([string]$Path, [object]$Value)
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            Set-Content -Path $Path -Value $Value -Encoding utf8
            return
        } catch [System.IO.IOException] {
            if ($attempt -eq 5) { throw }
            Start-Sleep -Milliseconds (100 * $attempt)
        }
    }
}

function Remove-ExternalInheritedMembers {
    param([string]$DocsDir)
    $apiDir = Join-Path $DocsDir 'api'
    if (-not (Test-Path $apiDir)) { return }

    foreach ($file in Get-ChildItem $apiDir -Filter '*.yml' -File) {
        if ($file.Name -eq 'toc.yml') { continue }
        $lines = [string[]](Get-Content $file.FullName)
        $out = [System.Collections.Generic.List[string]]::new()
        for ($i = 0; $i -lt $lines.Length; $i++) {
            if ($lines[$i] -ne '  inheritedMembers:') {
                $out.Add($lines[$i])
                continue
            }
            $keptMembers = [System.Collections.Generic.List[string]]::new()
            $i++
            while ($i -lt $lines.Length -and $lines[$i] -match '^  - (.+)$') {
                if ($Matches[1].StartsWith('Rowles.LeanCorpus.', [StringComparison]::Ordinal)) {
                    $keptMembers.Add($lines[$i])
                }
                $i++
            }
            if ($keptMembers.Count -gt 0) {
                $out.Add('  inheritedMembers:')
                $out.AddRange($keptMembers)
            }
            $i--
        }
        Set-GeneratedContent -Path $file.FullName -Value $out
    }
}
