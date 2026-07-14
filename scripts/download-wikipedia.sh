#!/usr/bin/env bash
# Downloads Wikipedia article introductions for benchmark testing.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

LANGUAGE="en"
OUTPUT_DIR=""
ARTICLE_COUNT=5000

print_help() {
    cat <<'EOF'
Usage:
  ./scripts/download-wikipedia.sh [options]

Downloads Wikipedia article introductions for benchmark testing. Uses the
MediaWiki API to fetch random-article extracts, writing each article to its
own plain-text .txt file in bench/data/wikipedia/{language}/. Files are named
with a random UUID (e.g. 7a3b2c1d-...txt).

Specify a BCP 47 language code (e.g. en, fr, de, zh, ja) to target that
language edition of Wikipedia. All language editions use the same API.

API rate: ~50 articles per request, 500ms between requests.

Licence: CC BY-SA 4.0 (https://creativecommons.org/licenses/by-sa/4.0/)

Options:
  --language LANG    BCP 47 language code (default: en)
  --output-dir DIR   Override output directory (default: bench/data/wikipedia/{lang})
  --article-count N  Total articles to download (default: 5000)
  --help             Show this help and exit

Examples:
  ./scripts/download-wikipedia.sh
  ./scripts/download-wikipedia.sh --language fr --article-count 2000
  ./scripts/download-wikipedia.sh --language zh --article-count 10000
EOF
}

require_command() {
    local command_name="$1"
    local reason="$2"
    if ! command -v "$command_name" >/dev/null 2>&1; then
        echo "Error: $command_name is required. $reason" >&2
        exit 1
    fi
}

# Generate a random UUID for filenames (no external deps required).
generate_uuid() {
    if [[ -f /proc/sys/kernel/random/uuid ]]; then
        cat /proc/sys/kernel/random/uuid
    elif command -v uuidgen >/dev/null 2>&1; then
        uuidgen
    else
        # Fallback: 16 random bytes from /dev/urandom, formatted as hex UUID
        local bytes
        bytes=$(od -A n -t x1 -N 16 /dev/urandom 2>/dev/null | tr -d ' \n')
        if [[ -n "$bytes" ]]; then
            echo "${bytes:0:8}-${bytes:8:4}-${bytes:12:4}-${bytes:16:4}-${bytes:20:12}"
        else
            # Last resort (shouldn't happen on real systems)
            printf '%04x%04x-%04x-%04x-%04x-%04x%04x%04x\n' \
                $RANDOM $RANDOM $RANDOM $RANDOM $RANDOM $RANDOM $RANDOM $RANDOM
        fi
    fi
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --language)
            LANGUAGE="$2"
            shift 2
            ;;
        --output-dir)
            OUTPUT_DIR="$2"
            shift 2
            ;;
        --article-count)
            ARTICLE_COUNT="$2"
            shift 2
            ;;
        --help)
            print_help
            exit 0
            ;;
        *)
            echo "Unknown option: $1" >&2
            print_help
            exit 1
            ;;
    esac
done

if ! [[ "$ARTICLE_COUNT" =~ ^[0-9]+$ ]] || (( ARTICLE_COUNT < 1 )); then
    echo "Error: --article-count must be a positive integer." >&2
    exit 1
fi

if [[ -z "$OUTPUT_DIR" ]]; then
    OUTPUT_DIR="$REPO_ROOT/bench/data/wikipedia/$LANGUAGE"
fi

mkdir -p "$OUTPUT_DIR"

require_command curl "Install curl to download benchmark data."
require_command jq   "Install jq (json parser) to process API responses."

API_BASE="https://${LANGUAGE}.wikipedia.org/w/api.php"
BATCH_SIZE=50
USER_AGENT="BenchmarkDataBot/1.0 ($LANGUAGE benchmark testing; non-commercial)"

total_fetched=0

echo "Downloading $ARTICLE_COUNT Wikipedia ($LANGUAGE) article introductions..."
echo "API:    $API_BASE"
echo "Output: $OUTPUT_DIR"
echo ""

# Rate-limit backoff: start at 3s, double on errors, cap at 60s
backoff=3
max_backoff=60

while (( total_fetched < ARTICLE_COUNT )); do
    limit=$(( BATCH_SIZE < (ARTICLE_COUNT - total_fetched) ? BATCH_SIZE : (ARTICLE_COUNT - total_fetched) ))

    url="${API_BASE}?action=query&generator=random&grnnamespace=0&grnlimit=${limit}"\
"&prop=extracts&exintro=1&explaintext=1&exsectionformat=plain"\
"&format=json&formatversion=2"

    response=$(curl -sS -H "User-Agent: $USER_AGENT" "$url") || {
        echo "  Network error at $total_fetched / $ARTICLE_COUNT — backing off ${backoff}s" >&2
        sleep "$backoff"
        backoff=$(( backoff * 2 < max_backoff ? backoff * 2 : max_backoff ))
        continue
    }

    # Validate response is JSON before passing to jq
    if ! echo "$response" | jq empty 2>/dev/null; then
        # Check if it's a rate-limit page
        if echo "$response" | grep -qi "too many requests\|rate limit"; then
            echo "  Rate limited at $total_fetched / $ARTICLE_COUNT — backing off ${backoff}s" >&2
        else
            echo "  Non-JSON response at $total_fetched / $ARTICLE_COUNT — backing off ${backoff}s" >&2
            echo "  Response: ${response:0:200}" >&2
        fi
        sleep "$backoff"
        backoff=$(( backoff * 2 < max_backoff ? backoff * 2 : max_backoff ))
        continue
    fi

    # Emit each valid page as a null-delimited record: title \0 extract \0
    # Null bytes cannot appear in Wikipedia article text, unlike newlines in extracts.
    # Use process substitution (not $()) so null bytes survive into the read loop.
    saw_any=false
    while IFS= read -r -d '' title && IFS= read -r -d '' extract; do
        # Write each article to its own UUID-named file: title on line 1, extract on line 2
        printf '%s\n%s\n' "$title" "$extract" > "$OUTPUT_DIR/$(generate_uuid).txt"
        total_fetched=$(( total_fetched + 1 ))
        saw_any=true
    done < <(echo "$response" | jq -j '
        .query.pages[] |
        select((.extract // "") | length > 50) |
        .title, "\u0000", .extract, "\u0000"
    ' 2>/dev/null || true)

    if [[ "$saw_any" != true ]]; then
        echo "  Empty batch (stubs/disambig) at $total_fetched / $ARTICLE_COUNT — backing off ${backoff}s" >&2
        sleep "$backoff"
        backoff=$(( backoff * 2 < max_backoff ? backoff * 2 : max_backoff ))
        continue
    fi

    # Success: reset backoff and pause before next request
    backoff=3
    echo "  $total_fetched / $ARTICLE_COUNT articles..."
    sleep "$backoff"
done

echo ""
echo "Complete: $total_fetched articles."
echo "Data in: $OUTPUT_DIR"
