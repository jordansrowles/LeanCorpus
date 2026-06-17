---
title: Benchmarks - Suggester
---

# Suggester

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                 | DocumentCount | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0      | Gen1     | Allocated  | Alloc Ratio |
|----------------------- |-------------- |----------:|----------:|----------:|------:|--------:|----------:|---------:|-----------:|------------:|
| LeanCorpus_DidYouMean  | 100000        |  5.821 ms | 0.0621 ms | 0.0581 ms |  1.00 |    0.00 |         - |        - |   24.91 KB |        1.00 |
| LeanCorpus_SpellIndex  | 100000        |  5.427 ms | 0.1020 ms | 0.0954 ms |  0.93 |    0.02 |         - |        - |    23.2 KB |        0.93 |
| LuceneNet_SpellChecker | 100000        | 10.573 ms | 0.1512 ms | 0.1341 ms |  1.82 |    0.03 | 1296.8750 | 203.1250 | 5351.24 KB |      214.79 |

