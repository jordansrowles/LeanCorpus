#!/usr/bin/env bash
#
# docs.sh
# Sets up and builds the LeanCorpus documentation site using DocFX.
#
# Ensures the docfx global tool is installed, then generates API metadata
# and builds the static site into ./docs/site.
#
# Usage:
#   ./scripts/docs.sh                  Build the site
#   ./scripts/docs.sh --serve          Build and serve on http://localhost:8080
#   ./scripts/docs.sh --metadata-only  Only generate API metadata, skip site build
#   ./scripts/docs.sh --skip-benchmarks  Skip benchmark page generation
#   ./scripts/docs.sh --skip-coverage    Skip coverage report generation

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DOCS_DIR="$REPO_ROOT/docs"
DOCFX_JSON="$DOCS_DIR/docfx.json"

SERVE=false
METADATA_ONLY=false
SKIP_BENCHMARKS=false
SKIP_COVERAGE=false

while [[ $# -gt 0 ]]; do
    case "$1" in
        --serve)           SERVE=true; shift ;;
        --metadata-only)   METADATA_ONLY=true; shift ;;
        --skip-benchmarks) SKIP_BENCHMARKS=true; shift ;;
        --skip-coverage)   SKIP_COVERAGE=true; shift ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
done

# ── Ensure docfx is available ─────────────────────────────────────────────────

if ! command -v docfx &>/dev/null; then
    echo "Installing docfx global tool..."
    dotnet tool install -g docfx
    if [[ $? -ne 0 ]]; then
        echo "ERROR: Failed to install docfx. Ensure the .NET SDK is on PATH." >&2
        exit 1
    fi
    echo "docfx installed."
else
    version=$(docfx --version 2>&1 | head -1 || true)
    echo "docfx found: $version"
fi

# ── Generate benchmark docs ───────────────────────────────────────────────────

if [[ "$SKIP_BENCHMARKS" != "true" ]]; then
    echo "Generating benchmark pages..."
    "$SCRIPT_DIR/generate-benchmark-docs.sh" || {
        echo "WARNING: Benchmark doc generation failed. Continuing without benchmark pages." >&2
    }
fi

# ── Generate coverage report ──────────────────────────────────────────────────

if [[ "$SKIP_COVERAGE" != "true" ]]; then
    COVERAGE_RESULTS_DIR="$REPO_ROOT/coverage-results"
    COVERAGE_OUT_DIR="$DOCS_DIR/coverage"

    # Find coverage XML files
    mapfile -t xml_files < <(find "$COVERAGE_RESULTS_DIR" -name 'coverage.cobertura.xml' -type f 2>/dev/null || true)

    if [[ ${#xml_files[@]} -eq 0 ]]; then
        echo "WARNING: No coverage XML files found in $COVERAGE_RESULTS_DIR. Skipping coverage report. Run scripts/coverage.ps1 first." >&2
    else
        if ! command -v reportgenerator &>/dev/null; then
            echo "Installing reportgenerator global tool..."
            dotnet tool install -g dotnet-reportgenerator-globaltool
            if [[ $? -ne 0 ]]; then
                echo "ERROR: Failed to install reportgenerator. Ensure the .NET SDK is on PATH." >&2
                exit 1
            fi
            echo "reportgenerator installed."
        else
            rg_version=$(reportgenerator --version 2>&1 | head -1 || true)
            echo "reportgenerator found: $rg_version"
        fi

        # Build semicolon-separated report paths
        report_paths=""
        for f in "${xml_files[@]}"; do
            if [[ -z "$report_paths" ]]; then
                report_paths="$f"
            else
                report_paths="$report_paths;$f"
            fi
        done

        echo "Generating coverage report..."
        reportgenerator \
            "-reports:$report_paths" \
            "-targetdir:$COVERAGE_OUT_DIR" \
            "-reporttypes:Html" \
            "-title:LeanCorpus Coverage" || {
            echo "WARNING: Coverage report generation failed. Continuing without coverage report." >&2
        }
        echo "Coverage report written to: $COVERAGE_OUT_DIR"
    fi
fi

# ── Clear and generate API metadata ───────────────────────────────────────────

API_DIR="$DOCS_DIR/api"
rm -f "$API_DIR"/*.yml "$API_DIR"/toc.yml 2>/dev/null || true
mkdir -p "$API_DIR"

echo "Generating API metadata..."
docfx metadata "$DOCFX_JSON"
if [[ $? -ne 0 ]]; then exit $?; fi

# ── YAML post-processing ──────────────────────────────────────────────────────

echo "Post-processing API YAML..."

python3 - "$API_DIR" << 'PYEOF'
import sys, os, re

api_dir = sys.argv[1]

def read_yml(path):
    if not os.path.exists(path):
        return []
    with open(path, 'r', encoding='utf-8') as f:
        return f.read().splitlines()

def write_yml(path, lines):
    with open(path, 'w', encoding='utf-8') as f:
        f.write('\n'.join(lines) + '\n')

def find_uid_blocks(lines):
    prefix = []
    blocks = []
    refs = []
    current = None
    current_uid = None
    in_refs = False
    for line in lines:
        if line == 'references:':
            if current is not None:
                blocks.append((current_uid, current))
                current = None
                current_uid = None
            in_refs = True
            refs.append(line)
            continue
        if in_refs:
            refs.append(line)
            continue
        m = re.match(r'^- uid: (.+)$', line)
        if m:
            if current is not None:
                blocks.append((current_uid, current))
            current_uid = m.group(1)
            current = [line]
            continue
        if current is not None:
            current.append(line)
        else:
            prefix.append(line)
    if current is not None:
        blocks.append((current_uid, current))
    return prefix, blocks, refs

def filter_inherited_members(block_lines, keep_prefix='Rowles.LeanCorpus.'):
    out = []
    i = 0
    while i < len(block_lines):
        line = block_lines[i]
        if line.strip() == 'inheritedMembers:':
            kept = []
            i += 1
            while i < len(block_lines) and re.match(r'^  - ', block_lines[i]):
                if block_lines[i].strip().lstrip('- ').startswith(keep_prefix):
                    kept.append(block_lines[i])
                i += 1
            if kept:
                out.append('  inheritedMembers:')
                out.extend(kept)
        else:
            out.append(line)
            i += 1
    return out

def filter_list_entries(block_lines, list_names, keep_prefix='Rowles.LeanCorpus.'):
    out = []
    i = 0
    while i < len(block_lines):
        line = block_lines[i]
        m = re.match(r'^  ([A-Za-z]+):$', line)
        if m and m.group(1) in list_names:
            list_name = m.group(1)
            kept = []
            i += 1
            while i < len(block_lines) and re.match(r'^  - ', block_lines[i]):
                if block_lines[i].strip().lstrip('- ').startswith(keep_prefix):
                    kept.append(block_lines[i])
                i += 1
            if kept:
                out.append(f'  {list_name}:')
                out.extend(kept)
        else:
            out.append(line)
            i += 1
    return out

# ── Process YAML files ────────────────────────────────────────────────────────

list_names = {'inheritance', 'implements', 'derivedClasses'}
count = 0
for fname in os.listdir(api_dir):
    if not fname.endswith('.yml') or fname == 'toc.yml':
        continue
    path = os.path.join(api_dir, fname)
    lines = read_yml(path)
    prefix, blocks, refs = find_uid_blocks(lines)
    out = list(prefix)
    for uid, block in blocks:
        block = filter_inherited_members(block)
        block = filter_list_entries(block, list_names)
        out.extend(block)
    out.extend(refs)
    write_yml(path, out)
    count += 1
print(f"  Processed {count} YAML files")

# ── Fix TOC namespaces ────────────────────────────────────────────────────────

toc_path = os.path.join(api_dir, 'toc.yml')
if os.path.exists(toc_path):
    toc_lines = read_yml(toc_path)

    # Extract SourceGen block from nested position
    source_gen_block = []
    i = 0
    while i < len(toc_lines):
        line = toc_lines[i]
        m = re.match(r'^(\s*)- uid: Rowles\.LeanCorpus\.SourceGen$', line)
        if m:
            indent = len(m.group(1))
            source_gen_block.append(line)
            i += 1
            while i < len(toc_lines):
                m2 = re.match(r'^(\s*)- uid: ', toc_lines[i])
                if m2 and len(m2.group(1)) <= indent:
                    i -= 1
                    break
                source_gen_block.append(toc_lines[i])
                i += 1
        i += 1

    # Extract Compression block
    compression_block = []
    i = 0
    while i < len(toc_lines):
        line = toc_lines[i]
        m = re.match(r'^(\s*)- uid: Rowles\.LeanCorpus\.Compression$', line)
        if m:
            indent = len(m.group(1))
            compression_block.append(line)
            i += 1
            while i < len(toc_lines):
                m2 = re.match(r'^(\s*)- uid: ', toc_lines[i])
                if m2 and len(m2.group(1)) <= indent:
                    i -= 1
                    break
                compression_block.append(toc_lines[i])
                i += 1
        i += 1

    # Remove extracted blocks from original position
    cleaned_toc = []
    skip_until_indent = -1
    for line in toc_lines:
        m = re.match(r'^(\s*)- uid: Rowles\.LeanCorpus\.(SourceGen|Compression)$', line)
        if m:
            skip_until_indent = len(m.group(1))
            continue
        if skip_until_indent >= 0:
            m2 = re.match(r'^(\s*)- uid: ', line)
            if m2 and len(m2.group(1)) <= skip_until_indent:
                skip_until_indent = -1
                cleaned_toc.append(line)
            continue
        cleaned_toc.append(line)

    # Build compression namespace entries from child YAML files
    def get_yml_type(yml_path):
        if not os.path.exists(yml_path):
            return 'Class'
        for line in read_yml(yml_path):
            m = re.match(r'^  type: (.+)$', line)
            if m:
                return m.group(1)
        return 'Class'

    def get_namespace_children(ns_yml_path):
        children = []
        if not os.path.exists(ns_yml_path):
            return children
        in_children = False
        for line in read_yml(ns_yml_path):
            if line == '  children:':
                in_children = True
                continue
            if in_children:
                m = re.match(r'^  - (.+)$', line)
                if m:
                    children.append(m.group(1))
                else:
                    break
        return children

    compression_ns_list = [
        'Rowles.LeanCorpus.Compression.LZ4',
        'Rowles.LeanCorpus.Compression.Snappy',
        'Rowles.LeanCorpus.Compression.Zstandard',
    ]

    compression_entries = []
    for ns in compression_ns_list:
        ns_yml = os.path.join(api_dir, f'{ns}.yml')
        children = get_namespace_children(ns_yml)
        if not children:
            continue
        compression_entries.append(f'- uid: {ns}')
        compression_entries.append(f'  name: {ns}')
        compression_entries.append('  type: Namespace')
        compression_entries.append('  items:')
        for child_uid in children:
            child_yml = os.path.join(api_dir, f'{child_uid}.yml')
            child_type = get_yml_type(child_yml)
            child_name = child_uid.split('.')[-1]
            compression_entries.append(f'  - uid: {child_uid}')
            compression_entries.append(f'    name: {child_name}')
            compression_entries.append(f'    type: {child_type}')

    # De-indent SourceGen block to top level
    source_gen_top = []
    for line in source_gen_block:
        if line.startswith('  '):
            source_gen_top.append(line[2:])
        else:
            source_gen_top.append(line)

    # Insert before memberLayout
    final_toc = []
    for line in cleaned_toc:
        if line == 'memberLayout: SeparatePages':
            if compression_entries:
                final_toc.append('')
                final_toc.extend(compression_entries)
                final_toc.append('')
            if source_gen_top:
                final_toc.extend(source_gen_top)
        final_toc.append(line)

    write_yml(toc_path, final_toc)
    print("  Added compression and SourceGen namespaces to API TOC")

print("API YAML post-processing complete.")
PYEOF

# ── Build ─────────────────────────────────────────────────────────────────────

if [[ "$METADATA_ONLY" == "true" ]]; then
    echo "Metadata written to: $API_DIR"
    exit 0
fi

if [[ "$SERVE" == "true" ]]; then
    echo "Building and serving docs on http://0.0.0.0:8080..."
    docfx build "$DOCFX_JSON"
    if [[ $? -ne 0 ]]; then exit $?; fi
    docfx serve "$DOCS_DIR/site" --hostname 0.0.0.0 -p 8080
else
    echo "Building documentation site..."
    docfx build "$DOCFX_JSON"
    if [[ $? -ne 0 ]]; then exit $?; fi
    echo "Site written to: $DOCS_DIR/site"
fi
