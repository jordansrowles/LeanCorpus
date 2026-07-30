```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  Job-AMZPBM : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2

IterationCount=5  WarmupCount=2  

```
| Method                 | Dimension | Mean       | Error     | StdDev    | Ratio | RatioSD | Gen0       | Allocated    | Alloc Ratio |
|----------------------- |---------- |-----------:|----------:|----------:|------:|--------:|-----------:|-------------:|------------:|
| **&#39;Flat scan&#39;**            | **64**        |   **9.876 ms** | **0.0882 ms** | **0.0229 ms** |  **1.00** |    **0.00** |          **-** |      **8.04 KB** |        **1.00** |
| &#39;HNSW two-phase&#39;       | 64        |   2.275 ms | 0.1045 ms | 0.0271 ms |  0.23 |    0.00 |     7.8125 |     39.98 KB |        4.97 |
| &#39;Lucene.NET flat scan&#39; | 64        | 253.153 ms | 4.6726 ms | 0.7231 ms | 25.63 |    0.09 | 21000.0000 |  86718.85 KB |   10,781.95 |
|                        |           |            |           |           |       |         |            |              |             |
| **&#39;Flat scan&#39;**            | **128**       |  **11.185 ms** | **0.1944 ms** | **0.0505 ms** |  **1.00** |    **0.00** |          **-** |      **12.9 KB** |        **1.00** |
| &#39;HNSW two-phase&#39;       | 128       |   3.987 ms | 0.1801 ms | 0.0468 ms |  0.36 |    0.00 |     7.8125 |     45.98 KB |        3.56 |
| &#39;Lucene.NET flat scan&#39; | 128       | 227.235 ms | 0.5869 ms | 0.0908 ms | 20.32 |    0.08 | 33333.3333 | 136718.85 KB |   10,596.44 |
