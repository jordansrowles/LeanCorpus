```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                     | DocumentCount | Mean     | Error     | StdDev    | Ratio | Gen0     | Gen1   | Allocated | Alloc Ratio |
|--------------------------- |-------------- |---------:|----------:|----------:|------:|---------:|-------:|----------:|------------:|
| &#39;LC MLT SingleSeg Scalar&#39;  | 100000        | 4.204 ms | 0.0312 ms | 0.0277 ms |  1.00 |        - |      - |  13.64 KB |        1.00 |
| &#39;Lucene.NET MLT SingleSeg&#39; | 100000        | 2.372 ms | 0.0121 ms | 0.0114 ms |  0.56 | 183.5938 | 7.8125 | 789.98 KB |       57.91 |
