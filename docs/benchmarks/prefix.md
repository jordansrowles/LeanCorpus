---
title: Benchmarks - Prefix queries
---

# Prefix queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                 | QueryPrefix | DocumentCount | Mean     | Error   | StdDev  | Median   | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|----------------------- |------------ |-------------- |---------:|--------:|--------:|---------:|------:|--------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_PrefixQuery** | **gov**         | **100000**        | **146.1 μs** | **2.92 μs** | **8.05 μs** | **143.2 μs** |  **1.00** |    **0.00** |  **3.9063** |      **-** |  **15.83 KB** |        **1.00** |
| LuceneNet_PrefixQuery  | gov         | 100000        | 266.5 μs | 2.55 μs | 2.26 μs | 266.2 μs |  1.83 |    0.10 | 23.9258 | 0.9766 |  99.62 KB |        6.29 |
|                        |             |               |          |         |         |          |       |         |         |        |           |             |
| **LeanCorpus_PrefixQuery** | **mark**        | **100000**        | **250.9 μs** | **4.92 μs** | **6.90 μs** | **249.4 μs** |  **1.00** |    **0.00** |  **4.8828** |      **-** |  **21.41 KB** |        **1.00** |
| LuceneNet_PrefixQuery  | mark        | 100000        | 395.5 μs | 2.62 μs | 2.33 μs | 396.3 μs |  1.58 |    0.04 | 27.8320 | 0.4883 | 115.72 KB |        5.41 |
|                        |             |               |          |         |         |          |       |         |         |        |           |             |
| **LeanCorpus_PrefixQuery** | **pres**        | **100000**        | **302.8 μs** | **5.99 μs** | **9.32 μs** | **302.6 μs** |  **1.00** |    **0.00** |  **8.3008** |      **-** |  **34.52 KB** |        **1.00** |
| LuceneNet_PrefixQuery  | pres        | 100000        | 544.4 μs | 3.84 μs | 3.60 μs | 544.0 μs |  1.80 |    0.06 | 29.2969 | 0.9766 | 122.74 KB |        3.56 |

