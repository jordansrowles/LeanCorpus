# Performance tuning

Use this page after measuring a representative workload. Do not tune from a microbenchmark alone.

1. Record corpus, hardware, runtime, query mix, concurrency and latency target.
2. Use metrics and benchmark artefacts to identify indexing, storage, query or allocation pressure.
3. Change one setting at a time and compare the same workload.
4. Retain the rollback setting and the measured evidence.

The usual controls are merge policy, indexing buffers, cache limits, parallel search and vector candidate budgets. Each changes a different part of the system and can worsen another workload.

See [Performance](../performance.md), [Benchmarking](03-benchmarking.md) and [Per-query resource controls](../searching/09-resource-controls.md).
