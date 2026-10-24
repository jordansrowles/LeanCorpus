# Benchmarking changes

LeanCorpus benchmarks answer two different questions: whether a focused operation became faster, and whether an end-to-end workload improved without changing its work. Keep those questions separate.

## Find the existing suite

List benchmark suites through the repository entry point:

```powershell
./devops benchmark --list
```

Add a case to the nearest existing suite when the setup, corpus, and measured operation are genuinely shared. Create a new suite when reusing setup would hide materially different work.

## Define equivalent work

Before comparing results, record:

- corpus and document count;
- indexed fields and options such as positions, DocValues, vectors, and stored fields;
- segment count and merge state;
- commit and durability behaviour;
- directory implementation;
- query, result count, and documents processed per invocation;
- runtime, architecture, and relevant hardware features.

Per-operation allocation is only comparable when each operation performs the same work. Resident memory, memory-mapped pages, managed allocation, and index size are separate measurements.

## BenchmarkDotNet shape

Keep global setup outside the measured method. Consume returned values so work cannot be removed. Parameterise meaningful dimensions, but avoid a Cartesian product that makes the suite impractical.

Use a dry run while developing:

```powershell
./devops benchmark --suite <suite> --strat fast
```

Run the repository's normal strategy for evidence intended to support a performance claim.

## Manual GitHub Actions runs

Open the **Benchmark** workflow (`.github/workflows/benchmark.yml`) in GitHub Actions
and select **Run workflow**. Choose the branch, suite, strategy, framework and
hosted runner OS. Suites accept `all`, `core`, `text`, `compression` or a named
suite from `./devops benchmark --list`, such as `query` or `index`.

The defaults run all three benchmark projects on Ubuntu with .NET 10 and the
`default` strategy. A document count of `0` keeps the strategy's default.
Optional area and group selections require `core` or `text`; a BenchmarkDotNet
filter cannot be combined with either selection. Corpus-only mode skips the
Lucene.NET comparison for Core benchmarks.

Enable **dry_run** to check the resolved commands without executing benchmarks.
Normal runs upload `artifacts/benchmark/runs` as a result artefact, including
available output from failed runs. The workflow is manual-only and uses the SDK
pinned by `global.json` alongside .NET 10. Hosted runner measurements require
the same workload and environment checks as local comparisons.

## Correctness before speed

Add or identify a focused correctness test for the path before optimising it. Search optimisation requires matching document IDs, ordering, total hits, and scores where the public contract promises score equivalence.

The Core `geo` suite compares Geo distance and bounding-box filters against Lucene.NET using the same deterministic synthetic coordinates and stored fixture IDs. It converts LeanCorpus metres to Lucene.NET kilometres with the same 6,371 km Earth radius, sets Lucene.NET's prefix-tree distance error percentage to zero, checks total hits and ordered IDs in global setup, and prepares both query forms before timing. The check warms each index, and BenchmarkDotNet warm-up continues that state, so these results describe warm-query performance only. Distance and bounding-box methods use separate baselines. The implementations use different index algorithms, so treat each operation as a focused comparison rather than a general product ranking. Run it with `./devops benchmark --suite geo --strat fast` for a development smoke; use the default strategy and controlled host for performance evidence.

The Geo comparison covers point-distance and point-bounding-box queries only. Geo nearest benchmarks compare LeanCorpus's exhaustive and best-first execution strategies, while the Geo aggregation and shape metadata benchmarks measure separate LeanCorpus operations. Those workloads do not share a Lucene.NET comparator in this suite: using the point geohash index as a stand-in for shape topology, aggregation results, or spatial metadata would compare different work.

The fuzzy `nohit-edit2` case uses a unique miss term on every measured invocation. LeanCorpus caches empty fuzzy expansions, so reusing one no-hit term would make later calls warm on one side while Lucene.NET continues doing the expansion work. Global setup checks that the scenario returns zero hits in both engines before timing.

For approximate vector search, report recall as well as latency. For cache work, include cold and warm cases. For indexing, include index size and merge or commit effects when they change.

## Interpret results

BenchmarkDotNet warnings are evidence, not decoration. Report denied process priority, unstable clocks, multimodal distributions, outliers, and short runs. Cross-machine or cross-runtime comparisons are directional unless the environment and workload are controlled.

A regression threshold should be larger than the observed run-to-run noise. Prefer repeated baseline and candidate measurements, and retain the raw artefacts used to reach a conclusion.

## Documentation output

Generated benchmark reports are not hand-edited. Update benchmark source or report-generation inputs, then use the repository tooling to regenerate the site artefacts when required.

The user-facing [benchmarking guide](../tips/03-benchmarking.md) covers running and reading the published suites.
