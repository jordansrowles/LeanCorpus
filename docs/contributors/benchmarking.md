# Benchmarking changes

LeanCorpus benchmarks answer two different questions: whether a focused operation became faster, and whether an end-to-end workload improved without changing its work. Keep those questions separate.

## Find the existing suite

List benchmark suites through the repository entry point:

```powershell
./devops benchmark -List
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
./devops benchmark -Suite <suite> -Strat fast
```

Run the repository's normal strategy for evidence intended to support a performance claim.

## Correctness before speed

Add or identify a focused correctness test for the path before optimising it. Search optimisation requires matching document IDs, ordering, total hits, and scores where the public contract promises score equivalence.

For approximate vector search, report recall as well as latency. For cache work, include cold and warm cases. For indexing, include index size and merge or commit effects when they change.

## Interpret results

BenchmarkDotNet warnings are evidence, not decoration. Report denied process priority, unstable clocks, multimodal distributions, outliers, and short runs. Cross-machine or cross-runtime comparisons are directional unless the environment and workload are controlled.

A regression threshold should be larger than the observed run-to-run noise. Prefer repeated baseline and candidate measurements, and retain the raw artefacts used to reach a conclusion.

## Documentation output

Generated benchmark reports are not hand-edited. Update benchmark source or report-generation inputs, then use the repository tooling to regenerate the site artefacts when required.

The user-facing [benchmarking guide](../tips/03-benchmarking.md) covers running and reading the published suites.
