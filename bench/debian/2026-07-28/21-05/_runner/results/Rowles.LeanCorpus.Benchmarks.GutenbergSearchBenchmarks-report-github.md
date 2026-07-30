```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                     | SearchTerm | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------------------- |----------- |---------:|---------:|---------:|------:|--------:|-------:|-------:|----------:|------------:|
| **LeanCorpus_Standard_Search** | **death**      | **11.80 μs** | **0.034 μs** | **0.032 μs** |  **1.00** |    **0.00** | **0.1221** |      **-** |     **560 B** |        **1.00** |
| LeanCorpus_English_Search  | death      | 11.73 μs | 0.082 μs | 0.072 μs |  0.99 |    0.01 | 0.1221 |      - |     560 B |        1.00 |
| LuceneNet_Search           | death      | 22.68 μs | 0.281 μs | 0.263 μs |  1.92 |    0.02 | 2.6550 | 0.0305 |   11231 B |       20.06 |
|                            |            |          |          |          |       |         |        |        |           |             |
| **LeanCorpus_Standard_Search** | **love**       | **15.41 μs** | **0.101 μs** | **0.095 μs** |  **1.00** |    **0.00** | **0.1221** |      **-** |     **552 B** |        **1.00** |
| LeanCorpus_English_Search  | love       | 19.99 μs | 0.121 μs | 0.113 μs |  1.30 |    0.01 | 0.1221 |      - |     552 B |        1.00 |
| LuceneNet_Search           | love       | 31.10 μs | 0.171 μs | 0.160 μs |  2.02 |    0.02 | 2.6245 | 0.0305 |   11175 B |       20.24 |
|                            |            |          |          |          |       |         |        |        |           |             |
| **LeanCorpus_Standard_Search** | **man**        | **40.92 μs** | **0.109 μs** | **0.096 μs** |  **1.00** |    **0.00** | **0.1221** |      **-** |     **552 B** |        **1.00** |
| LeanCorpus_English_Search  | man        | 40.71 μs | 0.179 μs | 0.149 μs |  1.00 |    0.00 | 0.1221 |      - |     552 B |        1.00 |
| LuceneNet_Search           | man        | 47.40 μs | 0.241 μs | 0.225 μs |  1.16 |    0.01 | 2.6245 | 0.0610 |   11038 B |       20.00 |
|                            |            |          |          |          |       |         |        |        |           |             |
| **LeanCorpus_Standard_Search** | **night**      | **26.00 μs** | **0.156 μs** | **0.146 μs** |  **1.00** |    **0.00** | **0.1221** |      **-** |     **560 B** |        **1.00** |
| LeanCorpus_English_Search  | night      | 26.53 μs | 0.144 μs | 0.135 μs |  1.02 |    0.01 | 0.1221 |      - |     560 B |        1.00 |
| LuceneNet_Search           | night      | 36.71 μs | 0.243 μs | 0.227 μs |  1.41 |    0.01 | 2.6245 |      - |   11223 B |       20.04 |
|                            |            |          |          |          |       |         |        |        |           |             |
| **LeanCorpus_Standard_Search** | **sea**        | **13.71 μs** | **0.035 μs** | **0.033 μs** |  **1.00** |    **0.00** | **0.1221** |      **-** |     **552 B** |        **1.00** |
| LeanCorpus_English_Search  | sea        | 14.52 μs | 0.081 μs | 0.076 μs |  1.06 |    0.01 | 0.1221 |      - |     552 B |        1.00 |
| LuceneNet_Search           | sea        | 26.81 μs | 0.156 μs | 0.146 μs |  1.96 |    0.01 | 2.6550 | 0.0305 |   11271 B |       20.42 |
