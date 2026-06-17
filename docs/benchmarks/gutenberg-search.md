---
title: Benchmarks - Gutenberg search
---

# Gutenberg search

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                     | SearchTerm | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------------------- |----------- |---------:|---------:|---------:|------:|--------:|-------:|-------:|----------:|------------:|
| **LeanCorpus_Standard_Search** | **death**      | **13.69 μs** | **0.072 μs** | **0.067 μs** |  **1.00** |    **0.00** | **0.1221** |      **-** |     **520 B** |        **1.00** |
| LeanCorpus_English_Search  | death      | 13.64 μs | 0.083 μs | 0.077 μs |  1.00 |    0.01 | 0.1221 |      - |     520 B |        1.00 |
| LuceneNet_Search           | death      | 22.89 μs | 0.372 μs | 0.348 μs |  1.67 |    0.03 | 2.6550 | 0.0305 |   11231 B |       21.60 |
|                            |            |          |          |          |       |         |        |        |           |             |
| **LeanCorpus_Standard_Search** | **love**       | **17.97 μs** | **0.118 μs** | **0.110 μs** |  **1.00** |    **0.00** | **0.1221** |      **-** |     **512 B** |        **1.00** |
| LeanCorpus_English_Search  | love       | 23.47 μs | 0.125 μs | 0.117 μs |  1.31 |    0.01 | 0.1221 |      - |     512 B |        1.00 |
| LuceneNet_Search           | love       | 30.34 μs | 0.275 μs | 0.230 μs |  1.69 |    0.02 | 2.6245 | 0.0305 |   11175 B |       21.83 |
|                            |            |          |          |          |       |         |        |        |           |             |
| **LeanCorpus_Standard_Search** | **man**        | **48.81 μs** | **0.234 μs** | **0.219 μs** |  **1.00** |    **0.00** | **0.1221** |      **-** |     **512 B** |        **1.00** |
| LeanCorpus_English_Search  | man        | 48.93 μs | 0.234 μs | 0.219 μs |  1.00 |    0.01 | 0.1221 |      - |     512 B |        1.00 |
| LuceneNet_Search           | man        | 50.87 μs | 0.319 μs | 0.298 μs |  1.04 |    0.01 | 2.6245 | 0.0610 |   11038 B |       21.56 |
|                            |            |          |          |          |       |         |        |        |           |             |
| **LeanCorpus_Standard_Search** | **night**      | **30.39 μs** | **0.147 μs** | **0.138 μs** |  **1.00** |    **0.00** | **0.1221** |      **-** |     **520 B** |        **1.00** |
| LeanCorpus_English_Search  | night      | 31.61 μs | 0.216 μs | 0.202 μs |  1.04 |    0.01 | 0.1221 |      - |     520 B |        1.00 |
| LuceneNet_Search           | night      | 37.04 μs | 0.268 μs | 0.251 μs |  1.22 |    0.01 | 2.6245 | 0.0610 |   11223 B |       21.58 |
|                            |            |          |          |          |       |         |        |        |           |             |
| **LeanCorpus_Standard_Search** | **sea**        | **15.35 μs** | **0.084 μs** | **0.075 μs** |  **1.00** |    **0.00** | **0.1221** |      **-** |     **512 B** |        **1.00** |
| LeanCorpus_English_Search  | sea        | 16.82 μs | 0.102 μs | 0.096 μs |  1.10 |    0.01 | 0.1221 |      - |     512 B |        1.00 |
| LuceneNet_Search           | sea        | 27.58 μs | 0.216 μs | 0.202 μs |  1.80 |    0.02 | 2.6550 | 0.0305 |   11271 B |       22.01 |

