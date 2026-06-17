---
title: Benchmarks - Multi-phrase
---

# Multi-phrase

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                      | DocumentCount | Mean       | Error   | StdDev  | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|---------------------------- |-------------- |-----------:|--------:|--------:|------:|--------:|--------:|-------:|----------:|------------:|
| LeanCorpus_MultiPhraseQuery | 100000        |   584.9 μs | 5.86 μs | 5.20 μs |  1.00 |    0.00 | 20.5078 |      - |  83.98 KB |        1.00 |
| LuceneNet_MultiPhraseQuery  | 100000        | 1,147.1 μs | 7.79 μs | 6.91 μs |  1.96 |    0.02 | 87.8906 | 3.9063 | 371.52 KB |        4.42 |

