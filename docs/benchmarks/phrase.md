---
title: Benchmarks - Phrase queries
---

# Phrase queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                 | PhraseType     | DocumentCount | Mean       | Error    | StdDev   | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|----------------------- |--------------- |-------------- |-----------:|---------:|---------:|------:|--------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_PhraseQuery** | **ExactThreeWord** | **100000**        |   **560.2 μs** |  **8.68 μs** |  **8.12 μs** |  **1.00** |    **0.00** | **14.6484** |      **-** |  **60.61 KB** |        **1.00** |
| LuceneNet_PhraseQuery  | ExactThreeWord | 100000        |   488.9 μs |  4.39 μs |  3.89 μs |  0.87 |    0.01 | 76.6602 | 1.4648 | 323.88 KB |        5.34 |
|                        |                |               |            |          |          |       |         |         |        |           |             |
| **LeanCorpus_PhraseQuery** | **ExactTwoWord**   | **100000**        |   **457.2 μs** |  **8.98 μs** | **11.67 μs** |  **1.00** |    **0.00** | **10.7422** |      **-** |  **43.44 KB** |        **1.00** |
| LuceneNet_PhraseQuery  | ExactTwoWord   | 100000        |   511.0 μs |  4.66 μs |  4.36 μs |  1.12 |    0.03 | 64.4531 | 2.9297 | 266.62 KB |        6.14 |
|                        |                |               |            |          |          |       |         |         |        |           |             |
| **LeanCorpus_PhraseQuery** | **SlopTwoWord**    | **100000**        | **1,370.5 μs** | **12.74 μs** | **11.92 μs** |  **1.00** |    **0.00** | **11.7188** |      **-** |  **49.13 KB** |        **1.00** |
| LuceneNet_PhraseQuery  | SlopTwoWord    | 100000        | 1,207.0 μs |  9.65 μs |  9.02 μs |  0.88 |    0.01 | 27.3438 | 1.9531 | 120.91 KB |        2.46 |

