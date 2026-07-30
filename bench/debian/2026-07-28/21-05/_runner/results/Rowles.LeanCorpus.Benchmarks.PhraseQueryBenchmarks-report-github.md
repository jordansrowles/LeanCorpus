```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                 | PhraseType     | DocumentCount | Mean       | Error    | StdDev   | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|----------------------- |--------------- |-------------- |-----------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_PhraseQuery** | **ExactThreeWord** | **100000**        |   **853.6 μs** |  **4.21 μs** |  **3.94 μs** |  **1.00** | **13.6719** |      **-** |  **56.81 KB** |        **1.00** |
| LuceneNet_PhraseQuery  | ExactThreeWord | 100000        |   462.2 μs |  3.30 μs |  2.75 μs |  0.54 | 75.1953 | 0.9766 | 320.23 KB |        5.64 |
|                        |                |               |            |          |          |       |         |        |           |             |
| **LeanCorpus_PhraseQuery** | **ExactTwoWord**   | **100000**        |   **812.2 μs** |  **5.06 μs** |  **4.48 μs** |  **1.00** |  **8.7891** |      **-** |  **38.34 KB** |        **1.00** |
| LuceneNet_PhraseQuery  | ExactTwoWord   | 100000        |   499.1 μs |  2.91 μs |  2.72 μs |  0.61 | 64.4531 | 2.9297 | 266.54 KB |        6.95 |
|                        |                |               |            |          |          |       |         |        |           |             |
| **LeanCorpus_PhraseQuery** | **SlopTwoWord**    | **100000**        | **2,064.7 μs** | **13.55 μs** | **12.68 μs** |  **1.00** |  **7.8125** |      **-** |  **44.87 KB** |        **1.00** |
| LuceneNet_PhraseQuery  | SlopTwoWord    | 100000        | 1,194.1 μs |  6.97 μs |  6.52 μs |  0.58 | 27.3438 | 1.9531 | 120.46 KB |        2.68 |
