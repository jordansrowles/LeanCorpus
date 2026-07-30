```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                      | RangeWidth | DocumentCount | Mean        | Error    | StdDev   | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|---------------------------- |----------- |-------------- |------------:|---------:|---------:|------:|--------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_RangeQuery**       | **0.01**       | **100000**        |    **31.40 μs** | **0.200 μs** | **0.187 μs** |  **1.00** |    **0.00** |  **0.6714** |      **-** |   **2.98 KB** |        **1.00** |
| LuceneNet_NumericRangeQuery | 0.01       | 100000        |   102.68 μs | 0.324 μs | 0.303 μs |  3.27 |    0.02 | 36.8652 |      - | 150.79 KB |       50.53 |
|                             |            |               |             |          |          |       |         |         |        |           |             |
| **LeanCorpus_RangeQuery**       | **0.1**        | **100000**        |   **165.94 μs** | **0.915 μs** | **0.855 μs** |  **1.00** |    **0.00** |  **0.4883** |      **-** |   **2.98 KB** |        **1.00** |
| LuceneNet_NumericRangeQuery | 0.1        | 100000        |   302.03 μs | 1.184 μs | 1.108 μs |  1.82 |    0.01 | 35.1563 | 0.9766 | 144.45 KB |       48.40 |
|                             |            |               |             |          |          |       |         |         |        |           |             |
| **LeanCorpus_RangeQuery**       | **0.5**        | **100000**        |   **738.77 μs** | **2.771 μs** | **2.592 μs** |  **1.00** |    **0.00** |       **-** |      **-** |   **2.98 KB** |        **1.00** |
| LuceneNet_NumericRangeQuery | 0.5        | 100000        | 1,064.16 μs | 5.413 μs | 5.063 μs |  1.44 |    0.01 | 41.0156 | 1.9531 | 172.22 KB |       57.71 |
