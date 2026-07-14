# Scripts

## `devops` — unified entry point

`./scripts/devops test|benchmark|docs [...]` covers all build, test, benchmark, and documentation tasks. Run it without arguments for help.

### Test

```
./scripts/devops test [-Suite <name>] [-Framework <tfm>] [-Filter <expr>] [-List]
```

Suites: `unit`, `integration`, `chaos`, `sourcegen`, `compressionparity` (default: `all`).

### Benchmark

```
./scripts/devops benchmark [-Suite <name>] [-Strat <name>] [-DocCount <n>] [-Framework <tfm>] [-PrepareData] [-CorpusOnly] [-Dry] [-List]
```

Strategies: `default` (20K, --job short), `fast` (500, --job dry), `quick-compare` (1K, short), `intense` (10K), `stress` (50K), `exhaustive` (100K). Run `-List` for available suites.

### Docs

```
./scripts/devops docs [-SkipBenchmarks] [-SkipCoverage] [-Serve]
./scripts/devops docs -Coverage [-Clean] [-IncludePerformance] [-GenerateReport] [-Framework <tfm>]
```

Coverage mode runs all test projects with `XPlat Code Coverage` and optionally generates an HTML report via `reportgenerator`.

## Supporting scripts

| Script | Purpose |
|---|---|
| `download-gutenberg.ps1` | Downloads Project Gutenberg plain-text ebooks into benchmark data storage. |
| `download-gutenberg.sh` | Bash equivalent for downloading Project Gutenberg benchmark data. |
| `download-news.ps1` | Downloads and extracts the 20 Newsgroups and Reuters-21578 benchmark datasets. |
| `download-news.sh` | Bash equivalent for downloading and extracting the news benchmark datasets. |
| `download-wikipedia.ps1` | Downloads Wikipedia article introductions (one file per article) for benchmark indexing and analysis data. |
| `download-wikipedia.sh` | Bash equivalent for downloading Wikipedia article introductions. |
| `generate-benchmark-docs.ps1` | Converts the latest BenchmarkDotNet output under `bench/` into DocFX benchmark pages. Called automatically by `devops docs`. |
| `generate-benchmark-docs.sh` | Bash wrapper for `generate-benchmark-docs.ps1`. |
| `nightly-benchmark.sh` | Cron-driven nightly benchmark runner for the Debian benchmark host. |
| `send-for-bench.ps1` | Connects to the Debian benchmark host, updates the repo from `origin/main`, and starts benchmarks in tmux. |
