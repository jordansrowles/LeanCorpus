```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method           | Scenario             | Mean         | Error     | StdDev    | Ratio  | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|----------------- |--------------------- |-------------:|----------:|----------:|-------:|--------:|-------:|-------:|----------:|------------:|
| **LeanCorpus_Apply** | **decim(...)ating [22]** |     **72.49 ns** |  **0.396 ns** |  **0.371 ns** |   **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | decim(...)ating [22] |  1,807.98 ns |  7.311 ns |  6.105 ns |  24.94 |    0.15 | 2.3708 |      - |    9912 B |      413.00 |
|                  |                      |              |           |           |        |         |        |        |           |             |
| **LeanCorpus_Apply** | **elision-mutating**     |     **86.13 ns** |  **0.556 ns** |  **0.520 ns** |   **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | elision-mutating     |  3,476.53 ns | 25.634 ns | 23.978 ns |  40.36 |    0.36 | 2.7313 |      - |   11432 B |      476.33 |
|                  |                      |              |           |           |        |         |        |        |           |             |
| **LeanCorpus_Apply** | **length-mutating**      |     **15.35 ns** |  **0.042 ns** |  **0.039 ns** |   **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | length-mutating      |  2,585.88 ns | 16.224 ns | 15.176 ns | 168.44 |    1.04 | 2.4986 |      - |   10448 B |      435.33 |
|                  |                      |              |           |           |        |         |        |        |           |             |
| **LeanCorpus_Apply** | **length-noop**          |     **15.97 ns** |  **0.108 ns** |  **0.101 ns** |   **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | length-noop          |  2,557.42 ns |  7.443 ns |  6.962 ns | 160.11 |    1.07 | 2.4986 |      - |   10448 B |      435.33 |
|                  |                      |              |           |           |        |         |        |        |           |             |
| **LeanCorpus_Apply** | **reverse-mutating**     |     **42.11 ns** |  **0.248 ns** |  **0.232 ns** |   **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | reverse-mutating     |  1,946.42 ns |  6.553 ns |  5.472 ns |  46.23 |    0.28 | 2.3880 |      - |    9984 B |      416.00 |
|                  |                      |              |           |           |        |         |        |        |           |             |
| **LeanCorpus_Apply** | **shingle-mutating**     |    **621.84 ns** |  **5.775 ns** |  **4.509 ns** |   **1.00** |    **0.00** | **0.0191** | **0.0095** |     **120 B** |        **1.00** |
| LuceneNet_Apply  | shingle-mutating     | 12,564.10 ns | 75.165 ns | 70.309 ns |  20.21 |    0.18 | 4.7302 |      - |   19816 B |      165.13 |
|                  |                      |              |           |           |        |         |        |        |           |             |
| **LeanCorpus_Apply** | **truncate-mutating**    |     **13.60 ns** |  **0.043 ns** |  **0.040 ns** |   **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | truncate-mutating    |  2,452.69 ns | 13.114 ns | 12.267 ns | 180.34 |    1.01 | 2.4948 |      - |   10433 B |      434.71 |
|                  |                      |              |           |           |        |         |        |        |           |             |
| **LeanCorpus_Apply** | **truncate-noop**        |     **15.43 ns** |  **0.092 ns** |  **0.086 ns** |   **1.00** |    **0.00** | **0.0057** |      **-** |      **24 B** |        **1.00** |
| LuceneNet_Apply  | truncate-noop        |  2,438.13 ns | 19.444 ns | 18.188 ns | 157.97 |    1.43 | 2.4948 |      - |   10433 B |      434.71 |
|                  |                      |              |           |           |        |         |        |        |           |             |
| **LeanCorpus_Apply** | **unique-mutating**      |    **165.78 ns** |  **0.743 ns** |  **0.695 ns** |   **1.00** |    **0.00** | **0.0362** |      **-** |     **152 B** |        **1.00** |
| LuceneNet_Apply  | unique-mutating      |  2,926.19 ns | 13.653 ns | 12.771 ns |  17.65 |    0.10 | 2.6283 |      - |   11000 B |       72.37 |
|                  |                      |              |           |           |        |         |        |        |           |             |
| **LeanCorpus_Apply** | **word-(...)ating [23]** |    **292.50 ns** |  **3.236 ns** |  **3.027 ns** |   **1.00** |    **0.00** | **0.1087** |      **-** |     **456 B** |        **1.00** |
| LuceneNet_Apply  | word-(...)ating [23] |  8,576.75 ns | 51.114 ns | 47.812 ns |  29.32 |    0.33 | 3.7842 |      - |   15880 B |       34.82 |
