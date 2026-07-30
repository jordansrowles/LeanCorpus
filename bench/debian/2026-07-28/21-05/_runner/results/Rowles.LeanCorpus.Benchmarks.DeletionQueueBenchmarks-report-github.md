```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  Job-FEWCWF : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2

InvocationCount=1  IterationCount=3  UnrollFactor=1  
WarmupCount=1  

```
| Method                  | DocumentCount | Mean     | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------ |-------------- |---------:|----------:|----------:|------:|--------:|----------:|------------:|
| LeanLucene_QueueDeletes | 100000        | 2.407 ms | 12.727 ms | 0.6976 ms |  1.00 |    0.00 |   2.96 MB |        1.00 |
| LuceneNet_QueueDeletes  | 100000        | 4.515 ms | 29.068 ms | 1.5933 ms |  1.99 |    0.81 |    2.8 MB |        0.94 |
