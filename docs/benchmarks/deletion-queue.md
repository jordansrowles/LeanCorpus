---
title: Benchmarks - Deletion (queue)
---

# Deletion (queue)

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                  | DocumentCount | Mean       | Error      | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------ |-------------- |-----------:|-----------:|---------:|------:|--------:|----------:|------------:|
| LeanLucene_QueueDeletes | 100000        |   652.7 μs |   596.2 μs | 32.68 μs |  1.00 |    0.00 |    1.5 MB |        1.00 |
| LuceneNet_QueueDeletes  | 100000        | 3,054.1 μs | 1,514.5 μs | 83.02 μs |  4.69 |    0.24 |    2.8 MB |        1.86 |

