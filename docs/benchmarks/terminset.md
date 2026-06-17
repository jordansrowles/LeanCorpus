---
title: Benchmarks - Term in set
---

# Term in set

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                         | SetSize | DocumentCount | Mean        | Error     | StdDev      | Ratio | RatioSD | Gen0     | Allocated | Alloc Ratio |
|------------------------------- |-------- |-------------- |------------:|----------:|------------:|------:|--------:|---------:|----------:|------------:|
| **LeanCorpus_TermInSetQuery**      | **5**       | **100000**        |    **794.0 μs** |  **14.12 μs** |    **13.20 μs** |  **1.00** |    **0.00** |   **1.9531** |    **9.5 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_Should | 5       | 100000        |  1,494.3 μs |  28.92 μs |    29.70 μs |  1.88 |    0.05 |   5.8594 |  23.42 KB |        2.47 |
|                                |         |               |             |           |             |       |         |          |           |             |
| **LeanCorpus_TermInSetQuery**      | **20**      | **100000**        |  **1,467.9 μs** |  **29.05 μs** |    **41.66 μs** |  **1.00** |    **0.00** |   **1.9531** |  **14.96 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_Should | 20      | 100000        | 10,144.0 μs | 200.27 μs |   230.63 μs |  6.92 |    0.24 |  15.6250 |  72.88 KB |        4.87 |
|                                |         |               |             |           |             |       |         |          |           |             |
| **LeanCorpus_TermInSetQuery**      | **100**     | **100000**        |  **2,660.1 μs** |  **53.13 μs** |    **93.06 μs** |  **1.00** |    **0.00** |   **7.8125** |  **44.62 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_Should | 100     | 100000        | 41,344.4 μs | 764.27 μs | 1,358.48 μs | 15.56 |    0.73 | 166.6667 |  974.2 KB |       21.83 |

