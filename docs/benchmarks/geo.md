---
title: Benchmarks - Geo queries
---

# Geo queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                         | GeoQueryType | DocumentCount | Mean     | Error    | StdDev   | Median   | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------------- |------------- |-------------- |---------:|---------:|---------:|---------:|------:|--------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_GeoDistanceQuery**    | **BoundingBox**  | **100000**        | **32.61 μs** | **0.681 μs** | **1.921 μs** | **31.94 μs** |  **1.00** |    **0.00** | **15.8081** |      **-** |  **63.34 KB** |        **1.00** |
| LeanCorpus_GeoBoundingBoxQuery | BoundingBox  | 100000        | 86.82 μs | 1.728 μs | 4.553 μs | 85.03 μs |  2.67 |    0.20 | 51.1475 | 0.3662 | 194.83 KB |        3.08 |
|                                |              |               |          |          |          |          |       |         |         |        |           |             |
| **LeanCorpus_GeoDistanceQuery**    | **Distance**     | **100000**        | **33.73 μs** | **0.815 μs** | **2.389 μs** | **32.66 μs** |  **1.00** |    **0.00** | **15.8691** |      **-** |  **63.38 KB** |        **1.00** |
| LeanCorpus_GeoBoundingBoxQuery | Distance     | 100000        | 84.10 μs | 2.082 μs | 6.139 μs | 81.16 μs |  2.50 |    0.25 | 50.5371 | 0.1221 | 194.77 KB |        3.07 |

