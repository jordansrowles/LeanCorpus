```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method           | Scenario             | Mean        | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|----------------- |--------------------- |------------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| **LeanCorpus_Apply** | **caching**              |   **716.47 ns** | **13.721 ns** | **11.457 ns** |  **1.00** |    **0.00** | **0.0238** | **0.0114** |     **152 B** |        **1.00** |
| LuceneNet_Apply  | caching              | 1,990.32 ns | 17.509 ns | 16.378 ns |  2.78 |    0.05 | 2.3689 |      - |    9912 B |       65.21 |
|                  |                      |             |           |           |       |         |        |        |           |             |
| **LeanCorpus_Apply** | **classic-mutating**     |   **149.64 ns** |  **0.669 ns** |  **0.625 ns** |  **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | classic-mutating     | 2,561.40 ns | 11.975 ns | 11.202 ns | 17.12 |    0.10 | 2.4910 | 0.0038 |   10424 B |      434.33 |
|                  |                      |             |           |           |       |         |        |        |           |             |
| **LeanCorpus_Apply** | **classic-noop**         |    **58.87 ns** |  **0.338 ns** |  **0.316 ns** |  **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | classic-noop         | 2,403.29 ns | 12.109 ns | 11.327 ns | 40.82 |    0.28 | 2.4910 | 0.0038 |   10424 B |      434.33 |
|                  |                      |             |           |           |       |         |        |        |           |             |
| **LeanCorpus_Apply** | **common-grams**         |   **323.44 ns** |  **1.978 ns** |  **1.851 ns** |  **1.00** |    **0.00** | **0.0591** |      **-** |     **248 B** |        **1.00** |
| LuceneNet_Apply  | common-grams         | 8,895.93 ns | 40.061 ns | 37.473 ns | 27.50 |    0.19 | 3.2501 |      - |   13648 B |       55.03 |
|                  |                      |             |           |           |       |         |        |        |           |             |
| **LeanCorpus_Apply** | **hyphenated-words**     |    **48.33 ns** |  **0.208 ns** |  **0.195 ns** |  **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | hyphenated-words     | 2,032.99 ns | 16.410 ns | 15.350 ns | 42.07 |    0.35 | 2.4300 |      - |   10176 B |      424.00 |
|                  |                      |             |           |           |       |         |        |        |           |             |
| **LeanCorpus_Apply** | **patte(...)ating [24]** |   **509.83 ns** |  **2.839 ns** |  **2.656 ns** |  **1.00** |    **0.00** | **0.0191** |      **-** |      **80 B** |        **1.00** |
| LuceneNet_Apply  | patte(...)ating [24] | 5,072.18 ns | 37.091 ns | 34.695 ns |  9.95 |    0.08 | 3.0518 |      - |   12793 B |      159.91 |
|                  |                      |             |           |           |       |         |        |        |           |             |
| **LeanCorpus_Apply** | **pattern-replace-noop** |   **104.72 ns** |  **0.373 ns** |  **0.349 ns** |  **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | pattern-replace-noop | 4,524.86 ns | 24.935 ns | 23.324 ns | 43.21 |    0.26 | 3.0289 |      - |   12681 B |      528.38 |
