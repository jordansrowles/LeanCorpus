---
title: Benchmarks - Deletion (commit)
---

# Deletion (commit)

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                   | DocumentCount | Mean     | Error     | StdDev   | Ratio | RatioSD | Gen0      | Allocated | Alloc Ratio |
|------------------------- |-------------- |---------:|----------:|---------:|------:|--------:|----------:|----------:|------------:|
| LeanLucene_CommitDeletes | 100000        | 282.0 ms | 319.59 ms | 17.52 ms |  1.00 |    0.00 |         - |   9.42 MB |        1.00 |
| LuceneNet_CommitDeletes  | 100000        | 180.0 ms |  41.67 ms |  2.28 ms |  0.64 |    0.03 | 4000.0000 |  19.25 MB |        2.04 |

