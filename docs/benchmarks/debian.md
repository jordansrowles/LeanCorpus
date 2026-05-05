---
title: Benchmarks - debian
---

# Benchmarks: debian

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `864b010` &nbsp;&middot;&nbsp; 5 May 2026 18:40 UTC &nbsp;&middot;&nbsp; 92 benchmarks

## Analysis

| Method             | DocumentCount | Mean    | Error    | StdDev   | Ratio | Gen0        | Gen1      | Allocated | Alloc Ratio |
|------------------- |-------------- |--------:|---------:|---------:|------:|------------:|----------:|----------:|------------:|
| LeanLucene_Analyse | 100000        | 1.523 s | 0.0026 s | 0.0024 s |  1.00 |  49000.0000 | 2000.0000 | 199.73 MB |        1.00 |
| LuceneNet_Analyse  | 100000        | 2.284 s | 0.0022 s | 0.0021 s |  1.50 | 144000.0000 |         - | 576.92 MB |        2.89 |

## analysis-filters

| Method | Scenario             | Mean      | Error    | StdDev   | Gen0   | Allocated |
|------- |--------------------- |----------:|---------:|---------:|-------:|----------:|
| **Apply**  | **decim(...)ating [22]** |  **90.87 ns** | **0.191 ns** | **0.169 ns** | **0.0286** |     **120 B** |
| **Apply**  | **elision-mutating**     | **168.02 ns** | **0.370 ns** | **0.346 ns** | **0.0362** |     **152 B** |
| **Apply**  | **length-mutating**      |  **54.19 ns** | **0.103 ns** | **0.096 ns** | **0.0249** |     **104 B** |
| **Apply**  | **length-noop**          |  **45.34 ns** | **0.157 ns** | **0.147 ns** | **0.0249** |     **104 B** |
| **Apply**  | **reverse-mutating**     |  **69.54 ns** | **0.174 ns** | **0.163 ns** | **0.0381** |     **160 B** |
| **Apply**  | **shingle-mutating**     | **257.21 ns** | **0.695 ns** | **0.651 ns** | **0.1202** |     **504 B** |
| **Apply**  | **truncate-mutating**    |  **54.13 ns** | **0.096 ns** | **0.085 ns** | **0.0306** |     **128 B** |
| **Apply**  | **truncate-noop**        |  **44.07 ns** | **0.298 ns** | **0.279 ns** | **0.0249** |     **104 B** |
| **Apply**  | **unique-mutating**      | **151.95 ns** | **0.302 ns** | **0.268 ns** | **0.0706** |     **296 B** |
| **Apply**  | **word-(...)ating [23]** | **442.88 ns** | **0.601 ns** | **0.562 ns** | **0.2217** |     **928 B** |

## analysis-parity

| Method                | Mean      | Error     | StdDev    | Ratio | Gen0   | Allocated | Alloc Ratio |
|---------------------- |----------:|----------:|----------:|------:|-------:|----------:|------------:|
| LeanLucene_Whitespace | 39.373 μs | 0.0714 μs | 0.0633 μs |  1.00 |      - |         - |          NA |
| LuceneNet_Whitespace  | 76.379 μs | 0.2772 μs | 0.2457 μs |  1.94 | 0.7324 |    3200 B |          NA |
| LeanLucene_Keyword    |  4.018 μs | 0.0074 μs | 0.0070 μs |  0.10 |      - |         - |          NA |
| LuceneNet_Keyword     | 12.188 μs | 0.0180 μs | 0.0159 μs |  0.31 | 0.7629 |    3200 B |          NA |
| LeanLucene_Simple     | 39.444 μs | 0.0903 μs | 0.0845 μs |  1.00 |      - |         - |          NA |
| LuceneNet_Simple      | 85.760 μs | 0.1244 μs | 0.1103 μs |  2.18 | 0.7324 |    3200 B |          NA |

## Block-Join

| Method                           | BlockCount | Mean          | Error       | StdDev      | Ratio | Gen0      | Gen1     | Allocated  | Alloc Ratio |
|--------------------------------- |----------- |--------------:|------------:|------------:|------:|----------:|---------:|-----------:|------------:|
| LeanLucene_IndexBlocks           | 500        | 72,149.252 μs | 507.6034 μs | 474.8125 μs | 1.000 | 1428.5714 | 571.4286 | 11040176 B |       1.000 |
| LeanLucene_BlockJoinQuery        | 500        |      7.342 μs |   0.0101 μs |   0.0089 μs | 0.000 |    0.1678 |        - |      720 B |       0.000 |
| LuceneNet_IndexBlocks            | 500        | 56,080.128 μs | 351.4232 μs | 328.7214 μs | 0.777 | 5000.0000 | 666.6667 | 28715319 B |       2.601 |
| LuceneNet_ToParentBlockJoinQuery | 500        |     21.568 μs |   0.0667 μs |   0.0591 μs | 0.000 |    3.0518 |        - |    12888 B |       0.001 |

## Boolean queries

| Method                  | BooleanType | DocumentCount | Mean     | Error   | StdDev  | Ratio | RatioSD | Gen0     | Gen1    | Allocated | Alloc Ratio |
|------------------------ |------------ |-------------- |---------:|--------:|--------:|------:|--------:|---------:|--------:|----------:|------------:|
| **LeanLucene_BooleanQuery** | **Must**        | **100000**        | **263.9 μs** | **2.38 μs** | **2.22 μs** |  **1.00** |    **0.00** |   **2.9297** |       **-** |  **12.94 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Must        | 100000        | 487.6 μs | 0.78 μs | 0.73 μs |  1.85 |    0.02 |  35.1563 |       - | 144.09 KB |       11.14 |
|                         |             |               |          |         |         |       |         |          |         |           |             |
| **LeanLucene_BooleanQuery** | **MustNot**     | **100000**        | **172.8 μs** | **1.04 μs** | **0.92 μs** |  **1.00** |    **0.00** |   **3.1738** |       **-** |   **13.3 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | MustNot     | 100000        | 381.7 μs | 1.38 μs | 1.29 μs |  2.21 |    0.01 |  36.1328 |       - | 149.06 KB |       11.21 |
|                         |             |               |          |         |         |       |         |          |         |           |             |
| **LeanLucene_BooleanQuery** | **Should**      | **100000**        | **221.8 μs** | **2.30 μs** | **2.15 μs** |  **1.00** |    **0.00** |   **3.1738** |       **-** |  **13.69 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Should      | 100000        | 581.0 μs | 1.72 μs | 1.61 μs |  2.62 |    0.03 | 169.9219 | 40.0391 | 695.01 KB |       50.76 |

## Deletion

| Method                     | DocumentCount | Mean     | Error    | StdDev   | Ratio | Gen0        | Gen1       | Gen2      | Allocated  | Alloc Ratio |
|--------------------------- |-------------- |---------:|---------:|---------:|------:|------------:|-----------:|----------:|-----------:|------------:|
| LeanLucene_DeleteDocuments | 100000        | 10.065 s | 0.0692 s | 0.0648 s |  1.00 | 153000.0000 | 75000.0000 | 5000.0000 |  962.68 MB |        1.00 |
| LuceneNet_DeleteDocuments  | 100000        |  7.180 s | 0.0204 s | 0.0191 s |  0.71 | 338000.0000 | 34000.0000 | 1000.0000 | 1959.98 MB |        2.04 |

## Fuzzy queries

| Method                | QueryTerm | DocumentCount | Mean     | Error     | StdDev    | Ratio | Gen0     | Gen1     | Allocated  | Alloc Ratio |
|---------------------- |---------- |-------------- |---------:|----------:|----------:|------:|---------:|---------:|-----------:|------------:|
| **LeanLucene_FuzzyQuery** | **goverment** | **100000**        | **6.873 ms** | **0.0437 ms** | **0.0365 ms** |  **1.00** |        **-** |        **-** |   **25.88 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | goverment | 100000        | 8.703 ms | 0.0213 ms | 0.0189 ms |  1.27 | 593.7500 | 203.1250 | 2870.85 KB |      110.93 |
|                       |           |               |          |           |           |       |          |          |            |             |
| **LeanLucene_FuzzyQuery** | **markts**    | **100000**        | **7.516 ms** | **0.0412 ms** | **0.0365 ms** |  **1.00** |   **7.8125** |        **-** |   **47.66 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | markts    | 100000        | 9.271 ms | 0.0216 ms | 0.0181 ms |  1.23 | 625.0000 | 187.5000 | 2806.02 KB |       58.87 |
|                       |           |               |          |           |           |       |          |          |            |             |
| **LeanLucene_FuzzyQuery** | **presiden**  | **100000**        | **7.989 ms** | **0.0725 ms** | **0.0643 ms** |  **1.00** |        **-** |        **-** |   **30.61 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | presiden  | 100000        | 8.729 ms | 0.0233 ms | 0.0206 ms |  1.09 | 593.7500 | 218.7500 | 2844.58 KB |       92.93 |

## gutenberg-analysis

| Method                      | Mean     | Error   | StdDev  | Ratio | RatioSD | Gen0       | Gen1      | Gen2      | Allocated | Alloc Ratio |
|---------------------------- |---------:|--------:|--------:|------:|--------:|-----------:|----------:|----------:|----------:|------------:|
| LeanLucene_Standard_Analyse | 123.2 ms | 0.51 ms | 0.48 ms |  1.00 |    0.00 |  1400.0000 |  600.0000 |         - |   7.23 MB |        1.00 |
| LeanLucene_English_Analyse  | 430.6 ms | 2.58 ms | 2.41 ms |  3.50 |    0.02 | 11000.0000 | 6000.0000 | 2000.0000 | 113.03 MB |       15.62 |

## gutenberg-index

| Method                    | Mean     | Error   | StdDev  | Ratio | Gen0       | Gen1       | Gen2      | Allocated | Alloc Ratio |
|-------------------------- |---------:|--------:|--------:|------:|-----------:|-----------:|----------:|----------:|------------:|
| LeanLucene_Standard_Index | 819.7 ms | 8.47 ms | 7.51 ms |  1.00 | 12000.0000 |  6000.0000 | 1000.0000 |  79.53 MB |        1.00 |
| LeanLucene_English_Index  | 846.7 ms | 8.96 ms | 8.38 ms |  1.03 | 28000.0000 | 10000.0000 | 1000.0000 | 173.92 MB |        2.19 |
| LuceneNet_Index           | 654.7 ms | 3.22 ms | 2.85 ms |  0.80 | 41000.0000 |  3000.0000 |         - | 207.68 MB |        2.61 |

## gutenberg-search

| Method                     | SearchTerm | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------------------- |----------- |---------:|---------:|---------:|------:|--------:|-------:|-------:|----------:|------------:|
| **LeanLucene_Standard_Search** | **death**      | **11.47 μs** | **0.028 μs** | **0.025 μs** |  **1.00** |    **0.00** | **0.1068** |      **-** |     **472 B** |        **1.00** |
| LeanLucene_English_Search  | death      | 11.75 μs | 0.035 μs | 0.033 μs |  1.02 |    0.00 | 0.1068 |      - |     472 B |        1.00 |
| LuceneNet_Search           | death      | 23.24 μs | 0.452 μs | 0.423 μs |  2.03 |    0.04 | 2.6550 | 0.0305 |   11231 B |       23.79 |
|                            |            |          |          |          |       |         |        |        |           |             |
| **LeanLucene_Standard_Search** | **love**       | **15.26 μs** | **0.023 μs** | **0.022 μs** |  **1.00** |    **0.00** | **0.1068** |      **-** |     **464 B** |        **1.00** |
| LeanLucene_English_Search  | love       | 20.29 μs | 0.028 μs | 0.026 μs |  1.33 |    0.00 | 0.0916 |      - |     464 B |        1.00 |
| LuceneNet_Search           | love       | 31.13 μs | 0.135 μs | 0.126 μs |  2.04 |    0.01 | 2.6245 | 0.0305 |   11175 B |       24.08 |
|                            |            |          |          |          |       |         |        |        |           |             |
| **LeanLucene_Standard_Search** | **man**        | **39.83 μs** | **0.055 μs** | **0.052 μs** |  **1.00** |    **0.00** | **0.0610** |      **-** |     **464 B** |        **1.00** |
| LeanLucene_English_Search  | man        | 39.44 μs | 0.107 μs | 0.100 μs |  0.99 |    0.00 | 0.0610 |      - |     464 B |        1.00 |
| LuceneNet_Search           | man        | 50.76 μs | 0.228 μs | 0.213 μs |  1.27 |    0.01 | 2.6245 | 0.0610 |   11038 B |       23.79 |
|                            |            |          |          |          |       |         |        |        |           |             |
| **LeanLucene_Standard_Search** | **night**      | **26.56 μs** | **0.048 μs** | **0.045 μs** |  **1.00** |    **0.00** | **0.0916** |      **-** |     **472 B** |        **1.00** |
| LeanLucene_English_Search  | night      | 27.08 μs | 0.051 μs | 0.048 μs |  1.02 |    0.00 | 0.0916 |      - |     472 B |        1.00 |
| LuceneNet_Search           | night      | 36.96 μs | 0.080 μs | 0.071 μs |  1.39 |    0.00 | 2.6245 | 0.0610 |   11223 B |       23.78 |
|                            |            |          |          |          |       |         |        |        |           |             |
| **LeanLucene_Standard_Search** | **sea**        | **13.07 μs** | **0.027 μs** | **0.025 μs** |  **1.00** |    **0.00** | **0.1068** |      **-** |     **464 B** |        **1.00** |
| LeanLucene_English_Search  | sea        | 13.83 μs | 0.020 μs | 0.019 μs |  1.06 |    0.00 | 0.1068 |      - |     464 B |        1.00 |
| LuceneNet_Search           | sea        | 27.52 μs | 0.051 μs | 0.045 μs |  2.11 |    0.01 | 2.6550 | 0.0305 |   11271 B |       24.29 |

## Indexing

| Method                    | DocumentCount | Mean    | Error    | StdDev   | Ratio | Gen0        | Gen1       | Gen2      | Allocated | Alloc Ratio |
|-------------------------- |-------------- |--------:|---------:|---------:|------:|------------:|-----------:|----------:|----------:|------------:|
| LeanLucene_IndexDocuments | 100000        | 9.923 s | 0.0679 s | 0.0635 s |  1.00 | 154000.0000 | 74000.0000 | 6000.0000 | 936.93 MB |        1.00 |
| LuceneNet_IndexDocuments  | 100000        | 7.177 s | 0.0273 s | 0.0242 s |  0.72 | 331000.0000 | 30000.0000 | 1000.0000 | 1925.7 MB |        2.06 |

## Index-sort (index)

| Method                    | DocumentCount | Mean    | Error   | StdDev  | Ratio | Gen0        | Gen1       | Gen2      | Allocated | Alloc Ratio |
|-------------------------- |-------------- |--------:|--------:|--------:|------:|------------:|-----------:|----------:|----------:|------------:|
| LeanLucene_Index_Unsorted | 100000        | 10.23 s | 0.033 s | 0.029 s |  1.00 | 160000.0000 | 77000.0000 | 8000.0000 | 967.79 MB |        1.00 |
| LeanLucene_Index_Sorted   | 100000        | 10.86 s | 0.039 s | 0.036 s |  1.06 | 159000.0000 | 72000.0000 | 5000.0000 | 978.26 MB |        1.01 |

## Index-sort (search)

| Method                                   | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|----------------------------------------- |-------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| LeanLucene_SortedSearch_EarlyTermination | 100000        | 245.6 μs | 0.65 μs | 0.54 μs |  1.00 | 28.3203 | 0.9766 | 117.66 KB |        1.00 |
| LeanLucene_SortedSearch_PostSort         | 100000        | 243.4 μs | 0.52 μs | 0.48 μs |  0.99 | 28.5645 | 0.4883 | 117.66 KB |        1.00 |

## Phrase queries

| Method                 | PhraseType     | DocumentCount | Mean       | Error   | StdDev  | Ratio | RatioSD | Gen0    | Gen1    | Allocated | Alloc Ratio |
|----------------------- |--------------- |-------------- |-----------:|--------:|--------:|------:|--------:|--------:|--------:|----------:|------------:|
| **LeanLucene_PhraseQuery** | **ExactThreeWord** | **100000**        |   **437.0 μs** | **3.86 μs** | **3.61 μs** |  **1.00** |    **0.00** | **14.6484** |       **-** |  **59.77 KB** |        **1.00** |
| LuceneNet_PhraseQuery  | ExactThreeWord | 100000        |   345.2 μs | 1.09 μs | 1.02 μs |  0.79 |    0.01 | 90.3320 |  0.4883 | 369.88 KB |        6.19 |
|                        |                |               |            |         |         |       |         |         |         |           |             |
| **LeanLucene_PhraseQuery** | **ExactTwoWord**   | **100000**        |   **342.8 μs** | **4.84 μs** | **4.53 μs** |  **1.00** |    **0.00** | **10.2539** |       **-** |  **42.92 KB** |        **1.00** |
| LuceneNet_PhraseQuery  | ExactTwoWord   | 100000        |   404.0 μs | 1.01 μs | 0.89 μs |  1.18 |    0.02 | 72.2656 | 18.0664 | 297.27 KB |        6.93 |
|                        |                |               |            |         |         |       |         |         |         |           |             |
| **LeanLucene_PhraseQuery** | **SlopTwoWord**    | **100000**        |   **986.3 μs** | **8.31 μs** | **7.37 μs** |  **1.00** |    **0.00** | **11.7188** |       **-** |  **48.69 KB** |        **1.00** |
| LuceneNet_PhraseQuery  | SlopTwoWord    | 100000        | 1,030.4 μs | 1.99 μs | 1.76 μs |  1.04 |    0.01 | 37.1094 |       - | 155.61 KB |        3.20 |

## Prefix queries

| Method                 | QueryPrefix | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|----------------------- |------------ |-------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| **LeanLucene_PrefixQuery** | **gov**         | **100000**        | **149.8 μs** | **1.03 μs** | **0.86 μs** |  **1.00** |  **5.8594** |      **-** |  **23.67 KB** |        **1.00** |
| LuceneNet_PrefixQuery  | gov         | 100000        | 187.1 μs | 0.41 μs | 0.39 μs |  1.25 | 26.8555 | 0.2441 | 110.04 KB |        4.65 |
|                        |             |               |          |         |         |       |         |        |           |             |
| **LeanLucene_PrefixQuery** | **mark**        | **100000**        | **243.6 μs** | **2.49 μs** | **2.33 μs** |  **1.00** |  **8.3008** |      **-** |  **34.51 KB** |        **1.00** |
| LuceneNet_PrefixQuery  | mark        | 100000        | 281.6 μs | 0.68 μs | 0.60 μs |  1.16 | 30.7617 |      - | 126.09 KB |        3.65 |
|                        |             |               |          |         |         |       |         |        |           |             |
| **LeanLucene_PrefixQuery** | **pres**        | **100000**        | **289.3 μs** | **2.37 μs** | **2.10 μs** |  **1.00** | **15.6250** | **0.4883** |  **62.99 KB** |        **1.00** |
| LuceneNet_PrefixQuery  | pres        | 100000        | 354.0 μs | 0.68 μs | 0.60 μs |  1.22 | 32.2266 |      - | 133.65 KB |        2.12 |

## Term queries

| Method               | QueryTerm  | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|--------------------- |----------- |-------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| **LeanLucene_TermQuery** | **government** | **100000**        | **106.2 μs** | **0.15 μs** | **0.13 μs** |  **1.00** |       **-** |      **-** |     **480 B** |        **1.00** |
| LuceneNet_TermQuery  | government | 100000        | 137.3 μs | 0.31 μs | 0.27 μs |  1.29 | 14.4043 |      - |   60896 B |      126.87 |
|                      |            |               |          |         |         |       |         |        |           |             |
| **LeanLucene_TermQuery** | **people**     | **100000**        | **150.7 μs** | **0.36 μs** | **0.33 μs** |  **1.00** |       **-** |      **-** |     **472 B** |        **1.00** |
| LuceneNet_TermQuery  | people     | 100000        | 177.0 μs | 0.36 μs | 0.32 μs |  1.17 | 13.9160 | 0.2441 |   58688 B |      124.34 |
|                      |            |               |          |         |         |       |         |        |           |             |
| **LeanLucene_TermQuery** | **said**       | **100000**        | **674.5 μs** | **0.96 μs** | **0.75 μs** |  **1.00** |       **-** |      **-** |     **464 B** |        **1.00** |
| LuceneNet_TermQuery  | said       | 100000        | 752.3 μs | 1.44 μs | 1.28 μs |  1.12 | 13.6719 |      - |   58720 B |      126.55 |

## Schema and JSON

| Method                      | DocumentCount | Mean       | Error    | StdDev   | Ratio | Gen0        | Gen1       | Gen2      | Allocated | Alloc Ratio |
|---------------------------- |-------------- |-----------:|---------:|---------:|------:|------------:|-----------:|----------:|----------:|------------:|
| LeanLucene_Index_NoSchema   | 100000        | 9,641.6 ms | 46.39 ms | 41.12 ms |  1.00 | 150000.0000 | 70000.0000 | 2000.0000 | 936.86 MB |        1.00 |
| LeanLucene_Index_WithSchema | 100000        | 9,948.5 ms | 29.15 ms | 27.26 ms |  1.03 | 151000.0000 | 70000.0000 | 2000.0000 | 940.69 MB |        1.00 |
| LeanLucene_JsonMapping      | 100000        |   426.8 ms |  2.08 ms |  1.94 ms |  0.04 |  51000.0000 |  1000.0000 |         - | 215.88 MB |        0.23 |

## Suggester

| Method                 | DocumentCount | Mean      | Error     | StdDev    | Ratio | Gen0      | Gen1    | Allocated  | Alloc Ratio |
|----------------------- |-------------- |----------:|----------:|----------:|------:|----------:|--------:|-----------:|------------:|
| LeanLucene_DidYouMean  | 100000        |  4.599 ms | 0.0241 ms | 0.0213 ms |  1.00 |         - |       - |   24.91 KB |        1.00 |
| LeanLucene_SpellIndex  | 100000        |  4.755 ms | 0.0306 ms | 0.0286 ms |  1.03 |         - |       - |    23.2 KB |        0.93 |
| LuceneNet_SpellChecker | 100000        | 10.229 ms | 0.0129 ms | 0.0108 ms |  2.22 | 1296.8750 | 31.2500 | 5351.15 KB |      214.78 |

## Wildcard queries

| Method                   | WildcardPattern | DocumentCount | Mean       | Error   | StdDev  | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------- |---------------- |-------------- |-----------:|--------:|--------:|------:|--------:|--------:|-------:|----------:|------------:|
| **LeanLucene_WildcardQuery** | **gov***            | **100000**        |   **150.1 μs** | **1.36 μs** | **1.27 μs** |  **1.00** |    **0.00** |  **5.8594** |      **-** |  **24.37 KB** |        **1.00** |
| LuceneNet_WildcardQuery  | gov*            | 100000        |   206.0 μs | 0.36 μs | 0.30 μs |  1.37 |    0.01 | 31.4941 |      - | 129.06 KB |        5.30 |
|                          |                 |               |            |         |         |       |         |         |        |           |             |
| **LeanLucene_WildcardQuery** | **m*rket**          | **100000**        |   **527.5 μs** | **5.58 μs** | **5.22 μs** |  **1.00** |    **0.00** |  **1.9531** |      **-** |  **10.17 KB** |        **1.00** |
| LuceneNet_WildcardQuery  | m*rket          | 100000        | 1,170.6 μs | 2.53 μs | 2.37 μs |  2.22 |    0.02 | 97.6563 | 9.7656 | 404.52 KB |       39.77 |
|                          |                 |               |            |         |         |       |         |         |        |           |             |
| **LeanLucene_WildcardQuery** | **pre*dent**        | **100000**        |   **106.3 μs** | **0.76 μs** | **0.67 μs** |  **1.00** |    **0.00** |  **2.0752** |      **-** |   **8.62 KB** |        **1.00** |
| LuceneNet_WildcardQuery  | pre*dent        | 100000        |   410.2 μs | 0.59 μs | 0.46 μs |  3.86 |    0.02 | 92.7734 |      - |  379.5 KB |       44.01 |

<details>
<summary>Full data (report.json)</summary>

<pre><code class="lang-json">{
  "schemaVersion": 2,
  "runId": "2026-05-05 18-40 (864b010)",
  "runType": "full",
  "generatedAtUtc": "2026-05-05T18:40:52.0531636\u002B00:00",
  "commandLineArgs": [],
  "hostMachineName": "debian",
  "commitHash": "864b010",
  "dotnetVersion": "10.0.3",
  "provenance": {
    "sourceCommit": "864b010",
    "sourceRef": "",
    "sourceManifestPath": "",
    "gitCommitHash": "864b010",
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
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.AnalysisBenchmarks-20260505-195550",
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
            "meanNanoseconds": 1522966069.2,
            "medianNanoseconds": 1522213806,
            "minNanoseconds": 1518792046,
            "maxNanoseconds": 1528289005,
            "standardDeviationNanoseconds": 2420748.7517295494,
            "operationsPerSecond": 0.6566134467626654
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
            "sampleCount": 15,
            "meanNanoseconds": 2283866281.5333333,
            "medianNanoseconds": 2284465947,
            "minNanoseconds": 2279711697,
            "maxNanoseconds": 2286757329,
            "standardDeviationNanoseconds": 2050306.8070032443,
            "operationsPerSecond": 0.4378540057645686
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
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.TokenFilterBenchmarks-20260505-200157",
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
            "sampleCount": 14,
            "meanNanoseconds": 90.87445448977607,
            "medianNanoseconds": 90.898029088974,
            "minNanoseconds": 90.50663888454437,
            "maxNanoseconds": 91.10435688495636,
            "standardDeviationNanoseconds": 0.16930323439780967,
            "operationsPerSecond": 11004192.604121834
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
            "sampleCount": 15,
            "meanNanoseconds": 168.02430001894632,
            "medianNanoseconds": 168.08764243125916,
            "minNanoseconds": 167.36835074424744,
            "maxNanoseconds": 168.4711148738861,
            "standardDeviationNanoseconds": 0.34594526875518683,
            "operationsPerSecond": 5951520.106837169
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
            "sampleCount": 15,
            "meanNanoseconds": 54.19001210133235,
            "medianNanoseconds": 54.17037224769592,
            "minNanoseconds": 54.047561287879944,
            "maxNanoseconds": 54.38444936275482,
            "standardDeviationNanoseconds": 0.09606495945210305,
            "operationsPerSecond": 18453585.102178145
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
            "sampleCount": 15,
            "meanNanoseconds": 45.3417556365331,
            "medianNanoseconds": 45.35531222820282,
            "minNanoseconds": 44.86424392461777,
            "maxNanoseconds": 45.51753920316696,
            "standardDeviationNanoseconds": 0.1469885582554578,
            "operationsPerSecond": 22054726.06786916
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
            "sampleCount": 15,
            "meanNanoseconds": 69.5367445786794,
            "medianNanoseconds": 69.54234087467194,
            "minNanoseconds": 69.24788403511047,
            "maxNanoseconds": 69.7982040643692,
            "standardDeviationNanoseconds": 0.16251848605676733,
            "operationsPerSecond": 14380886.048936624
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
            "sampleCount": 15,
            "meanNanoseconds": 257.2102154413859,
            "medianNanoseconds": 257.22217082977295,
            "minNanoseconds": 256.13201570510864,
            "maxNanoseconds": 258.24946641921997,
            "standardDeviationNanoseconds": 0.6505454606290159,
            "operationsPerSecond": 3887870.465346599
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
            "sampleCount": 14,
            "meanNanoseconds": 54.1340714778219,
            "medianNanoseconds": 54.142168909311295,
            "minNanoseconds": 53.976141691207886,
            "maxNanoseconds": 54.27146351337433,
            "standardDeviationNanoseconds": 0.0850079122686167,
            "operationsPerSecond": 18472654.51684506
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
            "sampleCount": 15,
            "meanNanoseconds": 44.06730084816615,
            "medianNanoseconds": 44.141855120658875,
            "minNanoseconds": 43.339115381240845,
            "maxNanoseconds": 44.34886163473129,
            "standardDeviationNanoseconds": 0.27888114741291425,
            "operationsPerSecond": 22692562.983276404
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
            "sampleCount": 14,
            "meanNanoseconds": 151.9533884014402,
            "medianNanoseconds": 151.98487091064453,
            "minNanoseconds": 151.43873715400696,
            "maxNanoseconds": 152.42692589759827,
            "standardDeviationNanoseconds": 0.26808998323784167,
            "operationsPerSecond": 6580965.456052456
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
            "meanNanoseconds": 442.87636585235595,
            "medianNanoseconds": 442.58363580703735,
            "minNanoseconds": 442.0701251029968,
            "maxNanoseconds": 443.9468870162964,
            "standardDeviationNanoseconds": 0.5622665903836627,
            "operationsPerSecond": 2257966.505111215
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
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.AnalyserParityBenchmarks-20260505-195903",
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
            "meanNanoseconds": 4018.255051167806,
            "medianNanoseconds": 4019.6898345947266,
            "minNanoseconds": 4008.0536727905273,
            "maxNanoseconds": 4027.4674224853516,
            "standardDeviationNanoseconds": 6.952865753878959,
            "operationsPerSecond": 248864.2426292415
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
            "sampleCount": 15,
            "meanNanoseconds": 39444.453080240884,
            "medianNanoseconds": 39425.096923828125,
            "minNanoseconds": 39304.139587402344,
            "maxNanoseconds": 39644.32940673828,
            "standardDeviationNanoseconds": 84.4754446341539,
            "operationsPerSecond": 25352.107125575414
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
            "meanNanoseconds": 39372.651763916016,
            "medianNanoseconds": 39379.37927246094,
            "minNanoseconds": 39249.01916503906,
            "maxNanoseconds": 39490.203552246094,
            "standardDeviationNanoseconds": 63.313611976740816,
            "operationsPerSecond": 25398.34009647461
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
            "meanNanoseconds": 12188.169916425433,
            "medianNanoseconds": 12190.514839172363,
            "minNanoseconds": 12147.40266418457,
            "maxNanoseconds": 12206.36296081543,
            "standardDeviationNanoseconds": 15.926956258466081,
            "operationsPerSecond": 82046.77214520502
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
            "sampleCount": 14,
            "meanNanoseconds": 85759.81583077567,
            "medianNanoseconds": 85744.59466552734,
            "minNanoseconds": 85620.7392578125,
            "maxNanoseconds": 85975.54553222656,
            "standardDeviationNanoseconds": 110.31361904026025,
            "operationsPerSecond": 11660.472802008295
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
            "meanNanoseconds": 76379.17039271763,
            "medianNanoseconds": 76275.78771972656,
            "minNanoseconds": 76162.57788085938,
            "maxNanoseconds": 76896.26696777344,
            "standardDeviationNanoseconds": 245.71804419728826,
            "operationsPerSecond": 13092.574779986679
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
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.BlockJoinBenchmarks-20260505-210720",
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
            "sampleCount": 14,
            "meanNanoseconds": 7342.147451128279,
            "medianNanoseconds": 7342.505023956299,
            "minNanoseconds": 7325.621780395508,
            "maxNanoseconds": 7356.877418518066,
            "standardDeviationNanoseconds": 8.932583292876785,
            "operationsPerSecond": 136199.9342367237
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
            "sampleCount": 15,
            "meanNanoseconds": 72149251.76190476,
            "medianNanoseconds": 72042833,
            "minNanoseconds": 71578959,
            "maxNanoseconds": 73011879.57142857,
            "standardDeviationNanoseconds": 474812.5273916849,
            "operationsPerSecond": 13.860157598030781
          },
          "gc": {
            "bytesAllocatedPerOperation": 11040176,
            "gen0Collections": 10,
            "gen1Collections": 4,
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
            "meanNanoseconds": 56080128.03703704,
            "medianNanoseconds": 56146550.333333336,
            "minNanoseconds": 55391565.333333336,
            "maxNanoseconds": 56744109.11111111,
            "standardDeviationNanoseconds": 328721.44039606676,
            "operationsPerSecond": 17.831628332581005
          },
          "gc": {
            "bytesAllocatedPerOperation": 28715319,
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
            "sampleCount": 14,
            "meanNanoseconds": 21567.766699654716,
            "medianNanoseconds": 21558.6417388916,
            "minNanoseconds": 21441.30892944336,
            "maxNanoseconds": 21652.918090820312,
            "standardDeviationNanoseconds": 59.12072935150346,
            "operationsPerSecond": 46365.4866971465
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
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.BooleanQueryBenchmarks-20260505-200633",
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
            "sampleCount": 15,
            "meanNanoseconds": 263874.06666666665,
            "medianNanoseconds": 263777.93603515625,
            "minNanoseconds": 259835.794921875,
            "maxNanoseconds": 268863.88037109375,
            "standardDeviationNanoseconds": 2223.9481684527304,
            "operationsPerSecond": 3789.686544920039
          },
          "gc": {
            "bytesAllocatedPerOperation": 13247,
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
            "sampleCount": 14,
            "meanNanoseconds": 172811.4817766462,
            "medianNanoseconds": 172825.83837890625,
            "minNanoseconds": 171478.02709960938,
            "maxNanoseconds": 173996.669921875,
            "standardDeviationNanoseconds": 919.7804609984771,
            "operationsPerSecond": 5786.652540208358
          },
          "gc": {
            "bytesAllocatedPerOperation": 13616,
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
            "meanNanoseconds": 221824.86987304688,
            "medianNanoseconds": 222421.74340820312,
            "minNanoseconds": 218213.75170898438,
            "maxNanoseconds": 224823.84497070312,
            "standardDeviationNanoseconds": 2148.873417898707,
            "operationsPerSecond": 4508.060798468235
          },
          "gc": {
            "bytesAllocatedPerOperation": 14022,
            "gen0Collections": 13,
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
            "sampleCount": 15,
            "meanNanoseconds": 487611.18681640626,
            "medianNanoseconds": 487579.3212890625,
            "minNanoseconds": 486448.4931640625,
            "maxNanoseconds": 488781.701171875,
            "standardDeviationNanoseconds": 731.6931963067843,
            "operationsPerSecond": 2050.814310739997
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
            "meanNanoseconds": 381653.724609375,
            "medianNanoseconds": 381058.56640625,
            "minNanoseconds": 380162.48876953125,
            "maxNanoseconds": 383781.47900390625,
            "standardDeviationNanoseconds": 1291.3206702691605,
            "operationsPerSecond": 2620.1761846383297
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
            "sampleCount": 15,
            "meanNanoseconds": 581049.5302734375,
            "medianNanoseconds": 581164.34375,
            "minNanoseconds": 578997.2822265625,
            "maxNanoseconds": 584225.9384765625,
            "standardDeviationNanoseconds": 1607.9933661158057,
            "operationsPerSecond": 1721.0236785311704
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
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.DeletionBenchmarks-20260505-203233",
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
            "meanNanoseconds": 10064591046.666666,
            "medianNanoseconds": 10040358392,
            "minNanoseconds": 9975505114,
            "maxNanoseconds": 10196018263,
            "standardDeviationNanoseconds": 64765012.65083807,
            "operationsPerSecond": 0.09935823476217587
          },
          "gc": {
            "bytesAllocatedPerOperation": 1009444528,
            "gen0Collections": 153,
            "gen1Collections": 75,
            "gen2Collections": 5
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
            "sampleCount": 15,
            "meanNanoseconds": 7179661301.8,
            "medianNanoseconds": 7177521957,
            "minNanoseconds": 7150858643,
            "maxNanoseconds": 7208803858,
            "standardDeviationNanoseconds": 19055391.71375146,
            "operationsPerSecond": 0.13928233630593295
          },
          "gc": {
            "bytesAllocatedPerOperation": 2055191072,
            "gen0Collections": 338,
            "gen1Collections": 34,
            "gen2Collections": 1
          }
        }
      ]
    },
    {
      "suiteName": "fuzzy",
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.FuzzyQueryBenchmarks-20260505-202205",
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
            "meanNanoseconds": 6873256.110576923,
            "medianNanoseconds": 6875468.9453125,
            "minNanoseconds": 6820067.640625,
            "maxNanoseconds": 6960164.2421875,
            "standardDeviationNanoseconds": 36506.009666508726,
            "operationsPerSecond": 145.49145032747262
          },
          "gc": {
            "bytesAllocatedPerOperation": 26500,
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
            "sampleCount": 14,
            "meanNanoseconds": 7515816.411272322,
            "medianNanoseconds": 7514013.36328125,
            "minNanoseconds": 7447247.1953125,
            "maxNanoseconds": 7598046.2421875,
            "standardDeviationNanoseconds": 36493.48838221192,
            "operationsPerSecond": 133.05274440980045
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
            "sampleCount": 14,
            "meanNanoseconds": 7989023.465401785,
            "medianNanoseconds": 7975492.515625,
            "minNanoseconds": 7897704.390625,
            "maxNanoseconds": 8121695.0625,
            "standardDeviationNanoseconds": 64274.86120184631,
            "operationsPerSecond": 125.17174399733821
          },
          "gc": {
            "bytesAllocatedPerOperation": 31345,
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
            "meanNanoseconds": 8703347.11607143,
            "medianNanoseconds": 8704235.75,
            "minNanoseconds": 8665393.84375,
            "maxNanoseconds": 8737178.078125,
            "standardDeviationNanoseconds": 18859.838674314415,
            "operationsPerSecond": 114.89832436459069
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
            "sampleCount": 13,
            "meanNanoseconds": 9270807.985576924,
            "medianNanoseconds": 9269970.828125,
            "minNanoseconds": 9228190.765625,
            "maxNanoseconds": 9301866.015625,
            "standardDeviationNanoseconds": 18054.462984072605,
            "operationsPerSecond": 107.86546345860596
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
            "meanNanoseconds": 8729251.78013393,
            "medianNanoseconds": 8737646.8515625,
            "minNanoseconds": 8698442.078125,
            "maxNanoseconds": 8763291.15625,
            "standardDeviationNanoseconds": 20614.173510753597,
            "operationsPerSecond": 114.55735556577764
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
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.GutenbergAnalysisBenchmarks-20260505-211015",
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
            "meanNanoseconds": 430607948.93333334,
            "medianNanoseconds": 431338687,
            "minNanoseconds": 426824955,
            "maxNanoseconds": 434669382,
            "standardDeviationNanoseconds": 2408874.814856497,
            "operationsPerSecond": 2.322298049715798
          },
          "gc": {
            "bytesAllocatedPerOperation": 118518488,
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
            "sampleCount": 15,
            "meanNanoseconds": 123154984.93333331,
            "medianNanoseconds": 122984203.8,
            "minNanoseconds": 122481063.8,
            "maxNanoseconds": 124054250.6,
            "standardDeviationNanoseconds": 475989.2923947999,
            "operationsPerSecond": 8.119849964183938
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
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.GutenbergIndexingBenchmarks-20260505-211211",
      "benchmarkCount": 3,
      "benchmarks": [
        {
          "key": "GutenbergIndexingBenchmarks.LeanLucene_English_Index",
          "displayInfo": "GutenbergIndexingBenchmarks.LeanLucene_English_Index: DefaultJob",
          "typeName": "GutenbergIndexingBenchmarks",
          "methodName": "LeanLucene_English_Index",
          "parameters": {},
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 846670197.6666666,
            "medianNanoseconds": 846722891,
            "minNanoseconds": 833303086,
            "maxNanoseconds": 860137332,
            "standardDeviationNanoseconds": 8379250.626809883,
            "operationsPerSecond": 1.1810974364704157
          },
          "gc": {
            "bytesAllocatedPerOperation": 182370600,
            "gen0Collections": 28,
            "gen1Collections": 10,
            "gen2Collections": 1
          }
        },
        {
          "key": "GutenbergIndexingBenchmarks.LeanLucene_Standard_Index",
          "displayInfo": "GutenbergIndexingBenchmarks.LeanLucene_Standard_Index: DefaultJob",
          "typeName": "GutenbergIndexingBenchmarks",
          "methodName": "LeanLucene_Standard_Index",
          "parameters": {},
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 819686136.6428572,
            "medianNanoseconds": 819555955.5,
            "minNanoseconds": 808027715,
            "maxNanoseconds": 834566682,
            "standardDeviationNanoseconds": 7505798.017741973,
            "operationsPerSecond": 1.219979154576951
          },
          "gc": {
            "bytesAllocatedPerOperation": 83388832,
            "gen0Collections": 12,
            "gen1Collections": 6,
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
            "meanNanoseconds": 654696924.5,
            "medianNanoseconds": 654046346,
            "minNanoseconds": 649386564,
            "maxNanoseconds": 660114462,
            "standardDeviationNanoseconds": 2853179.306015895,
            "operationsPerSecond": 1.527424312805062
          },
          "gc": {
            "bytesAllocatedPerOperation": 217770512,
            "gen0Collections": 41,
            "gen1Collections": 3,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "gutenberg-search",
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.GutenbergSearchBenchmarks-20260505-211449",
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
            "meanNanoseconds": 11748.72785949707,
            "medianNanoseconds": 11746.2744140625,
            "minNanoseconds": 11707.557052612305,
            "maxNanoseconds": 11801.188217163086,
            "standardDeviationNanoseconds": 32.756845965111204,
            "operationsPerSecond": 85115.59821275894
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
            "meanNanoseconds": 20287.15656941732,
            "medianNanoseconds": 20285.09344482422,
            "minNanoseconds": 20244.973663330078,
            "maxNanoseconds": 20324.786346435547,
            "standardDeviationNanoseconds": 25.8579588446531,
            "operationsPerSecond": 49292.27004180023
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
            "meanNanoseconds": 39440.42706298828,
            "medianNanoseconds": 39432.34765625,
            "minNanoseconds": 39254.10192871094,
            "maxNanoseconds": 39631.093994140625,
            "standardDeviationNanoseconds": 100.2974629324731,
            "operationsPerSecond": 25354.695029111914
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
            "sampleCount": 15,
            "meanNanoseconds": 27076.089121500652,
            "medianNanoseconds": 27074.816986083984,
            "minNanoseconds": 26977.15350341797,
            "maxNanoseconds": 27160.764923095703,
            "standardDeviationNanoseconds": 48.05988307089254,
            "operationsPerSecond": 36932.95569801908
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
            "meanNanoseconds": 13828.757827758789,
            "medianNanoseconds": 13833.22021484375,
            "minNanoseconds": 13796.323287963867,
            "maxNanoseconds": 13855.052703857422,
            "standardDeviationNanoseconds": 18.911975342901123,
            "operationsPerSecond": 72313.07485858756
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
            "sampleCount": 14,
            "meanNanoseconds": 11468.362190246582,
            "medianNanoseconds": 11467.486915588379,
            "minNanoseconds": 11419.41535949707,
            "maxNanoseconds": 11527.67610168457,
            "standardDeviationNanoseconds": 25.231976339993697,
            "operationsPerSecond": 87196.4089912039
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
            "sampleCount": 15,
            "meanNanoseconds": 15258.06780904134,
            "medianNanoseconds": 15251.590286254883,
            "minNanoseconds": 15228.545135498047,
            "maxNanoseconds": 15292.365142822266,
            "standardDeviationNanoseconds": 21.577118567617312,
            "operationsPerSecond": 65539.09790644913
          },
          "gc": {
            "bytesAllocatedPerOperation": 464,
            "gen0Collections": 7,
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
            "meanNanoseconds": 39825.1685953776,
            "medianNanoseconds": 39827.44543457031,
            "minNanoseconds": 39716.729553222656,
            "maxNanoseconds": 39925.111083984375,
            "standardDeviationNanoseconds": 51.76370531764354,
            "operationsPerSecond": 25109.749318577076
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
            "meanNanoseconds": 26564.47407430013,
            "medianNanoseconds": 26567.04266357422,
            "minNanoseconds": 26484.401092529297,
            "maxNanoseconds": 26645.062072753906,
            "standardDeviationNanoseconds": 45.204809515008286,
            "operationsPerSecond": 37644.261173890605
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
            "meanNanoseconds": 13068.564893595378,
            "medianNanoseconds": 13068.378509521484,
            "minNanoseconds": 13027.961639404297,
            "maxNanoseconds": 13105.968215942383,
            "standardDeviationNanoseconds": 25.332571716133987,
            "operationsPerSecond": 76519.49606877481
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
            "meanNanoseconds": 23240.827091471354,
            "medianNanoseconds": 23485.75601196289,
            "minNanoseconds": 22550.79653930664,
            "maxNanoseconds": 23611.711669921875,
            "standardDeviationNanoseconds": 422.5610576980631,
            "operationsPerSecond": 43027.72857713692
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
            "meanNanoseconds": 31125.87077636719,
            "medianNanoseconds": 31068.740936279297,
            "minNanoseconds": 30968.611083984375,
            "maxNanoseconds": 31331.93814086914,
            "standardDeviationNanoseconds": 126.4314443086372,
            "operationsPerSecond": 32127.615229941322
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
            "meanNanoseconds": 50757.08822835286,
            "medianNanoseconds": 50832.27880859375,
            "minNanoseconds": 50404.957946777344,
            "maxNanoseconds": 51003.825256347656,
            "standardDeviationNanoseconds": 213.34215126673536,
            "operationsPerSecond": 19701.68177301788
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
            "sampleCount": 14,
            "meanNanoseconds": 36959.35925728934,
            "medianNanoseconds": 36973.70852661133,
            "minNanoseconds": 36820.07824707031,
            "maxNanoseconds": 37046.72088623047,
            "standardDeviationNanoseconds": 70.90755397620148,
            "operationsPerSecond": 27056.746115066217
          },
          "gc": {
            "bytesAllocatedPerOperation": 11223,
            "gen0Collections": 43,
            "gen1Collections": 1,
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
            "sampleCount": 14,
            "meanNanoseconds": 27519.078157697404,
            "medianNanoseconds": 27508.784729003906,
            "minNanoseconds": 27469.33236694336,
            "maxNanoseconds": 27632.944946289062,
            "standardDeviationNanoseconds": 45.175373787582785,
            "operationsPerSecond": 36338.426537020045
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
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.IndexingBenchmarks-20260505-194709",
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
            "meanNanoseconds": 9923464266.4,
            "medianNanoseconds": 9915184714,
            "minNanoseconds": 9836181441,
            "maxNanoseconds": 10023510829,
            "standardDeviationNanoseconds": 63471965.342685565,
            "operationsPerSecond": 0.10077126023277116
          },
          "gc": {
            "bytesAllocatedPerOperation": 982445160,
            "gen0Collections": 154,
            "gen1Collections": 74,
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
            "meanNanoseconds": 7176939096.071428,
            "medianNanoseconds": 7174620581.5,
            "minNanoseconds": 7137127146,
            "maxNanoseconds": 7218595425,
            "standardDeviationNanoseconds": 24236899.177279573,
            "operationsPerSecond": 0.1393351659549944
          },
          "gc": {
            "bytesAllocatedPerOperation": 2019238840,
            "gen0Collections": 331,
            "gen1Collections": 30,
            "gen2Collections": 1
          }
        }
      ]
    },
    {
      "suiteName": "indexsort-index",
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.IndexSortIndexBenchmarks-20260505-205500",
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
            "meanNanoseconds": 10861080483.933332,
            "medianNanoseconds": 10855895508,
            "minNanoseconds": 10796334949,
            "maxNanoseconds": 10922360236,
            "standardDeviationNanoseconds": 36354706.07146812,
            "operationsPerSecond": 0.09207187088607696
          },
          "gc": {
            "bytesAllocatedPerOperation": 1025780216,
            "gen0Collections": 159,
            "gen1Collections": 72,
            "gen2Collections": 5
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
            "sampleCount": 14,
            "meanNanoseconds": 10229639933.642857,
            "medianNanoseconds": 10226762678.5,
            "minNanoseconds": 10185359211,
            "maxNanoseconds": 10277910638,
            "standardDeviationNanoseconds": 29168653.705764163,
            "operationsPerSecond": 0.09775515135300486
          },
          "gc": {
            "bytesAllocatedPerOperation": 1014804736,
            "gen0Collections": 160,
            "gen1Collections": 77,
            "gen2Collections": 8
          }
        }
      ]
    },
    {
      "suiteName": "indexsort-search",
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.IndexSortSearchBenchmarks-20260505-210440",
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
            "sampleCount": 13,
            "meanNanoseconds": 245608.05096905047,
            "medianNanoseconds": 245576.4951171875,
            "minNanoseconds": 244817.89404296875,
            "maxNanoseconds": 246683.412109375,
            "standardDeviationNanoseconds": 539.0962743026731,
            "operationsPerSecond": 4071.5277697717324
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
            "sampleCount": 15,
            "meanNanoseconds": 243427.62893880208,
            "medianNanoseconds": 243413.48315429688,
            "minNanoseconds": 242764.02294921875,
            "maxNanoseconds": 244571.24169921875,
            "standardDeviationNanoseconds": 483.1230987846983,
            "operationsPerSecond": 4107.997125714111
          },
          "gc": {
            "bytesAllocatedPerOperation": 120488,
            "gen0Collections": 117,
            "gen1Collections": 2,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "phrase",
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.PhraseQueryBenchmarks-20260505-201154",
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
            "sampleCount": 15,
            "meanNanoseconds": 436996.5597005208,
            "medianNanoseconds": 436191.58251953125,
            "minNanoseconds": 432675.6982421875,
            "maxNanoseconds": 445882.2724609375,
            "standardDeviationNanoseconds": 3613.4366755524075,
            "operationsPerSecond": 2288.347534555678
          },
          "gc": {
            "bytesAllocatedPerOperation": 61202,
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
            "meanNanoseconds": 342766.8949544271,
            "medianNanoseconds": 341782.13134765625,
            "minNanoseconds": 336875.1416015625,
            "maxNanoseconds": 350226.0537109375,
            "standardDeviationNanoseconds": 4529.504348209542,
            "operationsPerSecond": 2917.434602699762
          },
          "gc": {
            "bytesAllocatedPerOperation": 43950,
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
            "sampleCount": 14,
            "meanNanoseconds": 986324.4423828125,
            "medianNanoseconds": 988005.2421875,
            "minNanoseconds": 972104.888671875,
            "maxNanoseconds": 996220.69140625,
            "standardDeviationNanoseconds": 7367.432350771689,
            "operationsPerSecond": 1013.8651715698634
          },
          "gc": {
            "bytesAllocatedPerOperation": 49859,
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
            "meanNanoseconds": 345150.930078125,
            "medianNanoseconds": 345294.44091796875,
            "minNanoseconds": 343142.25146484375,
            "maxNanoseconds": 347067.91357421875,
            "standardDeviationNanoseconds": 1023.0483034473023,
            "operationsPerSecond": 2897.2832255548315
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
            "sampleCount": 14,
            "meanNanoseconds": 403999.5656389509,
            "medianNanoseconds": 404243.5144042969,
            "minNanoseconds": 402340.35791015625,
            "maxNanoseconds": 405331.78466796875,
            "standardDeviationNanoseconds": 894.424966949325,
            "operationsPerSecond": 2475.2501860204643
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
            "sampleCount": 14,
            "meanNanoseconds": 1030374.5696149553,
            "medianNanoseconds": 1030441.4150390625,
            "minNanoseconds": 1027834.275390625,
            "maxNanoseconds": 1034083.658203125,
            "standardDeviationNanoseconds": 1760.5529043872564,
            "operationsPerSecond": 970.5208469709165
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
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.PrefixQueryBenchmarks-20260505-201704",
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
            "sampleCount": 13,
            "meanNanoseconds": 149833.50758713944,
            "medianNanoseconds": 150048.58056640625,
            "minNanoseconds": 147417.64013671875,
            "maxNanoseconds": 150660.2373046875,
            "standardDeviationNanoseconds": 862.5546852734526,
            "operationsPerSecond": 6674.074551838312
          },
          "gc": {
            "bytesAllocatedPerOperation": 24241,
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
            "sampleCount": 15,
            "meanNanoseconds": 243618.1482747396,
            "medianNanoseconds": 242887.5107421875,
            "minNanoseconds": 240778.75,
            "maxNanoseconds": 248817.96630859375,
            "standardDeviationNanoseconds": 2331.9195297396664,
            "operationsPerSecond": 4104.784504281894
          },
          "gc": {
            "bytesAllocatedPerOperation": 35338,
            "gen0Collections": 17,
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
            "meanNanoseconds": 289345.1140136719,
            "medianNanoseconds": 288686.0183105469,
            "minNanoseconds": 286241.248046875,
            "maxNanoseconds": 292726.77783203125,
            "standardDeviationNanoseconds": 2104.782128497275,
            "operationsPerSecond": 3456.0804781820125
          },
          "gc": {
            "bytesAllocatedPerOperation": 64503,
            "gen0Collections": 32,
            "gen1Collections": 1,
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
            "meanNanoseconds": 187055.6639485677,
            "medianNanoseconds": 186930.51147460938,
            "minNanoseconds": 186499.67626953125,
            "maxNanoseconds": 187829.31103515625,
            "standardDeviationNanoseconds": 387.5406904865824,
            "operationsPerSecond": 5346.0022481594415
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
            "meanNanoseconds": 281584.8514229911,
            "medianNanoseconds": 281598.72900390625,
            "minNanoseconds": 280832.908203125,
            "maxNanoseconds": 282831.27978515625,
            "standardDeviationNanoseconds": 604.7208346356491,
            "operationsPerSecond": 3551.3274060962185
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
            "meanNanoseconds": 353981.3524344308,
            "medianNanoseconds": 353929.7744140625,
            "minNanoseconds": 352668.86962890625,
            "maxNanoseconds": 355260.31201171875,
            "standardDeviationNanoseconds": 603.0044262094135,
            "operationsPerSecond": 2825.0075692482515
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
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.TermQueryBenchmarks-20260505-194158",
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
            "sampleCount": 13,
            "meanNanoseconds": 106200.86023888222,
            "medianNanoseconds": 106212.69519042969,
            "minNanoseconds": 105940.27038574219,
            "maxNanoseconds": 106381.88928222656,
            "standardDeviationNanoseconds": 127.05756332236021,
            "operationsPerSecond": 9416.119584631015
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
            "meanNanoseconds": 150702.1984049479,
            "medianNanoseconds": 150680.33935546875,
            "minNanoseconds": 150254.0908203125,
            "maxNanoseconds": 151361.41333007812,
            "standardDeviationNanoseconds": 333.18787826244323,
            "operationsPerSecond": 6635.603266469454
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
            "sampleCount": 12,
            "meanNanoseconds": 674495.3002929688,
            "medianNanoseconds": 674498.3549804688,
            "minNanoseconds": 673179.248046875,
            "maxNanoseconds": 675700.6162109375,
            "standardDeviationNanoseconds": 751.2346693501481,
            "operationsPerSecond": 1482.5900188861915
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
            "meanNanoseconds": 137287.98221261162,
            "medianNanoseconds": 137324.5087890625,
            "minNanoseconds": 136889.423828125,
            "maxNanoseconds": 137731.30932617188,
            "standardDeviationNanoseconds": 271.3059571241084,
            "operationsPerSecond": 7283.958755044893
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
            "sampleCount": 14,
            "meanNanoseconds": 177040.96022251673,
            "medianNanoseconds": 177038.71313476562,
            "minNanoseconds": 176451.78125,
            "maxNanoseconds": 177610.39428710938,
            "standardDeviationNanoseconds": 316.51297201686054,
            "operationsPerSecond": 5648.4103946518035
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
            "sampleCount": 14,
            "meanNanoseconds": 752274.0498046875,
            "medianNanoseconds": 752387.6142578125,
            "minNanoseconds": 750194.8857421875,
            "maxNanoseconds": 754363.818359375,
            "standardDeviationNanoseconds": 1277.6972339017007,
            "operationsPerSecond": 1329.3027989728337
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
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.SchemaAndJsonBenchmarks-20260505-204504",
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
            "sampleCount": 14,
            "meanNanoseconds": 9641621439.357143,
            "medianNanoseconds": 9659156144.5,
            "minNanoseconds": 9562217847,
            "maxNanoseconds": 9681964652,
            "standardDeviationNanoseconds": 41121679.305595204,
            "operationsPerSecond": 0.10371699472850027
          },
          "gc": {
            "bytesAllocatedPerOperation": 982372904,
            "gen0Collections": 150,
            "gen1Collections": 70,
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
            "meanNanoseconds": 9948454894.266666,
            "medianNanoseconds": 9944989118,
            "minNanoseconds": 9916196059,
            "maxNanoseconds": 9996628483,
            "standardDeviationNanoseconds": 27264178.40878358,
            "operationsPerSecond": 0.10051812172122365
          },
          "gc": {
            "bytesAllocatedPerOperation": 986380192,
            "gen0Collections": 151,
            "gen1Collections": 70,
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
            "meanNanoseconds": 426829332.6666667,
            "medianNanoseconds": 427675719,
            "minNanoseconds": 424425227,
            "maxNanoseconds": 429456211,
            "standardDeviationNanoseconds": 1941887.5868867042,
            "operationsPerSecond": 2.342856789509713
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
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.SuggesterBenchmarks-20260505-204123",
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
            "sampleCount": 14,
            "meanNanoseconds": 4599011.651227678,
            "medianNanoseconds": 4589378.328125,
            "minNanoseconds": 4567243.7890625,
            "maxNanoseconds": 4638050.8203125,
            "standardDeviationNanoseconds": 21342.323862806523,
            "operationsPerSecond": 217.43802273974586
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
            "sampleCount": 15,
            "meanNanoseconds": 4754691.0578125,
            "medianNanoseconds": 4745449.7734375,
            "minNanoseconds": 4717733.6484375,
            "maxNanoseconds": 4816445.296875,
            "standardDeviationNanoseconds": 28620.47790172055,
            "operationsPerSecond": 210.31860700116067
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
            "sampleCount": 13,
            "meanNanoseconds": 10229031.811298076,
            "medianNanoseconds": 10225740.96875,
            "minNanoseconds": 10217159.59375,
            "maxNanoseconds": 10247197.21875,
            "standardDeviationNanoseconds": 10776.055515622009,
            "operationsPerSecond": 97.76096295794967
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
      "summaryTitle": "Rowles.LeanLucene.Benchmarks.WildcardQueryBenchmarks-20260505-202720",
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
            "sampleCount": 15,
            "meanNanoseconds": 150142.3483235677,
            "medianNanoseconds": 149758.11279296875,
            "minNanoseconds": 148873.23217773438,
            "maxNanoseconds": 152521.74243164062,
            "standardDeviationNanoseconds": 1273.6086795840376,
            "operationsPerSecond": 6660.346072681154
          },
          "gc": {
            "bytesAllocatedPerOperation": 24952,
            "gen0Collections": 24,
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
            "meanNanoseconds": 527511.5891927084,
            "medianNanoseconds": 527309.744140625,
            "minNanoseconds": 518344.4267578125,
            "maxNanoseconds": 535358.025390625,
            "standardDeviationNanoseconds": 5215.634880565607,
            "operationsPerSecond": 1895.6929487186756
          },
          "gc": {
            "bytesAllocatedPerOperation": 10416,
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
            "sampleCount": 14,
            "meanNanoseconds": 106256.30709402902,
            "medianNanoseconds": 106427.54797363281,
            "minNanoseconds": 104962.59130859375,
            "maxNanoseconds": 107075.31396484375,
            "standardDeviationNanoseconds": 671.3640134562922,
            "operationsPerSecond": 9411.206048362603
          },
          "gc": {
            "bytesAllocatedPerOperation": 8830,
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
            "sampleCount": 13,
            "meanNanoseconds": 205967.3678448017,
            "medianNanoseconds": 206022.46752929688,
            "minNanoseconds": 205253.90698242188,
            "maxNanoseconds": 206426.19409179688,
            "standardDeviationNanoseconds": 296.4540172386543,
            "operationsPerSecond": 4855.138027269976
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
            "meanNanoseconds": 1170559.4544270833,
            "medianNanoseconds": 1170879.849609375,
            "minNanoseconds": 1165695.271484375,
            "maxNanoseconds": 1174853.203125,
            "standardDeviationNanoseconds": 2366.614250644116,
            "operationsPerSecond": 854.2923609885654
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
            "sampleCount": 12,
            "meanNanoseconds": 410198.66251627606,
            "medianNanoseconds": 410244.2578125,
            "minNanoseconds": 409294.67236328125,
            "maxNanoseconds": 410751.3203125,
            "standardDeviationNanoseconds": 464.316628530706,
            "operationsPerSecond": 2437.8431510861437
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

