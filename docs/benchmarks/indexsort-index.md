---
title: Benchmarks - Index-sort (index)
---

# Index-sort (index)

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                    | DocumentCount | Mean    | Error   | StdDev  | Ratio | Gen0        | Gen1       | Gen2       | Allocated | Alloc Ratio |
|-------------------------- |-------------- |--------:|--------:|--------:|------:|------------:|-----------:|-----------:|----------:|------------:|
| LeanCorpus_Index_Unsorted | 100000        | 11.44 s | 0.169 s | 0.026 s |  1.00 | 169000.0000 | 70000.0000 |  7000.0000 |   1.33 GB |        1.00 |
| LeanCorpus_Index_Sorted   | 100000        | 12.25 s | 0.409 s | 0.106 s |  1.07 | 174000.0000 | 75000.0000 | 10000.0000 |   1.35 GB |        1.01 |

