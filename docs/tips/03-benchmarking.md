# Benchmarking

LeanCorpus uses BenchmarkDotNet through the repository `devops` entry point. Use a focused suite for development and retain a controlled, representative run for a performance claim.

## List suites and strategies

```powershell
./devops benchmark -List
```

Run a quick query suite:

```powershell
./devops benchmark -Suite query -Strat fast
```

Preview the resolved command without running it:

```powershell
./devops benchmark -Suite query -Strat quick-compare -Dry
```

Pass BenchmarkDotNet arguments after `--`:

```powershell
./devops benchmark -Suite query -Strat quick-compare -- --filter "*LeanCorpus*"
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

`-Controlled` selects a deterministic local diagnostic preset. It cannot make two different machines equivalent.

## Real data

Prepare the supported corpora:

```powershell
./devops benchmark -PrepareData -BookCount 200
```

The report records data-source names, file and byte counts, document counts, and SHA-256 fingerprints. Compare those fields before comparing timings.

Use `-CorpusOnly` when a suite should use corpus-backed cases without synthetic companions.

## Output

Runs are written beneath `bench/{machine}/...`.

| Artefact | Contents |
|---|---|
| `report.json` | Consolidated suites, statistics, allocation, GC, workload, and provenance |
| Suite directories | BenchmarkDotNet Markdown, CSV, and JSON |
| Machine `index.json` | Run inventory for generated documentation |

Treat the raw BenchmarkDotNet artefacts and `report.json` as the evidence. Generated site pages are a presentation layer.

## Interpret a result

Check these before calling a difference a regression:

- same benchmark method and parameters;
- same documents or bytes processed per operation;
- same corpus fingerprint and document count;
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

See the [performance overview](../performance.md) and the published [benchmark reports](../benchmarks/index.md).
