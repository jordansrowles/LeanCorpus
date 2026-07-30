```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  Job-AMZPBM : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2

IterationCount=5  WarmupCount=2  

```
| Method                    | Mean       | Error     | StdDev    | Ratio | RatioSD | Gen0       | Gen1       | Gen2      | Allocated | Alloc Ratio |
|-------------------------- |-----------:|----------:|----------:|------:|--------:|-----------:|-----------:|----------:|----------:|------------:|
| LeanCorpus_Standard_Index |   724.0 ms |  20.34 ms |   3.15 ms |  1.00 |    0.00 | 20000.0000 | 10000.0000 | 2000.0000 | 137.09 MB |        1.00 |
| LeanCorpus_English_Index  |   737.9 ms |  14.28 ms |   2.21 ms |  1.02 |    0.00 | 17000.0000 |  8000.0000 | 1000.0000 |  122.3 MB |        0.89 |
| LuceneNet_Index           | 1,513.2 ms | 394.01 ms | 102.32 ms |  2.09 |    0.13 | 42000.0000 |  3000.0000 |         - | 208.13 MB |        1.52 |
