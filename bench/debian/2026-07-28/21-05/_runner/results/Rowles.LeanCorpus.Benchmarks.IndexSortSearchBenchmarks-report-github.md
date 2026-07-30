```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                                   | DocumentCount | Mean       | Error     | StdDev    | Ratio  | RatioSD | Gen0    | Gen1    | Gen2   | Allocated | Alloc Ratio |
|----------------------------------------- |-------------- |-----------:|----------:|----------:|-------:|--------:|--------:|--------:|-------:|----------:|------------:|
| LeanCorpus_SortedSearch_EarlyTermination | 100000        |   2.744 μs | 0.0161 μs | 0.0151 μs |   1.00 |    0.00 |  0.2213 |       - |      - |     928 B |        1.00 |
| LeanCorpus_SortedSearch_PostSort         | 100000        | 311.965 μs | 3.4643 μs | 3.2405 μs | 113.69 |    1.30 | 38.0859 | 10.7422 | 9.2773 |  920908 B |      992.36 |
| LuceneNet_SortedSearch                   | 100000        | 127.141 μs | 0.7267 μs | 0.6797 μs |  46.34 |    0.35 | 20.0195 |  0.2441 |      - |   84677 B |       91.25 |
