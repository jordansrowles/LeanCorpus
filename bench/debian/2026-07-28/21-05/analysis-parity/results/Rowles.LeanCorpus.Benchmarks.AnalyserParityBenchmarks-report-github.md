```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------------- |----------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| LeanCorpus_Whitespace | 30.020 μs | 0.2114 μs | 0.1977 μs |  1.00 |    0.00 |      - |         - |          NA |
| LuceneNet_Whitespace  | 74.733 μs | 0.4064 μs | 0.3801 μs |  2.49 |    0.02 | 0.7324 |    3200 B |          NA |
| LeanCorpus_Keyword    |  3.320 μs | 0.0162 μs | 0.0152 μs |  0.11 |    0.00 |      - |         - |          NA |
| LuceneNet_Keyword     | 12.053 μs | 0.0786 μs | 0.0736 μs |  0.40 |    0.00 | 0.7629 |    3200 B |          NA |
| LeanCorpus_Simple     | 43.118 μs | 0.3306 μs | 0.3093 μs |  1.44 |    0.01 |      - |         - |          NA |
| LuceneNet_Simple      | 90.453 μs | 0.4797 μs | 0.4487 μs |  3.01 |    0.02 | 0.7324 |    3200 B |          NA |
