```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                 | Pattern    | DocumentCount | Mean        | Error     | StdDev    | Ratio | RatioSD | Gen0     | Gen1    | Allocated  | Alloc Ratio |
|----------------------- |----------- |-------------- |------------:|----------:|----------:|------:|--------:|---------:|--------:|-----------:|------------:|
| **LeanCorpus_RegexpQuery** | **.*nation.*** | **100000**        | **38,817.3 μs** | **182.46 μs** | **161.74 μs** |  **1.00** |    **0.00** |        **-** |       **-** |   **51.46 KB** |        **1.00** |
| LuceneNet_RegexpQuery  | .*nation.* | 100000        | 29,252.3 μs | 114.34 μs | 106.95 μs |  0.75 |    0.00 | 312.5000 | 31.2500 | 1342.69 KB |       26.09 |
|                        |            |               |             |           |           |       |         |          |         |            |             |
| **LeanCorpus_RegexpQuery** | **gov.*ment**  | **100000**        |    **264.8 μs** |   **1.37 μs** |   **1.28 μs** |  **1.00** |    **0.00** |  **10.2539** |       **-** |   **43.25 KB** |        **1.00** |
| LuceneNet_RegexpQuery  | gov.*ment  | 100000        |    427.9 μs |   4.23 μs |   3.95 μs |  1.62 |    0.02 |  89.8438 |  0.9766 |  369.11 KB |        8.53 |
|                        |            |               |             |           |           |       |         |          |         |            |             |
| **LeanCorpus_RegexpQuery** | **mark.***     | **100000**        |    **511.5 μs** |   **3.76 μs** |   **3.52 μs** |  **1.00** |    **0.00** |  **17.5781** |       **-** |   **74.58 KB** |        **1.00** |
| LuceneNet_RegexpQuery  | mark.*     | 100000        |    426.3 μs |   4.98 μs |   4.66 μs |  0.83 |    0.01 |  40.5273 |  0.4883 |  166.43 KB |        2.23 |
