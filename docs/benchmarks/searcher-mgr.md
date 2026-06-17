---
title: Benchmarks - Searcher manager
---

# Searcher manager

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                   | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0    | Allocated | Alloc Ratio |
|----------------------------------------- |-------------- |---------:|--------:|--------:|------:|--------:|----------:|------------:|
| LeanCorpus_SearcherManager_AcquireSearch | 100000        | 119.6 μs | 0.62 μs | 0.58 μs |  1.00 |  0.1221 |     811 B |        1.00 |
| LeanCorpus_SearcherManager_AcquireLease  | 100000        | 119.6 μs | 0.52 μs | 0.49 μs |  1.00 |  0.1221 |     875 B |        1.08 |
| LuceneNet_SearcherManager_AcquireSearch  | 100000        | 138.3 μs | 1.19 μs | 1.12 μs |  1.16 | 14.4043 |   60896 B |       75.09 |

