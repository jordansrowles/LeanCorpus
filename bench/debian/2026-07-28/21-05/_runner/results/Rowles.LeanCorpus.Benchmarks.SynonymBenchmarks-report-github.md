```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                  | SynonymCount | DocumentCount | Mean       | Error    | StdDev   | Ratio | RatioSD | Gen0        | Allocated  | Alloc Ratio |
|------------------------ |------------- |-------------- |-----------:|---------:|---------:|------:|--------:|------------:|-----------:|------------:|
| **LeanCorpus_NoSynonyms**   | **10**           | **100000**        |   **920.5 ms** |  **5.30 ms** |  **4.96 ms** |  **1.00** |    **0.00** |           **-** |    **2.29 MB** |        **1.00** |
| LeanCorpus_WithSynonyms | 10           | 100000        |   899.1 ms |  5.50 ms |  5.15 ms |  0.98 |    0.01 |           - |    2.29 MB |        1.00 |
| LuceneNet_NoSynonyms    | 10           | 100000        | 2,188.5 ms |  8.74 ms |  8.18 ms |  2.38 |    0.02 | 144000.0000 |  577.24 MB |      252.20 |
| LuceneNet_WithSynonyms  | 10           | 100000        | 3,134.5 ms |  8.80 ms |  8.23 ms |  3.41 |    0.02 | 222000.0000 |  887.25 MB |      387.64 |
|                         |              |               |            |          |          |       |         |             |            |             |
| **LeanCorpus_NoSynonyms**   | **50**           | **100000**        |   **884.0 ms** |  **5.77 ms** |  **4.82 ms** |  **1.00** |    **0.00** |           **-** |    **2.29 MB** |        **1.00** |
| LeanCorpus_WithSynonyms | 50           | 100000        |   890.0 ms |  6.08 ms |  5.69 ms |  1.01 |    0.01 |           - |    2.29 MB |        1.00 |
| LuceneNet_NoSynonyms    | 50           | 100000        | 2,216.7 ms |  5.09 ms |  4.76 ms |  2.51 |    0.01 | 144000.0000 |  577.24 MB |      252.20 |
| LuceneNet_WithSynonyms  | 50           | 100000        | 5,175.3 ms | 11.71 ms | 10.38 ms |  5.85 |    0.03 | 401000.0000 | 1599.35 MB |      698.77 |
|                         |              |               |            |          |          |       |         |             |            |             |
| **LeanCorpus_NoSynonyms**   | **200**          | **100000**        |   **892.0 ms** |  **3.20 ms** |  **2.99 ms** |  **1.00** |    **0.00** |           **-** |    **2.29 MB** |        **1.00** |
| LeanCorpus_WithSynonyms | 200          | 100000        |   883.9 ms |  5.80 ms |  5.43 ms |  0.99 |    0.01 |           - |    2.29 MB |        1.00 |
| LuceneNet_NoSynonyms    | 200          | 100000        | 2,222.4 ms |  6.74 ms |  6.30 ms |  2.49 |    0.01 | 144000.0000 |  577.24 MB |      252.20 |
| LuceneNet_WithSynonyms  | 200          | 100000        | 5,607.7 ms | 13.89 ms | 12.99 ms |  6.29 |    0.02 | 545000.0000 | 2175.32 MB |      950.41 |
