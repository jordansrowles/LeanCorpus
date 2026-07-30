```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                 | QueryPrefix | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|----------------------- |------------ |-------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_PrefixQuery** | **gov**         | **100000**        | **260.2 μs** | **1.68 μs** | **1.58 μs** |  **1.00** |  **2.4414** |      **-** |  **11.67 KB** |        **1.00** |
| LuceneNet_PrefixQuery  | gov         | 100000        | 254.6 μs | 2.00 μs | 1.78 μs |  0.98 | 24.4141 | 0.9766 | 100.59 KB |        8.62 |
|                        |             |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_PrefixQuery** | **mark**        | **100000**        | **407.6 μs** | **1.32 μs** | **1.23 μs** |  **1.00** |  **4.3945** |      **-** |  **19.23 KB** |        **1.00** |
| LuceneNet_PrefixQuery  | mark        | 100000        | 378.8 μs | 1.34 μs | 1.26 μs |  0.93 | 27.8320 | 0.4883 | 116.13 KB |        6.04 |
|                        |             |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_PrefixQuery** | **pres**        | **100000**        | **574.0 μs** | **2.98 μs** | **2.79 μs** |  **1.00** |  **8.7891** |      **-** |  **37.09 KB** |        **1.00** |
| LuceneNet_PrefixQuery  | pres        | 100000        | 525.0 μs | 2.11 μs | 1.97 μs |  0.91 | 29.2969 | 0.9766 | 122.78 KB |        3.31 |
