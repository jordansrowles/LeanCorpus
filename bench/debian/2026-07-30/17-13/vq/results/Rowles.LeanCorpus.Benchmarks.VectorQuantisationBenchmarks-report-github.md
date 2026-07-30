```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 1.82GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 5.52 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]   : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  ShortRun : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method        | Dimension | Quantisation        | Mean     | Error     | StdDev    | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|-------------- |---------- |-------------------- |---------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| **&#39;HNSW search&#39;** | **64**        | **ProductQuantisation** | **2.026 ms** | **0.2136 ms** | **0.0117 ms** |  **1.00** | **46.8750** |      **-** | **200.75 KB** |        **1.00** |
|               |           |                     |          |           |           |       |         |        |           |             |
| **&#39;HNSW search&#39;** | **128**       | **ProductQuantisation** | **3.299 ms** | **0.1383 ms** | **0.0076 ms** |  **1.00** | **85.9375** | **7.8125** | **367.26 KB** |        **1.00** |
