---
title: Benchmarks - Parallel indexing
---

# Parallel indexing

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                 | SegmentCount | DocumentCount | Mean     | Error   | StdDev  | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------------------------- |------------- |-------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_SequentialSearch**            | **4**            | **100000**        | **127.4 μs** | **0.81 μs** | **0.76 μs** |  **1.00** |    **0.00** |      **-** |     **600 B** |        **1.00** |
| LeanCorpus_ParallelSearch              | 4            | 100000        | 127.4 μs | 0.81 μs | 0.76 μs |  1.00 |    0.01 |      - |     600 B |        1.00 |
| LeanCorpus_ParallelSearch_BooleanQuery | 4            | 100000        | 285.9 μs | 3.81 μs | 3.18 μs |  2.24 |    0.03 | 2.4414 |   11499 B |       19.16 |
|                                        |              |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_SequentialSearch**            | **8**            | **100000**        | **128.6 μs** | **0.70 μs** | **0.62 μs** |  **1.00** |    **0.00** |      **-** |     **672 B** |        **1.00** |
| LeanCorpus_ParallelSearch              | 8            | 100000        | 129.0 μs | 0.62 μs | 0.51 μs |  1.00 |    0.01 |      - |     672 B |        1.00 |
| LeanCorpus_ParallelSearch_BooleanQuery | 8            | 100000        | 299.7 μs | 5.58 μs | 5.22 μs |  2.33 |    0.04 | 3.4180 |   15663 B |       23.31 |

