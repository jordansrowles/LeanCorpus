---
title: Benchmarks - Synonym
---

# Synonym

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                  | SynonymCount | DocumentCount | Mean       | Error    | StdDev  | Ratio | RatioSD | Gen0        | Allocated  | Alloc Ratio |
|------------------------ |------------- |-------------- |-----------:|---------:|--------:|------:|--------:|------------:|-----------:|------------:|
| **LeanCorpus_NoSynonyms**   | **10**           | **100000**        |   **878.4 ms** |  **3.56 ms** | **3.16 ms** |  **1.00** |    **0.00** |           **-** |    **2.29 MB** |        **1.00** |
| LeanCorpus_WithSynonyms | 10           | 100000        |   891.8 ms |  4.74 ms | 4.44 ms |  1.02 |    0.01 |           - |    2.29 MB |        1.00 |
| LuceneNet_NoSynonyms    | 10           | 100000        | 2,247.2 ms |  4.11 ms | 3.84 ms |  2.56 |    0.01 | 144000.0000 |  577.04 MB |      252.11 |
| LuceneNet_WithSynonyms  | 10           | 100000        | 3,487.2 ms |  7.55 ms | 6.69 ms |  3.97 |    0.02 | 222000.0000 |  887.23 MB |      387.64 |
|                         |              |               |            |          |         |       |         |             |            |             |
| **LeanCorpus_NoSynonyms**   | **50**           | **100000**        |   **900.3 ms** |  **5.91 ms** | **5.53 ms** |  **1.00** |    **0.00** |           **-** |    **2.29 MB** |        **1.00** |
| LeanCorpus_WithSynonyms | 50           | 100000        |   892.4 ms |  7.97 ms | 7.46 ms |  0.99 |    0.01 |           - |    2.29 MB |        1.00 |
| LuceneNet_NoSynonyms    | 50           | 100000        | 2,241.9 ms |  3.66 ms | 3.42 ms |  2.49 |    0.02 | 144000.0000 |  577.04 MB |      252.11 |
| LuceneNet_WithSynonyms  | 50           | 100000        | 4,345.8 ms |  8.56 ms | 8.01 ms |  4.83 |    0.03 | 401000.0000 | 1599.49 MB |      698.83 |
|                         |              |               |            |          |         |       |         |             |            |             |
| **LeanCorpus_NoSynonyms**   | **200**          | **100000**        |   **896.8 ms** |  **3.01 ms** | **2.67 ms** |  **1.00** |    **0.00** |           **-** |    **2.29 MB** |        **1.00** |
| LeanCorpus_WithSynonyms | 200          | 100000        |   929.0 ms |  3.00 ms | 2.66 ms |  1.04 |    0.00 |           - |    2.29 MB |        1.00 |
| LuceneNet_NoSynonyms    | 200          | 100000        | 2,230.4 ms |  6.83 ms | 6.38 ms |  2.49 |    0.01 | 144000.0000 |  577.04 MB |      252.11 |
| LuceneNet_WithSynonyms  | 200          | 100000        | 6,058.4 ms | 10.64 ms | 9.95 ms |  6.76 |    0.02 | 545000.0000 | 2175.42 MB |      950.46 |

