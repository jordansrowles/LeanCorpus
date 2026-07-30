```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                                  | DocumentCount | Mean         | Error       | StdDev      | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|---------------------------------------- |-------------- |-------------:|------------:|------------:|------:|--------:|--------:|-------:|----------:|------------:|
| LeanCorpus_NoCache                      | 100000        | 110,358.2 ns |   494.86 ns |   462.89 ns | 1.000 |    0.00 |  0.1221 |      - |     888 B |        1.00 |
| LeanCorpus_WithCache                    | 100000        |     261.1 ns |     1.51 ns |     1.41 ns | 0.002 |    0.00 |  0.1183 |      - |     496 B |        0.56 |
| &#39;Cache enabled, cacheable BooleanQuery&#39; | 100000        |     725.1 ns |     1.71 ns |     1.60 ns | 0.007 |    0.00 |  0.2556 |      - |    1072 B |        1.21 |
| &#39;Cache enabled, BooleanQuery misses&#39;    | 100000        | 410,557.8 ns | 4,924.98 ns | 4,606.83 ns | 3.720 |    0.04 |  3.4180 | 0.4883 |   17744 B |       19.98 |
| &#39;Cache disabled, BooleanQuery&#39;          | 100000        | 409,766.4 ns | 3,490.41 ns | 3,094.16 ns | 3.713 |    0.03 |  3.9063 |      - |   16598 B |       18.69 |
| LuceneNet_TermQuery                     | 100000        | 150,511.4 ns |   967.70 ns |   857.84 ns | 1.364 |    0.01 | 11.9629 | 0.2441 |   51119 B |       57.57 |
