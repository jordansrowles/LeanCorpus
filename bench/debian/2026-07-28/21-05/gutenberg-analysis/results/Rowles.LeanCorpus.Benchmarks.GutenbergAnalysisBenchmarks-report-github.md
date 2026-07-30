```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                      | Mean      | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|---------------------------- |----------:|---------:|---------:|------:|--------:|----------:|------------:|
| LeanCorpus_Standard_Analyse |  73.92 ms | 0.473 ms | 0.442 ms |  1.00 |    0.00 |         - |          NA |
| LeanCorpus_English_Analyse  | 183.53 ms | 1.209 ms | 1.130 ms |  2.48 |    0.02 |         - |          NA |
