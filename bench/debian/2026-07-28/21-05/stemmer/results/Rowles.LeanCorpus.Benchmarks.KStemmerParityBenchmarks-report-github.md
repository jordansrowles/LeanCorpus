```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                   | DocumentCount | Mean    | Error    | StdDev   | Ratio | Gen0        | Allocated | Alloc Ratio |
|------------------------- |-------------- |--------:|---------:|---------:|------:|------------:|----------:|------------:|
| LeanCorpus_KStem_Analyse | 100000        | 1.906 s | 0.0073 s | 0.0068 s |  1.00 |           - |   2.29 MB |        1.00 |
| LuceneNet_KStem_Analyse  | 100000        | 2.964 s | 0.0061 s | 0.0057 s |  1.56 | 146000.0000 |    583 MB |      254.72 |
