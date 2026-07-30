```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                   | WildcardPattern | DocumentCount | Mean       | Error    | StdDev   | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------- |---------------- |-------------- |-----------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_WildcardQuery** | **gov***            | **100000**        |   **258.7 μs** |  **1.32 μs** |  **1.24 μs** |  **1.00** |  **2.9297** |      **-** |  **12.38 KB** |        **1.00** |
| LuceneNet_WildcardQuery  | gov*            | 100000        |   273.7 μs |  1.68 μs |  1.57 μs |  1.06 | 28.8086 | 0.9766 | 119.67 KB |        9.67 |
|                          |                 |               |            |          |          |       |         |        |           |             |
| **LeanCorpus_WildcardQuery** | **m*rket**          | **100000**        | **2,437.1 μs** | **16.24 μs** | **15.19 μs** |  **1.00** |       **-** |      **-** |   **3.21 KB** |        **1.00** |
| LuceneNet_WildcardQuery  | m*rket          | 100000        | 1,408.8 μs |  7.08 μs |  6.62 μs |  0.58 | 95.7031 | 3.9063 | 396.38 KB |      123.45 |
|                          |                 |               |            |          |          |       |         |        |           |             |
| **LeanCorpus_WildcardQuery** | **pre*dent**        | **100000**        |   **323.5 μs** |  **2.31 μs** |  **2.16 μs** |  **1.00** |  **0.9766** |      **-** |    **4.2 KB** |        **1.00** |
| LuceneNet_WildcardQuery  | pre*dent        | 100000        |   443.7 μs |  4.21 μs |  3.93 μs |  1.37 | 90.3320 | 1.4648 | 370.48 KB |       88.14 |
