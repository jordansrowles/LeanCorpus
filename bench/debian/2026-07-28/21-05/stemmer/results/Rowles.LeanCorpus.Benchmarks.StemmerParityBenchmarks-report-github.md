```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                     | DocumentCount | Mean    | Error    | StdDev   | Ratio | Gen0        | Allocated | Alloc Ratio |
|--------------------------- |-------------- |--------:|---------:|---------:|------:|------------:|----------:|------------:|
| LeanCorpus_StemmedAnalyser | 100000        | 2.155 s | 0.0109 s | 0.0102 s |  1.00 |           - |   2.29 MB |        1.00 |
| LuceneNet_EnglishAnalyzer  | 100000        | 3.337 s | 0.0081 s | 0.0076 s |  1.55 | 143000.0000 | 573.51 MB |      250.57 |
