```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method               | QueryTerm  | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|--------------------- |----------- |-------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_TermQuery** | **government** | **100000**        | **110.6 μs** | **0.76 μs** | **0.71 μs** |  **1.00** |  **0.1221** |      **-** |     **888 B** |        **1.00** |
| LuceneNet_TermQuery  | government | 100000        | 152.9 μs | 0.66 μs | 0.61 μs |  1.38 | 11.9629 | 0.2441 |   51159 B |       57.61 |
|                      |            |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_TermQuery** | **people**     | **100000**        | **157.2 μs** | **0.89 μs** | **0.83 μs** |  **1.00** |       **-** |      **-** |     **880 B** |        **1.00** |
| LuceneNet_TermQuery  | people     | 100000        | 198.3 μs | 1.13 μs | 1.00 μs |  1.26 | 11.4746 | 0.2441 |   49034 B |       55.72 |
|                      |            |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_TermQuery** | **said**       | **100000**        | **695.8 μs** | **2.00 μs** | **1.87 μs** |  **1.00** |       **-** |      **-** |     **872 B** |        **1.00** |
| LuceneNet_TermQuery  | said       | 100000        | 780.1 μs | 4.73 μs | 4.43 μs |  1.12 | 10.7422 |      - |   48874 B |       56.05 |
