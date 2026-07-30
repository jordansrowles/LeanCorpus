```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 2.09GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 4.16 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]   : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  ShortRun : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method        | Dimension | Quantisation        | Mean     | Error     | StdDev    | Ratio | Gen0    | Allocated | Alloc Ratio |
|-------------- |---------- |-------------------- |---------:|----------:|----------:|------:|--------:|----------:|------------:|
| **&#39;HNSW search&#39;** | **64**        | **ProductQuantisation** | **2.152 ms** | **0.3139 ms** | **0.0172 ms** |  **1.00** |  **7.8125** |  **42.85 KB** |        **1.00** |
|               |           |                     |          |           |           |       |         |           |             |
| **&#39;HNSW search&#39;** | **128**       | **ProductQuantisation** | **3.745 ms** | **2.1169 ms** | **0.1160 ms** |  **1.00** | **11.7188** |  **49.36 KB** |        **1.00** |
