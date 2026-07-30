```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                         | SetSize | DocumentCount | Mean      | Error     | StdDev    | Ratio | Gen0      | Gen1     | Allocated  | Alloc Ratio |
|------------------------------- |-------- |-------------- |----------:|----------:|----------:|------:|----------:|---------:|-----------:|------------:|
| **LeanCorpus_TermInSetQuery**      | **5**       | **100000**        |  **2.927 ms** | **0.0096 ms** | **0.0090 ms** |  **1.00** |         **-** |        **-** |    **3.73 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_Should | 5       | 100000        |  1.958 ms | 0.0132 ms | 0.0117 ms |  0.67 |    3.9063 |        - |   24.68 KB |        6.62 |
| LuceneNet_BooleanQuery_Should  | 5       | 100000        |  2.089 ms | 0.0109 ms | 0.0102 ms |  0.71 |  199.2188 |  15.6250 |  827.46 KB |      222.04 |
|                                |         |               |           |           |           |       |           |          |            |             |
| **LeanCorpus_TermInSetQuery**      | **20**      | **100000**        |  **6.478 ms** | **0.0427 ms** | **0.0399 ms** |  **1.00** |         **-** |        **-** |   **11.25 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_Should | 20      | 100000        |  4.964 ms | 0.0320 ms | 0.0299 ms |  0.77 |   15.6250 |        - |   85.19 KB |        7.57 |
| LuceneNet_BooleanQuery_Should  | 20      | 100000        |  4.938 ms | 0.0275 ms | 0.0229 ms |  0.76 |  406.2500 |  15.6250 | 1704.66 KB |      151.53 |
|                                |         |               |           |           |           |       |           |          |            |             |
| **LeanCorpus_TermInSetQuery**      | **100**     | **100000**        | **13.308 ms** | **0.0357 ms** | **0.0334 ms** |  **1.00** |         **-** |        **-** |   **50.72 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_Should | 100     | 100000        | 12.256 ms | 0.0617 ms | 0.0577 ms |  0.92 |  171.8750 | 156.2500 |  999.31 KB |       19.70 |
| LuceneNet_BooleanQuery_Should  | 100     | 100000        | 11.982 ms | 0.0954 ms | 0.0892 ms |  0.90 | 1265.6250 | 265.6250 | 5961.94 KB |      117.55 |
