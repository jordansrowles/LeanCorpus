---
title: Benchmarks - Schema and JSON
---

# Schema and JSON

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                      | DocumentCount | Mean        | Error     | StdDev    | Ratio | Gen0        | Gen1       | Gen2      | Allocated  | Alloc Ratio |
|---------------------------- |-------------- |------------:|----------:|----------:|------:|------------:|-----------:|----------:|-----------:|------------:|
| LeanCorpus_Index_NoSchema   | 100000        | 10,866.6 ms |  87.54 ms |  77.61 ms |  1.00 | 155000.0000 | 60000.0000 | 3000.0000 | 1255.05 MB |        1.00 |
| LeanCorpus_Index_WithSchema | 100000        | 10,845.5 ms | 132.19 ms | 123.65 ms |  1.00 | 154000.0000 | 59000.0000 | 2000.0000 | 1258.86 MB |        1.00 |
| LeanCorpus_JsonMapping      | 100000        |    422.7 ms |   2.72 ms |   2.54 ms |  0.04 |  52000.0000 |          - |         - |  218.97 MB |        0.17 |

