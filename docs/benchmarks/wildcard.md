---
title: Benchmarks - Wildcard queries
---

# Wildcard queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                   | WildcardPattern | DocumentCount | Mean       | Error    | StdDev   | Median     | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------- |---------------- |-------------- |-----------:|---------:|---------:|-----------:|------:|--------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_WildcardQuery** | **gov***            | **100000**        |   **145.5 μs** |  **2.96 μs** |  **8.72 μs** |   **143.5 μs** |  **1.00** |    **0.00** |  **3.9063** |      **-** |  **16.53 KB** |        **1.00** |
| LuceneNet_WildcardQuery  | gov*            | 100000        |   286.0 μs |  2.57 μs |  2.41 μs |   286.3 μs |  1.97 |    0.11 | 28.8086 | 0.4883 |  118.7 KB |        7.18 |
|                          |                 |               |            |          |          |            |       |         |         |        |           |             |
| **LeanCorpus_WildcardQuery** | **m*rket**          | **100000**        | **1,326.3 μs** | **26.28 μs** | **71.95 μs** | **1,318.0 μs** |  **1.00** |    **0.00** |  **1.9531** |      **-** |   **9.41 KB** |        **1.00** |
| LuceneNet_WildcardQuery  | m*rket          | 100000        | 1,430.5 μs | 11.06 μs | 10.34 μs | 1,429.2 μs |  1.08 |    0.06 | 95.7031 | 3.9063 | 394.17 KB |       41.87 |
|                          |                 |               |            |          |          |            |       |         |         |        |           |             |
| **LeanCorpus_WildcardQuery** | **pre*dent**        | **100000**        |   **164.0 μs** |  **3.28 μs** |  **8.40 μs** |   **160.5 μs** |  **1.00** |    **0.00** |  **2.4414** |      **-** |   **9.92 KB** |        **1.00** |
| LuceneNet_WildcardQuery  | pre*dent        | 100000        |   464.5 μs |  3.89 μs |  3.45 μs |   464.6 μs |  2.84 |    0.14 | 89.8438 | 0.9766 | 369.13 KB |       37.22 |

