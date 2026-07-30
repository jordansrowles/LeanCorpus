```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                         | TieBreakerMultiplier | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------------- |--------------------- |-------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_DisjunctionMaxQuery** | **0**                    | **100000**        | **742.5 μs** | **5.92 μs** | **5.54 μs** |  **1.00** |       **-** |      **-** |   **3.38 KB** |        **1.00** |
| LuceneNet_DisjunctionMaxQuery  | 0                    | 100000        | 944.8 μs | 5.51 μs | 5.15 μs |  1.27 | 39.0625 | 0.9766 | 162.64 KB |       48.08 |
|                                |                      |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_DisjunctionMaxQuery** | **0.1**                  | **100000**        | **740.7 μs** | **4.82 μs** | **4.51 μs** |  **1.00** |       **-** |      **-** |   **3.38 KB** |        **1.00** |
| LuceneNet_DisjunctionMaxQuery  | 0.1                  | 100000        | 942.5 μs | 6.50 μs | 6.08 μs |  1.27 | 39.0625 | 0.9766 | 162.64 KB |       48.08 |
|                                |                      |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_DisjunctionMaxQuery** | **0.5**                  | **100000**        | **734.1 μs** | **5.60 μs** | **5.24 μs** |  **1.00** |       **-** |      **-** |   **3.38 KB** |        **1.00** |
| LuceneNet_DisjunctionMaxQuery  | 0.5                  | 100000        | 953.8 μs | 5.39 μs | 5.04 μs |  1.30 | 39.0625 | 0.9766 | 162.64 KB |       48.08 |
