---
title: Benchmarks - Function score
---

# Function score

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                        | Mode     | DocumentCount | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0     | Gen1    | Allocated | Alloc Ratio |
|------------------------------ |--------- |-------------- |---------:|---------:|---------:|------:|--------:|---------:|--------:|----------:|------------:|
| **LeanCorpus_BaseTermQuery**      | **Max**      | **100000**        | **120.1 μs** |  **0.57 μs** |  **0.53 μs** |  **1.00** |    **0.00** |   **0.1221** |       **-** |     **720 B** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | Max      | 100000        | 606.9 μs | 10.81 μs |  9.03 μs |  5.05 |    0.08 | 279.2969 |  1.9531 | 1163592 B |    1,616.10 |
|                               |          |               |          |          |          |       |         |          |         |           |             |
| **LeanCorpus_BaseTermQuery**      | **Multiply** | **100000**        | **119.5 μs** |  **0.51 μs** |  **0.47 μs** |  **1.00** |    **0.00** |   **0.1221** |       **-** |     **720 B** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | Multiply | 100000        | 592.8 μs | 11.67 μs | 16.74 μs |  4.96 |    0.14 | 277.3438 | 19.5313 | 1163605 B |    1,616.12 |
|                               |          |               |          |          |          |       |         |          |         |           |             |
| **LeanCorpus_BaseTermQuery**      | **Replace**  | **100000**        | **119.0 μs** |  **0.51 μs** |  **0.48 μs** |  **1.00** |    **0.00** |   **0.1221** |       **-** |     **720 B** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | Replace  | 100000        | 606.1 μs | 11.82 μs | 19.75 μs |  5.09 |    0.16 | 278.3203 |  9.7656 | 1163592 B |    1,616.10 |
|                               |          |               |          |          |          |       |         |          |         |           |             |
| **LeanCorpus_BaseTermQuery**      | **Sum**      | **100000**        | **119.4 μs** |  **0.58 μs** |  **0.55 μs** |  **1.00** |    **0.00** |   **0.1221** |       **-** |     **720 B** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | Sum      | 100000        | 602.8 μs | 11.72 μs | 14.82 μs |  5.05 |    0.12 | 279.2969 |  3.9063 | 1163585 B |    1,616.09 |

