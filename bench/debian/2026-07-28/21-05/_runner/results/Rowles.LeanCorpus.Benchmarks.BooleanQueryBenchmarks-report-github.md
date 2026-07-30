```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                  | BooleanShape  | DocumentCount | Mean       | Error   | StdDev  | Ratio | RatioSD | Gen0     | Gen1   | Allocated | Alloc Ratio |
|------------------------ |-------------- |-------------- |-----------:|--------:|--------:|------:|--------:|---------:|-------:|----------:|------------:|
| **LeanCorpus_BooleanQuery** | **Must2Common**   | **100000**        |   **489.6 μs** | **5.91 μs** | **5.52 μs** |  **1.00** |    **0.00** |   **2.9297** |      **-** |  **14.25 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Must2Common   | 100000        |   581.1 μs | 3.93 μs | 3.68 μs |  1.19 |    0.01 |  28.3203 | 0.9766 | 117.52 KB |        8.25 |
|                         |               |               |            |         |         |       |         |          |        |           |             |
| **LeanCorpus_BooleanQuery** | **Must3Mixed**    | **100000**        |   **165.1 μs** | **0.38 μs** | **0.36 μs** |  **1.00** |    **0.00** |   **3.9063** |      **-** |  **16.44 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Must3Mixed    | 100000        |   279.5 μs | 5.43 μs | 7.78 μs |  1.69 |    0.05 |  39.5508 | 0.9766 | 166.58 KB |       10.13 |
|                         |               |               |            |         |         |       |         |          |        |           |             |
| **LeanCorpus_BooleanQuery** | **MustNotCommon** | **100000**        |   **389.9 μs** | **2.67 μs** | **2.50 μs** |  **1.00** |    **0.00** |   **3.4180** |      **-** |   **14.4 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | MustNotCommon | 100000        |   445.5 μs | 2.67 μs | 2.50 μs |  1.14 |    0.01 |  30.2734 | 0.4883 | 125.84 KB |        8.74 |
|                         |               |               |            |         |         |       |         |          |        |           |             |
| **LeanCorpus_BooleanQuery** | **Should2Common** | **100000**        |   **456.9 μs** | **6.75 μs** | **6.31 μs** |  **1.00** |    **0.00** |   **3.4180** |      **-** |  **14.95 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Should2Common | 100000        |   624.2 μs | 5.12 μs | 4.79 μs |  1.37 |    0.02 | 164.0625 | 5.8594 | 675.76 KB |       45.19 |
|                         |               |               |            |         |         |       |         |          |        |           |             |
| **LeanCorpus_BooleanQuery** | **Should4Mixed**  | **100000**        |   **828.1 μs** | **6.23 μs** | **5.82 μs** |  **1.00** |    **0.00** |   **4.8828** |      **-** |  **20.88 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Should4Mixed  | 100000        | 1,026.3 μs | 7.37 μs | 6.90 μs |  1.24 |    0.01 | 191.4063 | 5.8594 | 789.83 KB |       37.82 |
