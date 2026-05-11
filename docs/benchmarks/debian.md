---
title: Benchmarks - debian
---

# Benchmarks: debian

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `b772bf2` &nbsp;&middot;&nbsp; 11 May 2026 10:32 UTC &nbsp;&middot;&nbsp; 92 benchmarks

## Analysis

| Method             | DocumentCount | Mean    | Error    | StdDev   | Ratio | Gen0        | Gen1      | Allocated | Alloc Ratio |
|------------------- |-------------- |--------:|---------:|---------:|------:|------------:|----------:|----------:|------------:|
| LeanLucene_Analyse | 100000        | 1.532 s | 0.0019 s | 0.0018 s |  1.00 |  49000.0000 | 2000.0000 | 199.73 MB |        1.00 |
| LuceneNet_Analyse  | 100000        | 2.256 s | 0.0031 s | 0.0027 s |  1.47 | 144000.0000 |         - | 576.92 MB |        2.89 |

## analysis-filters

| Method | Scenario             | Mean      | Error    | StdDev   | Gen0   | Allocated |
|------- |--------------------- |----------:|---------:|---------:|-------:|----------:|
| **Apply**  | **decim(...)ating [22]** |  **86.54 ns** | **0.158 ns** | **0.148 ns** | **0.0286** |     **120 B** |
| **Apply**  | **elision-mutating**     | **154.51 ns** | **0.433 ns** | **0.384 ns** | **0.0362** |     **152 B** |
| **Apply**  | **length-mutating**      |  **51.38 ns** | **0.128 ns** | **0.107 ns** | **0.0249** |     **104 B** |
| **Apply**  | **length-noop**          |  **47.62 ns** | **0.096 ns** | **0.085 ns** | **0.0249** |     **104 B** |
| **Apply**  | **reverse-mutating**     |  **69.78 ns** | **0.129 ns** | **0.114 ns** | **0.0381** |     **160 B** |
| **Apply**  | **shingle-mutating**     | **256.32 ns** | **0.388 ns** | **0.324 ns** | **0.1202** |     **504 B** |
| **Apply**  | **truncate-mutating**    |  **54.54 ns** | **0.145 ns** | **0.136 ns** | **0.0306** |     **128 B** |
| **Apply**  | **truncate-noop**        |  **44.01 ns** | **0.081 ns** | **0.068 ns** | **0.0249** |     **104 B** |
| **Apply**  | **unique-mutating**      | **152.78 ns** | **0.339 ns** | **0.317 ns** | **0.0706** |     **296 B** |
| **Apply**  | **word-(...)ating [23]** | **446.41 ns** | **0.524 ns** | **0.490 ns** | **0.2217** |     **928 B** |

## analysis-parity

| Method                | Mean      | Error     | StdDev    | Ratio | Gen0   | Allocated | Alloc Ratio |
|---------------------- |----------:|----------:|----------:|------:|-------:|----------:|------------:|
| LeanLucene_Whitespace | 39.833 μs | 0.0575 μs | 0.0509 μs |  1.00 |      - |         - |          NA |
| LuceneNet_Whitespace  | 76.348 μs | 0.1119 μs | 0.0992 μs |  1.92 | 0.7324 |    3200 B |          NA |
| LeanLucene_Keyword    |  4.017 μs | 0.0088 μs | 0.0082 μs |  0.10 |      - |         - |          NA |
| LuceneNet_Keyword     | 12.487 μs | 0.0209 μs | 0.0185 μs |  0.31 | 0.7629 |    3200 B |          NA |
| LeanLucene_Simple     | 39.521 μs | 0.0995 μs | 0.0882 μs |  0.99 |      - |         - |          NA |
| LuceneNet_Simple      | 94.659 μs | 0.1440 μs | 0.1347 μs |  2.38 | 0.7324 |    3200 B |          NA |

## Block-Join

| Method                           | BlockCount | Mean          | Error       | StdDev      | Ratio | Gen0      | Gen1     | Allocated  | Alloc Ratio |
|--------------------------------- |----------- |--------------:|------------:|------------:|------:|----------:|---------:|-----------:|------------:|
| LeanLucene_IndexBlocks           | 500        | 81,989.873 μs | 246.0180 μs | 218.0885 μs | 1.000 | 1857.1429 | 857.1429 | 13906016 B |       1.000 |
| LeanLucene_BlockJoinQuery        | 500        |      7.152 μs |   0.0167 μs |   0.0156 μs | 0.000 |    0.1678 |        - |      720 B |       0.000 |
| LuceneNet_IndexBlocks            | 500        | 57,079.239 μs | 322.5448 μs | 301.7086 μs | 0.696 | 5000.0000 | 666.6667 | 28715836 B |       2.065 |
| LuceneNet_ToParentBlockJoinQuery | 500        |     21.741 μs |   0.0467 μs |   0.0437 μs | 0.000 |    3.0518 |        - |    12888 B |       0.001 |

## Boolean queries

| Method                  | BooleanType | DocumentCount | Mean     | Error   | StdDev  | Ratio | RatioSD | Gen0     | Gen1    | Allocated | Alloc Ratio |
|------------------------ |------------ |-------------- |---------:|--------:|--------:|------:|--------:|---------:|--------:|----------:|------------:|
| **LeanLucene_BooleanQuery** | **Must**        | **100000**        | **264.6 μs** | **2.54 μs** | **2.25 μs** |  **1.00** |    **0.00** |   **2.9297** |       **-** |  **12.93 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Must        | 100000        | 484.1 μs | 0.38 μs | 0.30 μs |  1.83 |    0.02 |  35.1563 |       - | 144.09 KB |       11.14 |
|                         |             |               |          |         |         |       |         |          |         |           |             |
| **LeanLucene_BooleanQuery** | **MustNot**     | **100000**        | **173.6 μs** | **1.23 μs** | **1.15 μs** |  **1.00** |    **0.00** |   **3.1738** |       **-** |   **13.3 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | MustNot     | 100000        | 410.1 μs | 0.86 μs | 0.81 μs |  2.36 |    0.02 |  36.1328 |       - | 149.06 KB |       11.21 |
|                         |             |               |          |         |         |       |         |          |         |           |             |
| **LeanLucene_BooleanQuery** | **Should**      | **100000**        | **224.0 μs** | **3.19 μs** | **2.99 μs** |  **1.00** |    **0.00** |   **3.4180** |       **-** |   **13.7 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Should      | 100000        | 579.8 μs | 0.92 μs | 0.77 μs |  2.59 |    0.03 | 169.9219 | 40.0391 | 695.01 KB |       50.74 |

## Deletion

| Method                     | DocumentCount | Mean     | Error    | StdDev   | Ratio | Gen0        | Gen1       | Gen2      | Allocated | Alloc Ratio |
|--------------------------- |-------------- |---------:|---------:|---------:|------:|------------:|-----------:|----------:|----------:|------------:|
| LeanLucene_DeleteDocuments | 100000        | 10.946 s | 0.0598 s | 0.0559 s |  1.00 | 200000.0000 | 89000.0000 | 9000.0000 |   1.19 GB |        1.00 |
| LuceneNet_DeleteDocuments  | 100000        |  7.231 s | 0.0242 s | 0.0214 s |  0.66 | 339000.0000 | 37000.0000 | 1000.0000 |   1.91 GB |        1.61 |

## Fuzzy queries

| Method                | QueryTerm | DocumentCount | Mean     | Error     | StdDev    | Ratio | Gen0     | Gen1     | Allocated  | Alloc Ratio |
|---------------------- |---------- |-------------- |---------:|----------:|----------:|------:|---------:|---------:|-----------:|------------:|
| **LeanLucene_FuzzyQuery** | **goverment** | **100000**        | **6.979 ms** | **0.0575 ms** | **0.0480 ms** |  **1.00** |        **-** |        **-** |   **25.88 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | goverment | 100000        | 8.645 ms | 0.0200 ms | 0.0177 ms |  1.24 | 593.7500 | 203.1250 | 2870.85 KB |      110.94 |
|                       |           |               |          |           |           |       |          |          |            |             |
| **LeanLucene_FuzzyQuery** | **markts**    | **100000**        | **7.595 ms** | **0.0603 ms** | **0.0564 ms** |  **1.00** |   **7.8125** |        **-** |   **47.66 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | markts    | 100000        | 9.245 ms | 0.0255 ms | 0.0226 ms |  1.22 | 625.0000 | 187.5000 | 2806.02 KB |       58.87 |
|                       |           |               |          |           |           |       |          |          |            |             |
| **LeanLucene_FuzzyQuery** | **presiden**  | **100000**        | **8.035 ms** | **0.0928 ms** | **0.0868 ms** |  **1.00** |        **-** |        **-** |   **30.61 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | presiden  | 100000        | 8.768 ms | 0.0197 ms | 0.0174 ms |  1.09 | 593.7500 | 218.7500 | 2844.58 KB |       92.93 |

## gutenberg-analysis

| Method                      | Mean     | Error   | StdDev  | Ratio | RatioSD | Gen0       | Gen1      | Gen2      | Allocated | Alloc Ratio |
|---------------------------- |---------:|--------:|--------:|------:|--------:|-----------:|----------:|----------:|----------:|------------:|
| LeanLucene_Standard_Analyse | 124.5 ms | 0.22 ms | 0.19 ms |  1.00 |    0.00 |  1400.0000 |  600.0000 |         - |   7.23 MB |        1.00 |
| LeanLucene_English_Analyse  | 391.7 ms | 2.53 ms | 2.37 ms |  3.15 |    0.02 | 11000.0000 | 6000.0000 | 2000.0000 | 113.03 MB |       15.62 |

## gutenberg-index

| Method                    | Mean       | Error    | StdDev   | Ratio | Gen0       | Gen1       | Gen2      | Allocated | Alloc Ratio |
|-------------------------- |-----------:|---------:|---------:|------:|-----------:|-----------:|----------:|----------:|------------:|
| LeanLucene_Standard_Index | 1,004.9 ms |  7.91 ms |  6.61 ms |  1.00 | 19000.0000 | 10000.0000 | 1000.0000 | 123.51 MB |        1.00 |
| LeanLucene_English_Index  | 1,028.4 ms | 14.99 ms | 13.29 ms |  1.02 | 36000.0000 | 12000.0000 | 2000.0000 |  218.9 MB |        1.77 |
| LuceneNet_Index           |   650.5 ms |  2.45 ms |  2.17 ms |  0.65 | 41000.0000 |  3000.0000 |         - | 207.68 MB |        1.68 |

## gutenberg-search

| Method                     | SearchTerm | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------------------- |----------- |---------:|---------:|---------:|------:|--------:|-------:|-------:|----------:|------------:|
| **LeanLucene_Standard_Search** | **death**      | **11.39 μs** | **0.031 μs** | **0.029 μs** |  **1.00** |    **0.00** | **0.1068** |      **-** |     **472 B** |        **1.00** |
| LeanLucene_English_Search  | death      | 11.61 μs | 0.022 μs | 0.021 μs |  1.02 |    0.00 | 0.1068 |      - |     472 B |        1.00 |
| LuceneNet_Search           | death      | 23.19 μs | 0.428 μs | 0.401 μs |  2.04 |    0.03 | 2.6550 | 0.0305 |   11231 B |       23.79 |
|                            |            |          |          |          |       |         |        |        |           |             |
| **LeanLucene_Standard_Search** | **love**       | **15.49 μs** | **0.051 μs** | **0.045 μs** |  **1.00** |    **0.00** | **0.0916** |      **-** |     **464 B** |        **1.00** |
| LeanLucene_English_Search  | love       | 20.03 μs | 0.031 μs | 0.029 μs |  1.29 |    0.00 | 0.0916 |      - |     464 B |        1.00 |
| LuceneNet_Search           | love       | 29.89 μs | 0.044 μs | 0.041 μs |  1.93 |    0.01 | 2.6245 | 0.0305 |   11175 B |       24.08 |
|                            |            |          |          |          |       |         |        |        |           |             |
| **LeanLucene_Standard_Search** | **man**        | **40.09 μs** | **0.074 μs** | **0.069 μs** |  **1.00** |    **0.00** | **0.0610** |      **-** |     **464 B** |        **1.00** |
| LeanLucene_English_Search  | man        | 39.64 μs | 0.042 μs | 0.040 μs |  0.99 |    0.00 | 0.0610 |      - |     464 B |        1.00 |
| LuceneNet_Search           | man        | 52.77 μs | 0.216 μs | 0.202 μs |  1.32 |    0.01 | 2.6245 | 0.0610 |   11038 B |       23.79 |
|                            |            |          |          |          |       |         |        |        |           |             |
| **LeanLucene_Standard_Search** | **night**      | **25.25 μs** | **0.051 μs** | **0.048 μs** |  **1.00** |    **0.00** | **0.0916** |      **-** |     **472 B** |        **1.00** |
| LeanLucene_English_Search  | night      | 26.30 μs | 0.056 μs | 0.049 μs |  1.04 |    0.00 | 0.0916 |      - |     472 B |        1.00 |
| LuceneNet_Search           | night      | 37.74 μs | 0.082 μs | 0.077 μs |  1.49 |    0.00 | 2.6245 |      - |   11223 B |       23.78 |
|                            |            |          |          |          |       |         |        |        |           |             |
| **LeanLucene_Standard_Search** | **sea**        | **12.96 μs** | **0.027 μs** | **0.025 μs** |  **1.00** |    **0.00** | **0.1068** |      **-** |     **464 B** |        **1.00** |
| LeanLucene_English_Search  | sea        | 13.95 μs | 0.025 μs | 0.023 μs |  1.08 |    0.00 | 0.1068 |      - |     464 B |        1.00 |
| LuceneNet_Search           | sea        | 27.27 μs | 0.070 μs | 0.065 μs |  2.10 |    0.01 | 2.6550 | 0.0305 |   11271 B |       24.29 |

## Indexing

| Method                    | DocumentCount | Mean     | Error    | StdDev   | Ratio | Gen0        | Gen1       | Gen2      | Allocated | Alloc Ratio |
|-------------------------- |-------------- |---------:|---------:|---------:|------:|------------:|-----------:|----------:|----------:|------------:|
| LeanLucene_IndexDocuments | 100000        | 10.959 s | 0.0548 s | 0.0513 s |  1.00 | 196000.0000 | 87000.0000 | 6000.0000 |   1.17 GB |        1.00 |
| LuceneNet_IndexDocuments  | 100000        |  7.126 s | 0.0191 s | 0.0169 s |  0.65 | 332000.0000 | 34000.0000 | 1000.0000 |   1.88 GB |        1.61 |

## Index-sort (index)

| Method                    | DocumentCount | Mean    | Error   | StdDev  | Ratio | Gen0        | Gen1       | Gen2      | Allocated | Alloc Ratio |
|-------------------------- |-------------- |--------:|--------:|--------:|------:|------------:|-----------:|----------:|----------:|------------:|
| LeanLucene_Index_Unsorted | 100000        | 11.43 s | 0.041 s | 0.038 s |  1.00 | 205000.0000 | 87000.0000 | 6000.0000 |   1.24 GB |        1.00 |
| LeanLucene_Index_Sorted   | 100000        | 12.36 s | 0.035 s | 0.033 s |  1.08 | 208000.0000 | 87000.0000 | 6000.0000 |   1.27 GB |        1.02 |

## Index-sort (search)

| Method                                   | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|----------------------------------------- |-------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| LeanLucene_SortedSearch_EarlyTermination | 100000        | 255.2 μs | 0.81 μs | 0.76 μs |  1.00 | 28.3203 | 0.9766 | 117.66 KB |        1.00 |
| LeanLucene_SortedSearch_PostSort         | 100000        | 249.7 μs | 0.40 μs | 0.31 μs |  0.98 | 28.3203 | 0.9766 | 117.66 KB |        1.00 |

## Phrase queries

| Method                 | PhraseType     | DocumentCount | Mean       | Error   | StdDev  | Ratio | Gen0    | Gen1    | Allocated | Alloc Ratio |
|----------------------- |--------------- |-------------- |-----------:|--------:|--------:|------:|--------:|--------:|----------:|------------:|
| **LeanLucene_PhraseQuery** | **ExactThreeWord** | **100000**        |   **437.9 μs** | **1.91 μs** | **1.59 μs** |  **1.00** | **14.6484** |       **-** |  **59.75 KB** |        **1.00** |
| LuceneNet_PhraseQuery  | ExactThreeWord | 100000        |   343.8 μs | 1.15 μs | 1.07 μs |  0.79 | 90.3320 |  0.4883 | 369.88 KB |        6.19 |
|                        |                |               |            |         |         |       |         |         |           |             |
| **LeanLucene_PhraseQuery** | **ExactTwoWord**   | **100000**        |   **321.3 μs** | **4.00 μs** | **3.74 μs** |  **1.00** | **10.2539** |       **-** |  **42.91 KB** |        **1.00** |
| LuceneNet_PhraseQuery  | ExactTwoWord   | 100000        |   409.6 μs | 0.69 μs | 0.65 μs |  1.27 | 72.2656 | 18.0664 | 297.27 KB |        6.93 |
|                        |                |               |            |         |         |       |         |         |           |             |
| **LeanLucene_PhraseQuery** | **SlopTwoWord**    | **100000**        |   **985.9 μs** | **7.34 μs** | **6.13 μs** |  **1.00** | **11.7188** |       **-** |  **48.69 KB** |        **1.00** |
| LuceneNet_PhraseQuery  | SlopTwoWord    | 100000        | 1,037.2 μs | 2.24 μs | 2.10 μs |  1.05 | 37.1094 |       - | 155.61 KB |        3.20 |

## Prefix queries

| Method                 | QueryPrefix | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|----------------------- |------------ |-------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| **LeanLucene_PrefixQuery** | **gov**         | **100000**        | **151.9 μs** | **1.12 μs** | **0.99 μs** |  **1.00** |  **5.8594** |      **-** |  **23.67 KB** |        **1.00** |
| LuceneNet_PrefixQuery  | gov         | 100000        | 186.1 μs | 0.38 μs | 0.35 μs |  1.22 | 26.8555 | 0.2441 | 110.04 KB |        4.65 |
|                        |             |               |          |         |         |       |         |        |           |             |
| **LeanLucene_PrefixQuery** | **mark**        | **100000**        | **243.0 μs** | **1.27 μs** | **1.12 μs** |  **1.00** |  **8.5449** |      **-** |   **34.5 KB** |        **1.00** |
| LuceneNet_PrefixQuery  | mark        | 100000        | 285.5 μs | 0.52 μs | 0.46 μs |  1.17 | 30.7617 |      - | 126.09 KB |        3.65 |
|                        |             |               |          |         |         |       |         |        |           |             |
| **LeanLucene_PrefixQuery** | **pres**        | **100000**        | **289.7 μs** | **3.27 μs** | **2.90 μs** |  **1.00** | **15.6250** |      **-** |  **62.99 KB** |        **1.00** |
| LuceneNet_PrefixQuery  | pres        | 100000        | 356.8 μs | 0.63 μs | 0.56 μs |  1.23 | 32.2266 |      - | 133.65 KB |        2.12 |

## Term queries

| Method               | QueryTerm  | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|--------------------- |----------- |-------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| **LeanLucene_TermQuery** | **government** | **100000**        | **106.0 μs** | **0.28 μs** | **0.26 μs** |  **1.00** |       **-** |      **-** |     **480 B** |        **1.00** |
| LuceneNet_TermQuery  | government | 100000        | 136.3 μs | 0.35 μs | 0.31 μs |  1.29 | 14.4043 |      - |   60896 B |      126.87 |
|                      |            |               |          |         |         |       |         |        |           |             |
| **LeanLucene_TermQuery** | **people**     | **100000**        | **150.6 μs** | **0.30 μs** | **0.28 μs** |  **1.00** |       **-** |      **-** |     **472 B** |        **1.00** |
| LuceneNet_TermQuery  | people     | 100000        | 177.5 μs | 0.34 μs | 0.32 μs |  1.18 | 13.9160 | 0.2441 |   58688 B |      124.34 |
|                      |            |               |          |         |         |       |         |        |           |             |
| **LeanLucene_TermQuery** | **said**       | **100000**        | **687.3 μs** | **1.02 μs** | **0.96 μs** |  **1.00** |       **-** |      **-** |     **464 B** |        **1.00** |
| LuceneNet_TermQuery  | said       | 100000        | 753.3 μs | 1.25 μs | 1.17 μs |  1.10 | 13.6719 |      - |   58720 B |      126.55 |

## Schema and JSON

| Method                      | DocumentCount | Mean        | Error    | StdDev   | Ratio | Gen0        | Gen1       | Gen2      | Allocated  | Alloc Ratio |
|---------------------------- |-------------- |------------:|---------:|---------:|------:|------------:|-----------:|----------:|-----------:|------------:|
| LeanLucene_Index_NoSchema   | 100000        | 10,501.4 ms | 61.92 ms | 57.92 ms |  1.00 | 193000.0000 | 81000.0000 | 2000.0000 | 1194.41 MB |        1.00 |
| LeanLucene_Index_WithSchema | 100000        | 10,540.6 ms | 48.98 ms | 45.81 ms |  1.00 | 193000.0000 | 82000.0000 | 2000.0000 | 1198.28 MB |        1.00 |
| LeanLucene_JsonMapping      | 100000        |    443.4 ms |  2.52 ms |  2.36 ms |  0.04 |  51000.0000 |  1000.0000 |         - |  215.88 MB |        0.18 |

## Suggester

| Method                 | DocumentCount | Mean      | Error     | StdDev    | Ratio | Gen0      | Gen1    | Allocated  | Alloc Ratio |
|----------------------- |-------------- |----------:|----------:|----------:|------:|----------:|--------:|-----------:|------------:|
| LeanLucene_DidYouMean  | 100000        |  4.700 ms | 0.0334 ms | 0.0312 ms |  1.00 |         - |       - |   24.91 KB |        1.00 |
| LeanLucene_SpellIndex  | 100000        |  4.696 ms | 0.0133 ms | 0.0118 ms |  1.00 |         - |       - |    23.2 KB |        0.93 |
| LuceneNet_SpellChecker | 100000        | 10.287 ms | 0.0257 ms | 0.0241 ms |  2.19 | 1296.8750 | 31.2500 | 5351.15 KB |      214.78 |

## Wildcard queries

| Method                   | WildcardPattern | DocumentCount | Mean       | Error   | StdDev  | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------- |---------------- |-------------- |-----------:|--------:|--------:|------:|--------:|--------:|-------:|----------:|------------:|
| **LeanLucene_WildcardQuery** | **gov***            | **100000**        |   **149.8 μs** | **1.75 μs** | **1.55 μs** |  **1.00** |    **0.00** |  **6.1035** |      **-** |  **24.38 KB** |        **1.00** |
| LuceneNet_WildcardQuery  | gov*            | 100000        |   203.6 μs | 0.29 μs | 0.26 μs |  1.36 |    0.01 | 31.4941 |      - | 129.06 KB |        5.29 |
|                          |                 |               |            |         |         |       |         |         |        |           |             |
| **LeanLucene_WildcardQuery** | **m*rket**          | **100000**        |   **530.5 μs** | **8.82 μs** | **8.25 μs** |  **1.00** |    **0.00** |  **1.9531** |      **-** |  **10.18 KB** |        **1.00** |
| LuceneNet_WildcardQuery  | m*rket          | 100000        | 1,172.6 μs | 2.42 μs | 2.26 μs |  2.21 |    0.03 | 97.6563 | 9.7656 | 404.52 KB |       39.75 |
|                          |                 |               |            |         |         |       |         |         |        |           |             |
| **LeanLucene_WildcardQuery** | **pre*dent**        | **100000**        |   **106.1 μs** | **0.54 μs** | **0.45 μs** |  **1.00** |    **0.00** |  **2.0752** |      **-** |   **8.63 KB** |        **1.00** |
| LuceneNet_WildcardQuery  | pre*dent        | 100000        |   410.5 μs | 0.97 μs | 0.90 μs |  3.87 |    0.02 | 92.7734 |      - |  379.5 KB |       44.00 |

<details>
<summary>Full data (report.json)</summary>

<pre><code class="lang-json">{
  "schemaVersion": 2,
  "runId": "2026-05-11 10-32 (b772bf2)",
  "runType": "full",
  "generatedAtUtc": "2026-05-11T10:32:06.2651834\u002B00:00",
  "commandLineArgs": [],
  "hostMachineName": "debian",
  "commitHash": "b772bf2",
  "dotnetVersion": "10.0.3",
  "provenance": {
    "sourceCommit": "b772bf2",
    "sourceRef": "",
    "sourceManifestPath": "",
    "gitCommitHash": "b772bf2",
    "gitAvailable": true,
    "gitDirty": false,
    "benchmarkDotNetVersion": "0.16.0-nightly.20260427.506\u002Bc68dc1556c410c4bdfe21373c7689be5781fbaf9",
    "runtimeFramework": ".NET 10.0.3",
    "runtimeIdentifier": "linux-x64",
    "osDescription": "Debian GNU/Linux 13 (trixie)",
    "processArchitecture": "X64",
    "effectiveDocCount": 100000,
    "dataFingerprintSha256": "",
    "dataSources": []
  },
  "totalBenchmarkCount": 92,
  "suites": [
    {
      "suiteName": "analysis",
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.AnalysisBenchmarks-20260511-114756",
      "benchmarkCount": 2,
      "benchmarks": [
        {
          "key": "AnalysisBenchmarks.LeanLucene_Analyse|DocumentCount=100000",
          "displayInfo": "AnalysisBenchmarks.LeanLucene_Analyse: DefaultJob [DocumentCount=100000]",
          "typeName": "AnalysisBenchmarks",
          "methodName": "LeanLucene_Analyse",
          "parameters": {
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 1531943367.8666666,
            "medianNanoseconds": 1532094825,
            "minNanoseconds": 1527479526,
            "maxNanoseconds": 1534978435,
            "standardDeviationNanoseconds": 1753449.1059289754,
            "operationsPerSecond": 0.6527656445894385
          },
          "gc": {
            "bytesAllocatedPerOperation": 209429200,
            "gen0Collections": 49,
            "gen1Collections": 2,
            "gen2Collections": 0
          }
        },
        {
          "key": "AnalysisBenchmarks.LuceneNet_Analyse|DocumentCount=100000",
          "displayInfo": "AnalysisBenchmarks.LuceneNet_Analyse: DefaultJob [DocumentCount=100000]",
          "typeName": "AnalysisBenchmarks",
          "methodName": "LuceneNet_Analyse",
          "parameters": {
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 2255704285.285714,
            "medianNanoseconds": 2256229605.5,
            "minNanoseconds": 2250159429,
            "maxNanoseconds": 2260899083,
            "standardDeviationNanoseconds": 2732646.689312408,
            "operationsPerSecond": 0.44332052145449424
          },
          "gc": {
            "bytesAllocatedPerOperation": 604939928,
            "gen0Collections": 144,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "analysis-filters",
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.TokenFilterBenchmarks-20260511-115408",
      "benchmarkCount": 10,
      "benchmarks": [
        {
          "key": "TokenFilterBenchmarks.Apply|Scenario=decimal-digit-mutating",
          "displayInfo": "TokenFilterBenchmarks.Apply: DefaultJob [Scenario=decim(...)ating [22]]",
          "typeName": "TokenFilterBenchmarks",
          "methodName": "Apply",
          "parameters": {
            "Scenario": "decimal-digit-mutating"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 86.5421764532725,
            "medianNanoseconds": 86.54858815670013,
            "minNanoseconds": 86.26754093170166,
            "maxNanoseconds": 86.82399916648865,
            "standardDeviationNanoseconds": 0.14821908556779007,
            "operationsPerSecond": 11555059.52106415
          },
          "gc": {
            "bytesAllocatedPerOperation": 120,
            "gen0Collections": 240,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "TokenFilterBenchmarks.Apply|Scenario=elision-mutating",
          "displayInfo": "TokenFilterBenchmarks.Apply: DefaultJob [Scenario=elision-mutating]",
          "typeName": "TokenFilterBenchmarks",
          "methodName": "Apply",
          "parameters": {
            "Scenario": "elision-mutating"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 154.51180641991752,
            "medianNanoseconds": 154.49639976024628,
            "minNanoseconds": 153.55269837379456,
            "maxNanoseconds": 155.16837000846863,
            "standardDeviationNanoseconds": 0.3840894151846098,
            "operationsPerSecond": 6471997.339040196
          },
          "gc": {
            "bytesAllocatedPerOperation": 152,
            "gen0Collections": 152,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "TokenFilterBenchmarks.Apply|Scenario=length-mutating",
          "displayInfo": "TokenFilterBenchmarks.Apply: DefaultJob [Scenario=length-mutating]",
          "typeName": "TokenFilterBenchmarks",
          "methodName": "Apply",
          "parameters": {
            "Scenario": "length-mutating"
          },
          "statistics": {
            "sampleCount": 13,
            "meanNanoseconds": 51.37660347956877,
            "medianNanoseconds": 51.40299707651138,
            "minNanoseconds": 51.20801466703415,
            "maxNanoseconds": 51.50944012403488,
            "standardDeviationNanoseconds": 0.10712408069030205,
            "operationsPerSecond": 19464112.69475367
          },
          "gc": {
            "bytesAllocatedPerOperation": 104,
            "gen0Collections": 417,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "TokenFilterBenchmarks.Apply|Scenario=length-noop",
          "displayInfo": "TokenFilterBenchmarks.Apply: DefaultJob [Scenario=length-noop]",
          "typeName": "TokenFilterBenchmarks",
          "methodName": "Apply",
          "parameters": {
            "Scenario": "length-noop"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 47.617370639528545,
            "medianNanoseconds": 47.60520598292351,
            "minNanoseconds": 47.46851998567581,
            "maxNanoseconds": 47.79380422830582,
            "standardDeviationNanoseconds": 0.08481360765062097,
            "operationsPerSecond": 21000739.57401317
          },
          "gc": {
            "bytesAllocatedPerOperation": 104,
            "gen0Collections": 417,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "TokenFilterBenchmarks.Apply|Scenario=reverse-mutating",
          "displayInfo": "TokenFilterBenchmarks.Apply: DefaultJob [Scenario=reverse-mutating]",
          "typeName": "TokenFilterBenchmarks",
          "methodName": "Apply",
          "parameters": {
            "Scenario": "reverse-mutating"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 69.78391012975148,
            "medianNanoseconds": 69.80639290809631,
            "minNanoseconds": 69.62415504455566,
            "maxNanoseconds": 70.04579174518585,
            "standardDeviationNanoseconds": 0.11394661955115672,
            "operationsPerSecond": 14329950.81732548
          },
          "gc": {
            "bytesAllocatedPerOperation": 160,
            "gen0Collections": 320,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "TokenFilterBenchmarks.Apply|Scenario=shingle-mutating",
          "displayInfo": "TokenFilterBenchmarks.Apply: DefaultJob [Scenario=shingle-mutating]",
          "typeName": "TokenFilterBenchmarks",
          "methodName": "Apply",
          "parameters": {
            "Scenario": "shingle-mutating"
          },
          "statistics": {
            "sampleCount": 13,
            "meanNanoseconds": 256.31546889818634,
            "medianNanoseconds": 256.2268052101135,
            "minNanoseconds": 255.78462934494019,
            "maxNanoseconds": 256.9174733161926,
            "standardDeviationNanoseconds": 0.324187994598558,
            "operationsPerSecond": 3901442.251217464
          },
          "gc": {
            "bytesAllocatedPerOperation": 504,
            "gen0Collections": 252,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "TokenFilterBenchmarks.Apply|Scenario=truncate-mutating",
          "displayInfo": "TokenFilterBenchmarks.Apply: DefaultJob [Scenario=truncate-mutating]",
          "typeName": "TokenFilterBenchmarks",
          "methodName": "Apply",
          "parameters": {
            "Scenario": "truncate-mutating"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 54.53885383208593,
            "medianNanoseconds": 54.53612965345383,
            "minNanoseconds": 54.33203083276749,
            "maxNanoseconds": 54.76179111003876,
            "standardDeviationNanoseconds": 0.1355619566400577,
            "operationsPerSecond": 18335552.17494664
          },
          "gc": {
            "bytesAllocatedPerOperation": 128,
            "gen0Collections": 513,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "TokenFilterBenchmarks.Apply|Scenario=truncate-noop",
          "displayInfo": "TokenFilterBenchmarks.Apply: DefaultJob [Scenario=truncate-noop]",
          "typeName": "TokenFilterBenchmarks",
          "methodName": "Apply",
          "parameters": {
            "Scenario": "truncate-noop"
          },
          "statistics": {
            "sampleCount": 13,
            "meanNanoseconds": 44.00806801594221,
            "medianNanoseconds": 44.011815905570984,
            "minNanoseconds": 43.83614385128021,
            "maxNanoseconds": 44.117224752902985,
            "standardDeviationNanoseconds": 0.06799581835782567,
            "operationsPerSecond": 22723106.12767058
          },
          "gc": {
            "bytesAllocatedPerOperation": 104,
            "gen0Collections": 417,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "TokenFilterBenchmarks.Apply|Scenario=unique-mutating",
          "displayInfo": "TokenFilterBenchmarks.Apply: DefaultJob [Scenario=unique-mutating]",
          "typeName": "TokenFilterBenchmarks",
          "methodName": "Apply",
          "parameters": {
            "Scenario": "unique-mutating"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 152.78150444030763,
            "medianNanoseconds": 152.71781587600708,
            "minNanoseconds": 152.34994554519653,
            "maxNanoseconds": 153.47322392463684,
            "standardDeviationNanoseconds": 0.3173616070366511,
            "operationsPerSecond": 6545294.888038651
          },
          "gc": {
            "bytesAllocatedPerOperation": 296,
            "gen0Collections": 296,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "TokenFilterBenchmarks.Apply|Scenario=word-delimiter-mutating",
          "displayInfo": "TokenFilterBenchmarks.Apply: DefaultJob [Scenario=word-(...)ating [23]]",
          "typeName": "TokenFilterBenchmarks",
          "methodName": "Apply",
          "parameters": {
            "Scenario": "word-delimiter-mutating"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 446.4099799156189,
            "medianNanoseconds": 446.509313583374,
            "minNanoseconds": 445.5390658378601,
            "maxNanoseconds": 447.1637649536133,
            "standardDeviationNanoseconds": 0.49019480944563243,
            "operationsPerSecond": 2240093.288660396
          },
          "gc": {
            "bytesAllocatedPerOperation": 928,
            "gen0Collections": 465,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "analysis-parity",
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.AnalyserParityBenchmarks-20260511-115105",
      "benchmarkCount": 6,
      "benchmarks": [
        {
          "key": "AnalyserParityBenchmarks.LeanLucene_Keyword",
          "displayInfo": "AnalyserParityBenchmarks.LeanLucene_Keyword: DefaultJob",
          "typeName": "AnalyserParityBenchmarks",
          "methodName": "LeanLucene_Keyword",
          "parameters": {},
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 4016.934064737956,
            "medianNanoseconds": 4018.956230163574,
            "minNanoseconds": 3997.862464904785,
            "maxNanoseconds": 4030.459976196289,
            "standardDeviationNanoseconds": 8.245309309514672,
            "operationsPerSecond": 248946.08272969868
          },
          "gc": {
            "bytesAllocatedPerOperation": 0,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "AnalyserParityBenchmarks.LeanLucene_Simple",
          "displayInfo": "AnalyserParityBenchmarks.LeanLucene_Simple: DefaultJob",
          "typeName": "AnalyserParityBenchmarks",
          "methodName": "LeanLucene_Simple",
          "parameters": {},
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 39520.592267717635,
            "medianNanoseconds": 39506.00796508789,
            "minNanoseconds": 39402.302673339844,
            "maxNanoseconds": 39741.82598876953,
            "standardDeviationNanoseconds": 88.20527034117696,
            "operationsPerSecond": 25303.264516530264
          },
          "gc": {
            "bytesAllocatedPerOperation": 0,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "AnalyserParityBenchmarks.LeanLucene_Whitespace",
          "displayInfo": "AnalyserParityBenchmarks.LeanLucene_Whitespace: DefaultJob",
          "typeName": "AnalyserParityBenchmarks",
          "methodName": "LeanLucene_Whitespace",
          "parameters": {},
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 39832.5994480678,
            "medianNanoseconds": 39831.001861572266,
            "minNanoseconds": 39726.22479248047,
            "maxNanoseconds": 39927.947509765625,
            "standardDeviationNanoseconds": 50.93798276401993,
            "operationsPerSecond": 25105.065043614875
          },
          "gc": {
            "bytesAllocatedPerOperation": 0,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "AnalyserParityBenchmarks.LuceneNet_Keyword",
          "displayInfo": "AnalyserParityBenchmarks.LuceneNet_Keyword: DefaultJob",
          "typeName": "AnalyserParityBenchmarks",
          "methodName": "LuceneNet_Keyword",
          "parameters": {},
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 12487.420587812152,
            "medianNanoseconds": 12482.726173400879,
            "minNanoseconds": 12463.8193359375,
            "maxNanoseconds": 12514.581924438477,
            "standardDeviationNanoseconds": 18.496407797086803,
            "operationsPerSecond": 80080.5893393236
          },
          "gc": {
            "bytesAllocatedPerOperation": 3200,
            "gen0Collections": 50,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "AnalyserParityBenchmarks.LuceneNet_Simple",
          "displayInfo": "AnalyserParityBenchmarks.LuceneNet_Simple: DefaultJob",
          "typeName": "AnalyserParityBenchmarks",
          "methodName": "LuceneNet_Simple",
          "parameters": {},
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 94659.315234375,
            "medianNanoseconds": 94665.876953125,
            "minNanoseconds": 94456.7734375,
            "maxNanoseconds": 94847.57287597656,
            "standardDeviationNanoseconds": 134.70454007229333,
            "operationsPerSecond": 10564.20065499117
          },
          "gc": {
            "bytesAllocatedPerOperation": 3200,
            "gen0Collections": 6,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "AnalyserParityBenchmarks.LuceneNet_Whitespace",
          "displayInfo": "AnalyserParityBenchmarks.LuceneNet_Whitespace: DefaultJob",
          "typeName": "AnalyserParityBenchmarks",
          "methodName": "LuceneNet_Whitespace",
          "parameters": {},
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 76348.00620814732,
            "medianNanoseconds": 76344.76824951172,
            "minNanoseconds": 76152.63586425781,
            "maxNanoseconds": 76530.32458496094,
            "standardDeviationNanoseconds": 99.15504608630353,
            "operationsPerSecond": 13097.918985254222
          },
          "gc": {
            "bytesAllocatedPerOperation": 3200,
            "gen0Collections": 6,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "blockjoin",
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.BlockJoinBenchmarks-20260511-130257",
      "benchmarkCount": 4,
      "benchmarks": [
        {
          "key": "BlockJoinBenchmarks.LeanLucene_BlockJoinQuery|BlockCount=500",
          "displayInfo": "BlockJoinBenchmarks.LeanLucene_BlockJoinQuery: DefaultJob [BlockCount=500]",
          "typeName": "BlockJoinBenchmarks",
          "methodName": "LeanLucene_BlockJoinQuery",
          "parameters": {
            "BlockCount": "500"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 7151.729385375977,
            "medianNanoseconds": 7148.284332275391,
            "minNanoseconds": 7119.872222900391,
            "maxNanoseconds": 7179.967903137207,
            "standardDeviationNanoseconds": 15.646115636187227,
            "operationsPerSecond": 139826.31977725882
          },
          "gc": {
            "bytesAllocatedPerOperation": 720,
            "gen0Collections": 22,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "BlockJoinBenchmarks.LeanLucene_IndexBlocks|BlockCount=500",
          "displayInfo": "BlockJoinBenchmarks.LeanLucene_IndexBlocks: DefaultJob [BlockCount=500]",
          "typeName": "BlockJoinBenchmarks",
          "methodName": "LeanLucene_IndexBlocks",
          "parameters": {
            "BlockCount": "500"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 81989873.20408164,
            "medianNanoseconds": 82019957.78571428,
            "minNanoseconds": 81541203,
            "maxNanoseconds": 82392643.28571428,
            "standardDeviationNanoseconds": 218088.51749093426,
            "operationsPerSecond": 12.196628204447787
          },
          "gc": {
            "bytesAllocatedPerOperation": 13906016,
            "gen0Collections": 13,
            "gen1Collections": 6,
            "gen2Collections": 0
          }
        },
        {
          "key": "BlockJoinBenchmarks.LuceneNet_IndexBlocks|BlockCount=500",
          "displayInfo": "BlockJoinBenchmarks.LuceneNet_IndexBlocks: DefaultJob [BlockCount=500]",
          "typeName": "BlockJoinBenchmarks",
          "methodName": "LuceneNet_IndexBlocks",
          "parameters": {
            "BlockCount": "500"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 57079239.47407408,
            "medianNanoseconds": 56977248.44444445,
            "minNanoseconds": 56587986.777777776,
            "maxNanoseconds": 57555590,
            "standardDeviationNanoseconds": 301708.5885874823,
            "operationsPerSecond": 17.51950462574417
          },
          "gc": {
            "bytesAllocatedPerOperation": 28715836,
            "gen0Collections": 45,
            "gen1Collections": 6,
            "gen2Collections": 0
          }
        },
        {
          "key": "BlockJoinBenchmarks.LuceneNet_ToParentBlockJoinQuery|BlockCount=500",
          "displayInfo": "BlockJoinBenchmarks.LuceneNet_ToParentBlockJoinQuery: DefaultJob [BlockCount=500]",
          "typeName": "BlockJoinBenchmarks",
          "methodName": "LuceneNet_ToParentBlockJoinQuery",
          "parameters": {
            "BlockCount": "500"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 21740.85527750651,
            "medianNanoseconds": 21750.045776367188,
            "minNanoseconds": 21658.58184814453,
            "maxNanoseconds": 21815.35675048828,
            "standardDeviationNanoseconds": 43.66080406731814,
            "operationsPerSecond": 45996.35052235587
          },
          "gc": {
            "bytesAllocatedPerOperation": 12888,
            "gen0Collections": 100,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "boolean",
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.BooleanQueryBenchmarks-20260511-115846",
      "benchmarkCount": 6,
      "benchmarks": [
        {
          "key": "BooleanQueryBenchmarks.LeanLucene_BooleanQuery|BooleanType=Must, DocumentCount=100000",
          "displayInfo": "BooleanQueryBenchmarks.LeanLucene_BooleanQuery: DefaultJob [BooleanType=Must, DocumentCount=100000]",
          "typeName": "BooleanQueryBenchmarks",
          "methodName": "LeanLucene_BooleanQuery",
          "parameters": {
            "BooleanType": "Must",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 264569.8779296875,
            "medianNanoseconds": 264252.00390625,
            "minNanoseconds": 261521.1669921875,
            "maxNanoseconds": 269370.43603515625,
            "standardDeviationNanoseconds": 2248.703539421354,
            "operationsPerSecond": 3779.7197769647896
          },
          "gc": {
            "bytesAllocatedPerOperation": 13244,
            "gen0Collections": 6,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "BooleanQueryBenchmarks.LeanLucene_BooleanQuery|BooleanType=MustNot, DocumentCount=100000",
          "displayInfo": "BooleanQueryBenchmarks.LeanLucene_BooleanQuery: DefaultJob [BooleanType=MustNot, DocumentCount=100000]",
          "typeName": "BooleanQueryBenchmarks",
          "methodName": "LeanLucene_BooleanQuery",
          "parameters": {
            "BooleanType": "MustNot",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 173601.1173828125,
            "medianNanoseconds": 173568.27294921875,
            "minNanoseconds": 171166.54931640625,
            "maxNanoseconds": 175338.9560546875,
            "standardDeviationNanoseconds": 1153.5145130434064,
            "operationsPerSecond": 5760.331587007434
          },
          "gc": {
            "bytesAllocatedPerOperation": 13618,
            "gen0Collections": 13,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "BooleanQueryBenchmarks.LeanLucene_BooleanQuery|BooleanType=Should, DocumentCount=100000",
          "displayInfo": "BooleanQueryBenchmarks.LeanLucene_BooleanQuery: DefaultJob [BooleanType=Should, DocumentCount=100000]",
          "typeName": "BooleanQueryBenchmarks",
          "methodName": "LeanLucene_BooleanQuery",
          "parameters": {
            "BooleanType": "Should",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 223996.79331054687,
            "medianNanoseconds": 223746.1025390625,
            "minNanoseconds": 219815.57788085938,
            "maxNanoseconds": 229626.5458984375,
            "standardDeviationNanoseconds": 2987.6078316381713,
            "operationsPerSecond": 4464.3496240306
          },
          "gc": {
            "bytesAllocatedPerOperation": 14025,
            "gen0Collections": 14,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "BooleanQueryBenchmarks.LuceneNet_BooleanQuery|BooleanType=Must, DocumentCount=100000",
          "displayInfo": "BooleanQueryBenchmarks.LuceneNet_BooleanQuery: DefaultJob [BooleanType=Must, DocumentCount=100000]",
          "typeName": "BooleanQueryBenchmarks",
          "methodName": "LuceneNet_BooleanQuery",
          "parameters": {
            "BooleanType": "Must",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 12,
            "meanNanoseconds": 484088.5096842448,
            "medianNanoseconds": 484146.1862792969,
            "minNanoseconds": 483461.2099609375,
            "maxNanoseconds": 484386.62451171875,
            "standardDeviationNanoseconds": 298.8583424530632,
            "operationsPerSecond": 2065.737938403594
          },
          "gc": {
            "bytesAllocatedPerOperation": 147552,
            "gen0Collections": 72,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "BooleanQueryBenchmarks.LuceneNet_BooleanQuery|BooleanType=MustNot, DocumentCount=100000",
          "displayInfo": "BooleanQueryBenchmarks.LuceneNet_BooleanQuery: DefaultJob [BooleanType=MustNot, DocumentCount=100000]",
          "typeName": "BooleanQueryBenchmarks",
          "methodName": "LuceneNet_BooleanQuery",
          "parameters": {
            "BooleanType": "MustNot",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 410095.26396484376,
            "medianNanoseconds": 410006.189453125,
            "minNanoseconds": 409024.998046875,
            "maxNanoseconds": 411421.40576171875,
            "standardDeviationNanoseconds": 805.0096292690448,
            "operationsPerSecond": 2438.45781180669
          },
          "gc": {
            "bytesAllocatedPerOperation": 152640,
            "gen0Collections": 74,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "BooleanQueryBenchmarks.LuceneNet_BooleanQuery|BooleanType=Should, DocumentCount=100000",
          "displayInfo": "BooleanQueryBenchmarks.LuceneNet_BooleanQuery: DefaultJob [BooleanType=Should, DocumentCount=100000]",
          "typeName": "BooleanQueryBenchmarks",
          "methodName": "LuceneNet_BooleanQuery",
          "parameters": {
            "BooleanType": "Should",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 13,
            "meanNanoseconds": 579753.6161358173,
            "medianNanoseconds": 579654.9853515625,
            "minNanoseconds": 578558.60546875,
            "maxNanoseconds": 581467.23046875,
            "standardDeviationNanoseconds": 767.0324400444858,
            "operationsPerSecond": 1724.8706556851089
          },
          "gc": {
            "bytesAllocatedPerOperation": 711688,
            "gen0Collections": 174,
            "gen1Collections": 41,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "deletion",
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.DeletionBenchmarks-20260511-122528",
      "benchmarkCount": 2,
      "benchmarks": [
        {
          "key": "DeletionBenchmarks.LeanLucene_DeleteDocuments|DocumentCount=100000",
          "displayInfo": "DeletionBenchmarks.LeanLucene_DeleteDocuments: DefaultJob [DocumentCount=100000]",
          "typeName": "DeletionBenchmarks",
          "methodName": "LeanLucene_DeleteDocuments",
          "parameters": {
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 10946499400.4,
            "medianNanoseconds": 10950000846,
            "minNanoseconds": 10853505303,
            "maxNanoseconds": 11037912613,
            "standardDeviationNanoseconds": 55927897.1785428,
            "operationsPerSecond": 0.09135340563426685
          },
          "gc": {
            "bytesAllocatedPerOperation": 1279583560,
            "gen0Collections": 200,
            "gen1Collections": 89,
            "gen2Collections": 9
          }
        },
        {
          "key": "DeletionBenchmarks.LuceneNet_DeleteDocuments|DocumentCount=100000",
          "displayInfo": "DeletionBenchmarks.LuceneNet_DeleteDocuments: DefaultJob [DocumentCount=100000]",
          "typeName": "DeletionBenchmarks",
          "methodName": "LuceneNet_DeleteDocuments",
          "parameters": {
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 7230953932.571428,
            "medianNanoseconds": 7231338668,
            "minNanoseconds": 7191674531,
            "maxNanoseconds": 7270843478,
            "standardDeviationNanoseconds": 21431449.959614314,
            "operationsPerSecond": 0.1382943397683058
          },
          "gc": {
            "bytesAllocatedPerOperation": 2055208536,
            "gen0Collections": 339,
            "gen1Collections": 37,
            "gen2Collections": 1
          }
        }
      ]
    },
    {
      "suiteName": "fuzzy",
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.FuzzyQueryBenchmarks-20260511-121443",
      "benchmarkCount": 6,
      "benchmarks": [
        {
          "key": "FuzzyQueryBenchmarks.LeanLucene_FuzzyQuery|DocumentCount=100000, QueryTerm=goverment",
          "displayInfo": "FuzzyQueryBenchmarks.LeanLucene_FuzzyQuery: DefaultJob [QueryTerm=goverment, DocumentCount=100000]",
          "typeName": "FuzzyQueryBenchmarks",
          "methodName": "LeanLucene_FuzzyQuery",
          "parameters": {
            "QueryTerm": "goverment",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 13,
            "meanNanoseconds": 6978785.676682692,
            "medianNanoseconds": 6970482.8984375,
            "minNanoseconds": 6920301.421875,
            "maxNanoseconds": 7103157.703125,
            "standardDeviationNanoseconds": 47993.4282783503,
            "operationsPerSecond": 143.29140431137895
          },
          "gc": {
            "bytesAllocatedPerOperation": 26498,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "FuzzyQueryBenchmarks.LeanLucene_FuzzyQuery|DocumentCount=100000, QueryTerm=markts",
          "displayInfo": "FuzzyQueryBenchmarks.LeanLucene_FuzzyQuery: DefaultJob [QueryTerm=markts, DocumentCount=100000]",
          "typeName": "FuzzyQueryBenchmarks",
          "methodName": "LeanLucene_FuzzyQuery",
          "parameters": {
            "QueryTerm": "markts",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 7594638.521354167,
            "medianNanoseconds": 7578079.1171875,
            "minNanoseconds": 7502762.28125,
            "maxNanoseconds": 7693255.6484375,
            "standardDeviationNanoseconds": 56429.83286285594,
            "operationsPerSecond": 131.67183628137897
          },
          "gc": {
            "bytesAllocatedPerOperation": 48808,
            "gen0Collections": 1,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "FuzzyQueryBenchmarks.LeanLucene_FuzzyQuery|DocumentCount=100000, QueryTerm=presiden",
          "displayInfo": "FuzzyQueryBenchmarks.LeanLucene_FuzzyQuery: DefaultJob [QueryTerm=presiden, DocumentCount=100000]",
          "typeName": "FuzzyQueryBenchmarks",
          "methodName": "LeanLucene_FuzzyQuery",
          "parameters": {
            "QueryTerm": "presiden",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 8035371.34375,
            "medianNanoseconds": 8000354.921875,
            "minNanoseconds": 7953158.75,
            "maxNanoseconds": 8262527.5,
            "standardDeviationNanoseconds": 86841.8688057435,
            "operationsPerSecond": 124.4497556143203
          },
          "gc": {
            "bytesAllocatedPerOperation": 31344,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "FuzzyQueryBenchmarks.LuceneNet_FuzzyQuery|DocumentCount=100000, QueryTerm=goverment",
          "displayInfo": "FuzzyQueryBenchmarks.LuceneNet_FuzzyQuery: DefaultJob [QueryTerm=goverment, DocumentCount=100000]",
          "typeName": "FuzzyQueryBenchmarks",
          "methodName": "LuceneNet_FuzzyQuery",
          "parameters": {
            "QueryTerm": "goverment",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 8644523.479910715,
            "medianNanoseconds": 8643909.9609375,
            "minNanoseconds": 8620154.96875,
            "maxNanoseconds": 8687228.515625,
            "standardDeviationNanoseconds": 17744.268689837725,
            "operationsPerSecond": 115.68017627853428
          },
          "gc": {
            "bytesAllocatedPerOperation": 2939746,
            "gen0Collections": 38,
            "gen1Collections": 13,
            "gen2Collections": 0
          }
        },
        {
          "key": "FuzzyQueryBenchmarks.LuceneNet_FuzzyQuery|DocumentCount=100000, QueryTerm=markts",
          "displayInfo": "FuzzyQueryBenchmarks.LuceneNet_FuzzyQuery: DefaultJob [QueryTerm=markts, DocumentCount=100000]",
          "typeName": "FuzzyQueryBenchmarks",
          "methodName": "LuceneNet_FuzzyQuery",
          "parameters": {
            "QueryTerm": "markts",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 9245373.760044644,
            "medianNanoseconds": 9247418.046875,
            "minNanoseconds": 9193674.765625,
            "maxNanoseconds": 9282804.046875,
            "standardDeviationNanoseconds": 22609.42327604573,
            "operationsPerSecond": 108.16220370902249
          },
          "gc": {
            "bytesAllocatedPerOperation": 2873368,
            "gen0Collections": 40,
            "gen1Collections": 12,
            "gen2Collections": 0
          }
        },
        {
          "key": "FuzzyQueryBenchmarks.LuceneNet_FuzzyQuery|DocumentCount=100000, QueryTerm=presiden",
          "displayInfo": "FuzzyQueryBenchmarks.LuceneNet_FuzzyQuery: DefaultJob [QueryTerm=presiden, DocumentCount=100000]",
          "typeName": "FuzzyQueryBenchmarks",
          "methodName": "LuceneNet_FuzzyQuery",
          "parameters": {
            "QueryTerm": "presiden",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 8768333.685267856,
            "medianNanoseconds": 8766440.21875,
            "minNanoseconds": 8741820.96875,
            "maxNanoseconds": 8803764.1875,
            "standardDeviationNanoseconds": 17427.859189210245,
            "operationsPerSecond": 114.04675459376656
          },
          "gc": {
            "bytesAllocatedPerOperation": 2912850,
            "gen0Collections": 38,
            "gen1Collections": 14,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "gutenberg-analysis",
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.GutenbergAnalysisBenchmarks-20260511-130551",
      "benchmarkCount": 2,
      "benchmarks": [
        {
          "key": "GutenbergAnalysisBenchmarks.LeanLucene_English_Analyse",
          "displayInfo": "GutenbergAnalysisBenchmarks.LeanLucene_English_Analyse: DefaultJob",
          "typeName": "GutenbergAnalysisBenchmarks",
          "methodName": "LeanLucene_English_Analyse",
          "parameters": {},
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 391672074,
            "medianNanoseconds": 391682109,
            "minNanoseconds": 387804683,
            "maxNanoseconds": 394967827,
            "standardDeviationNanoseconds": 2367265.0086030564,
            "operationsPerSecond": 2.5531562405952895
          },
          "gc": {
            "bytesAllocatedPerOperation": 118518960,
            "gen0Collections": 11,
            "gen1Collections": 6,
            "gen2Collections": 2
          }
        },
        {
          "key": "GutenbergAnalysisBenchmarks.LeanLucene_Standard_Analyse",
          "displayInfo": "GutenbergAnalysisBenchmarks.LeanLucene_Standard_Analyse: DefaultJob",
          "typeName": "GutenbergAnalysisBenchmarks",
          "methodName": "LeanLucene_Standard_Analyse",
          "parameters": {},
          "statistics": {
            "sampleCount": 13,
            "meanNanoseconds": 124516439.69230771,
            "medianNanoseconds": 124492915,
            "minNanoseconds": 124263877.4,
            "maxNanoseconds": 124950831.4,
            "standardDeviationNanoseconds": 187331.47892693095,
            "operationsPerSecond": 8.031068045882918
          },
          "gc": {
            "bytesAllocatedPerOperation": 7585864,
            "gen0Collections": 7,
            "gen1Collections": 3,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "gutenberg-index",
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.GutenbergIndexingBenchmarks-20260511-130750",
      "benchmarkCount": 3,
      "benchmarks": [
        {
          "key": "GutenbergIndexingBenchmarks.LeanLucene_English_Index",
          "displayInfo": "GutenbergIndexingBenchmarks.LeanLucene_English_Index: DefaultJob",
          "typeName": "GutenbergIndexingBenchmarks",
          "methodName": "LeanLucene_English_Index",
          "parameters": {},
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 1028388103.9285715,
            "medianNanoseconds": 1026164810,
            "minNanoseconds": 1007272632,
            "maxNanoseconds": 1050422509,
            "standardDeviationNanoseconds": 13290001.783440474,
            "operationsPerSecond": 0.9723955345067438
          },
          "gc": {
            "bytesAllocatedPerOperation": 229529224,
            "gen0Collections": 36,
            "gen1Collections": 12,
            "gen2Collections": 2
          }
        },
        {
          "key": "GutenbergIndexingBenchmarks.LeanLucene_Standard_Index",
          "displayInfo": "GutenbergIndexingBenchmarks.LeanLucene_Standard_Index: DefaultJob",
          "typeName": "GutenbergIndexingBenchmarks",
          "methodName": "LeanLucene_Standard_Index",
          "parameters": {},
          "statistics": {
            "sampleCount": 13,
            "meanNanoseconds": 1004877883.3076923,
            "medianNanoseconds": 1004395023,
            "minNanoseconds": 993645396,
            "maxNanoseconds": 1018610602,
            "standardDeviationNanoseconds": 6606471.0170789035,
            "operationsPerSecond": 0.995145794938151
          },
          "gc": {
            "bytesAllocatedPerOperation": 129508728,
            "gen0Collections": 19,
            "gen1Collections": 10,
            "gen2Collections": 1
          }
        },
        {
          "key": "GutenbergIndexingBenchmarks.LuceneNet_Index",
          "displayInfo": "GutenbergIndexingBenchmarks.LuceneNet_Index: DefaultJob",
          "typeName": "GutenbergIndexingBenchmarks",
          "methodName": "LuceneNet_Index",
          "parameters": {},
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 650505426.2857143,
            "medianNanoseconds": 649821125.5,
            "minNanoseconds": 648028032,
            "maxNanoseconds": 655238751,
            "standardDeviationNanoseconds": 2171939.3203447126,
            "operationsPerSecond": 1.5372661927047186
          },
          "gc": {
            "bytesAllocatedPerOperation": 217772448,
            "gen0Collections": 41,
            "gen1Collections": 3,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "gutenberg-search",
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.GutenbergSearchBenchmarks-20260511-131043",
      "benchmarkCount": 15,
      "benchmarks": [
        {
          "key": "GutenbergSearchBenchmarks.LeanLucene_English_Search|SearchTerm=death",
          "displayInfo": "GutenbergSearchBenchmarks.LeanLucene_English_Search: DefaultJob [SearchTerm=death]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LeanLucene_English_Search",
          "parameters": {
            "SearchTerm": "death"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 11610.765129597981,
            "medianNanoseconds": 11609.063186645508,
            "minNanoseconds": 11577.45394897461,
            "maxNanoseconds": 11644.504684448242,
            "standardDeviationNanoseconds": 20.563498847124595,
            "operationsPerSecond": 86126.9682779833
          },
          "gc": {
            "bytesAllocatedPerOperation": 472,
            "gen0Collections": 7,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LeanLucene_English_Search|SearchTerm=love",
          "displayInfo": "GutenbergSearchBenchmarks.LeanLucene_English_Search: DefaultJob [SearchTerm=love]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LeanLucene_English_Search",
          "parameters": {
            "SearchTerm": "love"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 20028.54461669922,
            "medianNanoseconds": 20028.20620727539,
            "minNanoseconds": 19967.461181640625,
            "maxNanoseconds": 20081.411102294922,
            "standardDeviationNanoseconds": 29.111837089561536,
            "operationsPerSecond": 49928.74016248934
          },
          "gc": {
            "bytesAllocatedPerOperation": 464,
            "gen0Collections": 3,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LeanLucene_English_Search|SearchTerm=man",
          "displayInfo": "GutenbergSearchBenchmarks.LeanLucene_English_Search: DefaultJob [SearchTerm=man]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LeanLucene_English_Search",
          "parameters": {
            "SearchTerm": "man"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 39639.32342936198,
            "medianNanoseconds": 39643.35656738281,
            "minNanoseconds": 39574.960510253906,
            "maxNanoseconds": 39695.47442626953,
            "standardDeviationNanoseconds": 39.70090756444837,
            "operationsPerSecond": 25227.473969933384
          },
          "gc": {
            "bytesAllocatedPerOperation": 464,
            "gen0Collections": 1,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LeanLucene_English_Search|SearchTerm=night",
          "displayInfo": "GutenbergSearchBenchmarks.LeanLucene_English_Search: DefaultJob [SearchTerm=night]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LeanLucene_English_Search",
          "parameters": {
            "SearchTerm": "night"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 26295.763739449638,
            "medianNanoseconds": 26300.537216186523,
            "minNanoseconds": 26182.447296142578,
            "maxNanoseconds": 26379.711975097656,
            "standardDeviationNanoseconds": 49.21420604473421,
            "operationsPerSecond": 38028.93918231293
          },
          "gc": {
            "bytesAllocatedPerOperation": 472,
            "gen0Collections": 3,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LeanLucene_English_Search|SearchTerm=sea",
          "displayInfo": "GutenbergSearchBenchmarks.LeanLucene_English_Search: DefaultJob [SearchTerm=sea]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LeanLucene_English_Search",
          "parameters": {
            "SearchTerm": "sea"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 13945.973951212565,
            "medianNanoseconds": 13934.46499633789,
            "minNanoseconds": 13923.333572387695,
            "maxNanoseconds": 13985.153442382812,
            "standardDeviationNanoseconds": 23.387917183281154,
            "operationsPerSecond": 71705.28236309036
          },
          "gc": {
            "bytesAllocatedPerOperation": 464,
            "gen0Collections": 7,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LeanLucene_Standard_Search|SearchTerm=death",
          "displayInfo": "GutenbergSearchBenchmarks.LeanLucene_Standard_Search: DefaultJob [SearchTerm=death]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LeanLucene_Standard_Search",
          "parameters": {
            "SearchTerm": "death"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 11392.975388590496,
            "medianNanoseconds": 11390.760513305664,
            "minNanoseconds": 11347.674621582031,
            "maxNanoseconds": 11458.385025024414,
            "standardDeviationNanoseconds": 28.860018644005212,
            "operationsPerSecond": 87773.38367652851
          },
          "gc": {
            "bytesAllocatedPerOperation": 472,
            "gen0Collections": 7,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LeanLucene_Standard_Search|SearchTerm=love",
          "displayInfo": "GutenbergSearchBenchmarks.LeanLucene_Standard_Search: DefaultJob [SearchTerm=love]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LeanLucene_Standard_Search",
          "parameters": {
            "SearchTerm": "love"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 15488.94316973005,
            "medianNanoseconds": 15478.226211547852,
            "minNanoseconds": 15413.962188720703,
            "maxNanoseconds": 15577.442932128906,
            "standardDeviationNanoseconds": 45.348993750158606,
            "operationsPerSecond": 64562.184071686315
          },
          "gc": {
            "bytesAllocatedPerOperation": 464,
            "gen0Collections": 3,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LeanLucene_Standard_Search|SearchTerm=man",
          "displayInfo": "GutenbergSearchBenchmarks.LeanLucene_Standard_Search: DefaultJob [SearchTerm=man]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LeanLucene_Standard_Search",
          "parameters": {
            "SearchTerm": "man"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 40093.233723958336,
            "medianNanoseconds": 40107.169860839844,
            "minNanoseconds": 39931.69793701172,
            "maxNanoseconds": 40190.337646484375,
            "standardDeviationNanoseconds": 69.45734518170431,
            "operationsPerSecond": 24941.86442742418
          },
          "gc": {
            "bytesAllocatedPerOperation": 464,
            "gen0Collections": 1,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LeanLucene_Standard_Search|SearchTerm=night",
          "displayInfo": "GutenbergSearchBenchmarks.LeanLucene_Standard_Search: DefaultJob [SearchTerm=night]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LeanLucene_Standard_Search",
          "parameters": {
            "SearchTerm": "night"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 25249.6109375,
            "medianNanoseconds": 25257.84063720703,
            "minNanoseconds": 25181.152923583984,
            "maxNanoseconds": 25334.958465576172,
            "standardDeviationNanoseconds": 47.81517398617418,
            "operationsPerSecond": 39604.57063973325
          },
          "gc": {
            "bytesAllocatedPerOperation": 472,
            "gen0Collections": 3,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LeanLucene_Standard_Search|SearchTerm=sea",
          "displayInfo": "GutenbergSearchBenchmarks.LeanLucene_Standard_Search: DefaultJob [SearchTerm=sea]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LeanLucene_Standard_Search",
          "parameters": {
            "SearchTerm": "sea"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 12959.039407348633,
            "medianNanoseconds": 12962.078063964844,
            "minNanoseconds": 12901.75991821289,
            "maxNanoseconds": 12992.338241577148,
            "standardDeviationNanoseconds": 25.428471560781,
            "operationsPerSecond": 77166.21337172057
          },
          "gc": {
            "bytesAllocatedPerOperation": 464,
            "gen0Collections": 7,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LuceneNet_Search|SearchTerm=death",
          "displayInfo": "GutenbergSearchBenchmarks.LuceneNet_Search: DefaultJob [SearchTerm=death]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LuceneNet_Search",
          "parameters": {
            "SearchTerm": "death"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 23186.745823160807,
            "medianNanoseconds": 23398.912994384766,
            "minNanoseconds": 22527.024658203125,
            "maxNanoseconds": 23590.298858642578,
            "standardDeviationNanoseconds": 400.62732825347587,
            "operationsPerSecond": 43128.0873834878
          },
          "gc": {
            "bytesAllocatedPerOperation": 11231,
            "gen0Collections": 87,
            "gen1Collections": 1,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LuceneNet_Search|SearchTerm=love",
          "displayInfo": "GutenbergSearchBenchmarks.LuceneNet_Search: DefaultJob [SearchTerm=love]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LuceneNet_Search",
          "parameters": {
            "SearchTerm": "love"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 29887.637664794922,
            "medianNanoseconds": 29886.152465820312,
            "minNanoseconds": 29820.318267822266,
            "maxNanoseconds": 29970.177459716797,
            "standardDeviationNanoseconds": 41.43914393877274,
            "operationsPerSecond": 33458.64973389698
          },
          "gc": {
            "bytesAllocatedPerOperation": 11175,
            "gen0Collections": 86,
            "gen1Collections": 1,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LuceneNet_Search|SearchTerm=man",
          "displayInfo": "GutenbergSearchBenchmarks.LuceneNet_Search: DefaultJob [SearchTerm=man]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LuceneNet_Search",
          "parameters": {
            "SearchTerm": "man"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 52773.634114583336,
            "medianNanoseconds": 52702.66589355469,
            "minNanoseconds": 52511.88067626953,
            "maxNanoseconds": 53119.41979980469,
            "standardDeviationNanoseconds": 201.8120885506014,
            "operationsPerSecond": 18948.85612441957
          },
          "gc": {
            "bytesAllocatedPerOperation": 11038,
            "gen0Collections": 43,
            "gen1Collections": 1,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LuceneNet_Search|SearchTerm=night",
          "displayInfo": "GutenbergSearchBenchmarks.LuceneNet_Search: DefaultJob [SearchTerm=night]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LuceneNet_Search",
          "parameters": {
            "SearchTerm": "night"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 37735.44962565104,
            "medianNanoseconds": 37759.0341796875,
            "minNanoseconds": 37597.127502441406,
            "maxNanoseconds": 37839.309509277344,
            "standardDeviationNanoseconds": 76.55891103908662,
            "operationsPerSecond": 26500.280503355665
          },
          "gc": {
            "bytesAllocatedPerOperation": 11223,
            "gen0Collections": 43,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LuceneNet_Search|SearchTerm=sea",
          "displayInfo": "GutenbergSearchBenchmarks.LuceneNet_Search: DefaultJob [SearchTerm=sea]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LuceneNet_Search",
          "parameters": {
            "SearchTerm": "sea"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 27265.840411376954,
            "medianNanoseconds": 27261.136322021484,
            "minNanoseconds": 27163.498107910156,
            "maxNanoseconds": 27374.908477783203,
            "standardDeviationNanoseconds": 65.04535961446238,
            "operationsPerSecond": 36675.92800780641
          },
          "gc": {
            "bytesAllocatedPerOperation": 11271,
            "gen0Collections": 87,
            "gen1Collections": 1,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "index",
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.IndexingBenchmarks-20260511-113835",
      "benchmarkCount": 2,
      "benchmarks": [
        {
          "key": "IndexingBenchmarks.LeanLucene_IndexDocuments|DocumentCount=100000",
          "displayInfo": "IndexingBenchmarks.LeanLucene_IndexDocuments: DefaultJob [DocumentCount=100000]",
          "typeName": "IndexingBenchmarks",
          "methodName": "LeanLucene_IndexDocuments",
          "parameters": {
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 10958565167.866667,
            "medianNanoseconds": 10954167666,
            "minNanoseconds": 10869919762,
            "maxNanoseconds": 11037604579,
            "standardDeviationNanoseconds": 51281275.00091924,
            "operationsPerSecond": 0.09125282230672473
          },
          "gc": {
            "bytesAllocatedPerOperation": 1252511536,
            "gen0Collections": 196,
            "gen1Collections": 87,
            "gen2Collections": 6
          }
        },
        {
          "key": "IndexingBenchmarks.LuceneNet_IndexDocuments|DocumentCount=100000",
          "displayInfo": "IndexingBenchmarks.LuceneNet_IndexDocuments: DefaultJob [DocumentCount=100000]",
          "typeName": "IndexingBenchmarks",
          "methodName": "LuceneNet_IndexDocuments",
          "parameters": {
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 7125772885.357142,
            "medianNanoseconds": 7125928585.5,
            "minNanoseconds": 7102651297,
            "maxNanoseconds": 7157157115,
            "standardDeviationNanoseconds": 16932954.585346375,
            "operationsPerSecond": 0.14033565426354172
          },
          "gc": {
            "bytesAllocatedPerOperation": 2019258648,
            "gen0Collections": 332,
            "gen1Collections": 34,
            "gen2Collections": 1
          }
        }
      ]
    },
    {
      "suiteName": "indexsort-index",
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.IndexSortIndexBenchmarks-20260511-124923",
      "benchmarkCount": 2,
      "benchmarks": [
        {
          "key": "IndexSortIndexBenchmarks.LeanLucene_Index_Sorted|DocumentCount=100000",
          "displayInfo": "IndexSortIndexBenchmarks.LeanLucene_Index_Sorted: DefaultJob [DocumentCount=100000]",
          "typeName": "IndexSortIndexBenchmarks",
          "methodName": "LeanLucene_Index_Sorted",
          "parameters": {
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 12361434808.6,
            "medianNanoseconds": 12362712268,
            "minNanoseconds": 12282132229,
            "maxNanoseconds": 12415108389,
            "standardDeviationNanoseconds": 33200105.038193263,
            "operationsPerSecond": 0.08089675798025386
          },
          "gc": {
            "bytesAllocatedPerOperation": 1358620984,
            "gen0Collections": 208,
            "gen1Collections": 87,
            "gen2Collections": 6
          }
        },
        {
          "key": "IndexSortIndexBenchmarks.LeanLucene_Index_Unsorted|DocumentCount=100000",
          "displayInfo": "IndexSortIndexBenchmarks.LeanLucene_Index_Unsorted: DefaultJob [DocumentCount=100000]",
          "typeName": "IndexSortIndexBenchmarks",
          "methodName": "LeanLucene_Index_Unsorted",
          "parameters": {
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 11430628910.333334,
            "medianNanoseconds": 11431828978,
            "minNanoseconds": 11356964819,
            "maxNanoseconds": 11483111915,
            "standardDeviationNanoseconds": 38346272.086727045,
            "operationsPerSecond": 0.08748425024068414
          },
          "gc": {
            "bytesAllocatedPerOperation": 1334032064,
            "gen0Collections": 205,
            "gen1Collections": 87,
            "gen2Collections": 6
          }
        }
      ]
    },
    {
      "suiteName": "indexsort-search",
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.IndexSortSearchBenchmarks-20260511-130023",
      "benchmarkCount": 2,
      "benchmarks": [
        {
          "key": "IndexSortSearchBenchmarks.LeanLucene_SortedSearch_EarlyTermination|DocumentCount=100000",
          "displayInfo": "IndexSortSearchBenchmarks.LeanLucene_SortedSearch_EarlyTermination: DefaultJob [DocumentCount=100000]",
          "typeName": "IndexSortSearchBenchmarks",
          "methodName": "LeanLucene_SortedSearch_EarlyTermination",
          "parameters": {
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 255235.07652994792,
            "medianNanoseconds": 255138.896484375,
            "minNanoseconds": 254266.45703125,
            "maxNanoseconds": 256975.32958984375,
            "standardDeviationNanoseconds": 755.5091960779882,
            "operationsPerSecond": 3917.9567855465402
          },
          "gc": {
            "bytesAllocatedPerOperation": 120488,
            "gen0Collections": 58,
            "gen1Collections": 2,
            "gen2Collections": 0
          }
        },
        {
          "key": "IndexSortSearchBenchmarks.LeanLucene_SortedSearch_PostSort|DocumentCount=100000",
          "displayInfo": "IndexSortSearchBenchmarks.LeanLucene_SortedSearch_PostSort: DefaultJob [DocumentCount=100000]",
          "typeName": "IndexSortSearchBenchmarks",
          "methodName": "LeanLucene_SortedSearch_PostSort",
          "parameters": {
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 12,
            "meanNanoseconds": 249709.7948811849,
            "medianNanoseconds": 249707.7841796875,
            "minNanoseconds": 249108.11083984375,
            "maxNanoseconds": 250203.53173828125,
            "standardDeviationNanoseconds": 310.1292530626882,
            "operationsPerSecond": 4004.648678181858
          },
          "gc": {
            "bytesAllocatedPerOperation": 120488,
            "gen0Collections": 58,
            "gen1Collections": 2,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "phrase",
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.PhraseQueryBenchmarks-20260511-120416",
      "benchmarkCount": 6,
      "benchmarks": [
        {
          "key": "PhraseQueryBenchmarks.LeanLucene_PhraseQuery|DocumentCount=100000, PhraseType=ExactThreeWord",
          "displayInfo": "PhraseQueryBenchmarks.LeanLucene_PhraseQuery: DefaultJob [PhraseType=ExactThreeWord, DocumentCount=100000]",
          "typeName": "PhraseQueryBenchmarks",
          "methodName": "LeanLucene_PhraseQuery",
          "parameters": {
            "PhraseType": "ExactThreeWord",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 13,
            "meanNanoseconds": 437877.8270357572,
            "medianNanoseconds": 437924.0087890625,
            "minNanoseconds": 435960.68408203125,
            "maxNanoseconds": 440939.2587890625,
            "standardDeviationNanoseconds": 1594.4735097154626,
            "operationsPerSecond": 2283.742035465842
          },
          "gc": {
            "bytesAllocatedPerOperation": 61189,
            "gen0Collections": 30,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "PhraseQueryBenchmarks.LeanLucene_PhraseQuery|DocumentCount=100000, PhraseType=ExactTwoWord",
          "displayInfo": "PhraseQueryBenchmarks.LeanLucene_PhraseQuery: DefaultJob [PhraseType=ExactTwoWord, DocumentCount=100000]",
          "typeName": "PhraseQueryBenchmarks",
          "methodName": "LeanLucene_PhraseQuery",
          "parameters": {
            "PhraseType": "ExactTwoWord",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 321311.331640625,
            "medianNanoseconds": 320966.91162109375,
            "minNanoseconds": 316159.2900390625,
            "maxNanoseconds": 329905.28271484375,
            "standardDeviationNanoseconds": 3742.5118650812624,
            "operationsPerSecond": 3112.246290518205
          },
          "gc": {
            "bytesAllocatedPerOperation": 43944,
            "gen0Collections": 21,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "PhraseQueryBenchmarks.LeanLucene_PhraseQuery|DocumentCount=100000, PhraseType=SlopTwoWord",
          "displayInfo": "PhraseQueryBenchmarks.LeanLucene_PhraseQuery: DefaultJob [PhraseType=SlopTwoWord, DocumentCount=100000]",
          "typeName": "PhraseQueryBenchmarks",
          "methodName": "LeanLucene_PhraseQuery",
          "parameters": {
            "PhraseType": "SlopTwoWord",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 13,
            "meanNanoseconds": 985928.0563401442,
            "medianNanoseconds": 986485.671875,
            "minNanoseconds": 974043.365234375,
            "maxNanoseconds": 997839.119140625,
            "standardDeviationNanoseconds": 6127.552914410031,
            "operationsPerSecond": 1014.2727895502762
          },
          "gc": {
            "bytesAllocatedPerOperation": 49857,
            "gen0Collections": 6,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "PhraseQueryBenchmarks.LuceneNet_PhraseQuery|DocumentCount=100000, PhraseType=ExactThreeWord",
          "displayInfo": "PhraseQueryBenchmarks.LuceneNet_PhraseQuery: DefaultJob [PhraseType=ExactThreeWord, DocumentCount=100000]",
          "typeName": "PhraseQueryBenchmarks",
          "methodName": "LuceneNet_PhraseQuery",
          "parameters": {
            "PhraseType": "ExactThreeWord",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 343822.9452799479,
            "medianNanoseconds": 343888.27685546875,
            "minNanoseconds": 341939.81787109375,
            "maxNanoseconds": 345406.96826171875,
            "standardDeviationNanoseconds": 1073.4691699233779,
            "operationsPerSecond": 2908.4737180230336
          },
          "gc": {
            "bytesAllocatedPerOperation": 378760,
            "gen0Collections": 185,
            "gen1Collections": 1,
            "gen2Collections": 0
          }
        },
        {
          "key": "PhraseQueryBenchmarks.LuceneNet_PhraseQuery|DocumentCount=100000, PhraseType=ExactTwoWord",
          "displayInfo": "PhraseQueryBenchmarks.LuceneNet_PhraseQuery: DefaultJob [PhraseType=ExactTwoWord, DocumentCount=100000]",
          "typeName": "PhraseQueryBenchmarks",
          "methodName": "LuceneNet_PhraseQuery",
          "parameters": {
            "PhraseType": "ExactTwoWord",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 409596.01555989584,
            "medianNanoseconds": 409705.48095703125,
            "minNanoseconds": 408275.76904296875,
            "maxNanoseconds": 410862.4619140625,
            "standardDeviationNanoseconds": 650.0800233075787,
            "operationsPerSecond": 2441.429999344729
          },
          "gc": {
            "bytesAllocatedPerOperation": 304408,
            "gen0Collections": 148,
            "gen1Collections": 37,
            "gen2Collections": 0
          }
        },
        {
          "key": "PhraseQueryBenchmarks.LuceneNet_PhraseQuery|DocumentCount=100000, PhraseType=SlopTwoWord",
          "displayInfo": "PhraseQueryBenchmarks.LuceneNet_PhraseQuery: DefaultJob [PhraseType=SlopTwoWord, DocumentCount=100000]",
          "typeName": "PhraseQueryBenchmarks",
          "methodName": "LuceneNet_PhraseQuery",
          "parameters": {
            "PhraseType": "SlopTwoWord",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 1037226.0546875,
            "medianNanoseconds": 1037216.18359375,
            "minNanoseconds": 1033940.55859375,
            "maxNanoseconds": 1041785.142578125,
            "standardDeviationNanoseconds": 2096.6671055773304,
            "operationsPerSecond": 964.1099888310117
          },
          "gc": {
            "bytesAllocatedPerOperation": 159344,
            "gen0Collections": 19,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "prefix",
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.PrefixQueryBenchmarks-20260511-120928",
      "benchmarkCount": 6,
      "benchmarks": [
        {
          "key": "PrefixQueryBenchmarks.LeanLucene_PrefixQuery|DocumentCount=100000, QueryPrefix=gov",
          "displayInfo": "PrefixQueryBenchmarks.LeanLucene_PrefixQuery: DefaultJob [QueryPrefix=gov, DocumentCount=100000]",
          "typeName": "PrefixQueryBenchmarks",
          "methodName": "LeanLucene_PrefixQuery",
          "parameters": {
            "QueryPrefix": "gov",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 151946.39936174665,
            "medianNanoseconds": 152021.82580566406,
            "minNanoseconds": 150195.70068359375,
            "maxNanoseconds": 153446.1337890625,
            "standardDeviationNanoseconds": 994.0041418686847,
            "operationsPerSecond": 6581.268159038427
          },
          "gc": {
            "bytesAllocatedPerOperation": 24243,
            "gen0Collections": 24,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "PrefixQueryBenchmarks.LeanLucene_PrefixQuery|DocumentCount=100000, QueryPrefix=mark",
          "displayInfo": "PrefixQueryBenchmarks.LeanLucene_PrefixQuery: DefaultJob [QueryPrefix=mark, DocumentCount=100000]",
          "typeName": "PrefixQueryBenchmarks",
          "methodName": "LeanLucene_PrefixQuery",
          "parameters": {
            "QueryPrefix": "mark",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 243043.30250767298,
            "medianNanoseconds": 243236.13244628906,
            "minNanoseconds": 240527.22290039062,
            "maxNanoseconds": 244874.29370117188,
            "standardDeviationNanoseconds": 1124.1622121433027,
            "operationsPerSecond": 4114.493136334952
          },
          "gc": {
            "bytesAllocatedPerOperation": 35333,
            "gen0Collections": 35,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "PrefixQueryBenchmarks.LeanLucene_PrefixQuery|DocumentCount=100000, QueryPrefix=pres",
          "displayInfo": "PrefixQueryBenchmarks.LeanLucene_PrefixQuery: DefaultJob [QueryPrefix=pres, DocumentCount=100000]",
          "typeName": "PrefixQueryBenchmarks",
          "methodName": "LeanLucene_PrefixQuery",
          "parameters": {
            "QueryPrefix": "pres",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 289734.12908063614,
            "medianNanoseconds": 289883.255859375,
            "minNanoseconds": 284507.5771484375,
            "maxNanoseconds": 295131.12890625,
            "standardDeviationNanoseconds": 2900.237942686557,
            "operationsPerSecond": 3451.440129518498
          },
          "gc": {
            "bytesAllocatedPerOperation": 64501,
            "gen0Collections": 32,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "PrefixQueryBenchmarks.LuceneNet_PrefixQuery|DocumentCount=100000, QueryPrefix=gov",
          "displayInfo": "PrefixQueryBenchmarks.LuceneNet_PrefixQuery: DefaultJob [QueryPrefix=gov, DocumentCount=100000]",
          "typeName": "PrefixQueryBenchmarks",
          "methodName": "LuceneNet_PrefixQuery",
          "parameters": {
            "QueryPrefix": "gov",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 186072.07858072917,
            "medianNanoseconds": 186100.28588867188,
            "minNanoseconds": 185475.97045898438,
            "maxNanoseconds": 186624.45727539062,
            "standardDeviationNanoseconds": 351.88069917821514,
            "operationsPerSecond": 5374.261456246056
          },
          "gc": {
            "bytesAllocatedPerOperation": 112680,
            "gen0Collections": 110,
            "gen1Collections": 1,
            "gen2Collections": 0
          }
        },
        {
          "key": "PrefixQueryBenchmarks.LuceneNet_PrefixQuery|DocumentCount=100000, QueryPrefix=mark",
          "displayInfo": "PrefixQueryBenchmarks.LuceneNet_PrefixQuery: DefaultJob [QueryPrefix=mark, DocumentCount=100000]",
          "typeName": "PrefixQueryBenchmarks",
          "methodName": "LuceneNet_PrefixQuery",
          "parameters": {
            "QueryPrefix": "mark",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 285475.65680803574,
            "medianNanoseconds": 285473.97021484375,
            "minNanoseconds": 284841.4951171875,
            "maxNanoseconds": 286451.953125,
            "standardDeviationNanoseconds": 461.19007107957725,
            "operationsPerSecond": 3502.9256476058713
          },
          "gc": {
            "bytesAllocatedPerOperation": 129112,
            "gen0Collections": 63,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "PrefixQueryBenchmarks.LuceneNet_PrefixQuery|DocumentCount=100000, QueryPrefix=pres",
          "displayInfo": "PrefixQueryBenchmarks.LuceneNet_PrefixQuery: DefaultJob [QueryPrefix=pres, DocumentCount=100000]",
          "typeName": "PrefixQueryBenchmarks",
          "methodName": "LuceneNet_PrefixQuery",
          "parameters": {
            "QueryPrefix": "pres",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 356816.8024204799,
            "medianNanoseconds": 356742.6884765625,
            "minNanoseconds": 355884.390625,
            "maxNanoseconds": 357769.95751953125,
            "standardDeviationNanoseconds": 559.4189666557379,
            "operationsPerSecond": 2802.5586049100357
          },
          "gc": {
            "bytesAllocatedPerOperation": 136856,
            "gen0Collections": 66,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "query",
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.TermQueryBenchmarks-20260511-113315",
      "benchmarkCount": 6,
      "benchmarks": [
        {
          "key": "TermQueryBenchmarks.LeanLucene_TermQuery|DocumentCount=100000, QueryTerm=government",
          "displayInfo": "TermQueryBenchmarks.LeanLucene_TermQuery: DefaultJob [QueryTerm=government, DocumentCount=100000]",
          "typeName": "TermQueryBenchmarks",
          "methodName": "LeanLucene_TermQuery",
          "parameters": {
            "QueryTerm": "government",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 106001.23293457032,
            "medianNanoseconds": 105936.20629882812,
            "minNanoseconds": 105601.29260253906,
            "maxNanoseconds": 106461.26489257812,
            "standardDeviationNanoseconds": 262.1319525744494,
            "operationsPerSecond": 9433.852534689422
          },
          "gc": {
            "bytesAllocatedPerOperation": 480,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "TermQueryBenchmarks.LeanLucene_TermQuery|DocumentCount=100000, QueryTerm=people",
          "displayInfo": "TermQueryBenchmarks.LeanLucene_TermQuery: DefaultJob [QueryTerm=people, DocumentCount=100000]",
          "typeName": "TermQueryBenchmarks",
          "methodName": "LeanLucene_TermQuery",
          "parameters": {
            "QueryTerm": "people",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 150593.67825520833,
            "medianNanoseconds": 150572.30029296875,
            "minNanoseconds": 150283.72436523438,
            "maxNanoseconds": 151135.31909179688,
            "standardDeviationNanoseconds": 284.5749945437358,
            "operationsPerSecond": 6640.384985519236
          },
          "gc": {
            "bytesAllocatedPerOperation": 472,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "TermQueryBenchmarks.LeanLucene_TermQuery|DocumentCount=100000, QueryTerm=said",
          "displayInfo": "TermQueryBenchmarks.LeanLucene_TermQuery: DefaultJob [QueryTerm=said, DocumentCount=100000]",
          "typeName": "TermQueryBenchmarks",
          "methodName": "LeanLucene_TermQuery",
          "parameters": {
            "QueryTerm": "said",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 687340.3497395833,
            "medianNanoseconds": 687346.3359375,
            "minNanoseconds": 685916.751953125,
            "maxNanoseconds": 689114.4775390625,
            "standardDeviationNanoseconds": 957.1010550425198,
            "operationsPerSecond": 1454.8833054524964
          },
          "gc": {
            "bytesAllocatedPerOperation": 464,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "TermQueryBenchmarks.LuceneNet_TermQuery|DocumentCount=100000, QueryTerm=government",
          "displayInfo": "TermQueryBenchmarks.LuceneNet_TermQuery: DefaultJob [QueryTerm=government, DocumentCount=100000]",
          "typeName": "TermQueryBenchmarks",
          "methodName": "LuceneNet_TermQuery",
          "parameters": {
            "QueryTerm": "government",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 136321.69264439173,
            "medianNanoseconds": 136341.23693847656,
            "minNanoseconds": 135751.56469726562,
            "maxNanoseconds": 136782.15625,
            "standardDeviationNanoseconds": 308.7529025843745,
            "operationsPerSecond": 7335.58966736568
          },
          "gc": {
            "bytesAllocatedPerOperation": 60896,
            "gen0Collections": 59,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "TermQueryBenchmarks.LuceneNet_TermQuery|DocumentCount=100000, QueryTerm=people",
          "displayInfo": "TermQueryBenchmarks.LuceneNet_TermQuery: DefaultJob [QueryTerm=people, DocumentCount=100000]",
          "typeName": "TermQueryBenchmarks",
          "methodName": "LuceneNet_TermQuery",
          "parameters": {
            "QueryTerm": "people",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 177489.9166829427,
            "medianNanoseconds": 177481.46533203125,
            "minNanoseconds": 176790.48486328125,
            "maxNanoseconds": 178053.828125,
            "standardDeviationNanoseconds": 318.5398950262198,
            "operationsPerSecond": 5634.122876886238
          },
          "gc": {
            "bytesAllocatedPerOperation": 58688,
            "gen0Collections": 57,
            "gen1Collections": 1,
            "gen2Collections": 0
          }
        },
        {
          "key": "TermQueryBenchmarks.LuceneNet_TermQuery|DocumentCount=100000, QueryTerm=said",
          "displayInfo": "TermQueryBenchmarks.LuceneNet_TermQuery: DefaultJob [QueryTerm=said, DocumentCount=100000]",
          "typeName": "TermQueryBenchmarks",
          "methodName": "LuceneNet_TermQuery",
          "parameters": {
            "QueryTerm": "said",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 753346.7697916667,
            "medianNanoseconds": 753194.7138671875,
            "minNanoseconds": 751806.5009765625,
            "maxNanoseconds": 755421.3642578125,
            "standardDeviationNanoseconds": 1169.0287392343105,
            "operationsPerSecond": 1327.4099526258588
          },
          "gc": {
            "bytesAllocatedPerOperation": 58720,
            "gen0Collections": 14,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "schemajson",
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.SchemaAndJsonBenchmarks-20260511-123839",
      "benchmarkCount": 3,
      "benchmarks": [
        {
          "key": "SchemaAndJsonBenchmarks.LeanLucene_Index_NoSchema|DocumentCount=100000",
          "displayInfo": "SchemaAndJsonBenchmarks.LeanLucene_Index_NoSchema: DefaultJob [DocumentCount=100000]",
          "typeName": "SchemaAndJsonBenchmarks",
          "methodName": "LeanLucene_Index_NoSchema",
          "parameters": {
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 10501423292.666666,
            "medianNanoseconds": 10519699218,
            "minNanoseconds": 10406486560,
            "maxNanoseconds": 10567336754,
            "standardDeviationNanoseconds": 57917185.658851944,
            "operationsPerSecond": 0.09522518730373607
          },
          "gc": {
            "bytesAllocatedPerOperation": 1252430424,
            "gen0Collections": 193,
            "gen1Collections": 81,
            "gen2Collections": 2
          }
        },
        {
          "key": "SchemaAndJsonBenchmarks.LeanLucene_Index_WithSchema|DocumentCount=100000",
          "displayInfo": "SchemaAndJsonBenchmarks.LeanLucene_Index_WithSchema: DefaultJob [DocumentCount=100000]",
          "typeName": "SchemaAndJsonBenchmarks",
          "methodName": "LeanLucene_Index_WithSchema",
          "parameters": {
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 10540583395.2,
            "medianNanoseconds": 10555947979,
            "minNanoseconds": 10446585425,
            "maxNanoseconds": 10603285252,
            "standardDeviationNanoseconds": 45811625.54260629,
            "operationsPerSecond": 0.09487140915325262
          },
          "gc": {
            "bytesAllocatedPerOperation": 1256486800,
            "gen0Collections": 193,
            "gen1Collections": 82,
            "gen2Collections": 2
          }
        },
        {
          "key": "SchemaAndJsonBenchmarks.LeanLucene_JsonMapping|DocumentCount=100000",
          "displayInfo": "SchemaAndJsonBenchmarks.LeanLucene_JsonMapping: DefaultJob [DocumentCount=100000]",
          "typeName": "SchemaAndJsonBenchmarks",
          "methodName": "LeanLucene_JsonMapping",
          "parameters": {
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 443446771.1333333,
            "medianNanoseconds": 443586715,
            "minNanoseconds": 439315092,
            "maxNanoseconds": 448139129,
            "standardDeviationNanoseconds": 2358848.365388557,
            "operationsPerSecond": 2.2550620843269713
          },
          "gc": {
            "bytesAllocatedPerOperation": 226364856,
            "gen0Collections": 51,
            "gen1Collections": 1,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "suggester",
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.SuggesterBenchmarks-20260511-123446",
      "benchmarkCount": 3,
      "benchmarks": [
        {
          "key": "SuggesterBenchmarks.LeanLucene_DidYouMean|DocumentCount=100000",
          "displayInfo": "SuggesterBenchmarks.LeanLucene_DidYouMean: DefaultJob [DocumentCount=100000]",
          "typeName": "SuggesterBenchmarks",
          "methodName": "LeanLucene_DidYouMean",
          "parameters": {
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 4700082.345833333,
            "medianNanoseconds": 4688422.0859375,
            "minNanoseconds": 4664718.9453125,
            "maxNanoseconds": 4772246.6796875,
            "standardDeviationNanoseconds": 31236.24380689237,
            "operationsPerSecond": 212.7622297695506
          },
          "gc": {
            "bytesAllocatedPerOperation": 25512,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "SuggesterBenchmarks.LeanLucene_SpellIndex|DocumentCount=100000",
          "displayInfo": "SuggesterBenchmarks.LeanLucene_SpellIndex: DefaultJob [DocumentCount=100000]",
          "typeName": "SuggesterBenchmarks",
          "methodName": "LeanLucene_SpellIndex",
          "parameters": {
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 4696301.515625,
            "medianNanoseconds": 4697744.80078125,
            "minNanoseconds": 4677200.1015625,
            "maxNanoseconds": 4720489.9453125,
            "standardDeviationNanoseconds": 11771.373198101603,
            "operationsPerSecond": 212.93351729502754
          },
          "gc": {
            "bytesAllocatedPerOperation": 23752,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "SuggesterBenchmarks.LuceneNet_SpellChecker|DocumentCount=100000",
          "displayInfo": "SuggesterBenchmarks.LuceneNet_SpellChecker: DefaultJob [DocumentCount=100000]",
          "typeName": "SuggesterBenchmarks",
          "methodName": "LuceneNet_SpellChecker",
          "parameters": {
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 10286502.404166667,
            "medianNanoseconds": 10283477.375,
            "minNanoseconds": 10251324,
            "maxNanoseconds": 10324908.1875,
            "standardDeviationNanoseconds": 24086.46889074488,
            "operationsPerSecond": 97.21477337087273
          },
          "gc": {
            "bytesAllocatedPerOperation": 5479576,
            "gen0Collections": 83,
            "gen1Collections": 2,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "wildcard",
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.WildcardQueryBenchmarks-20260511-122004",
      "benchmarkCount": 6,
      "benchmarks": [
        {
          "key": "WildcardQueryBenchmarks.LeanLucene_WildcardQuery|DocumentCount=100000, WildcardPattern=gov*",
          "displayInfo": "WildcardQueryBenchmarks.LeanLucene_WildcardQuery: DefaultJob [WildcardPattern=gov*, DocumentCount=100000]",
          "typeName": "WildcardQueryBenchmarks",
          "methodName": "LeanLucene_WildcardQuery",
          "parameters": {
            "WildcardPattern": "gov*",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 149830.23709542412,
            "medianNanoseconds": 149683.90405273438,
            "minNanoseconds": 147632.59643554688,
            "maxNanoseconds": 152820.87377929688,
            "standardDeviationNanoseconds": 1553.4165234628094,
            "operationsPerSecond": 6674.22023341736
          },
          "gc": {
            "bytesAllocatedPerOperation": 24963,
            "gen0Collections": 25,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "WildcardQueryBenchmarks.LeanLucene_WildcardQuery|DocumentCount=100000, WildcardPattern=m*rket",
          "displayInfo": "WildcardQueryBenchmarks.LeanLucene_WildcardQuery: DefaultJob [WildcardPattern=m*rket, DocumentCount=100000]",
          "typeName": "WildcardQueryBenchmarks",
          "methodName": "LeanLucene_WildcardQuery",
          "parameters": {
            "WildcardPattern": "m*rket",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 530479.6484375,
            "medianNanoseconds": 532997.04296875,
            "minNanoseconds": 515256.5439453125,
            "maxNanoseconds": 543284.6767578125,
            "standardDeviationNanoseconds": 8254.510782744564,
            "operationsPerSecond": 1885.08645514573
          },
          "gc": {
            "bytesAllocatedPerOperation": 10420,
            "gen0Collections": 2,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "WildcardQueryBenchmarks.LeanLucene_WildcardQuery|DocumentCount=100000, WildcardPattern=pre*dent",
          "displayInfo": "WildcardQueryBenchmarks.LeanLucene_WildcardQuery: DefaultJob [WildcardPattern=pre*dent, DocumentCount=100000]",
          "typeName": "WildcardQueryBenchmarks",
          "methodName": "LeanLucene_WildcardQuery",
          "parameters": {
            "WildcardPattern": "pre*dent",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 13,
            "meanNanoseconds": 106107.36450195312,
            "medianNanoseconds": 106163.78784179688,
            "minNanoseconds": 105383.63232421875,
            "maxNanoseconds": 106909.51013183594,
            "standardDeviationNanoseconds": 448.9649637427327,
            "operationsPerSecond": 9424.41653031155
          },
          "gc": {
            "bytesAllocatedPerOperation": 8833,
            "gen0Collections": 17,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "WildcardQueryBenchmarks.LuceneNet_WildcardQuery|DocumentCount=100000, WildcardPattern=gov*",
          "displayInfo": "WildcardQueryBenchmarks.LuceneNet_WildcardQuery: DefaultJob [WildcardPattern=gov*, DocumentCount=100000]",
          "typeName": "WildcardQueryBenchmarks",
          "methodName": "LuceneNet_WildcardQuery",
          "parameters": {
            "WildcardPattern": "gov*",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 203592.39512416295,
            "medianNanoseconds": 203536.01684570312,
            "minNanoseconds": 203095.88305664062,
            "maxNanoseconds": 204032.2001953125,
            "standardDeviationNanoseconds": 255.643246695371,
            "operationsPerSecond": 4911.774820420672
          },
          "gc": {
            "bytesAllocatedPerOperation": 132160,
            "gen0Collections": 129,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "WildcardQueryBenchmarks.LuceneNet_WildcardQuery|DocumentCount=100000, WildcardPattern=m*rket",
          "displayInfo": "WildcardQueryBenchmarks.LuceneNet_WildcardQuery: DefaultJob [WildcardPattern=m*rket, DocumentCount=100000]",
          "typeName": "WildcardQueryBenchmarks",
          "methodName": "LuceneNet_WildcardQuery",
          "parameters": {
            "WildcardPattern": "m*rket",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 1172578.1557291667,
            "medianNanoseconds": 1171661.466796875,
            "minNanoseconds": 1169235.740234375,
            "maxNanoseconds": 1177489.8984375,
            "standardDeviationNanoseconds": 2263.007425107306,
            "operationsPerSecond": 852.8216179996556
          },
          "gc": {
            "bytesAllocatedPerOperation": 414224,
            "gen0Collections": 50,
            "gen1Collections": 5,
            "gen2Collections": 0
          }
        },
        {
          "key": "WildcardQueryBenchmarks.LuceneNet_WildcardQuery|DocumentCount=100000, WildcardPattern=pre*dent",
          "displayInfo": "WildcardQueryBenchmarks.LuceneNet_WildcardQuery: DefaultJob [WildcardPattern=pre*dent, DocumentCount=100000]",
          "typeName": "WildcardQueryBenchmarks",
          "methodName": "LuceneNet_WildcardQuery",
          "parameters": {
            "WildcardPattern": "pre*dent",
            "DocumentCount": "100000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 410478.7944986979,
            "medianNanoseconds": 410312.2529296875,
            "minNanoseconds": 409117.1611328125,
            "maxNanoseconds": 412156.8369140625,
            "standardDeviationNanoseconds": 902.693138436569,
            "operationsPerSecond": 2436.179440697447
          },
          "gc": {
            "bytesAllocatedPerOperation": 388608,
            "gen0Collections": 190,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    }
  ]
}</code></pre>

</details>

