---
title: Benchmarks - numeric-aggregator
---

# numeric-aggregator

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c4ff69e` &nbsp;&middot;&nbsp; 6 August 2026 20:14 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method    | SpanLength | Mean        | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|---------- |----------- |------------:|---------:|---------:|------:|----------:|------------:|
| **Scalar**    | **64**         |    **73.66 ns** | **0.027 ns** | **0.021 ns** |  **1.00** |         **-** |          **NA** |
| Vector256 | 64         |    74.96 ns | 0.804 ns | 0.752 ns |  1.02 |         - |          NA |
|           |            |             |          |          |       |           |             |
| **Scalar**    | **256**        |   **323.21 ns** | **0.645 ns** | **0.572 ns** |  **1.00** |         **-** |          **NA** |
| Vector256 | 256        |   319.11 ns | 0.723 ns | 0.677 ns |  0.99 |         - |          NA |
|           |            |             |          |          |       |           |             |
| **Scalar**    | **1024**       | **1,220.52 ns** | **0.766 ns** | **0.640 ns** |  **1.00** |         **-** |          **NA** |
| Vector256 | 1024       | 1,213.38 ns | 1.221 ns | 1.082 ns |  0.99 |         - |          NA |
|           |            |             |          |          |       |           |             |
| **Scalar**    | **4096**       | **4,774.03 ns** | **1.870 ns** | **1.561 ns** |  **1.00** |         **-** |          **NA** |
| Vector256 | 4096       | 4,765.63 ns | 2.256 ns | 1.762 ns |  1.00 |         - |          NA |



