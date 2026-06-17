---
title: Benchmarks - Combined queries
---

# Combined queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                             | MinimumShouldMatch | DocumentCount | Mean     | Error    | StdDev   | Ratio | Gen0     | Gen1   | Allocated | Alloc Ratio |
|----------------------------------- |------------------- |-------------- |---------:|---------:|---------:|------:|---------:|-------:|----------:|------------:|
| **LeanCorpus_CombinedFieldsQuery**     | **1**                  | **100000**        | **901.1 μs** | **10.67 μs** |  **9.46 μs** |  **1.00** | **120.1172** | **1.9531** |  **492.7 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_MultiField | 1                  | 100000        | 398.9 μs |  7.72 μs | 11.79 μs |  0.44 |   4.8828 |      - |  20.45 KB |        0.04 |
|                                    |                    |               |          |          |          |       |          |        |           |             |
| **LeanCorpus_CombinedFieldsQuery**     | **2**                  | **100000**        | **871.3 μs** | **11.52 μs** | **10.77 μs** |  **1.00** | **120.1172** | **1.9531** | **491.91 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_MultiField | 2                  | 100000        | 394.1 μs |  7.63 μs | 11.88 μs |  0.45 |   4.8828 |      - |  20.44 KB |        0.04 |

