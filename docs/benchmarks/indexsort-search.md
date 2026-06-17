---
title: Benchmarks - Index-sort (search)
---

# Index-sort (search)

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                   | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|----------------------------------------- |-------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| LeanCorpus_SortedSearch_EarlyTermination | 100000        | 260.4 μs | 1.69 μs | 1.58 μs |  1.00 | 28.8086 | 1.9531 |  117.9 KB |        1.00 |
| LeanCorpus_SortedSearch_PostSort         | 100000        | 257.4 μs | 1.73 μs | 1.62 μs |  0.99 | 28.8086 | 1.9531 |  117.9 KB |        1.00 |

