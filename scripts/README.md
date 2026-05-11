# Scripts

| Script | Purpose |
|---|---|
| `aot-smoke.ps1` | Publishes and runs the Native AOT example for a selected runtime identifier. |
| `benchmark.ps1` | Runs BenchmarkDotNet suites from PowerShell, with suite, strategy, document count, data preparation, and diagnostic options. |
| `benchmark.sh` | Runs the same benchmark suites from bash on Linux or macOS hosts. |
| `coverage.ps1` | Runs the unit and integration test projects with XPlat Code Coverage collection. |
| `docs.ps1` | Builds the DocFX documentation site, with options for serving locally and skipping generated benchmark or coverage content. |
| `download-gutenberg.ps1` | Downloads Project Gutenberg plain-text ebooks into benchmark data storage. |
| `download-gutenberg.sh` | Bash equivalent for downloading Project Gutenberg benchmark data. |
| `download-news.ps1` | Downloads and extracts the 20 Newsgroups and Reuters-21578 benchmark datasets. |
| `download-news.sh` | Bash equivalent for downloading and extracting the news benchmark datasets. |
| `download-wikipedia.ps1` | Downloads English Wikipedia article introductions for benchmark indexing and analysis data. |
| `generate-benchmark-docs.ps1` | Converts the latest BenchmarkDotNet output under `bench/` into DocFX benchmark pages. |
| `send-for-bench.ps1` | Connects to the Debian benchmark host, updates `/home/jordan/code/leancorpus` from `origin/main`, and starts benchmarks in tmux. |
