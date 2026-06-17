#!/usr/bin/env bash
#
# generate-benchmark-docs.sh
# Generates DocFX benchmark pages from BDN output files — one page per suite.
#
# For each machine directory under bench/, scans all completed runs and keeps the
# newest run per suite.  Writes one markdown page per suite into docs/benchmarks/
# and generates a toc.yml listing all suites.
#
# Run this before docfx build; docs.sh calls it automatically.
#
# Usage:
#   ./scripts/generate-benchmark-docs.sh [--bench-dir <path>] [--output-dir <path>]

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

BENCH_DIR="${REPO_ROOT}/bench"
OUTPUT_DIR="${REPO_ROOT}/docs/benchmarks"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --bench-dir)   BENCH_DIR="$2"; shift 2 ;;
        --output-dir)  OUTPUT_DIR="$2"; shift 2 ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
done

BENCH_DIR="$(cd "$BENCH_DIR" 2>/dev/null && pwd || echo "$BENCH_DIR")"
OUTPUT_DIR="$(cd "$OUTPUT_DIR" 2>/dev/null && pwd || echo "$OUTPUT_DIR")"

# ── Suite display names ───────────────────────────────────────────────────────

declare -A SUITE_NAMES=(
    ["aggregation"]="Aggregation"
    ["analysis"]="Analysis"
    ["analysis-filters"]="Analysis filters"
    ["analysis-filters-v2"]="Analysis filters v2"
    ["analysis-parity"]="Analysis parity"
    ["async-index"]="Async index"
    ["blockjoin"]="Block-Join"
    ["blockjoin-index"]="Block-Join (index)"
    ["blockjoin-search"]="Block-Join (search)"
    ["boolean"]="Boolean queries"
    ["collapse-facet"]="Collapse and facet"
    ["combined"]="Combined queries"
    ["compound-index"]="Compound file (index)"
    ["compound-search"]="Compound file (search)"
    ["deletion"]="Deletion"
    ["deletion-commit"]="Deletion (commit)"
    ["deletion-queue"]="Deletion (queue)"
    ["dismax"]="Disjunction max"
    ["function-score"]="Function score"
    ["fuzzy"]="Fuzzy queries"
    ["geo"]="Geo queries"
    ["gutenberg-analysis"]="Gutenberg analysis"
    ["gutenberg-index"]="Gutenberg index"
    ["gutenberg-search"]="Gutenberg search"
    ["highlighter"]="Highlighter"
    ["hunspell"]="Hunspell"
    ["index"]="Indexing"
    ["indexsort-index"]="Index-sort (index)"
    ["indexsort-search"]="Index-sort (search)"
    ["kstemmer"]="KStemmer"
    ["lightenglish"]="Light English stemmer"
    ["mlt"]="More like this"
    ["multiphrase"]="Multi-phrase"
    ["ngram"]="N-gram"
    ["parallel"]="Parallel indexing"
    ["pattern-tokeniser"]="Pattern tokeniser"
    ["phrase"]="Phrase queries"
    ["prefix"]="Prefix queries"
    ["query"]="Term queries"
    ["query-cache"]="Query cache"
    ["range"]="Range queries"
    ["regexp"]="Regexp queries"
    ["schemajson"]="Schema and JSON"
    ["searcher-mgr"]="Searcher manager"
    ["similarity"]="Similarity"
    ["span"]="Span queries"
    ["stemmer"]="Stemmer"
    ["suggester"]="Suggester"
    ["synonym"]="Synonym"
    ["terminset"]="Term in set"
    ["tv-highlighter"]="Term-vector highlighter"
    ["vq"]="Vector queries"
    ["wildcard"]="Wildcard queries"
)

# ── Helpers ───────────────────────────────────────────────────────────────────

# Extracts just the GFM table rows from a BDN markdown file, skipping the
# environment code block that BDN prepends.
get_table_content() {
    local path="$1"
    if [[ ! -f "$path" ]]; then
        return 1
    fi
    # Find the first line starting with '|' and print from there to EOF
    awk '/^\|/{found=1} found{print}' "$path"
}

# ── Collect runs ──────────────────────────────────────────────────────────────

if [[ ! -d "$BENCH_DIR" ]]; then
    echo "Bench directory not found: $BENCH_DIR" >&2
    exit 1
fi

# Find machine directories (skip 'data')
machines=()
for d in "$BENCH_DIR"/*/; do
    name="$(basename "$d")"
    if [[ "$name" != "data" ]]; then
        machines+=("$name")
    fi
done

if [[ ${#machines[@]} -eq 0 ]]; then
    echo "No machine directories found in $BENCH_DIR" >&2
    exit 0
fi

# Temporary file to store newest-per-suite data
TMP_MAP="$(mktemp)"
trap 'rm -f "$TMP_MAP"' EXIT

echo "Scanning runs..." >&2

for machine in "${machines[@]}"; do
    echo "  $machine" >&2
    machine_dir="$BENCH_DIR/$machine"

    # Walk date dirs in reverse (newest first)
    for date_dir in $(ls -1d "$machine_dir"/*/ 2>/dev/null | sort -r); do
        for time_dir in $(ls -1d "$date_dir"*/ 2>/dev/null | sort -r); do
            report_json="$time_dir/report.json"
            if [[ ! -f "$report_json" ]]; then
                continue
            fi

            # Check the report has benchmarks
            total=$(jq -r '.totalBenchmarkCount // 0' "$report_json" 2>/dev/null || echo "0")
            if [[ "$total" -le 0 ]]; then
                continue
            fi

            # Read suite names from this report; only record suites we haven't seen yet
            jq -r '.suites[].suiteName' "$report_json" 2>/dev/null | while IFS= read -r suite; do
                # Only record if not already in the map
                if ! grep -q "^${suite}|" "$TMP_MAP" 2>/dev/null; then
                    generated_at=$(jq -r '.generatedAtUtc' "$report_json")
                    echo "${suite}|${time_dir}|${generated_at}|${machine}" >> "$TMP_MAP"
                fi
            done
        done
    done
done

suite_count=$(wc -l < "$TMP_MAP" | tr -d ' ')
if [[ "$suite_count" -eq 0 ]]; then
    echo "No suites found in any run." >&2
    exit 0
fi

echo "Found $suite_count suites across all runs." >&2

# ── Generate pages ────────────────────────────────────────────────────────────

mkdir -p "$OUTPUT_DIR"

# Remove old generated markdown files
rm -f "$OUTPUT_DIR"/*.md

toc_entries=()
page_count=0

# Sort by display name
sort -t'|' -k1,1 "$TMP_MAP" | while IFS='|' read -r suite_name run_dir generated_at machine; do
    report_json="$run_dir/report.json"

    # Read metadata from report.json
    dotnet_ver=$(jq -r '.dotnetVersion // "?"' "$report_json")
    commit_hash=$(jq -r '.commitHash // "?"' "$report_json")
    commit_short="${commit_hash:0:7}"
    doc_count=$(jq -r '.provenance.effectiveDocCount // 0' "$report_json")

    # Format date
    if command -v python3 &>/dev/null; then
        run_date=$(python3 -c "
import sys, datetime
dt = datetime.datetime.fromisoformat('${generated_at}'.replace('Z','+00:00'))
print(dt.strftime('%d %B %Y %H:%M UTC'))
" 2>/dev/null || echo "$generated_at")
    else
        run_date="$generated_at"
    fi

    # Display name
    display_name="${SUITE_NAMES[$suite_name]:-$suite_name}"

    # File name
    if [[ ${#machines[@]} -gt 1 ]]; then
        file_name="${machine}-${suite_name}.md"
    else
        file_name="${suite_name}.md"
    fi

    # Find the BDN markdown file
    suite_results_dir="$run_dir/$suite_name"
    md_file=$(find "$suite_results_dir" -name '*-report-github.md' -print -quit 2>/dev/null || true)

    if [[ -z "$md_file" || ! -f "$md_file" ]]; then
        echo "  WARNING: no markdown for '$suite_name', skipping." >&2
        continue
    fi

    table_content=$(get_table_content "$md_file")

    if [[ -z "$table_content" ]]; then
        echo "  WARNING: no table in $(basename "$md_file"), skipping." >&2
        continue
    fi

    # Format doc count with commas
    doc_fmt=$(printf "%'d" "$doc_count" 2>/dev/null || echo "$doc_count")

    out_path="$OUTPUT_DIR/$file_name"

    {
        echo "---"
        echo "title: Benchmarks - $display_name"
        echo "---"
        echo
        echo "# $display_name"
        echo
        echo "**.NET** $dotnet_ver &nbsp;&middot;&nbsp; **Commit** \`$commit_short\` &nbsp;&middot;&nbsp; $run_date &nbsp;&middot;&nbsp; $doc_fmt docs"
        echo
        echo "$table_content"
        echo
    } > "$out_path"

    echo "  $file_name" >&2
    page_count=$((page_count + 1))

    toc_entries+=("- name: $display_name")
    toc_entries+=("  href: $file_name")
done

# ── toc.yml ───────────────────────────────────────────────────────────────────

# Re-count pages from files actually written
page_count=$(find "$OUTPUT_DIR" -maxdepth 1 -name '*.md' | wc -l | tr -d ' ')

# Generate toc.yml from the files on disk (more reliable than the pipe variable)
{
    for f in $(ls -1 "$OUTPUT_DIR"/*.md 2>/dev/null | sort); do
        base="$(basename "$f")"
        # Read title from front matter
        title=$(sed -n '/^---$/,/^---$/ { /^title:/ { s/^title: *//p; q } }' "$f" | sed 's/^Benchmarks - //')
        if [[ -z "$title" ]]; then
            title="$base"
        fi
        echo "- name: $title"
        echo "  href: $base"
    done
} > "$OUTPUT_DIR/toc.yml"

echo "Written: toc.yml ($page_count suites)" >&2
echo "Done. $page_count benchmark pages written." >&2
