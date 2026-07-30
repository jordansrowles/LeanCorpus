```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                      | DocumentCount | Mean     | Error     | StdDev    | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|---------------------------- |-------------- |---------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| LeanCorpus_MultiPhraseQuery | 100000        | 1.109 ms | 0.0028 ms | 0.0026 ms |  1.00 | 17.5781 |      - |  78.84 KB |        1.00 |
| LuceneNet_MultiPhraseQuery  | 100000        | 1.119 ms | 0.0093 ms | 0.0087 ms |  1.01 | 87.8906 | 1.9531 | 371.22 KB |        4.71 |
