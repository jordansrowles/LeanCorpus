```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                        | Mode     | DocumentCount | Mean     | Error   | StdDev  | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------------ |--------- |-------------- |---------:|--------:|--------:|------:|--------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_BaseTermQuery**      | **Max**      | **100000**        | **110.3 μs** | **0.48 μs** | **0.45 μs** |  **1.00** |    **0.00** |  **0.1221** |      **-** |     **888 B** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | Max      | 100000        | 146.2 μs | 0.76 μs | 0.71 μs |  1.32 |    0.01 |       - |      - |     960 B |        1.08 |
| LuceneNet_TermQuery           | Max      | 100000        | 192.0 μs | 1.31 μs | 1.23 μs |  1.74 |    0.01 | 18.3105 | 0.2441 |   77541 B |       87.32 |
| LuceneNet_FunctionScoreQuery  | Max      | 100000        | 226.8 μs | 1.78 μs | 1.67 μs |  2.06 |    0.02 | 18.5547 | 0.2441 |   78878 B |       88.83 |
|                               |          |               |          |         |         |       |         |         |        |           |             |
| **LeanCorpus_BaseTermQuery**      | **Multiply** | **100000**        | **110.2 μs** | **0.71 μs** | **0.67 μs** |  **1.00** |    **0.00** |  **0.1221** |      **-** |     **888 B** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | Multiply | 100000        | 140.4 μs | 0.89 μs | 0.83 μs |  1.27 |    0.01 |       - |      - |     960 B |        1.08 |
| LuceneNet_TermQuery           | Multiply | 100000        | 190.4 μs | 0.90 μs | 0.84 μs |  1.73 |    0.01 | 18.3105 | 0.2441 |   77541 B |       87.32 |
| LuceneNet_FunctionScoreQuery  | Multiply | 100000        | 228.4 μs | 1.77 μs | 1.65 μs |  2.07 |    0.02 | 18.5547 | 0.2441 |   78874 B |       88.82 |
|                               |          |               |          |         |         |       |         |         |        |           |             |
| **LeanCorpus_BaseTermQuery**      | **Replace**  | **100000**        | **112.2 μs** | **0.62 μs** | **0.58 μs** |  **1.00** |    **0.00** |  **0.1221** |      **-** |     **888 B** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | Replace  | 100000        | 137.3 μs | 1.07 μs | 1.00 μs |  1.22 |    0.01 |       - |      - |     960 B |        1.08 |
| LuceneNet_TermQuery           | Replace  | 100000        | 189.8 μs | 1.05 μs | 0.99 μs |  1.69 |    0.01 | 18.3105 | 0.2441 |   77541 B |       87.32 |
| LuceneNet_FunctionScoreQuery  | Replace  | 100000        | 226.2 μs | 2.19 μs | 2.05 μs |  2.02 |    0.02 | 18.5547 | 0.2441 |   78878 B |       88.83 |
|                               |          |               |          |         |         |       |         |         |        |           |             |
| **LeanCorpus_BaseTermQuery**      | **Sum**      | **100000**        | **110.2 μs** | **0.78 μs** | **0.73 μs** |  **1.00** |    **0.00** |  **0.1221** |      **-** |     **888 B** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | Sum      | 100000        | 141.0 μs | 1.00 μs | 0.94 μs |  1.28 |    0.01 |       - |      - |     960 B |        1.08 |
| LuceneNet_TermQuery           | Sum      | 100000        | 190.8 μs | 0.83 μs | 0.78 μs |  1.73 |    0.01 | 18.3105 | 0.2441 |   77541 B |       87.32 |
| LuceneNet_FunctionScoreQuery  | Sum      | 100000        | 227.2 μs | 1.69 μs | 1.58 μs |  2.06 |    0.02 | 18.5547 | 0.2441 |   78878 B |       88.83 |
