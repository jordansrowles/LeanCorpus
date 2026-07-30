```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                           | BlockCount | Mean     | Error     | StdDev    | Ratio | Gen0    | Allocated | Alloc Ratio |
|--------------------------------- |----------- |---------:|----------:|----------:|------:|--------:|----------:|------------:|
| LeanLucene_BlockJoinQuery        | 100000     | 1.634 ms | 0.0140 ms | 0.0124 ms |  1.00 |       - |   2.23 KB |        1.00 |
| LuceneNet_ToParentBlockJoinQuery | 100000     | 2.031 ms | 0.0118 ms | 0.0110 ms |  1.24 | 11.7188 |  48.14 KB |       21.54 |
