---
title: Benchmarks - Block-Join (search)
---

# Block-Join (search)

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                           | BlockCount | Mean     | Error     | StdDev    | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|--------------------------------- |----------- |---------:|----------:|----------:|------:|--------:|--------:|----------:|------------:|
| LeanLucene_BlockJoinQuery        | 100000     | 1.937 ms | 0.0185 ms | 0.0164 ms |  1.00 |    0.00 |       - |   3.16 KB |        1.00 |
| LuceneNet_ToParentBlockJoinQuery | 100000     | 3.120 ms | 0.0249 ms | 0.0233 ms |  1.61 |    0.02 | 11.7188 |  48.48 KB |       15.32 |

