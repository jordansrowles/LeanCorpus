---
title: Benchmarks - Boolean queries
---

# Boolean queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                  | BooleanShape  | DocumentCount | Mean        | Error     | StdDev    | Median      | Ratio | RatioSD | Gen0     | Gen1   | Allocated | Alloc Ratio |
|------------------------ |-------------- |-------------- |------------:|----------:|----------:|------------:|------:|--------:|---------:|-------:|----------:|------------:|
| **LeanCorpus_BooleanQuery** | **Must2Common**   | **100000**        |   **333.52 μs** |  **6.122 μs** |  **5.727 μs** |   **330.78 μs** |  **1.00** |    **0.00** |   **2.9297** |      **-** |  **13.64 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Must2Common   | 100000        |   591.33 μs |  3.286 μs |  2.744 μs |   591.65 μs |  1.77 |    0.03 |  28.3203 | 0.9766 | 117.92 KB |        8.64 |
|                         |               |               |             |           |           |             |       |         |          |        |           |             |
| **LeanCorpus_BooleanQuery** | **Must3Mixed**    | **100000**        |    **76.96 μs** |  **1.521 μs** |  **2.894 μs** |    **76.46 μs** |  **1.00** |    **0.00** |   **3.9063** |      **-** |  **16.08 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Must3Mixed    | 100000        |   295.32 μs |  3.650 μs |  3.048 μs |   293.90 μs |  3.84 |    0.14 |  39.5508 | 0.9766 | 166.78 KB |       10.37 |
|                         |               |               |             |           |           |             |       |         |          |        |           |             |
| **LeanCorpus_BooleanQuery** | **MustNotCommon** | **100000**        |   **195.47 μs** |  **2.564 μs** |  **2.399 μs** |   **195.33 μs** |  **1.00** |    **0.00** |   **3.4180** |      **-** |  **13.84 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | MustNotCommon | 100000        |   428.77 μs |  1.972 μs |  1.844 μs |   428.91 μs |  2.19 |    0.03 |  30.2734 | 0.9766 | 125.77 KB |        9.09 |
|                         |               |               |             |           |           |             |       |         |          |        |           |             |
| **LeanCorpus_BooleanQuery** | **Should2Common** | **100000**        |   **237.16 μs** |  **4.776 μs** | **14.083 μs** |   **231.93 μs** |  **1.00** |    **0.00** |   **3.4180** |      **-** |  **14.43 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Should2Common | 100000        |   693.77 μs | 13.656 μs | 24.273 μs |   700.56 μs |  2.94 |    0.20 | 164.0625 | 6.8359 | 675.54 KB |       46.80 |
|                         |               |               |             |           |           |             |       |         |          |        |           |             |
| **LeanCorpus_BooleanQuery** | **Should4Mixed**  | **100000**        |   **579.43 μs** |  **5.826 μs** |  **4.865 μs** |   **579.20 μs** |  **1.00** |    **0.00** |   **4.8828** |      **-** |  **19.82 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Should4Mixed  | 100000        | 1,084.75 μs |  7.533 μs |  7.047 μs | 1,082.07 μs |  1.87 |    0.02 | 191.4063 | 7.8125 | 789.69 KB |       39.84 |

