```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                      | DocumentCount | Mean       | Error    | StdDev   | Ratio | Gen0        | Gen1       | Gen2      | Allocated  | Alloc Ratio |
|---------------------------- |-------------- |-----------:|---------:|---------:|------:|------------:|-----------:|----------:|-----------:|------------:|
| LeanCorpus_Index_NoSchema   | 100000        | 7,925.5 ms | 58.14 ms | 51.54 ms |  1.00 | 166000.0000 | 68000.0000 | 2000.0000 | 1116.03 MB |        1.00 |
| LeanCorpus_Index_WithSchema | 100000        | 7,974.5 ms | 49.27 ms | 46.09 ms |  1.01 | 167000.0000 | 67000.0000 | 2000.0000 | 1119.86 MB |        1.00 |
| LeanCorpus_JsonMapping      | 100000        |   416.5 ms |  2.28 ms |  2.13 ms |  0.05 |  52000.0000 |          - |         - |  219.01 MB |        0.20 |
