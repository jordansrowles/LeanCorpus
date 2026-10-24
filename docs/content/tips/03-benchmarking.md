# Benchmarking

LeanCorpus uses BenchmarkDotNet through the repository `devops` entry point. On Linux and macOS, run `./devops`; on Windows, use `./devops.exe`. Use a focused suite for development and retain a controlled, representative run for a performance claim.

## List suites and strategies

```bash
./devops benchmark --list
```

Run a quick query suite:

```bash
./devops benchmark --suite query --strat fast
```

Preview the resolved command without running it:

```bash
./devops benchmark --suite query --strat quick-compare --dry
```

Pass BenchmarkDotNet arguments after `--`:

```bash
./devops benchmark --suite query --strat quick-compare -- --filter "*LeanCorpus*"
```

## Strategies

| Strategy | Corpus and job | Use |
|---|---|---|
| `fast` | 500 documents, dry job | Minimal smoke check |
| `quick-compare` | 1,000 documents, short job | Development comparison |
| `default` | 20,000 documents, short job | Normal development baseline |
| `intense` | 10,000 documents, default job | Longer statistical run |
| `stress` | 50,000 documents, default job | Larger workload |
| `exhaustive` | 100,000 documents, default job | Official reference workload |

The names are presets, not a ranking of statistical quality. `intense` has fewer documents than `default` but a longer BenchmarkDotNet job.

`--controlled` selects a deterministic local diagnostic preset. It cannot make two different machines equivalent.

## Benchmark data

Ordinary benchmark runs use deterministic synthetic DataForge records. They do not download or discover a local corpus. Select a Gutenberg suite explicitly when measuring real ebook text. `--prepare-data` downloads the requested books before the run:

```bash
./devops benchmark --suite gutenberg-index --prepare-data --book-count 200
./devops benchmark --suite gutenberg-search --prepare-data --book-count 200
./devops benchmark --suite text --prepare-data --book-count 200 -- --filter "*GutenbergAnalysisBenchmarks*"
```

The report records generated profile identity, version, seed, record count, parameters and content SHA-256. Explicit Gutenberg runs record the external data source. Compare the complete identity and workload parameters before comparing timings.

Rowles.Text benchmark artefacts also contain canonical DataForge identity sidecars, including a separate `rowles-text-multilingual` identity for each language.

### Frozen Wikipedia reference

Wikipedia v1 is a separate release and deep-investigation dataset. Download its pinned source once, build the immutable reference, then inspect or verify it without network access:

```powershell
./devops dataforge reference download
./devops dataforge reference build
./devops dataforge reference inspect
./devops dataforge reference verify
```

Benchmark it explicitly. The runner verifies the reference before starting and fails if the selected directory is missing or invalid:

```bash
./devops benchmark --suite query --dataset wikipedia --strat fast
./devops benchmark --suite index --dataset wikipedia --reference-path ./artifacts/dataforge/reference/leancorpus-wikipedia-en-v1 --doc-count 1000 --strat fast
```

Reference mode accepts `index`, `query`, `boolean`, `phrase`, `prefix`, `fuzzy`, `wildcard`, `regexp`, `mlt`, `highlighter`, `combined`, `terminset`, `parallel`, `similarity`, and `async-index`. The `merge` suite runs its plain-text method only; `flush` runs its text-only method only. Suites that need generated structured fields or vectors are unavailable. The frozen reference contains 20,000 records, so a larger `-DocCount` or strategy fails instead of switching datasets.

The imported benchmark identity contains `DatasetId=leancorpus-wikipedia-en`, version 1, the reference's full record count and content SHA-256, with no seed. It has its own data fingerprint and result series. Normal synthetic benchmarks remain the default, and benchmark execution never downloads or queries live Wikipedia.

Use `--corpus-only` to run LeanCorpus cases without Lucene.NET comparisons.

## Output

Normal runs are written beneath `artifacts/benchmark/runs/<run-id>/`, with one
provenance envelope containing Core, Rowles.Text and compression results.

| Artefact | Contents |
|---|---|
| `run-report.json` | Root orchestration status for the requested benchmark projects |
| `core/report.json` | Core suites, statistics, allocation, GC, workload, and provenance |
| Suite directories | BenchmarkDotNet Markdown, CSV, and JSON |
| Machine `index.json` | Run inventory for generated documentation |

Treat the raw BenchmarkDotNet artefacts, `run-report.json`, and `core/report.json` as the evidence. Generated site pages are a presentation layer.

## Interpret a result

A Passed run status records successful execution and complete runner output. It does not qualify a comparison. The managed report records source, host and dataset identity separately; missing or mismatched identity, a dirty source tree, and Core report schema v4 leave the result unqualified. Schema v5 preserves each BenchmarkDotNet result row with its job, runtime, parameters, toolchain and terminal-statistics state. August 2026 reports and schema-v4 output remain historical even if copied into a newer run inventory. Generated benchmark pages label retained historical and unqualified output and never treat it as a 4.0 comparison.

Check these before calling a difference a regression:

- Inspect qualification state, reason codes, comparability and ratio eligibility in run-report.json. Execution success alone is not comparison evidence.
- Verify the source commit and clean state, host identity, and dataset content fingerprints between the managed run and each Core child report.
- Check that the Core report schema is supported. Schema-v5 is required for current comparisons; schema-v4 and August 2026 output are historical only.
- same benchmark method and parameters;
- same documents or bytes processed per operation;
- same dataset identity, content SHA-256 and document count;
- same indexed features, segment count, merge state, and directory;
- same runtime and architecture;
- no critical BenchmarkDotNet warnings;
- enough separation from normal run-to-run noise.

`Allocated` is managed bytes allocated per operation. It is not resident memory, memory-mapped working set, total process memory, or index size.

Process-priority warnings and CPU-frequency behaviour are separate. Record both when they affect reproducibility.

## Add or change a suite

Contributor workflow is covered in [Benchmarking changes](../contributors/benchmarking.md). In brief:

1. add the case to the nearest suite with genuinely shared setup;
2. keep corpus preparation outside the measured method;
3. consume results so work cannot be removed;
4. add correctness coverage for the path;
5. register a suite alias in `scripts/devops/config/benchmark-suites.psd1` only when it needs a `devops` entry;
6. run `fast`, then a comparison strategy appropriate to the claim.

## What constitutes a regression

There is no universal percentage. Establish repeated baseline variance for the benchmark and host, then use a threshold larger than that noise. Small nanosecond changes in a microbenchmark can be irrelevant to an end-to-end query, while a small throughput loss in a saturated indexing service may be material.

Report latency distribution, throughput, allocation, index size, recall, or correctness according to the feature. Avoid reducing every performance change to one mean value.

See the [performance overview](../performance.md) and the published [benchmark reports](../benchmarks/overview.md).
