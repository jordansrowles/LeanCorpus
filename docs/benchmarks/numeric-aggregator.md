---
title: Benchmarks - numeric-aggregator
---

# numeric-aggregator

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c1bfdfd` &nbsp;&middot;&nbsp; 16 June 2026 17:50 UTC &nbsp;&middot;&nbsp; 20,000 docs

| Method    | Job        | IterationCount | LaunchCount | WarmupCount | SpanLength | Mean        | Error      | StdDev    | Ratio | Allocated | Alloc Ratio |
|---------- |----------- |--------------- |------------ |------------ |----------- |------------:|-----------:|----------:|------:|----------:|------------:|
| **Scalar**    | **DefaultJob** | **Default**        | **Default**     | **Default**     | **64**         |    **74.73 ns** |   **0.360 ns** |  **0.336 ns** |  **1.00** |         **-** |          **NA** |
| Vector256 | DefaultJob | Default        | Default     | Default     | 64         |    75.38 ns |   0.383 ns |  0.358 ns |  1.01 |         - |          NA |
|           |            |                |             |             |            |             |            |           |       |           |             |
| Scalar    | ShortRun   | 3              | 1           | 3           | 64         |    74.55 ns |   6.919 ns |  0.379 ns |  1.00 |         - |          NA |
| Vector256 | ShortRun   | 3              | 1           | 3           | 64         |    75.15 ns |   4.348 ns |  0.238 ns |  1.01 |         - |          NA |
|           |            |                |             |             |            |             |            |           |       |           |             |
| **Scalar**    | **DefaultJob** | **Default**        | **Default**     | **Default**     | **256**        |   **326.25 ns** |   **1.677 ns** |  **1.569 ns** |  **1.00** |         **-** |          **NA** |
| Vector256 | DefaultJob | Default        | Default     | Default     | 256        |   322.87 ns |   2.081 ns |  1.946 ns |  0.99 |         - |          NA |
|           |            |                |             |             |            |             |            |           |       |           |             |
| Scalar    | ShortRun   | 3              | 1           | 3           | 256        |   326.97 ns |  10.792 ns |  0.592 ns |  1.00 |         - |          NA |
| Vector256 | ShortRun   | 3              | 1           | 3           | 256        |   322.63 ns |  40.267 ns |  2.207 ns |  0.99 |         - |          NA |
|           |            |                |             |             |            |             |            |           |       |           |             |
| **Scalar**    | **DefaultJob** | **Default**        | **Default**     | **Default**     | **1024**       | **1,235.87 ns** |   **5.650 ns** |  **5.009 ns** |  **1.00** |         **-** |          **NA** |
| Vector256 | DefaultJob | Default        | Default     | Default     | 1024       | 1,229.53 ns |   6.610 ns |  6.183 ns |  0.99 |         - |          NA |
|           |            |                |             |             |            |             |            |           |       |           |             |
| Scalar    | ShortRun   | 3              | 1           | 3           | 1024       | 1,241.69 ns | 232.600 ns | 12.750 ns |  1.00 |         - |          NA |
| Vector256 | ShortRun   | 3              | 1           | 3           | 1024       | 1,236.82 ns |  66.792 ns |  3.661 ns |  1.00 |         - |          NA |
|           |            |                |             |             |            |             |            |           |       |           |             |
| **Scalar**    | **DefaultJob** | **Default**        | **Default**     | **Default**     | **4096**       | **4,851.74 ns** |  **28.346 ns** | **26.515 ns** |  **1.00** |         **-** |          **NA** |
| Vector256 | DefaultJob | Default        | Default     | Default     | 4096       | 4,840.83 ns |  32.534 ns | 30.433 ns |  1.00 |         - |          NA |
|           |            |                |             |             |            |             |            |           |       |           |             |
| Scalar    | ShortRun   | 3              | 1           | 3           | 4096       | 4,870.46 ns | 533.918 ns | 29.266 ns |  1.00 |         - |          NA |
| Vector256 | ShortRun   | 3              | 1           | 3           | 4096       | 4,840.80 ns | 256.907 ns | 14.082 ns |  0.99 |         - |          NA |

