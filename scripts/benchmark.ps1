<#
.SYNOPSIS
    Unified LeanCorpus benchmark runner.

.DESCRIPTION
    Runs BenchmarkDotNet suites for LeanCorpus. Results are written to
    bench/{machine}/{date}/{time}/ with JSON, Markdown, and HTML output.
    A consolidated report.json and per-machine index.json are generated.

.PARAMETER Suite
    Which benchmark suite to run. Default: all.
    Valid values: all, index, query, analysis, boolean, phrase, prefix,
    fuzzy, wildcard, deletion, deletion-queue, deletion-commit, suggester, schemajson, indexsort,
    blockjoin, blockjoin-index, blockjoin-search, range, regexp, dismax, multiphrase, span, mlt, highlighter,
    searcher-mgr, combined, terminset, aggregation, query-cache, parallel,
    function-score, geo, collapse-facet, similarity, stemmer, ngram, synonym,
    async-index, gutenberg-analysis, gutenberg-index, gutenberg-search,
    tokenbudget, diagnostics.

.PARAMETER Strat
    Predefined strategy that configures DocCount and BDN job.
    Valid values: default, fast, quick-compare, intense, stress, exhaustive.
      default       - 20K docs, --job short (development baseline). Type: short.
      fast          - 500 docs, --job dry (minimal smoke-test). Type: smoke.
      quick-compare - 1000 docs, --job short (quick comparison). Type: partial.
      intense       - 10K docs, default BDN job. Type: full.
      stress        - 50K docs, default BDN job. Type: stress.
      exhaustive    - 100K docs, default BDN job (official reference). Type: exhaustive.

.PARAMETER DocCount
    Override document count for all suites.

.PARAMETER Framework
    Target framework for the benchmark host. Defaults to net10.0.

.PARAMETER PrepareData
    Check for benchmark data and download if absent. Runs download-gutenberg.ps1
    and download-news.ps1 before benchmarking. Safe to re-run; existing files are
    skipped. Pass -BookCount to control how many Gutenberg books to fetch.

.PARAMETER BookCount
    Number of Gutenberg books to download when -PrepareData is used. Defaults to 200.

.PARAMETER CorpusOnly
    Skip Lucene.NET comparison benchmarks; run LeanCorpus methods only.

.PARAMETER Help
    Show usage information and exit.

.PARAMETER List
    List available benchmark suites and exit.

.PARAMETER Dry
    Print the dotnet command that would be run without executing it.

.PARAMETER GcDump
    Collect GC heap dumps after each benchmark run (requires dotnet-gcdump).

.PARAMETER Controlled
    Use a deterministic local diagnostic preset. Defaults to 1000 documents,
    --job short, and LeanCorpus-only when those are not otherwise specified.

.PARAMETER SourceCommit
    Source commit for copied benchmark runs where .git is unavailable.

.PARAMETER SourceRef
    Source branch or ref for copied benchmark runs where .git is unavailable.

.PARAMETER SourceManifest
    Path to a source manifest for copied benchmark runs where .git is unavailable.

.PARAMETER BenchmarkArgs
    Additional arguments passed through to BenchmarkDotNet
    (e.g. --filter "*Lean*", --job short, --memory true).

.EXAMPLE
    .\scripts\benchmark.ps1
    Runs all standard benchmark suites with default settings.

.EXAMPLE
    .\scripts\benchmark.ps1 -Suite query
    Runs only the TermQuery benchmark suite.

.EXAMPLE
    .\scripts\benchmark.ps1 -Suite gutenberg-search -CorpusOnly
    Runs Gutenberg search benchmarks, LeanCorpus only.

.EXAMPLE
    .\scripts\benchmark.ps1 -Strat fast -Suite boolean
    Runs BooleanQuery benchmarks with 500 docs and --job dry.

.EXAMPLE
    .\scripts\benchmark.ps1 -Strat intense -DocCount 20000
    Runs all suites with 20K docs.

.EXAMPLE
    .\scripts\benchmark.ps1 -List
    Lists all available benchmark suites and strategies.

.EXAMPLE
    .\scripts\benchmark.ps1 -Dry -Suite index -Strat fast
    Prints the command that would run without executing it.
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [ValidateSet('all', 'index', 'query', 'analysis', 'boolean', 'phrase',
                 'prefix', 'fuzzy', 'wildcard', 'deletion', 'deletion-queue', 'deletion-commit',
                 'suggester', 'schemajson', 'indexsort', 'blockjoin', 'blockjoin-index', 'blockjoin-search',
                 'range', 'regexp', 'dismax', 'multiphrase', 'span',
                 'mlt', 'highlighter', 'searcher-mgr',
                 'combined', 'terminset', 'aggregation', 'query-cache',
                 'parallel', 'function-score', 'geo', 'collapse-facet', 'similarity',
                 'stemmer', 'kstemmer', 'lightenglish', 'hunspell', 'ngram', 'synonym', 'async-index',
                 'gutenberg-analysis', 'gutenberg-index', 'gutenberg-search',
                 'tokenbudget', 'diagnostics')]
    [string]$Suite = 'all',

    [ValidateSet('default', 'fast', 'quick-compare', 'intense', 'stress', 'exhaustive')]
    [string]$Strat = 'default',

    [ValidateSet('net10.0', 'net11.0')]
    [string]$Framework = 'net10.0',

    [int]$DocCount = 0,

    [switch]$PrepareData,

    [int]$BookCount = 200,

    [switch]$CorpusOnly,

    [switch]$Help,

    [switch]$List,

    [switch]$Dry,

    [switch]$GcDump,

    [switch]$Controlled,

    [string]$SourceCommit = '',

    [string]$SourceRef = '',

    [string]$SourceManifest = '',

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$BenchmarkArgs
)

function Test-HasBdnOption {
    param(
        [string[]]$Arguments,
        [string[]]$Names
    )

    foreach ($argument in $Arguments) {
        foreach ($name in $Names) {
            if ($argument.Equals($name, [System.StringComparison]::OrdinalIgnoreCase) -or
                $argument.StartsWith("$name=", [System.StringComparison]::OrdinalIgnoreCase)) {
                return $true
            }
        }
    }

    return $false
}

if ($BenchmarkArgs -and $BenchmarkArgs.Count -gt 0 -and $BenchmarkArgs[0] -eq '--') {
    if ($BenchmarkArgs.Count -eq 1) {
        $BenchmarkArgs = @()
    } else {
        $BenchmarkArgs = $BenchmarkArgs[1..($BenchmarkArgs.Count - 1)]
    }
}

$suiteDescriptions = [ordered]@{
    all                  = 'Run all primary benchmark suites, including Gutenberg (default)'
    index                = 'IndexingBenchmarks          -- bulk indexing throughput (vs Lucene.NET)'
    query                = 'TermQueryBenchmarks         -- single-term search (vs Lucene.NET)'
    analysis             = 'AnalysisBenchmarks          -- tokenisation pipeline'
    boolean              = 'BooleanQueryBenchmarks      -- deterministic BooleanQuery clause shapes'
    phrase               = 'PhraseQueryBenchmarks       -- exact and slop phrase'
    prefix               = 'PrefixQueryBenchmarks       -- prefix matching (vs Lucene.NET)'
    fuzzy                = 'FuzzyQueryBenchmarks        -- deterministic fuzzy/edit-distance scenarios'
    wildcard             = 'WildcardQueryBenchmarks     -- wildcard patterns'
    deletion             = 'DeletionQueue/Commit        -- delete queueing and commit application'
    'deletion-queue'     = 'DeletionQueueBenchmarks     -- enqueue delete terms (vs Lucene.NET)'
    'deletion-commit'    = 'DeletionCommitBenchmarks    -- apply queued deletes on commit (vs Lucene.NET)'
    suggester            = 'SuggesterBenchmarks         -- DidYouMean spelling (vs Lucene.NET)'
    schemajson           = 'SchemaAndJsonBenchmarks     -- schema validation + JSON mapping'
    indexsort            = 'IndexSortIndex/Search       -- index-time sort + early termination'
    blockjoin            = 'BlockJoinIndex/Search       -- block-join indexing and query hot path'
    'blockjoin-index'    = 'BlockJoinIndexBenchmarks    -- block-join indexing (vs Lucene.NET)'
    'blockjoin-search'   = 'BlockJoinSearchBenchmarks   -- block-join query hot path (vs Lucene.NET)'
    range                = 'RangeQueryBenchmarks        -- BKD range query (vs Lucene.NET NumericRange)'
    regexp               = 'RegexpQueryBenchmarks       -- regexp query parity (vs Lucene.NET)'
    dismax               = 'DisjunctionMaxQueryBenchmarks -- tie-breaker parity (vs Lucene.NET)'
    multiphrase          = 'MultiPhraseQueryBenchmarks  -- multi-slot phrase parity (vs Lucene.NET)'
    span                 = 'SpanQueryBenchmarks         -- span Near/Or/Not parity (vs Lucene.NET)'
    mlt                  = 'MoreLikeThisBenchmarks      -- MoreLikeThis query (standalone)'
    highlighter          = 'HighlighterBenchmarks       -- snippet highlighting (standalone)'
    'searcher-mgr'       = 'SearcherManagerBenchmarks   -- acquire/release hot path (vs Lucene.NET)'
    combined             = 'CombinedFieldsQueryBenchmarks -- BM25F multi-field (standalone)'
    terminset            = 'TermInSetQueryBenchmarks    -- set membership vs BooleanQuery(Should)'
    aggregation          = 'AggregationBenchmarks       -- stats and histogram aggregation overhead'
    'query-cache'        = 'QueryCacheBenchmarks        -- query cache hit/miss/warm overhead'
    parallel             = 'ParallelSearchBenchmarks    -- ParallelSearch=true vs false'
    'function-score'     = 'FunctionScoreQueryBenchmarks -- score mode variants'
    geo                  = 'GeoQueryBenchmarks          -- distance and bounding-box queries'
    'collapse-facet'     = 'CollapseAndFacetBenchmarks  -- field collapse + facet collection'
    similarity           = 'SimilarityBenchmarks        -- BM25 vs TF-IDF'
    stemmer              = 'StemmerParityBenchmarks     -- StemmedAnalyser vs EnglishAnalyzer'
    kstemmer             = 'KStemmerParityBenchmarks    -- Krovetz stemmer throughput'
    lightenglish         = 'LightEnglishStemmerBenchmarks -- LightEnglish vs Porter throughput'
    hunspell             = 'HunspellBenchmarks          -- Hunspell parse and stem throughput'
    ngram                = 'NGramTokeniserBenchmarks    -- edge/full N-gram parity (vs Lucene.NET)'
    synonym              = 'SynonymBenchmarks           -- SynonymGraphFilter indexing overhead'
    'async-index'        = 'AsyncIndexingBenchmarks     -- sync vs async vs batch ingestion'
    'gutenberg-analysis' = 'GutenbergAnalysis           -- analysis on real ebook text'
    'gutenberg-index'    = 'GutenbergIndex              -- indexing real ebook data'
    'gutenberg-search'   = 'GutenbergSearch             -- search on real ebook data'
    tokenbudget          = 'TokenBudgetBenchmarks       -- token budget enforcement overhead (explicit only)'
    diagnostics          = 'DiagnosticsBenchmarks       -- SlowQueryLog + Analytics overhead (explicit only)'
}

$stratDescriptions = [ordered]@{
    'default'       = '20K docs, --job short (development baseline).'
    'fast'          = '500 docs, --job dry (minimal smoke-test).'
    'quick-compare' = '1000 docs, --job short (quick comparison).'
    'intense'       = '10K docs, default BDN job.'
    'stress'        = '50K docs, default BDN job.'
    'exhaustive'    = '100K docs, default BDN job (official reference).'
}

if ($Help) {
    Write-Host ''
    Write-Host '  LeanCorpus Benchmark Runner'
    Write-Host '  ============================'
    Write-Host ''
    Write-Host '  Usage:'
    Write-Host '    .\scripts\benchmark.ps1 [options] [BenchmarkDotNet args...]'
    Write-Host ''
    Write-Host '  Options:'
    Write-Host '    -Suite <name>      Benchmark suite to run (default: all)'
    Write-Host '    -Strat <name>      Predefined strategy (default: default)'
    Write-Host '    -Framework <tfm>   Target framework for dotnet run (default: net10.0)'
    Write-Host '    -DocCount <n>      Override document count (overrides -Strat)'
    Write-Host '    -PrepareData       Download benchmark data if not already present'
    Write-Host '    -BookCount <n>     Number of Gutenberg books to fetch with -PrepareData (default: 200)'
    Write-Host '    -CorpusOnly        Skip Lucene.NET comparison benchmarks'
    Write-Host '    -List              List available suites and strategies and exit'
    Write-Host '    -Dry               Print the command that would run without executing it'
    Write-Host '    -GcDump            Collect GC heap dumps (requires dotnet-gcdump)'
    Write-Host '    -Controlled        Use a deterministic local diagnostic preset'
    Write-Host '    -SourceCommit <s>  Source commit for copied runs without .git'
    Write-Host '    -SourceRef <s>     Source branch/ref for copied runs without .git'
    Write-Host '    -SourceManifest <p> Source manifest path for copied runs'
    Write-Host '    -Help              Show this help message and exit'
    Write-Host ''
    Write-Host '  Suites (-Suite):'
    foreach ($name in $suiteDescriptions.Keys) {
        Write-Host ("    {0,-22} {1}" -f $name, $suiteDescriptions[$name])
    }
    Write-Host ''
    Write-Host '  Strategies (-Strat):'
    foreach ($name in $stratDescriptions.Keys) {
        Write-Host ("    {0,-16} {1}" -f $name, $stratDescriptions[$name])
    }
    Write-Host ''
    Write-Host '  Output:'
    Write-Host '    bench/{machine-name}/{yyyy-MM-dd}/{HH-mm}/'
    Write-Host '    bench/{machine-name}/index.json   Per-machine run index'
    Write-Host ''
    Write-Host '  BenchmarkDotNet pass-through examples:'
    Write-Host '    --filter *Lean*            Run only LeanCorpus methods'
    Write-Host '    --job short                Use the Short job instead of Default'
    Write-Host '    --runtimes net10.0         Override the target runtime'
    Write-Host ''
    Write-Host '  Examples:'
    Write-Host '    .\scripts\benchmark.ps1                                          # all suites'
    Write-Host '    .\scripts\benchmark.ps1 -Suite query                             # query only'
    Write-Host '    .\scripts\benchmark.ps1 -Suite gutenberg-search -CorpusOnly      # real data, corpus only'
    Write-Host '    .\scripts\benchmark.ps1 -Strat fast -Suite boolean               # smoke: boolean'
    Write-Host '    .\scripts\benchmark.ps1 -Strat intense -DocCount 20000           # full: 20K docs'
    Write-Host '    .\scripts\benchmark.ps1 -List                                    # list suites'
    Write-Host '    .\scripts\benchmark.ps1 -Dry -Suite index -Strat fast            # dry run'
    Write-Host ''
    exit 0
}

if ($List) {
    Write-Host ""
    Write-Host "  Available benchmark suites (-Suite):"
    Write-Host ""
    foreach ($name in $suiteDescriptions.Keys) {
        Write-Host ("    {0,-22} {1}" -f $name, $suiteDescriptions[$name])
    }
    Write-Host ""
    Write-Host "  Available strategies (-Strat):"
    Write-Host ""
    foreach ($name in $stratDescriptions.Keys) {
        Write-Host ("    {0,-16} {1}" -f $name, $stratDescriptions[$name])
    }
    Write-Host ""
    exit 0
}

# --- Resolve strategy presets ---
$stratDocCount = 0
$stratJobArgs = @()

switch ($Strat) {
    'default' {
        $stratJobArgs = @('--job', 'short')
    }
    'fast' {
        $stratDocCount = 500
        $stratJobArgs = @('--job', 'dry')
    }
    'quick-compare' {
        $stratDocCount = 1000
        $stratJobArgs = @('--job', 'short')
    }
    'intense' {
        $stratDocCount = 10000
    }
    'stress' {
        $stratDocCount = 50000
    }
    'exhaustive' {
        $stratDocCount = 100000
    }
}

if ($Controlled) {
    if ($DocCount -le 0 -and $stratDocCount -le 0) {
        $stratDocCount = 1000
    }
    if ($stratJobArgs.Count -eq 0 -and -not (Test-HasBdnOption $BenchmarkArgs @('--job', '-j'))) {
        $stratJobArgs = @('--job', 'short')
    }
    $CorpusOnly = $true
}

# DocCount parameter overrides strategy; if neither set, leave unset (BDN [Params] defaults)
$effectiveDocCount = 0
if ($DocCount -gt 0) {
    $effectiveDocCount = $DocCount
} elseif ($stratDocCount -gt 0) {
    $effectiveDocCount = $stratDocCount
}

$projectPath = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot "..\src\devops\Rowles.LeanCorpus.Benchmarks\Rowles.LeanCorpus.Benchmarks.csproj")
)

if (-not (Test-Path $projectPath)) {
    Write-Error "Benchmark project not found at: $projectPath"
    exit 1
}

# --- Prepare data if requested ---
if ($PrepareData) {
    $dataDir      = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\bench\data"))
    $gutenbergDir = Join-Path $dataDir "gutenberg-ebooks"
    $newsDir      = Join-Path $dataDir "20newsgroups"
    $reutersDir   = Join-Path $dataDir "reuters21578"

    $gutenbergCount = if (Test-Path $gutenbergDir) {
        (Get-ChildItem -Path $gutenbergDir -Filter "*.txt" -ErrorAction SilentlyContinue).Count
    } else { 0 }

    if ($gutenbergCount -lt $BookCount) {
        Write-Host "Preparing Gutenberg data (BookCount=$BookCount)..." -ForegroundColor Cyan
        & (Join-Path $PSScriptRoot "download-gutenberg.ps1") -BookCount $BookCount
    } else {
        Write-Host "Gutenberg data present ($gutenbergCount books), skipping download." -ForegroundColor DarkGray
    }

    $newsCount = if (Test-Path $newsDir) {
        (Get-ChildItem -Path $newsDir -File -Recurse -ErrorAction SilentlyContinue).Count
    } else { 0 }

    $reutersCount = if (Test-Path $reutersDir) {
        (Get-ChildItem -Path $reutersDir -Filter "*.sgm" -File -ErrorAction SilentlyContinue).Count
    } else { 0 }

    if ($newsCount -eq 0 -or $reutersCount -eq 0) {
        Write-Host "Preparing news data..." -ForegroundColor Cyan
        & (Join-Path $PSScriptRoot "download-news.ps1")
    } else {
        Write-Host "News data present ($newsCount posts, $reutersCount Reuters files), skipping download." -ForegroundColor DarkGray
    }

    Write-Host ""
}

# Build argument list
$runArgs = @('--suite', $Suite)

if ($effectiveDocCount -gt 0) {
    $runArgs += @('--doccount', $effectiveDocCount.ToString())
    $env:BENCH_DOC_COUNT = $effectiveDocCount.ToString()
}

if ($CorpusOnly) {
    $runArgs += '--corpus-only'
}

if (-not [string]::IsNullOrWhiteSpace($SourceCommit)) {
    $env:BENCH_SOURCE_COMMIT = $SourceCommit
}
if (-not [string]::IsNullOrWhiteSpace($SourceRef)) {
    $env:BENCH_SOURCE_REF = $SourceRef
}
if (-not [string]::IsNullOrWhiteSpace($SourceManifest)) {
    $env:BENCH_SOURCE_MANIFEST = [System.IO.Path]::GetFullPath($SourceManifest)
}

# Prepend strategy job args before user-supplied BDN args
$allExtraArgs = @()
if (-not (Test-HasBdnOption $BenchmarkArgs @('--job', '-j'))) {
    $allExtraArgs += $stratJobArgs
}
if ($BenchmarkArgs) { $allExtraArgs += $BenchmarkArgs }

Write-Host "Suite:    $Suite"
Write-Host "Strat:    $Strat"
Write-Host "Framework: $Framework"
if ($Controlled) { Write-Host "Mode:     controlled" }
if ($CorpusOnly) { Write-Host "CorpusOnly: enabled" }
if ($effectiveDocCount -gt 0) {
    Write-Host "Docs:     $effectiveDocCount"
}
if ($allExtraArgs) {
    Write-Host "Extra:    $($allExtraArgs -join ' ')"
}

if ($Dry) {
    $cmdDisplay = "dotnet run -c Release --framework $Framework --project `"$projectPath`" -- $($runArgs -join ' ')"
    if ($allExtraArgs) { $cmdDisplay += " $($allExtraArgs -join ' ')" }
    Write-Host ""
    Write-Host 'Dry run - command that would execute:'
    Write-Host "  $cmdDisplay"
    if ($effectiveDocCount -gt 0) {
        Write-Host "  env: BENCH_DOC_COUNT=$effectiveDocCount"
    }
    if ($env:BENCH_SOURCE_COMMIT) { Write-Host "  env: BENCH_SOURCE_COMMIT=$($env:BENCH_SOURCE_COMMIT)" }
    if ($env:BENCH_SOURCE_REF) { Write-Host "  env: BENCH_SOURCE_REF=$($env:BENCH_SOURCE_REF)" }
    if ($env:BENCH_SOURCE_MANIFEST) { Write-Host "  env: BENCH_SOURCE_MANIFEST=$($env:BENCH_SOURCE_MANIFEST)" }
    Write-Host ""
    exit 0
}

if ($GcDump) {
    $runArgs += '--gcdump'
    if (-not (Get-Command dotnet-gcdump -ErrorAction SilentlyContinue)) {
        Write-Host 'Installing dotnet-gcdump global tool...'
        dotnet tool install -g dotnet-gcdump
    }
    Write-Host "GcDump:   enabled"
}

Write-Host ""
dotnet run -c Release --framework $Framework --project $projectPath -- @runArgs @allExtraArgs
