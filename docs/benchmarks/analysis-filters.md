---
title: Benchmarks - Analysis filters
---

# Analysis filters

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method           | Scenario             | Mean         | Error      | StdDev     | Ratio  | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------- |--------------------- |-------------:|-----------:|-----------:|-------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_Apply** | **decim(...)ating [22]** |     **13.80 ns** |   **0.116 ns** |   **0.108 ns** |   **1.00** |    **0.00** | **0.0057** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | decim(...)ating [22] |  1,883.23 ns |  18.399 ns |  17.210 ns | 136.51 |    1.59 | 2.3708 |    9912 B |      413.00 |
|                  |                      |              |            |            |        |         |        |           |             |
| **LeanCorpus_Apply** | **elision-mutating**     |     **86.62 ns** |   **0.524 ns** |   **0.490 ns** |   **1.00** |    **0.00** | **0.0057** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | elision-mutating     |  3,387.38 ns |  35.822 ns |  33.508 ns |  39.11 |    0.43 | 2.7313 |   11432 B |      476.33 |
|                  |                      |              |            |            |        |         |        |           |             |
| **LeanCorpus_Apply** | **length-mutating**      |     **15.83 ns** |   **0.136 ns** |   **0.127 ns** |   **1.00** |    **0.00** | **0.0057** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | length-mutating      |  2,661.04 ns |  27.386 ns |  25.617 ns | 168.10 |    2.04 | 2.4986 |   10448 B |      435.33 |
|                  |                      |              |            |            |        |         |        |           |             |
| **LeanCorpus_Apply** | **length-noop**          |     **19.50 ns** |   **0.149 ns** |   **0.139 ns** |   **1.00** |    **0.00** | **0.0057** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | length-noop          |  2,715.45 ns |  24.349 ns |  21.585 ns | 139.24 |    1.44 | 2.4986 |   10448 B |      435.33 |
|                  |                      |              |            |            |        |         |        |           |             |
| **LeanCorpus_Apply** | **reverse-mutating**     |     **46.31 ns** |   **0.257 ns** |   **0.241 ns** |   **1.00** |    **0.00** | **0.0057** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | reverse-mutating     |  1,963.74 ns |  11.324 ns |   8.841 ns |  42.41 |    0.28 | 2.3880 |    9984 B |      416.00 |
|                  |                      |              |            |            |        |         |        |           |             |
| **LeanCorpus_Apply** | **shingle-mutating**     |     **15.54 ns** |   **0.148 ns** |   **0.139 ns** |   **1.00** |    **0.00** | **0.0057** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | shingle-mutating     | 13,050.79 ns | 116.399 ns | 108.880 ns | 840.04 |    9.89 | 4.7302 |   19816 B |      825.67 |
|                  |                      |              |            |            |        |         |        |           |             |
| **LeanCorpus_Apply** | **truncate-mutating**    |     **14.08 ns** |   **0.135 ns** |   **0.126 ns** |   **1.00** |    **0.00** | **0.0057** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | truncate-mutating    |  2,589.97 ns |  17.542 ns |  14.648 ns | 183.97 |    1.88 | 2.4948 |   10433 B |      434.71 |
|                  |                      |              |            |            |        |         |        |           |             |
| **LeanCorpus_Apply** | **truncate-noop**        |     **16.03 ns** |   **0.150 ns** |   **0.140 ns** |   **1.00** |    **0.00** | **0.0057** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | truncate-noop        |  2,614.10 ns |  25.980 ns |  24.302 ns | 163.10 |    2.01 | 2.4948 |   10433 B |      434.71 |
|                  |                      |              |            |            |        |         |        |           |             |
| **LeanCorpus_Apply** | **unique-mutating**      |     **17.55 ns** |   **0.162 ns** |   **0.151 ns** |   **1.00** |    **0.00** | **0.0057** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | unique-mutating      |  3,048.54 ns |  30.363 ns |  28.401 ns | 173.76 |    2.13 | 2.6283 |   11000 B |      458.33 |
|                  |                      |              |            |            |        |         |        |           |             |
| **LeanCorpus_Apply** | **word-(...)ating [23]** |     **10.59 ns** |   **0.084 ns** |   **0.079 ns** |   **1.00** |    **0.00** | **0.0057** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | word-(...)ating [23] |  8,569.80 ns |  72.405 ns |  67.728 ns | 809.16 |    8.48 | 3.7842 |   15880 B |      661.67 |

