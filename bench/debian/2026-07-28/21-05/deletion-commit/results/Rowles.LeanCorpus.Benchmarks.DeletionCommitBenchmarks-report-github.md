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
| Method                   | DocumentCount | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0      | Allocated | Alloc Ratio |
|------------------------- |-------------- |---------:|---------:|---------:|------:|--------:|----------:|----------:|------------:|
| LeanLucene_CommitDeletes | 100000        | 145.3 ms | 230.6 ms | 12.64 ms |  1.00 |    0.00 |         - |  17.96 MB |        1.00 |
| LuceneNet_CommitDeletes  | 100000        | 194.0 ms | 136.5 ms |  7.48 ms |  1.34 |    0.11 | 4000.0000 |  19.24 MB |        1.07 |
