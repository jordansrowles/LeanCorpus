# Performance

No qualified benchmark results are published for this build. The [Benchmarks page](benchmarks/overview.md) shows the current retained-report state. Do not treat a dry, partial, interrupted or stale run as a release performance result.

## Compare like with like

Before comparing results, match or record:

- the exact source revision, package versions and working-tree state;
- operating system, processor, memory, runtime, SDK, target framework and build configuration;
- dataset identity, content fingerprint, document count and indexed features;
- benchmark method, parameters, query shape, index state and correctness checks;
- warm-up, measured iterations, repetitions and observed run-to-run variation.

Keep microbenchmark latency and allocation separate from end-to-end indexing throughput, service throughput, tail latency, resident memory and index size. A single-operation BenchmarkDotNet mean does not establish service behaviour.

## Reproduce a measurement

Use the repository's [`devops` benchmark command](contributors/devops-entry-point.md#benchmarks) and retain its run manifest, project reports and raw BenchmarkDotNet outputs. Record the workload identity and host conditions alongside any comparison. The [Benchmarking guide](tips/03-benchmarking.md) explains the repository datasets, strategies, output and interpretation limits.

Publish results only when the retained run passed its required correctness and qualification checks. Attribute every result to its source revision, runtime and workload so readers can reproduce the comparison.
