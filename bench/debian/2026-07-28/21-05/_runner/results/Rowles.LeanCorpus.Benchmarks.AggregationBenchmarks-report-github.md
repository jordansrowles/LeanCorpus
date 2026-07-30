```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                                 | DocumentCount | Mean        | Error    | StdDev   | Ratio | RatioSD | Gen0     | Gen1     | Allocated | Alloc Ratio |
|--------------------------------------- |-------------- |------------:|---------:|---------:|------:|--------:|---------:|---------:|----------:|------------:|
| LeanCorpus_SearchOnly                  | 100000        |    111.1 μs |  0.59 μs |  0.55 μs |  1.00 |    0.00 |   0.1221 |        - |     888 B |        1.00 |
| LeanCorpus_SearchWithStats             | 100000        |    379.2 μs |  1.26 μs |  1.18 μs |  3.41 |    0.02 |  53.7109 |   1.9531 |  225480 B |      253.92 |
| LeanCorpus_SearchWithHistogram         | 100000        |    422.6 μs |  2.43 μs |  2.27 μs |  3.80 |    0.03 |  63.4766 |        - |  265800 B |      299.32 |
| LeanCorpus_SearchWithStatsAndHistogram | 100000        |    658.2 μs |  5.05 μs |  4.73 μs |  5.92 |    0.05 | 100.5859 |        - |  424392 B |      477.92 |
| LuceneNet_TermQuery                    | 100000        |    188.9 μs |  1.45 μs |  1.28 μs |  1.70 |    0.01 |  18.3105 |   0.2441 |   77541 B |       87.32 |
| LuceneNet_SearchWithStats              | 100000        | 10,342.1 μs | 51.93 μs | 48.57 μs | 93.08 |    0.62 | 562.5000 | 421.8750 | 4114497 B |    4,633.44 |
