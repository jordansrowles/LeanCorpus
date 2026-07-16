#!/usr/bin/env bash
# Unified LeanCorpus benchmark runner.
#
# Usage:
#   ./scripts/benchmark.sh [options] [BenchmarkDotNet args...]
#
# Options:
#   --suite <name>        Benchmark suite to run (default: all)
#   --strat <name>        Predefined strategy (default: default)
#   --framework <tfm>     Target framework for dotnet run (default: net10.0)
#   --doccount <n>        Override document count (overrides --strat)
#   --prepare-data        Download benchmark data if not already present
#   --book-count <n>      Number of Gutenberg books to fetch with --prepare-data (default: 200)
#   --corpus-only         Skip Lucene.NET comparison benchmarks
#   --list                List available suites and strategies and exit
#   --dry                 Print the command that would run without executing it
#   --gcdump              Collect GC heap dumps (requires dotnet-gcdump)
#   --controlled          Use a deterministic local diagnostic preset
#   --source-commit <s>   Source commit for copied runs without .git
#   --source-ref <s>      Source branch/ref for copied runs without .git
#   --source-manifest <p> Source manifest path for copied runs
#   --help                Show help and exit
#
# Extra arguments after -- are passed through to BenchmarkDotNet.
#
# Examples:
#   ./scripts/benchmark.sh
#   ./scripts/benchmark.sh --suite query
#   ./scripts/benchmark.sh --suite gutenberg-search --corpus-only
#   ./scripts/benchmark.sh --strat fast --suite boolean
#   ./scripts/benchmark.sh --strat intense --doccount 20000
#   ./scripts/benchmark.sh --prepare-data --book-count 200
#   ./scripts/benchmark.sh --list
#   ./scripts/benchmark.sh --dry --suite index --strat fast

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJECT_PATH="$REPO_ROOT/src/devops/Rowles.LeanCorpus.Benchmarks/Rowles.LeanCorpus.Benchmarks.csproj"

SUITE="all"
STRAT="default"
TARGET_FRAMEWORK="net10.0"
DOC_COUNT=0
CORPUS_ONLY=false
DRY=false
LIST=false
HELP=false
GCDUMP=false
PREPARE_DATA=false
BOOK_COUNT=200
CONTROLLED=false
SOURCE_COMMIT=""
SOURCE_REF=""
SOURCE_MANIFEST=""
EXTRA_ARGS=()

declare -A SUITE_DESC=(
    [all]="Run all primary benchmark suites, including Gutenberg (default)"
    [index]="IndexingBenchmarks          -- bulk indexing throughput (vs Lucene.NET)"
    [query]="TermQueryBenchmarks         -- single-term search (vs Lucene.NET)"
    [analysis]="AnalysisBenchmarks          -- tokenisation pipeline"
    ["analysis-filters-v2"]="NewTokenFilterBenchmarks    -- new filter parity (Classic, PatternReplace, CommonGrams, HyphenatedWords, Caching)"
    ["pattern-tokeniser"]="PatternTokeniserBenchmarks  -- regex tokenisation (vs Lucene.NET PatternTokenizer)"
    [boolean]="BooleanQueryBenchmarks      -- deterministic BooleanQuery clause shapes"
    [phrase]="PhraseQueryBenchmarks       -- exact and slop phrase"
    [prefix]="PrefixQueryBenchmarks        -- prefix matching (vs Lucene.NET)"
    [fuzzy]="FuzzyQueryBenchmarks        -- deterministic fuzzy/edit-distance scenarios"
    [wildcard]="WildcardQueryBenchmarks     -- wildcard patterns"
    [deletion]="DeletionQueue/Commit        -- delete queueing and commit application"
    ["deletion-queue"]="DeletionQueueBenchmarks     -- enqueue delete terms (vs Lucene.NET)"
    ["deletion-commit"]="DeletionCommitBenchmarks    -- apply queued deletes on commit (vs Lucene.NET)"
    [suggester]="SuggesterBenchmarks         -- DidYouMean spelling (vs Lucene.NET)"
    [schemajson]="SchemaAndJsonBenchmarks     -- schema validation + JSON mapping"
    [indexsort]="IndexSortIndex/Search       -- index-time sort + early termination"
    [blockjoin]="BlockJoinIndex/Search       -- block-join indexing and query hot path"
    ["blockjoin-index"]="BlockJoinIndexBenchmarks    -- block-join indexing (vs Lucene.NET)"
    ["blockjoin-search"]="BlockJoinSearchBenchmarks   -- block-join query hot path (vs Lucene.NET)"
    [range]="RangeQueryBenchmarks        -- BKD range query (vs Lucene.NET NumericRange)"
    [regexp]="RegexpQueryBenchmarks       -- regexp query parity (vs Lucene.NET)"
    [dismax]="DisjunctionMaxQueryBenchmarks -- tie-breaker parity (vs Lucene.NET)"
    [multiphrase]="MultiPhraseQueryBenchmarks  -- multi-slot phrase parity (vs Lucene.NET)"
    [span]="SpanQueryBenchmarks         -- span Near/Or/Not parity (vs Lucene.NET)"
    [mlt]="MoreLikeThisBenchmarks + MoreLikeThisSingleSegmentBenchmarks -- MoreLikeThis query (standalone)"
    [highlighter]="HighlighterBenchmarks       -- snippet highlighting (standalone)"
    ["searcher-mgr"]="SearcherManagerBenchmarks   -- acquire/release hot path (vs Lucene.NET)"
    [combined]="CombinedFieldsQueryBenchmarks -- BM25F multi-field (standalone)"
    [terminset]="TermInSetQueryBenchmarks    -- set membership vs BooleanQuery(Should)"
    [aggregation]="AggregationBenchmarks       -- stats and histogram aggregation overhead"
    ["query-cache"]="QueryCacheBenchmarks        -- query cache hit/miss/warm overhead"
    [parallel]="ParallelSearchBenchmarks    -- ParallelSearch=true vs false"
    ["function-score"]="FunctionScoreQueryBenchmarks -- score mode variants"
    [geo]="GeoQueryBenchmarks          -- distance and bounding-box queries"
    ["collapse-facet"]="CollapseAndFacetBenchmarks  -- field collapse + facet collection"
    [similarity]="SimilarityBenchmarks        -- BM25 vs TF-IDF"
    [stemmer]="StemmerParityBenchmarks     -- StemmedAnalyser vs EnglishAnalyzer"
    [kstemmer]="KStemmerParityBenchmarks    -- Krovetz stemmer throughput"
    [lightenglish]="LightEnglishStemmerBenchmarks -- LightEnglish vs Porter throughput"
    [hunspell]="HunspellBenchmarks          -- Hunspell parse and stem throughput"
    [ngram]="NGramTokeniserBenchmarks    -- edge/full N-gram parity (vs Lucene.NET)"
    [synonym]="SynonymBenchmarks           -- SynonymGraphFilter indexing overhead"
    ["async-index"]="AsyncIndexingBenchmarks     -- sync vs async vs batch ingestion"
    ["gutenberg-analysis"]="GutenbergAnalysis           -- analysis on real ebook text"
    ["gutenberg-index"]="GutenbergIndex              -- indexing real ebook data"
    ["gutenberg-search"]="GutenbergSearch             -- search on real ebook data"
    [tokenbudget]="TokenBudgetBenchmarks     -- token budget enforcement overhead (explicit only)"
    [diagnostics]="DiagnosticsBenchmarks     -- SlowQueryLog + Analytics overhead (explicit only)"
    ["packed-int-codec"]="PackedIntCodecBenchmarks    -- Pack/Unpack scalar loop throughput (explicit only)"
    ["numeric-aggregator"]="NumericAggregatorSimdBenchmarks -- scalar vs Vector256 aggregation (explicit only)"
    ["index-writer"]="IndexWriterContentionBenchmarks -- concurrent AddDocument throughput (explicit only)"
    ["concurrent-write"]="ConcurrentVsSequentialBenchmarks -- DWPT parallel vs sequential indexing throughput (explicit only)"
)

SUITE_ORDER=(
    all index query analysis boolean phrase prefix fuzzy wildcard deletion deletion-queue deletion-commit
    suggester schemajson indexsort blockjoin blockjoin-index blockjoin-search
    range regexp dismax multiphrase span
    mlt highlighter searcher-mgr
    combined terminset aggregation query-cache parallel function-score geo collapse-facet similarity
    stemmer kstemmer lightenglish hunspell ngram synonym async-index
    gutenberg-analysis gutenberg-index gutenberg-search tokenbudget diagnostics
    analysis-filters-v2 pattern-tokeniser
    packed-int-codec numeric-aggregator index-writer concurrent-write
)

declare -A STRAT_DESC=(
    [default]="20K docs, --job short (development baseline)."
    [fast]="500 docs, --job dry (minimal smoke-test)."
    [quick-compare]="1000 docs, --job short (quick comparison)."
    [intense]="10K docs, default BDN job."
    [stress]="50K docs, default BDN job."
    [exhaustive]="100K docs, default BDN job (official reference)."
)

STRAT_ORDER=(default fast quick-compare intense stress exhaustive)

contains() {
    local needle="$1"
    shift
    local item
    for item in "$@"; do
        if [[ "$item" == "$needle" ]]; then
            return 0
        fi
    done
    return 1
}

has_bdn_option() {
    local item
    local name
    for item in "${EXTRA_ARGS[@]}"; do
        for name in "$@"; do
            if [[ "$item" == "$name" || "$item" == "$name="* ]]; then
                return 0
            fi
        done
    done
    return 1
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --suite)
            SUITE="${2:-}"
            shift 2
            ;;
        --strat)
            STRAT="${2:-}"
            shift 2
            ;;
        --framework)
            TARGET_FRAMEWORK="${2:-}"
            shift 2
            ;;
        --doccount)
            DOC_COUNT="${2:-}"
            shift 2
            ;;
        --prepare-data)
            PREPARE_DATA=true
            shift
            ;;
        --book-count)
            BOOK_COUNT="${2:-}"
            shift 2
            ;;
        --corpus-only)
            CORPUS_ONLY=true
            shift
            ;;
        --dry)
            DRY=true
            shift
            ;;
        --list)
            LIST=true
            shift
            ;;
        --gcdump)
            GCDUMP=true
            shift
            ;;
        --controlled)
            CONTROLLED=true
            shift
            ;;
        --source-commit)
            SOURCE_COMMIT="${2:-}"
            shift 2
            ;;
        --source-ref)
            SOURCE_REF="${2:-}"
            shift 2
            ;;
        --source-manifest)
            SOURCE_MANIFEST="${2:-}"
            shift 2
            ;;
        --help|-h)
            HELP=true
            shift
            ;;
        --)
            shift
            EXTRA_ARGS+=("$@")
            break
            ;;
        *)
            EXTRA_ARGS+=("$1")
            shift
            ;;
    esac
done

if ! contains "$SUITE" "${SUITE_ORDER[@]}"; then
    echo "Error: invalid suite '$SUITE'. Valid: ${SUITE_ORDER[*]}" >&2
    exit 1
fi

if ! contains "$STRAT" "${STRAT_ORDER[@]}"; then
    echo "Error: invalid strat '$STRAT'. Valid: ${STRAT_ORDER[*]}" >&2
    exit 1
fi

if $HELP; then
    echo ""
    echo "  LeanCorpus Benchmark Runner"
    echo "  ============================"
    echo ""
    echo "  Usage:"
    echo "    ./scripts/benchmark.sh [options] [BenchmarkDotNet args...]"
    echo ""
    echo "  Options:"
    echo "    --suite <name>        Benchmark suite to run (default: all)"
    echo "    --strat <name>        Predefined strategy (default: default)"
    echo "    --framework <tfm>     Target framework for dotnet run (default: net10.0)"
    echo "    --doccount <n>        Override document count (overrides --strat)"
    echo "    --prepare-data        Download benchmark data if not already present"
    echo "    --book-count <n>      Number of Gutenberg books to fetch (default: 200)"
    echo "    --corpus-only         Skip Lucene.NET comparison benchmarks"
    echo "    --list                List available suites and strategies and exit"
    echo "    --dry                 Print the command that would run without executing it"
    echo "    --gcdump              Collect GC heap dumps (requires dotnet-gcdump)"
    echo "    --controlled          Use a deterministic local diagnostic preset"
    echo "    --source-commit <s>   Source commit for copied runs without .git"
    echo "    --source-ref <s>      Source branch/ref for copied runs without .git"
    echo "    --source-manifest <p> Source manifest path for copied runs"
    echo "    --help                Show this help message and exit"
    echo ""
    echo "  Suites (--suite):"
    for name in "${SUITE_ORDER[@]}"; do
        printf "    %-22s %s\n" "$name" "${SUITE_DESC[$name]}"
    done
    echo ""
    echo "  Strategies (--strat):"
    for name in "${STRAT_ORDER[@]}"; do
        printf "    %-16s %s\n" "$name" "${STRAT_DESC[$name]}"
    done
    echo ""
    echo "  Output:"
    echo "    bench/{machine-name}/{yyyy-MM-dd}/{HH-mm}/"
    echo "    bench/{machine-name}/index.json   Per-machine run index"
    echo ""
    echo "  BenchmarkDotNet pass-through examples:"
    echo "    --filter *Lean*            Run only LeanCorpus methods"
    echo "    --job short                Use the Short job instead of Default"
    echo "    --runtimes net10.0         Override the target runtime"
    echo ""
    echo "  Examples:"
    echo "    ./scripts/benchmark.sh                                          # all suites"
    echo "    ./scripts/benchmark.sh --suite query                            # query only"
    echo "    ./scripts/benchmark.sh --suite gutenberg-search --corpus-only   # real data, corpus only"
    echo "    ./scripts/benchmark.sh --strat fast --suite boolean             # smoke: boolean"
    echo "    ./scripts/benchmark.sh --strat intense --doccount 20000         # full: 20K docs"
    echo "    ./scripts/benchmark.sh --prepare-data --book-count 200          # fetch data then run all"
    echo "    ./scripts/benchmark.sh --list                                    # list suites"
    echo "    ./scripts/benchmark.sh --dry --suite index --strat fast         # dry run"
    echo ""
    exit 0
fi

if $LIST; then
    echo ""
    echo "  Available benchmark suites (--suite):"
    echo ""
    for name in "${SUITE_ORDER[@]}"; do
        printf "    %-22s %s\n" "$name" "${SUITE_DESC[$name]}"
    done
    echo ""
    echo "  Available strategies (--strat):"
    echo ""
    for name in "${STRAT_ORDER[@]}"; do
        printf "    %-16s %s\n" "$name" "${STRAT_DESC[$name]}"
    done
    echo ""
    exit 0
fi

STRAT_DOC_COUNT=0
STRAT_JOB_ARGS=()

case "$STRAT" in
    default)
        STRAT_JOB_ARGS=(--job short)
        ;;
    fast)
        STRAT_DOC_COUNT=500
        STRAT_JOB_ARGS=(--job dry)
        ;;
    quick-compare)
        STRAT_DOC_COUNT=1000
        STRAT_JOB_ARGS=(--job short)
        ;;
    intense)
        STRAT_DOC_COUNT=10000
        STRAT_JOB_ARGS=(--job default)
        ;;
    stress)
        STRAT_DOC_COUNT=50000
        STRAT_JOB_ARGS=(--job default)
        ;;
    exhaustive)
        STRAT_DOC_COUNT=100000
        STRAT_JOB_ARGS=(--job default)
        ;;
esac

if $CONTROLLED; then
    CORPUS_ONLY=true
    if [[ "$DOC_COUNT" -le 0 && "$STRAT_DOC_COUNT" -le 0 ]]; then
        STRAT_DOC_COUNT=1000
    fi
    if [[ ${#STRAT_JOB_ARGS[@]} -eq 0 ]] && ! has_bdn_option --job -j; then
        STRAT_JOB_ARGS=(--job short)
    fi
fi

EFFECTIVE_DOC_COUNT=0
if [[ "$DOC_COUNT" -gt 0 ]]; then
    EFFECTIVE_DOC_COUNT="$DOC_COUNT"
elif [[ "$STRAT_DOC_COUNT" -gt 0 ]]; then
    EFFECTIVE_DOC_COUNT="$STRAT_DOC_COUNT"
fi

if [[ ! -f "$PROJECT_PATH" ]]; then
    echo "Error: benchmark project not found at: $PROJECT_PATH" >&2
    exit 1
fi

if $PREPARE_DATA; then
    DATA_DIR="$REPO_ROOT/bench/data"
    GUTENBERG_DIR="$DATA_DIR/gutenberg-ebooks"
    NEWS_DIR="$DATA_DIR/20newsgroups"
    REUTERS_DIR="$DATA_DIR/reuters21578"

    GUTENBERG_COUNT=0
    if [[ -d "$GUTENBERG_DIR" ]]; then
        GUTENBERG_COUNT=$(find "$GUTENBERG_DIR" -maxdepth 1 -name "*.txt" | wc -l)
    fi

    if [[ "$GUTENBERG_COUNT" -lt "$BOOK_COUNT" ]]; then
        echo "Preparing Gutenberg data (book-count=$BOOK_COUNT)..."
        bash "$SCRIPT_DIR/download-gutenberg.sh" --book-count "$BOOK_COUNT"
    else
        echo "Gutenberg data present ($GUTENBERG_COUNT books), skipping download."
    fi

    NEWS_COUNT=0
    if [[ -d "$NEWS_DIR" ]]; then
        NEWS_COUNT=$(find "$NEWS_DIR" -type f | wc -l)
    fi

    REUTERS_COUNT=0
    if [[ -d "$REUTERS_DIR" ]]; then
        REUTERS_COUNT=$(find "$REUTERS_DIR" -maxdepth 1 -type f -name "*.sgm" | wc -l)
    fi

    if [[ "$NEWS_COUNT" -eq 0 || "$REUTERS_COUNT" -eq 0 ]]; then
        echo "Preparing news data..."
        bash "$SCRIPT_DIR/download-news.sh"
    else
        echo "News data present ($NEWS_COUNT posts, $REUTERS_COUNT Reuters files), skipping download."
    fi

    echo ""
fi

RUN_ARGS=(--suite "$SUITE")
if $CORPUS_ONLY; then
    RUN_ARGS+=(--corpus-only)
fi

if [[ "$EFFECTIVE_DOC_COUNT" -gt 0 ]]; then
    RUN_ARGS+=(--doccount "$EFFECTIVE_DOC_COUNT")
    export BENCH_DOC_COUNT="$EFFECTIVE_DOC_COUNT"
fi

if [[ -n "$SOURCE_COMMIT" ]]; then
    export BENCH_SOURCE_COMMIT="$SOURCE_COMMIT"
fi
if [[ -n "$SOURCE_REF" ]]; then
    export BENCH_SOURCE_REF="$SOURCE_REF"
fi
if [[ -n "$SOURCE_MANIFEST" ]]; then
    export BENCH_SOURCE_MANIFEST="$(cd "$(dirname "$SOURCE_MANIFEST")" && pwd)/$(basename "$SOURCE_MANIFEST")"
fi

ALL_EXTRA_ARGS=()
if ! has_bdn_option --job -j; then
    ALL_EXTRA_ARGS+=("${STRAT_JOB_ARGS[@]}")
fi
if [[ ${#EXTRA_ARGS[@]} -gt 0 ]]; then
    ALL_EXTRA_ARGS+=("${EXTRA_ARGS[@]}")
fi

echo "Suite:    $SUITE"
echo "Strat:    $STRAT"
echo "Framework: $TARGET_FRAMEWORK"
if $CONTROLLED; then
    echo "Mode:     controlled"
fi
if $CORPUS_ONLY; then
    echo "CorpusOnly: enabled"
fi
if [[ "$EFFECTIVE_DOC_COUNT" -gt 0 ]]; then
    echo "Docs:     $EFFECTIVE_DOC_COUNT"
fi
if [[ ${#ALL_EXTRA_ARGS[@]} -gt 0 ]]; then
    echo "Extra:    ${ALL_EXTRA_ARGS[*]}"
fi

if $DRY; then
    echo ""
    cmd_display="dotnet run -c Release --framework $TARGET_FRAMEWORK --project \"$PROJECT_PATH\" -- ${RUN_ARGS[*]}"
    if [[ ${#ALL_EXTRA_ARGS[@]} -gt 0 ]]; then
        cmd_display+=" ${ALL_EXTRA_ARGS[*]}"
    fi
    echo "Dry run - command that would execute:"
    echo "  $cmd_display"
    if [[ "$EFFECTIVE_DOC_COUNT" -gt 0 ]]; then
        echo "  env: BENCH_DOC_COUNT=$EFFECTIVE_DOC_COUNT"
    fi
    if [[ -n "${BENCH_SOURCE_COMMIT:-}" ]]; then
        echo "  env: BENCH_SOURCE_COMMIT=$BENCH_SOURCE_COMMIT"
    fi
    if [[ -n "${BENCH_SOURCE_REF:-}" ]]; then
        echo "  env: BENCH_SOURCE_REF=$BENCH_SOURCE_REF"
    fi
    if [[ -n "${BENCH_SOURCE_MANIFEST:-}" ]]; then
        echo "  env: BENCH_SOURCE_MANIFEST=$BENCH_SOURCE_MANIFEST"
    fi
    echo ""
    exit 0
fi

if $GCDUMP; then
    RUN_ARGS+=(--gcdump)
    if ! command -v dotnet-gcdump &>/dev/null; then
        echo "Installing dotnet-gcdump global tool..."
        dotnet tool install -g dotnet-gcdump
    fi
    echo "GcDump:   enabled"
fi

echo ""
dotnet run -c Release --framework "$TARGET_FRAMEWORK" --project "$PROJECT_PATH" -- "${RUN_ARGS[@]}" "${ALL_EXTRA_ARGS[@]}"
