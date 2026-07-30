```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                 | DocumentCount | Mean     | Error     | StdDev    | Ratio | RatioSD | Gen0      | Gen1     | Allocated  | Alloc Ratio |
|----------------------- |-------------- |---------:|----------:|----------:|------:|--------:|----------:|---------:|-----------:|------------:|
| LeanCorpus_DidYouMean  | 100000        | 4.554 ms | 0.0422 ms | 0.0395 ms |  1.00 |    0.00 |         - |        - |   24.91 KB |        1.00 |
| LeanCorpus_SpellIndex  | 100000        | 4.639 ms | 0.0295 ms | 0.0261 ms |  1.02 |    0.01 |         - |        - |    23.2 KB |        0.93 |
| LuceneNet_SpellChecker | 100000        | 9.899 ms | 0.0454 ms | 0.0403 ms |  2.17 |    0.02 | 1296.8750 | 140.6250 | 5351.46 KB |      214.80 |
