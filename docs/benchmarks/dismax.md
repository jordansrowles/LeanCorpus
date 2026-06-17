---
title: Benchmarks - Disjunction max
---

# Disjunction max

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                         | TieBreakerMultiplier | DocumentCount | Mean       | Error   | StdDev  | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------------- |--------------------- |-------------- |-----------:|--------:|--------:|------:|--------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_DisjunctionMaxQuery** | **0**                    | **100000**        |   **372.0 μs** | **7.28 μs** | **7.79 μs** |  **1.00** |    **0.00** |  **1.9531** |      **-** |   **9.46 KB** |        **1.00** |
| LuceneNet_DisjunctionMaxQuery  | 0                    | 100000        |   973.1 μs | 4.31 μs | 3.82 μs |  2.62 |    0.05 | 39.0625 | 0.9766 | 162.51 KB |       17.17 |
|                                |                      |               |            |         |         |       |         |         |        |           |             |
| **LeanCorpus_DisjunctionMaxQuery** | **0.1**                  | **100000**        |   **370.4 μs** | **7.16 μs** | **7.35 μs** |  **1.00** |    **0.00** |  **1.9531** |      **-** |   **9.46 KB** |        **1.00** |
| LuceneNet_DisjunctionMaxQuery  | 0.1                  | 100000        |   983.5 μs | 6.19 μs | 5.79 μs |  2.66 |    0.05 | 39.0625 | 0.9766 | 162.51 KB |       17.18 |
|                                |                      |               |            |         |         |       |         |         |        |           |             |
| **LeanCorpus_DisjunctionMaxQuery** | **0.5**                  | **100000**        |   **370.2 μs** | **7.32 μs** | **9.52 μs** |  **1.00** |    **0.00** |  **1.9531** |      **-** |   **9.46 KB** |        **1.00** |
| LuceneNet_DisjunctionMaxQuery  | 0.5                  | 100000        | 1,101.4 μs | 8.08 μs | 7.56 μs |  2.98 |    0.08 | 39.0625 | 1.9531 | 162.51 KB |       17.18 |

