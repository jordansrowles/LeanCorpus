---
title: Benchmarks - Aggregation
---

# Aggregation

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                 | DocumentCount | Mean       | Error    | StdDev   | Ratio | RatioSD | Gen0     | Gen1    | Allocated | Alloc Ratio |
|--------------------------------------- |-------------- |-----------:|---------:|---------:|------:|--------:|---------:|--------:|----------:|------------:|
| LeanCorpus_SearchOnly                  | 100000        |   120.6 μs |  0.60 μs |  0.56 μs |  1.00 |    0.00 |   0.1221 |       - |     720 B |        1.00 |
| LeanCorpus_SearchWithStats             | 100000        | 1,186.5 μs | 22.97 μs | 25.53 μs |  9.84 |    0.21 |  87.8906 | 11.7188 |  476848 B |      662.29 |
| LeanCorpus_SearchWithHistogram         | 100000        | 1,249.2 μs | 24.38 μs | 29.95 μs | 10.36 |    0.25 |  97.6563 | 17.5781 |  517088 B |      718.18 |
| LeanCorpus_SearchWithStatsAndHistogram | 100000        | 1,483.2 μs | 22.09 μs | 19.58 μs | 12.30 |    0.17 | 134.7656 | 17.5781 |  675288 B |      937.90 |

