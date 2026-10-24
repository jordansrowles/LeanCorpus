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

## Short sustained workload checks

The managed benchmark command can run a bounded correctness smoke for `ingest-throughput`, `search-readonly` or `ingest-search-refresh`. Use `--dry` to inspect the selected engines and resource limits without building or running the worker:

```bash
./devops benchmark workload ingest-search-refresh --engine both --doc-count 128 \
  --search-operations 128 --concurrency 2 --queue-capacity 16 \
  --refresh-every 16 --duration-seconds 10 --memory-mib 512 --dry
```

Remove `--dry` to execute the internal worker. The run stores versioned input and terminal JSON, child logs, source and host identity, input/output hashes, logical IDs, operation samples and counters under `artifacts/benchmark/runs/`. Two-engine runs record whether completed logical work matched. Every sustained-worker result remains unqualified and cannot supply a performance ratio; controlled, repeated host qualification is a separate step.

## BenchmarkDotNet shape

Keep global setup outside the measured method. Consume returned values so work cannot be removed. Parameterise meaningful dimensions, but avoid a Cartesian product that makes the suite impractical.

Use a dry run while developing:

```powershell
./devops benchmark --suite <suite> --strat fast
```

Run the repository's normal strategy for evidence intended to support a performance claim.

## Select bounded semantic cases

`--list` includes the configured profiles, case families, engines and workloads. When a profile, case family, engine or workload is selected, the managed planner intersects it with the suite, area, group and any BenchmarkDotNet method filter, then fails when no manifest case remains. The budget counts the selected BenchmarkDotNet parameter combinations, defaults to 16 cases and accepts a `--max-cases` budget up to 64.

```powershell
./devops benchmark --suite all --profile leancorpus-search-v1 `
  --case collapse-complete-leancorpus,collapse-complete-lucenenet `
  --engine leancorpus,lucenenet --workload collapse-top-score-count-v1 `
  --area Search --group collapse-top-score-25-20k --max-cases 2 --dry
```

The semantic manifest is an extension to the existing suite catalogue. Existing suite IDs and aliases still work, and unselected BenchmarkDotNet arguments remain pass-through. A typed case selection converts a matching method filter into exact class/method invocations, applies the registered parameter values in each benchmark host, and shows the parameter map in `--dry` output. A requested `--doccount` must be among the selected family's registered values; otherwise planning fails before a benchmark runs. Core selections include explicit registered cases when needed.

## Manual GitHub Actions runs

Open the **Benchmark** workflow (`.github/workflows/benchmark.yml`) in GitHub Actions
and select **Run workflow**. Choose the framework and hosted runner OS. The
workflow validates the managed case catalogue, prints four fixed two-case
plans, builds the Core benchmark project, runs one fast smoke case, then runs
the collapse, facet and MoreLikeThis shards sequentially with the
`quick-compare` budget. **plan_only** stops after selection validation and
planning. Exhaustive and stress strategies are not offered by this workflow.

Each shard has its own run report, case counts and raw BenchmarkDotNet output.
The collector writes `collected-run-reports.json` and a
`qualification-summary.json`; the summary withholds comparative values unless
the comparator qualifies every selected pair. These bounded CI runs do not
replace controlled-machine performance qualification.

## Correctness before speed

The `compression` suite uses the first 128, 4,096 or 65,536 UTF-8 bytes from the
DataForge `rowles-text-multilingual` v1 English corpus (seed 42, 1,024 records).
Setup verifies an exact encode/decode round trip for the selected codec before
timing one codec call. `OriginalBytes`, `CompressedBytes` and `CompressionRatio`
accompany throughput; the ratio is input bytes divided by encoded bytes.
`correctness/compression-<policy>-<bytes>.json` retains those counts, payload
and decoded hashes, selection rule and DataForge identity within the child run.
These directional codec cases do not qualify a competitive timing comparison.


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
