---
title: Benchmarks - Pattern tokeniser
---

# Pattern tokeniser

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method              | Scenario         | Mean         | Error       | StdDev      | Ratio | RatioSD | Gen0      | Gen1   | Allocated | Alloc Ratio |
|-------------------- |----------------- |-------------:|------------:|------------:|------:|--------:|----------:|-------:|----------:|------------:|
| **LeanCorpus_Tokenise** | **comma-long**       |  **25,485.8 ns** |   **108.42 ns** |   **101.42 ns** |  **1.00** |    **0.00** |         **-** |      **-** |         **-** |          **NA** |
| LuceneNet_Tokenise  | comma-long       | 545,916.1 ns | 7,315.65 ns | 6,843.06 ns | 21.42 |    0.27 | 1091.7969 | 0.9766 | 4559840 B |          NA |
|                     |                  |              |             |             |       |         |           |        |           |             |
| **LeanCorpus_Tokenise** | **comma-short**      |     **439.6 ns** |     **2.55 ns** |     **2.39 ns** |  **1.00** |    **0.00** |         **-** |      **-** |         **-** |          **NA** |
| LuceneNet_Tokenise  | comma-short      |   4,178.5 ns |    63.37 ns |    59.27 ns |  9.50 |    0.14 |    5.0964 |      - |   21344 B |          NA |
|                     |                  |              |             |             |       |         |           |        |           |             |
| **LeanCorpus_Tokenise** | **whitespace-long**  |  **12,124.4 ns** |    **67.01 ns** |    **62.68 ns** |  **1.00** |    **0.00** |         **-** |      **-** |         **-** |          **NA** |
| LuceneNet_Tokenise  | whitespace-long  | 102,593.3 ns | 1,092.54 ns | 1,021.97 ns |  8.46 |    0.09 |  144.7754 | 0.1221 |  605960 B |          NA |
|                     |                  |              |             |             |       |         |           |        |           |             |
| **LeanCorpus_Tokenise** | **whitespace-short** |     **526.1 ns** |     **2.91 ns** |     **2.72 ns** |  **1.00** |    **0.00** |         **-** |      **-** |         **-** |          **NA** |
| LuceneNet_Tokenise  | whitespace-short |   4,326.7 ns |    63.53 ns |    59.42 ns |  8.22 |    0.12 |    5.1804 |      - |   21696 B |          NA |

