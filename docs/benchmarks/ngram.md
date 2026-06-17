---
title: Benchmarks - N-gram
---

# N-gram

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                        | GramRange | DocumentCount | Mean       | Error   | StdDev  | Median     | Ratio | RatioSD | Gen0        | Allocated   | Alloc Ratio |
|---------------------------------------------- |---------- |-------------- |-----------:|--------:|--------:|-----------:|------:|--------:|------------:|------------:|------------:|
| **LeanCorpus_EdgeNGramTokeniser_SpanSink**        | **2-3**       | **100000**        |   **284.3 ms** | **0.88 ms** | **0.68 ms** |   **284.5 ms** |  **1.00** |    **0.00** |           **-** |           **-** |          **NA** |
| LeanCorpus_NGramTokeniser_SpanSink            | 2-3       | 100000        |   313.0 ms | 6.05 ms | 8.86 ms |   308.3 ms |  1.10 |    0.03 |           - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_SpanSink  | 2-3       | 100000        |   439.5 ms | 1.94 ms | 1.72 ms |   438.7 ms |  1.55 |    0.01 |           - |           - |          NA |
| LeanCorpus_EdgeNGramTokeniser_Streaming       | 2-3       | 100000        |   399.7 ms | 1.89 ms | 1.77 ms |   399.0 ms |  1.41 |    0.01 |           - |           - |          NA |
| LeanCorpus_NGramTokeniser_Streaming           | 2-3       | 100000        | 1,026.0 ms | 3.30 ms | 3.09 ms | 1,026.0 ms |  3.61 |    0.01 |           - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_Streaming | 2-3       | 100000        |   979.5 ms | 4.33 ms | 4.05 ms |   979.6 ms |  3.44 |    0.02 |           - |           - |          NA |
| LuceneNet_EdgeNGramTokenizer                  | 2-3       | 100000        |   947.6 ms | 6.20 ms | 5.80 ms |   947.0 ms |  3.33 |    0.02 | 211000.0000 | 885600000 B |          NA |
| LuceneNet_NGramTokenizer                      | 2-3       | 100000        | 5,984.3 ms | 3.90 ms | 3.46 ms | 5,985.1 ms | 21.05 |    0.05 | 211000.0000 | 885600000 B |          NA |
|                                               |           |               |            |         |         |            |       |         |             |             |             |
| **LeanCorpus_EdgeNGramTokeniser_SpanSink**        | **3-5**       | **100000**        |   **292.0 ms** | **1.45 ms** | **1.29 ms** |   **291.9 ms** |  **1.00** |    **0.00** |           **-** |           **-** |          **NA** |
| LeanCorpus_NGramTokeniser_SpanSink            | 3-5       | 100000        |   474.7 ms | 2.20 ms | 2.06 ms |   474.1 ms |  1.63 |    0.01 |           - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_SpanSink  | 3-5       | 100000        |   457.8 ms | 2.18 ms | 2.04 ms |   456.7 ms |  1.57 |    0.01 |           - |           - |          NA |
| LeanCorpus_EdgeNGramTokeniser_Streaming       | 3-5       | 100000        |   417.3 ms | 2.18 ms | 2.04 ms |   416.1 ms |  1.43 |    0.01 |           - |           - |          NA |
| LeanCorpus_NGramTokeniser_Streaming           | 3-5       | 100000        | 1,449.1 ms | 6.99 ms | 6.20 ms | 1,449.0 ms |  4.96 |    0.03 |           - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_Streaming | 3-5       | 100000        | 1,007.2 ms | 3.78 ms | 3.54 ms | 1,006.6 ms |  3.45 |    0.02 |           - |           - |          NA |
| LuceneNet_EdgeNGramTokenizer                  | 3-5       | 100000        |   955.3 ms | 6.50 ms | 6.08 ms |   952.8 ms |  3.27 |    0.02 | 212000.0000 | 888000000 B |          NA |
| LuceneNet_NGramTokenizer                      | 3-5       | 100000        | 9,491.7 ms | 8.32 ms | 7.38 ms | 9,490.9 ms | 32.51 |    0.14 | 212000.0000 | 888000000 B |          NA |

