---
title: Benchmarks - Analysis filters v2
---

# Analysis filters v2

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method           | Scenario             | Mean        | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|----------------- |--------------------- |------------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| **LeanCorpus_Apply** | **caching**              |   **739.67 ns** | **14.670 ns** | **12.250 ns** |  **1.00** |    **0.00** | **0.0238** | **0.0114** |     **152 B** |        **1.00** |
| LuceneNet_Apply  | caching              | 2,031.08 ns | 39.858 ns | 40.931 ns |  2.75 |    0.07 | 2.3689 |      - |    9912 B |       65.21 |
|                  |                      |             |           |           |       |         |        |        |           |             |
| **LeanCorpus_Apply** | **classic-mutating**     |   **150.31 ns** |  **0.677 ns** |  **0.600 ns** |  **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | classic-mutating     | 2,644.70 ns | 18.261 ns | 16.188 ns | 17.60 |    0.12 | 2.4910 | 0.0038 |   10424 B |      434.33 |
|                  |                      |             |           |           |       |         |        |        |           |             |
| **LeanCorpus_Apply** | **classic-noop**         |    **60.15 ns** |  **0.160 ns** |  **0.125 ns** |  **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | classic-noop         | 2,469.07 ns | 27.948 ns | 26.143 ns | 41.05 |    0.43 | 2.4910 | 0.0038 |   10424 B |      434.33 |
|                  |                      |             |           |           |       |         |        |        |           |             |
| **LeanCorpus_Apply** | **common-grams**         |   **337.76 ns** |  **1.822 ns** |  **1.704 ns** |  **1.00** |    **0.00** | **0.0591** |      **-** |     **248 B** |        **1.00** |
| LuceneNet_Apply  | common-grams         | 9,203.54 ns | 81.672 ns | 76.396 ns | 27.25 |    0.26 | 3.2501 |      - |   13648 B |       55.03 |
|                  |                      |             |           |           |       |         |        |        |           |             |
| **LeanCorpus_Apply** | **hyphenated-words**     |    **48.84 ns** |  **0.230 ns** |  **0.215 ns** |  **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | hyphenated-words     | 2,218.10 ns | 19.982 ns | 18.691 ns | 45.42 |    0.42 | 2.4300 |      - |   10176 B |      424.00 |
|                  |                      |             |           |           |       |         |        |        |           |             |
| **LeanCorpus_Apply** | **patte(...)ating [24]** |   **296.24 ns** |  **1.385 ns** |  **1.228 ns** |  **1.00** |    **0.00** | **0.0191** |      **-** |      **80 B** |        **1.00** |
| LuceneNet_Apply  | patte(...)ating [24] | 5,222.59 ns | 49.157 ns | 45.982 ns | 17.63 |    0.17 | 3.0518 |      - |   12793 B |      159.91 |
|                  |                      |             |           |           |       |         |        |        |           |             |
| **LeanCorpus_Apply** | **pattern-replace-noop** |    **82.96 ns** |  **0.497 ns** |  **0.465 ns** |  **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | pattern-replace-noop | 4,645.50 ns | 71.367 ns | 66.757 ns | 56.00 |    0.84 | 3.0289 |      - |   12681 B |      528.38 |

