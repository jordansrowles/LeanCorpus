```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                         | GeoQueryType | DocumentCount | Mean        | Error     | StdDev    | Ratio | RatioSD | Gen0     | Gen1   | Allocated | Alloc Ratio |
|------------------------------- |------------- |-------------- |------------:|----------:|----------:|------:|--------:|---------:|-------:|----------:|------------:|
| **LeanCorpus_GeoDistanceQuery**    | **BoundingBox**  | **100000**        |    **66.67 μs** |  **0.530 μs** |  **0.496 μs** |  **1.00** |    **0.00** |  **14.4043** |      **-** |  **58.91 KB** |        **1.00** |
| LeanCorpus_GeoBoundingBoxQuery | BoundingBox  | 100000        |   169.37 μs |  1.054 μs |  0.986 μs |  2.54 |    0.02 |  46.3867 |      - | 190.05 KB |        3.23 |
| LuceneNet_GeoDistanceQuery     | BoundingBox  | 100000        | 2,224.74 μs | 11.928 μs | 11.158 μs | 33.37 |    0.29 |  35.1563 |      - | 147.77 KB |        2.51 |
| LuceneNet_GeoBoundingBoxQuery  | BoundingBox  | 100000        |   621.06 μs |  2.739 μs |  2.562 μs |  9.32 |    0.08 | 161.1328 | 1.9531 | 660.55 KB |       11.21 |
|                                |              |               |             |           |           |       |         |          |        |           |             |
| **LeanCorpus_GeoDistanceQuery**    | **Distance**     | **100000**        |    **67.48 μs** |  **0.497 μs** |  **0.465 μs** |  **1.00** |    **0.00** |  **14.4043** |      **-** |  **58.91 KB** |        **1.00** |
| LeanCorpus_GeoBoundingBoxQuery | Distance     | 100000        |   168.60 μs |  0.904 μs |  0.846 μs |  2.50 |    0.02 |  46.3867 |      - | 190.05 KB |        3.23 |
| LuceneNet_GeoDistanceQuery     | Distance     | 100000        | 2,255.60 μs |  9.777 μs |  9.146 μs | 33.43 |    0.26 |  35.1563 |      - | 147.77 KB |        2.51 |
| LuceneNet_GeoBoundingBoxQuery  | Distance     | 100000        |   629.13 μs |  4.170 μs |  3.901 μs |  9.32 |    0.08 | 161.1328 | 1.9531 | 660.55 KB |       11.21 |
