---
title: Benchmarks - More like this
---

# More like this

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                      | DocumentCount | Mean      | Error     | StdDev    | Median    | Ratio | RatioSD | Gen0     | Gen1    | Allocated | Alloc Ratio |
|-------------------------------------------- |-------------- |----------:|----------:|----------:|----------:|------:|--------:|---------:|--------:|----------:|------------:|
| LeanCorpus_MoreLikeThisQuery_DefaultParams  | 100000        | 699.37 μs | 13.967 μs | 21.330 μs | 688.06 μs |  1.00 |    0.00 |  22.4609 |  0.9766 |     88 KB |        1.00 |
| LeanCorpus_MoreLikeThisQuery_HighMinDocFreq | 100000        |  48.96 μs |  0.555 μs |  0.464 μs |  49.06 μs |  0.07 |    0.00 |   5.4321 |       - |  21.75 KB |        0.25 |
| LeanCorpus_MoreLikeThisQuery_NoBoost        | 100000        | 693.57 μs | 13.652 μs | 18.225 μs | 690.42 μs |  0.99 |    0.04 |  23.4375 |  0.9766 |  88.15 KB |        1.00 |
| LuceneNet_MoreLikeThis_DefaultParams        | 100000        | 152.69 μs |  1.342 μs |  1.255 μs | 152.35 μs |  0.22 |    0.01 | 140.6250 | 31.2500 | 574.67 KB |        6.53 |
| LuceneNet_MoreLikeThis_HighMinDocFreq       | 100000        | 151.97 μs |  1.501 μs |  1.404 μs | 151.59 μs |  0.22 |    0.01 | 140.6250 | 31.2500 | 574.67 KB |        6.53 |
| LuceneNet_MoreLikeThis_NoBoost              | 100000        | 152.91 μs |  1.319 μs |  1.169 μs | 152.79 μs |  0.22 |    0.01 | 140.6250 | 31.2500 | 574.67 KB |        6.53 |

