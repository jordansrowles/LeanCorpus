---
title: Benchmarks - Analysis parity
---

# Analysis parity

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------------- |----------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| LeanCorpus_Whitespace | 30.126 μs | 0.1222 μs | 0.1143 μs |  1.00 |    0.00 |      - |         - |          NA |
| LuceneNet_Whitespace  | 75.226 μs | 0.4574 μs | 0.4055 μs |  2.50 |    0.02 | 0.7324 |    3200 B |          NA |
| LeanCorpus_Keyword    |  3.289 μs | 0.0142 μs | 0.0132 μs |  0.11 |    0.00 |      - |         - |          NA |
| LuceneNet_Keyword     | 12.599 μs | 0.0609 μs | 0.0569 μs |  0.42 |    0.00 | 0.7629 |    3200 B |          NA |
| LeanCorpus_Simple     | 41.528 μs | 0.2311 μs | 0.2162 μs |  1.38 |    0.01 |      - |         - |          NA |
| LuceneNet_Simple      | 84.407 μs | 0.4502 μs | 0.3760 μs |  2.80 |    0.02 | 0.7324 |    3200 B |          NA |

