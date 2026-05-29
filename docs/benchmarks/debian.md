---
title: Benchmarks - debian
---

# Benchmarks: debian

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `8df4f79` &nbsp;&middot;&nbsp; 29 May 2026 09:00 UTC &nbsp;&middot;&nbsp; 222 benchmarks

## aggregation

| Method                                 | Job        | IterationCount | LaunchCount | WarmupCount | DocumentCount | Mean      | Error     | StdDev    | Ratio | Gen0   | Allocated | Alloc Ratio |
|--------------------------------------- |----------- |--------------- |------------ |------------ |-------------- |----------:|----------:|----------:|------:|-------:|----------:|------------:|
| LeanCorpus_SearchOnly                  | DefaultJob | Default        | Default     | Default     | 20000         |  2.643 μs | 0.0034 μs | 0.0032 μs |  1.00 | 0.1259 |     528 B |        1.00 |
| LeanCorpus_SearchWithStats             | DefaultJob | Default        | Default     | Default     | 20000         |  9.535 μs | 0.0113 μs | 0.0094 μs |  3.61 | 1.3885 |    5832 B |       11.05 |
| LeanCorpus_SearchWithHistogram         | DefaultJob | Default        | Default     | Default     | 20000         | 10.270 μs | 0.0289 μs | 0.0270 μs |  3.89 | 1.6479 |    6952 B |       13.17 |
| LeanCorpus_SearchWithStatsAndHistogram | DefaultJob | Default        | Default     | Default     | 20000         | 12.275 μs | 0.0264 μs | 0.0247 μs |  4.64 | 2.0599 |    8672 B |       16.42 |
|                                        |            |                |             |             |               |           |           |           |       |        |           |             |
| LeanCorpus_SearchOnly                  | ShortRun   | 3              | 1           | 3           | 20000         |  2.671 μs | 0.0207 μs | 0.0011 μs |  1.00 | 0.1259 |     528 B |        1.00 |
| LeanCorpus_SearchWithStats             | ShortRun   | 3              | 1           | 3           | 20000         |  9.503 μs | 0.2274 μs | 0.0125 μs |  3.56 | 1.3885 |    5832 B |       11.05 |
| LeanCorpus_SearchWithHistogram         | ShortRun   | 3              | 1           | 3           | 20000         | 10.182 μs | 0.3727 μs | 0.0204 μs |  3.81 | 1.6479 |    6952 B |       13.17 |
| LeanCorpus_SearchWithStatsAndHistogram | ShortRun   | 3              | 1           | 3           | 20000         | 12.160 μs | 0.0202 μs | 0.0011 μs |  4.55 | 2.0599 |    8672 B |       16.42 |

## Analysis

| Method             | Job        | IterationCount | LaunchCount | WarmupCount | DocumentCount | Mean     | Error    | StdDev  | Ratio | Gen0       | Allocated | Alloc Ratio |
|------------------- |----------- |--------------- |------------ |------------ |-------------- |---------:|---------:|--------:|------:|-----------:|----------:|------------:|
| LeanCorpus_Analyse | DefaultJob | Default        | Default     | Default     | 20000         | 170.1 ms |  0.26 ms | 0.23 ms |  1.00 |  3333.3333 |  13.65 MB |        1.00 |
| LuceneNet_Analyse  | DefaultJob | Default        | Default     | Default     | 20000         | 247.4 ms |  0.74 ms | 0.69 ms |  1.45 | 15666.6667 |  63.39 MB |        4.64 |
|                    |            |                |             |             |               |          |          |         |       |            |           |             |
| LeanCorpus_Analyse | ShortRun   | 3              | 1           | 3           | 20000         | 168.9 ms |  2.72 ms | 0.15 ms |  1.00 |  3333.3333 |  13.65 MB |        1.00 |
| LuceneNet_Analyse  | ShortRun   | 3              | 1           | 3           | 20000         | 243.5 ms | 22.81 ms | 1.25 ms |  1.44 | 15666.6667 |  63.39 MB |        4.64 |

## analysis-filters

| Method | Job        | IterationCount | LaunchCount | WarmupCount | Scenario             | Mean     | Error    | StdDev  | Gen0   | Allocated |
|------- |----------- |--------------- |------------ |------------ |--------------------- |---------:|---------:|--------:|-------:|----------:|
| **Apply**  | **DefaultJob** | **Default**        | **Default**     | **Default**     | **decim(...)ating [22]** | **152.9 ns** |  **0.17 ns** | **0.15 ns** | **0.0401** |     **168 B** |
| Apply  | ShortRun   | 3              | 1           | 3           | decim(...)ating [22] | 154.3 ns |  3.45 ns | 0.19 ns | 0.0401 |     168 B |
| **Apply**  | **DefaultJob** | **Default**        | **Default**     | **Default**     | **elision-mutating**     | **213.4 ns** |  **0.36 ns** | **0.33 ns** | **0.0477** |     **200 B** |
| Apply  | ShortRun   | 3              | 1           | 3           | elision-mutating     | 211.7 ns |  7.90 ns | 0.43 ns | 0.0477 |     200 B |
| **Apply**  | **DefaultJob** | **Default**        | **Default**     | **Default**     | **length-mutating**      | **319.2 ns** |  **0.57 ns** | **0.51 ns** | **0.0420** |     **176 B** |
| Apply  | ShortRun   | 3              | 1           | 3           | length-mutating      | 121.0 ns |  3.17 ns | 0.17 ns | 0.0420 |     176 B |
| **Apply**  | **DefaultJob** | **Default**        | **Default**     | **Default**     | **length-noop**          | **105.1 ns** |  **0.13 ns** | **0.11 ns** | **0.0421** |     **176 B** |
| Apply  | ShortRun   | 3              | 1           | 3           | length-noop          | 103.3 ns |  2.76 ns | 0.15 ns | 0.0421 |     176 B |
| **Apply**  | **DefaultJob** | **Default**        | **Default**     | **Default**     | **reverse-mutating**     | **139.4 ns** |  **0.17 ns** | **0.16 ns** | **0.0496** |     **208 B** |
| Apply  | ShortRun   | 3              | 1           | 3           | reverse-mutating     | 139.5 ns |  4.25 ns | 0.23 ns | 0.0496 |     208 B |
| **Apply**  | **DefaultJob** | **Default**        | **Default**     | **Default**     | **shingle-mutating**     | **398.7 ns** |  **1.66 ns** | **1.47 ns** | **0.2065** |     **864 B** |
| Apply  | ShortRun   | 3              | 1           | 3           | shingle-mutating     | 397.9 ns |  6.23 ns | 0.34 ns | 0.2065 |     864 B |
| **Apply**  | **DefaultJob** | **Default**        | **Default**     | **Default**     | **truncate-mutating**    | **117.1 ns** |  **0.11 ns** | **0.10 ns** | **0.0421** |     **176 B** |
| Apply  | ShortRun   | 3              | 1           | 3           | truncate-mutating    | 116.5 ns |  3.27 ns | 0.18 ns | 0.0421 |     176 B |
| **Apply**  | **DefaultJob** | **Default**        | **Default**     | **Default**     | **truncate-noop**        | **101.7 ns** |  **0.16 ns** | **0.15 ns** | **0.0421** |     **176 B** |
| Apply  | ShortRun   | 3              | 1           | 3           | truncate-noop        | 101.7 ns |  2.35 ns | 0.13 ns | 0.0421 |     176 B |
| **Apply**  | **DefaultJob** | **Default**        | **Default**     | **Default**     | **unique-mutating**      | **222.9 ns** |  **0.33 ns** | **0.29 ns** | **0.0937** |     **392 B** |
| Apply  | ShortRun   | 3              | 1           | 3           | unique-mutating      | 221.3 ns | 13.74 ns | 0.75 ns | 0.0937 |     392 B |
| **Apply**  | **DefaultJob** | **Default**        | **Default**     | **Default**     | **word-(...)ating [23]** | **590.2 ns** |  **1.34 ns** | **1.25 ns** | **0.3424** |    **1432 B** |
| Apply  | ShortRun   | 3              | 1           | 3           | word-(...)ating [23] | 607.6 ns |  3.21 ns | 0.18 ns | 0.3424 |    1432 B |

## analysis-parity

| Method                | Job        | IterationCount | LaunchCount | WarmupCount | Mean      | Error     | StdDev    | Ratio | Gen0   | Allocated | Alloc Ratio |
|---------------------- |----------- |--------------- |------------ |------------ |----------:|----------:|----------:|------:|-------:|----------:|------------:|
| LeanCorpus_Whitespace | DefaultJob | Default        | Default     | Default     | 48.159 μs | 0.0468 μs | 0.0438 μs |  1.00 |      - |         - |          NA |
| LuceneNet_Whitespace  | DefaultJob | Default        | Default     | Default     | 74.583 μs | 0.0509 μs | 0.0397 μs |  1.55 | 0.7324 |    3200 B |          NA |
| LeanCorpus_Keyword    | DefaultJob | Default        | Default     | Default     |  4.265 μs | 0.0010 μs | 0.0008 μs |  0.09 |      - |         - |          NA |
| LuceneNet_Keyword     | DefaultJob | Default        | Default     | Default     | 12.205 μs | 0.0453 μs | 0.0354 μs |  0.25 | 0.7629 |    3200 B |          NA |
| LeanCorpus_Simple     | DefaultJob | Default        | Default     | Default     | 42.165 μs | 0.2439 μs | 0.2281 μs |  0.88 |      - |         - |          NA |
| LuceneNet_Simple      | DefaultJob | Default        | Default     | Default     | 82.777 μs | 0.0877 μs | 0.0821 μs |  1.72 | 0.7324 |    3200 B |          NA |
|                       |            |                |             |             |           |           |           |       |        |           |             |
| LeanCorpus_Whitespace | ShortRun   | 3              | 1           | 3           | 48.214 μs | 1.2690 μs | 0.0696 μs |  1.00 |      - |         - |          NA |
| LuceneNet_Whitespace  | ShortRun   | 3              | 1           | 3           | 76.523 μs | 3.0154 μs | 0.1653 μs |  1.59 | 0.7324 |    3200 B |          NA |
| LeanCorpus_Keyword    | ShortRun   | 3              | 1           | 3           |  4.099 μs | 0.0193 μs | 0.0011 μs |  0.09 |      - |         - |          NA |
| LuceneNet_Keyword     | ShortRun   | 3              | 1           | 3           | 12.096 μs | 1.0206 μs | 0.0559 μs |  0.25 | 0.7629 |    3200 B |          NA |
| LeanCorpus_Simple     | ShortRun   | 3              | 1           | 3           | 42.566 μs | 0.8337 μs | 0.0457 μs |  0.88 |      - |         - |          NA |
| LuceneNet_Simple      | ShortRun   | 3              | 1           | 3           | 91.419 μs | 1.5158 μs | 0.0831 μs |  1.90 | 0.7324 |    3200 B |          NA |

## async-index

| Method                                 | Job        | IterationCount | LaunchCount | WarmupCount | DocumentCount | Mean     | Error     | StdDev   | Ratio | RatioSD | Gen0       | Gen1      | Allocated | Alloc Ratio |
|--------------------------------------- |----------- |--------------- |------------ |------------ |-------------- |---------:|----------:|---------:|------:|--------:|-----------:|----------:|----------:|------------:|
| LeanCorpus_AddDocument_Sync            | DefaultJob | Default        | Default     | Default     | 20000         | 993.5 ms |   7.15 ms |  6.34 ms |  1.00 |    0.00 | 17000.0000 | 8000.0000 | 129.43 MB |        1.00 |
| LeanCorpus_AddDocumentAsync_Sequential | DefaultJob | Default        | Default     | Default     | 20000         | 990.9 ms |   5.36 ms |  4.75 ms |  1.00 |    0.01 | 18000.0000 | 9000.0000 | 129.45 MB |        1.00 |
| LeanCorpus_AddDocumentsAsync_Batch     | DefaultJob | Default        | Default     | Default     | 20000         | 974.2 ms |   7.26 ms |  6.79 ms |  0.98 |    0.01 | 17000.0000 | 8000.0000 | 129.45 MB |        1.00 |
|                                        |            |                |             |             |               |          |           |          |       |         |            |           |           |             |
| LeanCorpus_AddDocument_Sync            | ShortRun   | 3              | 1           | 3           | 20000         | 996.4 ms | 527.02 ms | 28.89 ms |  1.00 |    0.00 | 18000.0000 | 9000.0000 | 129.43 MB |        1.00 |
| LeanCorpus_AddDocumentAsync_Sequential | ShortRun   | 3              | 1           | 3           | 20000         | 998.6 ms | 539.50 ms | 29.57 ms |  1.00 |    0.04 | 17000.0000 | 8000.0000 | 129.45 MB |        1.00 |
| LeanCorpus_AddDocumentsAsync_Batch     | ShortRun   | 3              | 1           | 3           | 20000         | 999.1 ms | 518.60 ms | 28.43 ms |  1.00 |    0.04 | 18000.0000 | 8000.0000 | 129.45 MB |        1.00 |

## Block-Join (index)

| Method                 | Job        | IterationCount | LaunchCount | WarmupCount | BlockCount | Mean     | Error     | StdDev   | Ratio | Gen0      | Gen1      | Allocated | Alloc Ratio |
|----------------------- |----------- |--------------- |------------ |------------ |----------- |---------:|----------:|---------:|------:|----------:|----------:|----------:|------------:|
| LeanLucene_IndexBlocks | Job-CNUJVU | Default        | Default     | Default     | 500        | 61.69 ms |  0.575 ms | 0.449 ms |  1.00 | 1000.0000 |         - |  12.53 MB |        1.00 |
| LuceneNet_IndexBlocks  | Job-CNUJVU | Default        | Default     | Default     | 500        | 56.78 ms |  0.905 ms | 0.846 ms |  0.92 | 5000.0000 | 1000.0000 |  27.05 MB |        2.16 |
|                        |            |                |             |             |            |          |           |          |       |           |           |           |             |
| LeanLucene_IndexBlocks | ShortRun   | 3              | 1           | 3           | 500        | 61.90 ms |  9.291 ms | 0.509 ms |  1.00 | 1000.0000 |         - |  12.53 MB |        1.00 |
| LuceneNet_IndexBlocks  | ShortRun   | 3              | 1           | 3           | 500        | 55.39 ms | 14.565 ms | 0.798 ms |  0.89 | 5000.0000 | 1000.0000 |  27.05 MB |        2.16 |

## Block-Join (search)

| Method                           | Job        | IterationCount | LaunchCount | WarmupCount | BlockCount | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------------------------- |----------- |--------------- |------------ |------------ |----------- |----------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| LeanLucene_BlockJoinQuery        | DefaultJob | Default        | Default     | Default     | 500        |  7.078 μs | 0.0155 μs | 0.0145 μs |  1.00 |    0.00 | 0.1755 |      - |     744 B |        1.00 |
| LuceneNet_ToParentBlockJoinQuery | DefaultJob | Default        | Default     | Default     | 500        | 24.841 μs | 0.1309 μs | 0.1224 μs |  3.51 |    0.02 | 2.5635 | 0.0305 |   10941 B |       14.71 |
|                                  |            |                |             |             |            |           |           |           |       |         |        |        |           |             |
| LeanLucene_BlockJoinQuery        | ShortRun   | 3              | 1           | 3           | 500        |  7.161 μs | 0.3096 μs | 0.0170 μs |  1.00 |    0.00 | 0.1755 |      - |     744 B |        1.00 |
| LuceneNet_ToParentBlockJoinQuery | ShortRun   | 3              | 1           | 3           | 500        | 24.975 μs | 2.3690 μs | 0.1299 μs |  3.49 |    0.02 | 2.5635 | 0.0305 |   10941 B |       14.71 |

## Boolean queries

| Method                  | Job        | IterationCount | LaunchCount | WarmupCount | BooleanShape  | DocumentCount | Mean       | Error     | StdDev    | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------ |----------- |--------------- |------------ |------------ |-------------- |-------------- |-----------:|----------:|----------:|------:|--------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_BooleanQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **Must2Common**   | **20000**         |  **10.221 μs** | **0.0250 μs** | **0.0221 μs** |  **1.00** |    **0.00** |  **1.1597** |      **-** |   **4.73 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | DefaultJob | Default        | Default     | Default     | Must2Common   | 20000         |  32.349 μs | 0.3449 μs | 0.3226 μs |  3.17 |    0.03 |  5.4321 | 0.1221 |  22.56 KB |        4.77 |
|                         |            |                |             |             |               |               |            |           |           |       |         |         |        |           |             |
| LeanCorpus_BooleanQuery | ShortRun   | 3              | 1           | 3           | Must2Common   | 20000         |  10.407 μs | 0.7194 μs | 0.0394 μs |  1.00 |    0.00 |  1.1597 |      - |   4.73 KB |        1.00 |
| LuceneNet_BooleanQuery  | ShortRun   | 3              | 1           | 3           | Must2Common   | 20000         |  32.609 μs | 6.4003 μs | 0.3508 μs |  3.13 |    0.03 |  5.4321 | 0.1221 |  22.56 KB |        4.77 |
|                         |            |                |             |             |               |               |            |           |           |       |         |         |        |           |             |
| **LeanCorpus_BooleanQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **Must3Mixed**    | **20000**         |   **7.885 μs** | **0.0319 μs** | **0.0299 μs** |  **1.00** |    **0.00** |  **1.2817** |      **-** |   **5.18 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | DefaultJob | Default        | Default     | Default     | Must3Mixed    | 20000         |  36.096 μs | 0.0990 μs | 0.0877 μs |  4.58 |    0.02 |  7.5989 | 0.0610 |  31.29 KB |        6.04 |
|                         |            |                |             |             |               |               |            |           |           |       |         |         |        |           |             |
| LeanCorpus_BooleanQuery | ShortRun   | 3              | 1           | 3           | Must3Mixed    | 20000         |   7.855 μs | 1.3755 μs | 0.0754 μs |  1.00 |    0.00 |  1.2817 |      - |   5.18 KB |        1.00 |
| LuceneNet_BooleanQuery  | ShortRun   | 3              | 1           | 3           | Must3Mixed    | 20000         |  35.720 μs | 1.3810 μs | 0.0757 μs |  4.55 |    0.04 |  7.5989 | 0.0610 |  31.29 KB |        6.04 |
|                         |            |                |             |             |               |               |            |           |           |       |         |         |        |           |             |
| **LeanCorpus_BooleanQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **MustNotCommon** | **20000**         |   **9.230 μs** | **0.0338 μs** | **0.0316 μs** |  **1.00** |    **0.00** |  **1.2360** |      **-** |   **5.03 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | DefaultJob | Default        | Default     | Default     | MustNotCommon | 20000         |  30.580 μs | 0.4143 μs | 0.3876 μs |  3.31 |    0.04 |  5.8594 | 0.0610 |  24.11 KB |        4.79 |
|                         |            |                |             |             |               |               |            |           |           |       |         |         |        |           |             |
| LeanCorpus_BooleanQuery | ShortRun   | 3              | 1           | 3           | MustNotCommon | 20000         |   9.458 μs | 0.4472 μs | 0.0245 μs |  1.00 |    0.00 |  1.2512 |      - |   5.06 KB |        1.00 |
| LuceneNet_BooleanQuery  | ShortRun   | 3              | 1           | 3           | MustNotCommon | 20000         |  29.821 μs | 6.2673 μs | 0.3435 μs |  3.15 |    0.03 |  5.8594 | 0.0610 |  24.11 KB |        4.76 |
|                         |            |                |             |             |               |               |            |           |           |       |         |         |        |           |             |
| **LeanCorpus_BooleanQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **Should2Common** | **20000**         |  **31.396 μs** | **0.1820 μs** | **0.1703 μs** |  **1.00** |    **0.00** |  **1.2817** |      **-** |   **5.27 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | DefaultJob | Default        | Default     | Default     | Should2Common | 20000         |  83.397 μs | 0.3090 μs | 0.2890 μs |  2.66 |    0.02 | 33.0811 | 1.3428 | 136.36 KB |       25.88 |
|                         |            |                |             |             |               |               |            |           |           |       |         |         |        |           |             |
| LeanCorpus_BooleanQuery | ShortRun   | 3              | 1           | 3           | Should2Common | 20000         |  31.511 μs | 1.6099 μs | 0.0882 μs |  1.00 |    0.00 |  1.2817 |      - |   5.27 KB |        1.00 |
| LuceneNet_BooleanQuery  | ShortRun   | 3              | 1           | 3           | Should2Common | 20000         |  82.188 μs | 3.4971 μs | 0.1917 μs |  2.61 |    0.01 | 33.0811 | 1.3428 | 136.36 KB |       25.88 |
|                         |            |                |             |             |               |               |            |           |           |       |         |         |        |           |             |
| **LeanCorpus_BooleanQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **Should4Mixed**  | **20000**         |  **45.176 μs** | **0.2859 μs** | **0.2674 μs** |  **1.00** |    **0.00** |  **1.5869** |      **-** |   **6.48 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | DefaultJob | Default        | Default     | Default     | Should4Mixed  | 20000         | 105.631 μs | 0.3076 μs | 0.2877 μs |  2.34 |    0.01 | 38.5742 | 0.8545 |  158.7 KB |       24.49 |
|                         |            |                |             |             |               |               |            |           |           |       |         |         |        |           |             |
| LeanCorpus_BooleanQuery | ShortRun   | 3              | 1           | 3           | Should4Mixed  | 20000         |  44.857 μs | 1.4732 μs | 0.0808 μs |  1.00 |    0.00 |  1.5869 |      - |   6.49 KB |        1.00 |
| LuceneNet_BooleanQuery  | ShortRun   | 3              | 1           | 3           | Should4Mixed  | 20000         | 104.784 μs | 3.1973 μs | 0.1753 μs |  2.34 |    0.00 | 38.5742 | 0.9766 |  158.7 KB |       24.46 |

## collapse-facet

| Method                                 | Job        | IterationCount | LaunchCount | WarmupCount | DocumentCount | Mean      | Error     | StdDev    | Ratio | Gen0   | Allocated | Alloc Ratio |
|--------------------------------------- |----------- |--------------- |------------ |------------ |-------------- |----------:|----------:|----------:|------:|-------:|----------:|------------:|
| LeanCorpus_BaseSearch                  | DefaultJob | Default        | Default     | Default     | 20000         |  2.630 μs | 0.0069 μs | 0.0065 μs |  1.00 | 0.1259 |     528 B |        1.00 |
| LeanCorpus_SearchWithCollapse          | DefaultJob | Default        | Default     | Default     | 20000         |  9.559 μs | 0.0179 μs | 0.0167 μs |  3.64 | 0.6409 |    2712 B |        5.14 |
| LeanCorpus_SearchWithFacets            | DefaultJob | Default        | Default     | Default     | 20000         | 12.706 μs | 0.0213 μs | 0.0199 μs |  4.83 | 1.6785 |    7048 B |       13.35 |
| LeanCorpus_SearchWithCollapseAndFacets | DefaultJob | Default        | Default     | Default     | 20000         |  9.854 μs | 0.0138 μs | 0.0129 μs |  3.75 | 0.6409 |    2712 B |        5.14 |
|                                        |            |                |             |             |               |           |           |           |       |        |           |             |
| LeanCorpus_BaseSearch                  | ShortRun   | 3              | 1           | 3           | 20000         |  2.690 μs | 0.1002 μs | 0.0055 μs |  1.00 | 0.1259 |     528 B |        1.00 |
| LeanCorpus_SearchWithCollapse          | ShortRun   | 3              | 1           | 3           | 20000         |  9.624 μs | 0.2431 μs | 0.0133 μs |  3.58 | 0.6409 |    2712 B |        5.14 |
| LeanCorpus_SearchWithFacets            | ShortRun   | 3              | 1           | 3           | 20000         | 12.829 μs | 0.2549 μs | 0.0140 μs |  4.77 | 1.6785 |    7048 B |       13.35 |
| LeanCorpus_SearchWithCollapseAndFacets | ShortRun   | 3              | 1           | 3           | 20000         |  9.734 μs | 0.3955 μs | 0.0217 μs |  3.62 | 0.6409 |    2712 B |        5.14 |

## combined

| Method                             | Job        | IterationCount | LaunchCount | WarmupCount | MinimumShouldMatch | DocumentCount | Mean     | Error    | StdDev   | Ratio | Gen0   | Allocated | Alloc Ratio |
|----------------------------------- |----------- |--------------- |------------ |------------ |------------------- |-------------- |---------:|---------:|---------:|------:|-------:|----------:|------------:|
| **LeanCorpus_CombinedFieldsQuery**     | **DefaultJob** | **Default**        | **Default**     | **Default**     | **1**                  | **20000**         | **21.76 μs** | **0.113 μs** | **0.100 μs** |  **1.00** | **3.2654** |  **13.31 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_MultiField | DefaultJob | Default        | Default     | Default     | 1                  | 20000         | 14.15 μs | 0.077 μs | 0.072 μs |  0.65 | 1.7548 |   7.06 KB |        0.53 |
|                                    |            |                |             |             |                    |               |          |          |          |       |        |           |             |
| LeanCorpus_CombinedFieldsQuery     | ShortRun   | 3              | 1           | 3           | 1                  | 20000         | 22.02 μs | 0.569 μs | 0.031 μs |  1.00 | 3.2654 |  13.31 KB |        1.00 |
| LeanCorpus_BooleanQuery_MultiField | ShortRun   | 3              | 1           | 3           | 1                  | 20000         | 13.92 μs | 0.958 μs | 0.053 μs |  0.63 | 1.7548 |   7.06 KB |        0.53 |
|                                    |            |                |             |             |                    |               |          |          |          |       |        |           |             |
| **LeanCorpus_CombinedFieldsQuery**     | **DefaultJob** | **Default**        | **Default**     | **Default**     | **2**                  | **20000**         | **19.56 μs** | **0.081 μs** | **0.072 μs** |  **1.00** | **3.1433** |  **12.85 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_MultiField | DefaultJob | Default        | Default     | Default     | 2                  | 20000         | 14.15 μs | 0.116 μs | 0.108 μs |  0.72 | 1.7548 |   7.06 KB |        0.55 |
|                                    |            |                |             |             |                    |               |          |          |          |       |        |           |             |
| LeanCorpus_CombinedFieldsQuery     | ShortRun   | 3              | 1           | 3           | 2                  | 20000         | 19.53 μs | 2.717 μs | 0.149 μs |  1.00 | 3.1433 |  12.85 KB |        1.00 |
| LeanCorpus_BooleanQuery_MultiField | ShortRun   | 3              | 1           | 3           | 2                  | 20000         | 14.17 μs | 0.516 μs | 0.028 μs |  0.73 | 1.7548 |   7.06 KB |        0.55 |

## Deletion (commit)

| Method                   | Job        | IterationCount | LaunchCount | WarmupCount | DocumentCount | Mean     | Error     | StdDev    | Median   | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------- |----------- |--------------- |------------ |------------ |-------------- |---------:|----------:|----------:|---------:|------:|--------:|----------:|------------:|
| LeanLucene_CommitDeletes | Job-CNUJVU | Default        | Default     | Default     | 20000         | 4.285 ms | 0.0757 ms | 0.0708 ms | 4.275 ms |  1.00 |    0.00 |   1.77 MB |        1.00 |
| LuceneNet_CommitDeletes  | Job-CNUJVU | Default        | Default     | Default     | 20000         | 2.479 ms | 0.0538 ms | 0.1543 ms | 2.385 ms |  0.58 |    0.04 |   3.87 MB |        2.19 |
|                          |            |                |             |             |               |          |           |           |          |       |         |           |             |
| LeanLucene_CommitDeletes | ShortRun   | 3              | 1           | 3           | 20000         | 4.362 ms | 0.7316 ms | 0.0401 ms | 4.374 ms |  1.00 |    0.00 |   1.77 MB |        1.00 |
| LuceneNet_CommitDeletes  | ShortRun   | 3              | 1           | 3           | 20000         | 2.824 ms | 1.1005 ms | 0.0603 ms | 2.850 ms |  0.65 |    0.01 |   3.87 MB |        2.19 |

## Deletion (queue)

| Method                  | Job        | IterationCount | LaunchCount | WarmupCount | DocumentCount | Mean      | Error        | StdDev    | Median    | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------ |----------- |--------------- |------------ |------------ |-------------- |----------:|-------------:|----------:|----------:|------:|--------:|----------:|------------:|
| LeanLucene_QueueDeletes | Job-CNUJVU | Default        | Default     | Default     | 20000         |  87.32 μs |     3.283 μs |  9.259 μs |  82.41 μs |  1.00 |    0.00 | 243.02 KB |        1.00 |
| LuceneNet_QueueDeletes  | Job-CNUJVU | Default        | Default     | Default     | 20000         | 553.90 μs |    10.499 μs |  8.767 μs | 551.61 μs |  6.40 |    0.59 | 590.98 KB |        2.43 |
|                         |            |                |             |             |               |           |              |           |           |       |         |           |             |
| LeanLucene_QueueDeletes | ShortRun   | 3              | 1           | 3           | 20000         | 176.24 μs |    41.503 μs |  2.275 μs | 175.53 μs |  1.00 |    0.00 | 243.02 KB |        1.00 |
| LuceneNet_QueueDeletes  | ShortRun   | 3              | 1           | 3           | 20000         | 911.10 μs | 1,191.461 μs | 65.308 μs | 911.12 μs |  5.17 |    0.33 | 590.98 KB |        2.43 |

## dismax

| Method                         | Job        | IterationCount | LaunchCount | WarmupCount | TieBreakerMultiplier | DocumentCount | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------------------- |----------- |--------------- |------------ |------------ |--------------------- |-------------- |---------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_DisjunctionMaxQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **0**                    | **20000**         | **32.95 μs** | **0.132 μs** | **0.123 μs** |  **1.00** |    **0.00** | **1.1597** |   **4.71 KB** |        **1.00** |
| LuceneNet_DisjunctionMaxQuery  | DefaultJob | Default        | Default     | Default     | 0                    | 20000         | 55.89 μs | 0.086 μs | 0.081 μs |  1.70 |    0.01 | 9.6436 |  39.48 KB |        8.38 |
|                                |            |                |             |             |                      |               |          |          |          |       |         |        |           |             |
| LeanCorpus_DisjunctionMaxQuery | ShortRun   | 3              | 1           | 3           | 0                    | 20000         | 31.83 μs | 3.429 μs | 0.188 μs |  1.00 |    0.00 | 1.1597 |   4.71 KB |        1.00 |
| LuceneNet_DisjunctionMaxQuery  | ShortRun   | 3              | 1           | 3           | 0                    | 20000         | 56.96 μs | 2.105 μs | 0.115 μs |  1.79 |    0.01 | 9.6436 |  39.48 KB |        8.39 |
|                                |            |                |             |             |                      |               |          |          |          |       |         |        |           |             |
| **LeanCorpus_DisjunctionMaxQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **0.1**                  | **20000**         | **30.95 μs** | **0.174 μs** | **0.154 μs** |  **1.00** |    **0.00** | **1.1597** |    **4.7 KB** |        **1.00** |
| LuceneNet_DisjunctionMaxQuery  | DefaultJob | Default        | Default     | Default     | 0.1                  | 20000         | 56.23 μs | 0.130 μs | 0.122 μs |  1.82 |    0.01 | 9.6436 |  39.48 KB |        8.39 |
|                                |            |                |             |             |                      |               |          |          |          |       |         |        |           |             |
| LeanCorpus_DisjunctionMaxQuery | ShortRun   | 3              | 1           | 3           | 0.1                  | 20000         | 33.12 μs | 7.478 μs | 0.410 μs |  1.00 |    0.00 | 1.1597 |   4.71 KB |        1.00 |
| LuceneNet_DisjunctionMaxQuery  | ShortRun   | 3              | 1           | 3           | 0.1                  | 20000         | 56.32 μs | 1.634 μs | 0.090 μs |  1.70 |    0.02 | 9.6436 |  39.48 KB |        8.37 |
|                                |            |                |             |             |                      |               |          |          |          |       |         |        |           |             |
| **LeanCorpus_DisjunctionMaxQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **0.5**                  | **20000**         | **31.76 μs** | **0.147 μs** | **0.138 μs** |  **1.00** |    **0.00** | **1.1597** |   **4.71 KB** |        **1.00** |
| LuceneNet_DisjunctionMaxQuery  | DefaultJob | Default        | Default     | Default     | 0.5                  | 20000         | 58.24 μs | 0.062 μs | 0.058 μs |  1.83 |    0.01 | 9.6436 |  39.48 KB |        8.39 |
|                                |            |                |             |             |                      |               |          |          |          |       |         |        |           |             |
| LeanCorpus_DisjunctionMaxQuery | ShortRun   | 3              | 1           | 3           | 0.5                  | 20000         | 32.29 μs | 1.529 μs | 0.084 μs |  1.00 |    0.00 | 1.1597 |   4.71 KB |        1.00 |
| LuceneNet_DisjunctionMaxQuery  | ShortRun   | 3              | 1           | 3           | 0.5                  | 20000         | 56.08 μs | 2.554 μs | 0.140 μs |  1.74 |    0.01 | 9.6436 |  39.48 KB |        8.39 |

## function-score

| Method                        | Job        | IterationCount | LaunchCount | WarmupCount | Mode     | DocumentCount | Mean      | Error      | StdDev    | Ratio | RatioSD | Gen0    | Gen1    | Allocated | Alloc Ratio |
|------------------------------ |----------- |--------------- |------------ |------------ |--------- |-------------- |----------:|-----------:|----------:|------:|--------:|--------:|--------:|----------:|------------:|
| **LeanCorpus_BaseTermQuery**      | **DefaultJob** | **Default**        | **Default**     | **Default**     | **Max**      | **20000**         |  **2.620 μs** |  **0.0039 μs** | **0.0037 μs** |  **1.00** |    **0.00** |  **0.1259** |       **-** |     **528 B** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | DefaultJob | Default        | Default     | Default     | Max      | 20000         | 23.595 μs |  0.2462 μs | 0.2056 μs |  9.01 |    0.08 | 41.6565 | 12.5427 |  167439 B |      317.12 |
|                               |            |                |             |             |          |               |           |            |           |       |         |         |         |           |             |
| LeanCorpus_BaseTermQuery      | ShortRun   | 3              | 1           | 3           | Max      | 20000         |  2.672 μs |  0.0197 μs | 0.0011 μs |  1.00 |    0.00 |  0.1259 |       - |     528 B |        1.00 |
| LeanCorpus_FunctionScoreQuery | ShortRun   | 3              | 1           | 3           | Max      | 20000         | 23.088 μs |  0.3969 μs | 0.0218 μs |  8.64 |    0.01 | 41.6565 |  9.8267 |  167440 B |      317.12 |
|                               |            |                |             |             |          |               |           |            |           |       |         |         |         |           |             |
| **LeanCorpus_BaseTermQuery**      | **DefaultJob** | **Default**        | **Default**     | **Default**     | **Multiply** | **20000**         |  **2.673 μs** |  **0.0015 μs** | **0.0012 μs** |  **1.00** |    **0.00** |  **0.1259** |       **-** |     **528 B** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | DefaultJob | Default        | Default     | Default     | Multiply | 20000         | 23.086 μs |  0.3762 μs | 0.3519 μs |  8.64 |    0.13 | 41.6565 | 11.5662 |  167439 B |      317.12 |
|                               |            |                |             |             |          |               |           |            |           |       |         |         |         |           |             |
| LeanCorpus_BaseTermQuery      | ShortRun   | 3              | 1           | 3           | Multiply | 20000         |  2.670 μs |  0.0319 μs | 0.0017 μs |  1.00 |    0.00 |  0.1259 |       - |     528 B |        1.00 |
| LeanCorpus_FunctionScoreQuery | ShortRun   | 3              | 1           | 3           | Multiply | 20000         | 23.272 μs | 11.7528 μs | 0.6442 μs |  8.71 |    0.21 | 41.6565 |  8.8196 |  167441 B |      317.12 |
|                               |            |                |             |             |          |               |           |            |           |       |         |         |         |           |             |
| **LeanCorpus_BaseTermQuery**      | **DefaultJob** | **Default**        | **Default**     | **Default**     | **Replace**  | **20000**         |  **2.630 μs** |  **0.0018 μs** | **0.0014 μs** |  **1.00** |    **0.00** |  **0.1259** |       **-** |     **528 B** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | DefaultJob | Default        | Default     | Default     | Replace  | 20000         | 23.540 μs |  0.3458 μs | 0.3234 μs |  8.95 |    0.12 | 41.6565 | 12.2070 |  167439 B |      317.12 |
|                               |            |                |             |             |          |               |           |            |           |       |         |         |         |           |             |
| LeanCorpus_BaseTermQuery      | ShortRun   | 3              | 1           | 3           | Replace  | 20000         |  2.673 μs |  0.0498 μs | 0.0027 μs |  1.00 |    0.00 |  0.1259 |       - |     528 B |        1.00 |
| LeanCorpus_FunctionScoreQuery | ShortRun   | 3              | 1           | 3           | Replace  | 20000         | 22.839 μs |  3.5113 μs | 0.1925 μs |  8.54 |    0.06 | 41.6565 | 10.8948 |  167441 B |      317.12 |
|                               |            |                |             |             |          |               |           |            |           |       |         |         |         |           |             |
| **LeanCorpus_BaseTermQuery**      | **DefaultJob** | **Default**        | **Default**     | **Default**     | **Sum**      | **20000**         |  **2.649 μs** |  **0.0029 μs** | **0.0027 μs** |  **1.00** |    **0.00** |  **0.1259** |       **-** |     **528 B** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | DefaultJob | Default        | Default     | Default     | Sum      | 20000         | 23.467 μs |  0.3253 μs | 0.3043 μs |  8.86 |    0.11 | 41.6565 | 13.8550 |  167438 B |      317.12 |
|                               |            |                |             |             |          |               |           |            |           |       |         |         |         |           |             |
| LeanCorpus_BaseTermQuery      | ShortRun   | 3              | 1           | 3           | Sum      | 20000         |  2.635 μs |  0.0886 μs | 0.0049 μs |  1.00 |    0.00 |  0.1259 |       - |     528 B |        1.00 |
| LeanCorpus_FunctionScoreQuery | ShortRun   | 3              | 1           | 3           | Sum      | 20000         | 23.898 μs |  1.8651 μs | 0.1022 μs |  9.07 |    0.04 | 41.6565 |  9.9182 |  167439 B |      317.12 |

## Fuzzy queries

| Method                | Job        | IterationCount | LaunchCount | WarmupCount | Scenario            | DocumentCount | Mean         | Error       | StdDev     | Ratio    | RatioSD | Gen0     | Gen1     | Allocated  | Alloc Ratio |
|---------------------- |----------- |--------------- |------------ |------------ |-------------------- |-------------- |-------------:|------------:|-----------:|---------:|--------:|---------:|---------:|-----------:|------------:|
| **LeanCorpus_FuzzyQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **long-edit1-common**   | **20000**         |     **2.408 μs** |   **0.0108 μs** |  **0.0091 μs** |     **1.00** |    **0.00** |   **0.7439** |        **-** |    **3.03 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | DefaultJob | Default        | Default     | Default     | long-edit1-common   | 20000         |   286.408 μs |   0.3652 μs |  0.3237 μs |   118.95 |    0.45 |  53.2227 |        - |  217.58 KB |       71.83 |
|                       |            |                |             |             |                     |               |              |             |            |          |         |          |          |            |             |
| LeanCorpus_FuzzyQuery | ShortRun   | 3              | 1           | 3           | long-edit1-common   | 20000         |     2.507 μs |   0.2346 μs |  0.0129 μs |     1.00 |    0.00 |   0.7477 |        - |    3.03 KB |        1.00 |
| LuceneNet_FuzzyQuery  | ShortRun   | 3              | 1           | 3           | long-edit1-common   | 20000         |   286.526 μs |   1.4017 μs |  0.0768 μs |   114.31 |    0.51 |  53.2227 |        - |  217.58 KB |       71.83 |
|                       |            |                |             |             |                     |               |              |             |            |          |         |          |          |            |             |
| **LeanCorpus_FuzzyQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **medium-edit1-common** | **20000**         |     **5.705 μs** |   **0.0241 μs** |  **0.0201 μs** |     **1.00** |    **0.00** |   **0.8545** |        **-** |    **3.46 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | DefaultJob | Default        | Default     | Default     | medium-edit1-common | 20000         |   355.151 μs |   0.1345 μs |  0.1050 μs |    62.26 |    0.21 |  65.4297 |   6.3477 |  268.87 KB |       77.75 |
|                       |            |                |             |             |                     |               |              |             |            |          |         |          |          |            |             |
| LeanCorpus_FuzzyQuery | ShortRun   | 3              | 1           | 3           | medium-edit1-common | 20000         |     6.010 μs |   0.8232 μs |  0.0451 μs |     1.00 |    0.00 |   0.8621 |        - |    3.49 KB |        1.00 |
| LuceneNet_FuzzyQuery  | ShortRun   | 3              | 1           | 3           | medium-edit1-common | 20000         |   357.809 μs |   8.0223 μs |  0.4397 μs |    59.53 |    0.39 |  65.4297 |   6.3477 |  268.87 KB |       77.10 |
|                       |            |                |             |             |                     |               |              |             |            |          |         |          |          |            |             |
| **LeanCorpus_FuzzyQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **medium-edit2-common** | **20000**         |    **15.187 μs** |   **0.0550 μs** |  **0.0515 μs** |     **1.00** |    **0.00** |   **0.9613** |        **-** |    **3.91 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | DefaultJob | Default        | Default     | Default     | medium-edit2-common | 20000         | 2,306.872 μs |  20.7965 μs | 19.4530 μs |   151.90 |    1.34 | 304.6875 | 101.5625 | 1387.38 KB |      354.46 |
|                       |            |                |             |             |                     |               |              |             |            |          |         |          |          |            |             |
| LeanCorpus_FuzzyQuery | ShortRun   | 3              | 1           | 3           | medium-edit2-common | 20000         |    14.991 μs |   0.9784 μs |  0.0536 μs |     1.00 |    0.00 |   0.9613 |        - |    3.91 KB |        1.00 |
| LuceneNet_FuzzyQuery  | ShortRun   | 3              | 1           | 3           | medium-edit2-common | 20000         | 2,292.846 μs | 427.7223 μs | 23.4449 μs |   152.95 |    1.43 | 304.6875 | 101.5625 | 1387.38 KB |      354.46 |
|                       |            |                |             |             |                     |               |              |             |            |          |         |          |          |            |             |
| **LeanCorpus_FuzzyQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **nohit-edit2**         | **20000**         |     **2.080 μs** |   **0.0090 μs** |  **0.0084 μs** |     **1.00** |    **0.00** |   **0.7095** |        **-** |    **2.88 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | DefaultJob | Default        | Default     | Default     | nohit-edit2         | 20000         | 2,205.984 μs |   3.8226 μs |  3.5757 μs | 1,060.79 |    4.50 | 371.0938 | 132.8125 | 1801.43 KB |      624.88 |
|                       |            |                |             |             |                     |               |              |             |            |          |         |          |          |            |             |
| LeanCorpus_FuzzyQuery | ShortRun   | 3              | 1           | 3           | nohit-edit2         | 20000         |     2.081 μs |   0.1155 μs |  0.0063 μs |     1.00 |    0.00 |   0.7095 |        - |    2.88 KB |        1.00 |
| LuceneNet_FuzzyQuery  | ShortRun   | 3              | 1           | 3           | nohit-edit2         | 20000         | 2,208.048 μs | 141.2486 μs |  7.7423 μs | 1,061.23 |    4.27 | 371.0938 | 132.8125 | 1801.43 KB |      626.16 |
|                       |            |                |             |             |                     |               |              |             |            |          |         |          |          |            |             |
| **LeanCorpus_FuzzyQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **short-edit1-common**  | **20000**         |    **32.125 μs** |   **0.1325 μs** |  **0.1240 μs** |     **1.00** |    **0.00** |   **0.9766** |        **-** |    **4.13 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | DefaultJob | Default        | Default     | Default     | short-edit1-common  | 20000         |   446.431 μs |   2.0286 μs |  1.6940 μs |    13.90 |    0.07 |  82.0313 |   1.4648 |  337.41 KB |       81.60 |
|                       |            |                |             |             |                     |               |              |             |            |          |         |          |          |            |             |
| LeanCorpus_FuzzyQuery | ShortRun   | 3              | 1           | 3           | short-edit1-common  | 20000         |    31.742 μs |   4.0884 μs |  0.2241 μs |     1.00 |    0.00 |   0.9766 |        - |    4.14 KB |        1.00 |
| LuceneNet_FuzzyQuery  | ShortRun   | 3              | 1           | 3           | short-edit1-common  | 20000         |   445.415 μs |  22.1922 μs |  1.2164 μs |    14.03 |    0.09 |  82.0313 |   0.9766 |   337.4 KB |       81.58 |

## geo

| Method                         | Job        | IterationCount | LaunchCount | WarmupCount | GeoQueryType | DocumentCount | Mean     | Error    | StdDev   | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------------- |----------- |--------------- |------------ |------------ |------------- |-------------- |---------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_GeoDistanceQuery**    | **DefaultJob** | **Default**        | **Default**     | **Default**     | **BoundingBox**  | **20000**         | **10.10 μs** | **0.024 μs** | **0.020 μs** |  **1.00** |  **3.5553** |      **-** |  **14.13 KB** |        **1.00** |
| LeanCorpus_GeoBoundingBoxQuery | DefaultJob | Default        | Default     | Default     | BoundingBox  | 20000         | 18.98 μs | 0.055 μs | 0.052 μs |  1.88 | 10.4065 |      - |  41.01 KB |        2.90 |
|                                |            |                |             |             |              |               |          |          |          |       |         |        |           |             |
| LeanCorpus_GeoDistanceQuery    | ShortRun   | 3              | 1           | 3           | BoundingBox  | 20000         | 10.07 μs | 0.617 μs | 0.034 μs |  1.00 |  3.5553 |      - |  14.13 KB |        1.00 |
| LeanCorpus_GeoBoundingBoxQuery | ShortRun   | 3              | 1           | 3           | BoundingBox  | 20000         | 18.98 μs | 1.709 μs | 0.094 μs |  1.89 | 10.4065 |      - |  41.01 KB |        2.90 |
|                                |            |                |             |             |              |               |          |          |          |       |         |        |           |             |
| **LeanCorpus_GeoDistanceQuery**    | **DefaultJob** | **Default**        | **Default**     | **Default**     | **Distance**     | **20000**         | **10.01 μs** | **0.026 μs** | **0.025 μs** |  **1.00** |  **3.5553** |      **-** |  **14.13 KB** |        **1.00** |
| LeanCorpus_GeoBoundingBoxQuery | DefaultJob | Default        | Default     | Default     | Distance     | 20000         | 19.04 μs | 0.068 μs | 0.064 μs |  1.90 | 10.4065 | 0.0305 |  41.01 KB |        2.90 |
|                                |            |                |             |             |              |               |          |          |          |       |         |        |           |             |
| LeanCorpus_GeoDistanceQuery    | ShortRun   | 3              | 1           | 3           | Distance     | 20000         | 10.13 μs | 0.828 μs | 0.045 μs |  1.00 |  3.5553 |      - |  14.13 KB |        1.00 |
| LeanCorpus_GeoBoundingBoxQuery | ShortRun   | 3              | 1           | 3           | Distance     | 20000         | 19.17 μs | 0.386 μs | 0.021 μs |  1.89 | 10.4065 | 0.0305 |  41.01 KB |        2.90 |

## gutenberg-analysis

| Method                      | Job        | IterationCount | LaunchCount | WarmupCount | Mean     | Error     | StdDev   | Median   | Ratio | RatioSD | Gen0       | Gen1      | Gen2      | Allocated | Alloc Ratio |
|---------------------------- |----------- |--------------- |------------ |------------ |---------:|----------:|---------:|---------:|------:|--------:|-----------:|----------:|----------:|----------:|------------:|
| LeanCorpus_Standard_Analyse | DefaultJob | Default        | Default     | Default     | 126.9 ms |   0.44 ms |  0.42 ms | 127.1 ms |  1.00 |    0.00 |  1250.0000 |  750.0000 |         - |   7.27 MB |        1.00 |
| LeanCorpus_English_Analyse  | DefaultJob | Default        | Default     | Default     | 458.9 ms |  11.83 ms | 34.87 ms | 435.5 ms |  3.62 |    0.27 | 13000.0000 | 9000.0000 | 4000.0000 | 199.01 MB |       27.39 |
|                             |            |                |             |             |          |           |          |          |       |         |            |           |           |           |             |
| LeanCorpus_Standard_Analyse | ShortRun   | 3              | 1           | 3           | 127.9 ms |   7.57 ms |  0.42 ms | 128.0 ms |  1.00 |    0.00 |  1250.0000 |  750.0000 |         - |   7.27 MB |        1.00 |
| LeanCorpus_English_Analyse  | ShortRun   | 3              | 1           | 3           | 449.2 ms | 838.75 ms | 45.97 ms | 427.0 ms |  3.51 |    0.31 | 11000.0000 | 7000.0000 | 3000.0000 |    199 MB |       27.39 |

## gutenberg-index

| Method                    | Job        | IterationCount | LaunchCount | WarmupCount | Mean     | Error     | StdDev   | Ratio | RatioSD | Gen0       | Gen1       | Gen2      | Allocated | Alloc Ratio |
|-------------------------- |----------- |--------------- |------------ |------------ |---------:|----------:|---------:|------:|--------:|-----------:|-----------:|----------:|----------:|------------:|
| LeanCorpus_Standard_Index | DefaultJob | Default        | Default     | Default     | 746.4 ms |   6.04 ms |  5.65 ms |  1.00 |    0.00 | 17000.0000 |  9000.0000 | 1000.0000 | 111.89 MB |        1.00 |
| LeanCorpus_English_Index  | DefaultJob | Default        | Default     | Default     | 967.5 ms |   5.89 ms |  5.51 ms |  1.30 |    0.01 | 47000.0000 | 14000.0000 | 1000.0000 | 296.35 MB |        2.65 |
| LuceneNet_Index           | DefaultJob | Default        | Default     | Default     | 612.5 ms |   4.71 ms |  4.18 ms |  0.82 |    0.01 | 42000.0000 |  3000.0000 |         - | 208.13 MB |        1.86 |
|                           |            |                |             |             |          |           |          |       |         |            |            |           |           |             |
| LeanCorpus_Standard_Index | ShortRun   | 3              | 1           | 3           | 736.6 ms | 178.69 ms |  9.79 ms |  1.00 |    0.00 | 16000.0000 |  8000.0000 | 1000.0000 | 111.94 MB |        1.00 |
| LeanCorpus_English_Index  | ShortRun   | 3              | 1           | 3           | 969.0 ms | 191.97 ms | 10.52 ms |  1.32 |    0.02 | 47000.0000 | 14000.0000 | 1000.0000 | 297.46 MB |        2.66 |
| LuceneNet_Index           | ShortRun   | 3              | 1           | 3           | 627.1 ms | 140.17 ms |  7.68 ms |  0.85 |    0.01 | 42000.0000 |  3000.0000 |         - | 208.13 MB |        1.86 |

## gutenberg-search

| Method                     | Job        | IterationCount | LaunchCount | WarmupCount | SearchTerm | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------------------- |----------- |--------------- |------------ |------------ |----------- |---------:|---------:|---------:|------:|--------:|-------:|-------:|----------:|------------:|
| **LeanCorpus_Standard_Search** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **death**      | **13.08 μs** | **0.031 μs** | **0.029 μs** |  **1.00** |    **0.00** | **0.1221** |      **-** |     **520 B** |        **1.00** |
| LeanCorpus_English_Search  | DefaultJob | Default        | Default     | Default     | death      | 13.09 μs | 0.031 μs | 0.027 μs |  1.00 |    0.00 | 0.1221 |      - |     520 B |        1.00 |
| LuceneNet_Search           | DefaultJob | Default        | Default     | Default     | death      | 22.43 μs | 0.425 μs | 0.398 μs |  1.71 |    0.03 | 2.6550 | 0.0305 |   11231 B |       21.60 |
|                            |            |                |             |             |            |          |          |          |       |         |        |        |           |             |
| LeanCorpus_Standard_Search | ShortRun   | 3              | 1           | 3           | death      | 13.12 μs | 0.466 μs | 0.026 μs |  1.00 |    0.00 | 0.1221 |      - |     520 B |        1.00 |
| LeanCorpus_English_Search  | ShortRun   | 3              | 1           | 3           | death      | 13.14 μs | 0.431 μs | 0.024 μs |  1.00 |    0.00 | 0.1221 |      - |     520 B |        1.00 |
| LuceneNet_Search           | ShortRun   | 3              | 1           | 3           | death      | 22.60 μs | 9.628 μs | 0.528 μs |  1.72 |    0.03 | 2.6550 | 0.0305 |   11231 B |       21.60 |
|                            |            |                |             |             |            |          |          |          |       |         |        |        |           |             |
| **LeanCorpus_Standard_Search** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **love**       | **17.42 μs** | **0.029 μs** | **0.027 μs** |  **1.00** |    **0.00** | **0.1221** |      **-** |     **512 B** |        **1.00** |
| LeanCorpus_English_Search  | DefaultJob | Default        | Default     | Default     | love       | 23.24 μs | 0.034 μs | 0.032 μs |  1.33 |    0.00 | 0.1221 |      - |     512 B |        1.00 |
| LuceneNet_Search           | DefaultJob | Default        | Default     | Default     | love       | 28.69 μs | 0.035 μs | 0.032 μs |  1.65 |    0.00 | 2.6245 | 0.0305 |   11175 B |       21.83 |
|                            |            |                |             |             |            |          |          |          |       |         |        |        |           |             |
| LeanCorpus_Standard_Search | ShortRun   | 3              | 1           | 3           | love       | 17.37 μs | 0.801 μs | 0.044 μs |  1.00 |    0.00 | 0.1221 |      - |     512 B |        1.00 |
| LeanCorpus_English_Search  | ShortRun   | 3              | 1           | 3           | love       | 23.11 μs | 0.555 μs | 0.030 μs |  1.33 |    0.00 | 0.1221 |      - |     512 B |        1.00 |
| LuceneNet_Search           | ShortRun   | 3              | 1           | 3           | love       | 28.94 μs | 0.197 μs | 0.011 μs |  1.67 |    0.00 | 2.6245 | 0.0305 |   11175 B |       21.83 |
|                            |            |                |             |             |            |          |          |          |       |         |        |        |           |             |
| **LeanCorpus_Standard_Search** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **man**        | **46.04 μs** | **0.071 μs** | **0.067 μs** |  **1.00** |    **0.00** | **0.1221** |      **-** |     **512 B** |        **1.00** |
| LeanCorpus_English_Search  | DefaultJob | Default        | Default     | Default     | man        | 45.75 μs | 0.071 μs | 0.066 μs |  0.99 |    0.00 | 0.1221 |      - |     512 B |        1.00 |
| LuceneNet_Search           | DefaultJob | Default        | Default     | Default     | man        | 50.46 μs | 0.029 μs | 0.024 μs |  1.10 |    0.00 | 2.6245 | 0.0610 |   11038 B |       21.56 |
|                            |            |                |             |             |            |          |          |          |       |         |        |        |           |             |
| LeanCorpus_Standard_Search | ShortRun   | 3              | 1           | 3           | man        | 45.92 μs | 0.918 μs | 0.050 μs |  1.00 |    0.00 | 0.1221 |      - |     512 B |        1.00 |
| LeanCorpus_English_Search  | ShortRun   | 3              | 1           | 3           | man        | 45.50 μs | 0.945 μs | 0.052 μs |  0.99 |    0.00 | 0.1221 |      - |     512 B |        1.00 |
| LuceneNet_Search           | ShortRun   | 3              | 1           | 3           | man        | 49.56 μs | 0.208 μs | 0.011 μs |  1.08 |    0.00 | 2.6245 | 0.0610 |   11038 B |       21.56 |
|                            |            |                |             |             |            |          |          |          |       |         |        |        |           |             |
| **LeanCorpus_Standard_Search** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **night**      | **28.93 μs** | **0.026 μs** | **0.023 μs** |  **1.00** |    **0.00** | **0.1221** |      **-** |     **520 B** |        **1.00** |
| LeanCorpus_English_Search  | DefaultJob | Default        | Default     | Default     | night      | 30.19 μs | 0.044 μs | 0.042 μs |  1.04 |    0.00 | 0.1221 |      - |     520 B |        1.00 |
| LuceneNet_Search           | DefaultJob | Default        | Default     | Default     | night      | 36.20 μs | 0.043 μs | 0.040 μs |  1.25 |    0.00 | 2.6245 | 0.0610 |   11223 B |       21.58 |
|                            |            |                |             |             |            |          |          |          |       |         |        |        |           |             |
| LeanCorpus_Standard_Search | ShortRun   | 3              | 1           | 3           | night      | 29.42 μs | 2.733 μs | 0.150 μs |  1.00 |    0.00 | 0.1221 |      - |     520 B |        1.00 |
| LeanCorpus_English_Search  | ShortRun   | 3              | 1           | 3           | night      | 29.90 μs | 0.429 μs | 0.024 μs |  1.02 |    0.00 | 0.1221 |      - |     520 B |        1.00 |
| LuceneNet_Search           | ShortRun   | 3              | 1           | 3           | night      | 36.54 μs | 0.354 μs | 0.019 μs |  1.24 |    0.01 | 2.6245 | 0.0610 |   11223 B |       21.58 |
|                            |            |                |             |             |            |          |          |          |       |         |        |        |           |             |
| **LeanCorpus_Standard_Search** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **sea**        | **14.54 μs** | **0.025 μs** | **0.023 μs** |  **1.00** |    **0.00** | **0.1221** |      **-** |     **512 B** |        **1.00** |
| LeanCorpus_English_Search  | DefaultJob | Default        | Default     | Default     | sea        | 15.93 μs | 0.015 μs | 0.013 μs |  1.10 |    0.00 | 0.1221 |      - |     512 B |        1.00 |
| LuceneNet_Search           | DefaultJob | Default        | Default     | Default     | sea        | 26.45 μs | 0.034 μs | 0.032 μs |  1.82 |    0.00 | 2.6550 | 0.0305 |   11271 B |       22.01 |
|                            |            |                |             |             |            |          |          |          |       |         |        |        |           |             |
| LeanCorpus_Standard_Search | ShortRun   | 3              | 1           | 3           | sea        | 14.46 μs | 0.207 μs | 0.011 μs |  1.00 |    0.00 | 0.1221 |      - |     512 B |        1.00 |
| LeanCorpus_English_Search  | ShortRun   | 3              | 1           | 3           | sea        | 16.03 μs | 0.323 μs | 0.018 μs |  1.11 |    0.00 | 0.1221 |      - |     512 B |        1.00 |
| LuceneNet_Search           | ShortRun   | 3              | 1           | 3           | sea        | 25.90 μs | 0.133 μs | 0.007 μs |  1.79 |    0.00 | 2.6550 | 0.0305 |   11271 B |       22.01 |

## highlighter

| Method                         | Job        | IterationCount | LaunchCount | WarmupCount | MaxSnippetLength | DocumentCount | Mean     | Error    | StdDev  | Ratio | Gen0      | Gen1     | Allocated | Alloc Ratio |
|------------------------------- |----------- |--------------- |------------ |------------ |----------------- |-------------- |---------:|---------:|--------:|------:|----------:|---------:|----------:|------------:|
| **LeanCorpus_Highlight_TwoTerms**  | **DefaultJob** | **Default**        | **Default**     | **Default**     | **100**              | **20000**         | **205.2 ms** |  **0.33 ms** | **0.31 ms** |  **1.00** | **6000.0000** |        **-** |  **24.26 MB** |        **1.00** |
| LeanCorpus_Highlight_FiveTerms | DefaultJob | Default        | Default     | Default     | 100              | 20000         | 210.4 ms |  0.30 ms | 0.27 ms |  1.03 | 6333.3333 |        - |  25.27 MB |        1.04 |
|                                |            |                |             |             |                  |               |          |          |         |       |           |          |           |             |
| LeanCorpus_Highlight_TwoTerms  | ShortRun   | 3              | 1           | 3           | 100              | 20000         | 207.9 ms |  6.89 ms | 0.38 ms |  1.00 | 6000.0000 |        - |  24.26 MB |        1.00 |
| LeanCorpus_Highlight_FiveTerms | ShortRun   | 3              | 1           | 3           | 100              | 20000         | 216.2 ms |  2.52 ms | 0.14 ms |  1.04 | 6333.3333 |        - |  25.27 MB |        1.04 |
|                                |            |                |             |             |                  |               |          |          |         |       |           |          |           |             |
| **LeanCorpus_Highlight_TwoTerms**  | **DefaultJob** | **Default**        | **Default**     | **Default**     | **200**              | **20000**         | **213.9 ms** |  **0.54 ms** | **0.50 ms** |  **1.00** | **7000.0000** | **333.3333** |  **29.56 MB** |        **1.00** |
| LeanCorpus_Highlight_FiveTerms | DefaultJob | Default        | Default     | Default     | 200              | 20000         | 220.8 ms |  0.38 ms | 0.36 ms |  1.03 | 7333.3333 |        - |  30.42 MB |        1.03 |
|                                |            |                |             |             |                  |               |          |          |         |       |           |          |           |             |
| LeanCorpus_Highlight_TwoTerms  | ShortRun   | 3              | 1           | 3           | 200              | 20000         | 219.4 ms | 10.01 ms | 0.55 ms |  1.00 | 7000.0000 | 333.3333 |  29.56 MB |        1.00 |
| LeanCorpus_Highlight_FiveTerms | ShortRun   | 3              | 1           | 3           | 200              | 20000         | 217.3 ms |  3.35 ms | 0.18 ms |  0.99 | 7333.3333 |        - |  30.42 MB |        1.03 |
|                                |            |                |             |             |                  |               |          |          |         |       |           |          |           |             |
| **LeanCorpus_Highlight_TwoTerms**  | **DefaultJob** | **Default**        | **Default**     | **Default**     | **500**              | **20000**         | **235.0 ms** |  **0.27 ms** | **0.24 ms** |  **1.00** | **9666.6667** |        **-** |  **38.99 MB** |        **1.00** |
| LeanCorpus_Highlight_FiveTerms | DefaultJob | Default        | Default     | Default     | 500              | 20000         | 231.7 ms |  0.52 ms | 0.48 ms |  0.99 | 9666.6667 |        - |  39.53 MB |        1.01 |
|                                |            |                |             |             |                  |               |          |          |         |       |           |          |           |             |
| LeanCorpus_Highlight_TwoTerms  | ShortRun   | 3              | 1           | 3           | 500              | 20000         | 220.0 ms |  5.00 ms | 0.27 ms |  1.00 | 9666.6667 |        - |  38.99 MB |        1.00 |
| LeanCorpus_Highlight_FiveTerms | ShortRun   | 3              | 1           | 3           | 500              | 20000         | 229.0 ms |  9.50 ms | 0.52 ms |  1.04 | 9666.6667 |        - |  39.53 MB |        1.01 |

## hunspell

| Method           | Job        | IterationCount | LaunchCount | WarmupCount | Mean     | Error   | StdDev  | Gen0   | Allocated |
|----------------- |----------- |--------------- |------------ |------------ |---------:|--------:|--------:|-------:|----------:|
| Parse_Dictionary | DefaultJob | Default        | Default     | Default     | 298.4 ns | 0.18 ns | 0.15 ns | 0.0420 |     176 B |
| Stem_Words       | DefaultJob | Default        | Default     | Default     | 102.5 ns | 0.10 ns | 0.09 ns |      - |         - |
| Parse_Dictionary | ShortRun   | 3              | 1           | 3           | 295.0 ns | 9.74 ns | 0.53 ns | 0.0420 |     176 B |
| Stem_Words       | ShortRun   | 3              | 1           | 3           | 102.7 ns | 0.32 ns | 0.02 ns |      - |         - |

## Indexing

| Method                    | Job        | IterationCount | LaunchCount | WarmupCount | DocumentCount | Mean       | Error     | StdDev   | Ratio | RatioSD | Gen0       | Gen1      | Allocated | Alloc Ratio |
|-------------------------- |----------- |--------------- |------------ |------------ |-------------- |-----------:|----------:|---------:|------:|--------:|-----------:|----------:|----------:|------------:|
| LeanCorpus_IndexDocuments | DefaultJob | Default        | Default     | Default     | 20000         | 1,011.4 ms |   4.62 ms |  4.32 ms |  1.00 |    0.00 | 18000.0000 | 9000.0000 | 133.69 MB |        1.00 |
| LuceneNet_IndexDocuments  | DefaultJob | Default        | Default     | Default     | 20000         |   800.5 ms |   6.37 ms |  4.97 ms |  0.79 |    0.01 | 46000.0000 | 4000.0000 | 239.32 MB |        1.79 |
|                           |            |                |             |             |               |            |           |          |       |         |            |           |           |             |
| LeanCorpus_IndexDocuments | ShortRun   | 3              | 1           | 3           | 20000         | 1,006.4 ms | 484.83 ms | 26.58 ms |  1.00 |    0.00 | 18000.0000 | 9000.0000 | 133.69 MB |        1.00 |
| LuceneNet_IndexDocuments  | ShortRun   | 3              | 1           | 3           | 20000         |   797.4 ms |  23.48 ms |  1.29 ms |  0.79 |    0.02 | 46000.0000 | 4000.0000 | 239.32 MB |        1.79 |

## Index-sort (index)

| Method                    | Job        | IterationCount | LaunchCount | WarmupCount | DocumentCount | Mean    | Error    | StdDev   | Ratio | RatioSD | Gen0       | Gen1       | Gen2      | Allocated | Alloc Ratio |
|-------------------------- |----------- |--------------- |------------ |------------ |-------------- |--------:|---------:|---------:|------:|--------:|-----------:|-----------:|----------:|----------:|------------:|
| LeanCorpus_Index_Unsorted | DefaultJob | Default        | Default     | Default     | 20000         | 1.056 s | 0.0075 s | 0.0071 s |  1.00 |    0.00 | 21000.0000 | 11000.0000 | 1000.0000 | 149.89 MB |        1.00 |
| LeanCorpus_Index_Sorted   | DefaultJob | Default        | Default     | Default     | 20000         | 1.180 s | 0.0058 s | 0.0052 s |  1.12 |    0.01 | 21000.0000 | 11000.0000 | 1000.0000 | 154.59 MB |        1.03 |
|                           |            |                |             |             |               |         |          |          |       |         |            |            |           |           |             |
| LeanCorpus_Index_Unsorted | ShortRun   | 3              | 1           | 3           | 20000         | 1.080 s | 0.7231 s | 0.0396 s |  1.00 |    0.00 | 21000.0000 | 11000.0000 | 1000.0000 | 149.89 MB |        1.00 |
| LeanCorpus_Index_Sorted   | ShortRun   | 3              | 1           | 3           | 20000         | 1.221 s | 1.2714 s | 0.0697 s |  1.13 |    0.07 | 21000.0000 | 11000.0000 | 1000.0000 | 154.59 MB |        1.03 |

## Index-sort (search)

| Method                                   | Job        | IterationCount | LaunchCount | WarmupCount | DocumentCount | Mean     | Error     | StdDev    | Ratio | Gen0   | Allocated | Alloc Ratio |
|----------------------------------------- |----------- |--------------- |------------ |------------ |-------------- |---------:|----------:|----------:|------:|-------:|----------:|------------:|
| LeanCorpus_SortedSearch_EarlyTermination | DefaultJob | Default        | Default     | Default     | 20000         | 6.509 μs | 0.0117 μs | 0.0104 μs |  1.00 | 1.2360 |   5.05 KB |        1.00 |
| LeanCorpus_SortedSearch_PostSort         | DefaultJob | Default        | Default     | Default     | 20000         | 6.382 μs | 0.0073 μs | 0.0065 μs |  0.98 | 1.2360 |   5.05 KB |        1.00 |
|                                          |            |                |             |             |               |          |           |           |       |        |           |             |
| LeanCorpus_SortedSearch_EarlyTermination | ShortRun   | 3              | 1           | 3           | 20000         | 6.299 μs | 0.0789 μs | 0.0043 μs |  1.00 | 1.2360 |   5.05 KB |        1.00 |
| LeanCorpus_SortedSearch_PostSort         | ShortRun   | 3              | 1           | 3           | 20000         | 6.310 μs | 0.1021 μs | 0.0056 μs |  1.00 | 1.2360 |   5.05 KB |        1.00 |

## kstemmer

| Method        | Job        | IterationCount | LaunchCount | WarmupCount | DocumentCount | Mean     | Error    | StdDev  | Gen0       | Gen1      | Allocated |
|-------------- |----------- |--------------- |------------ |------------ |-------------- |---------:|---------:|--------:|-----------:|----------:|----------:|
| KStem_Analyse | DefaultJob | Default        | Default     | Default     | 20000         | 443.6 ms |  2.76 ms | 2.58 ms | 61000.0000 | 7000.0000 | 268.31 MB |
| KStem_Analyse | ShortRun   | 3              | 1           | 3           | 20000         | 443.3 ms | 42.22 ms | 2.31 ms | 61000.0000 | 5000.0000 | 268.31 MB |

## lightenglish

| Method            | Job        | IterationCount | LaunchCount | WarmupCount | DocumentCount | Mean      | Error     | StdDev   | Ratio | Gen0      | Allocated | Alloc Ratio |
|------------------ |----------- |--------------- |------------ |------------ |-------------- |----------:|----------:|---------:|------:|----------:|----------:|------------:|
| LightEnglish_Stem | DefaultJob | Default        | Default     | Default     | 20000         |  97.93 ms |  0.258 ms | 0.242 ms |  1.00 | 3500.0000 |     14 MB |        1.00 |
| Porter_Stem       | DefaultJob | Default        | Default     | Default     | 20000         | 139.43 ms |  0.434 ms | 0.406 ms |  1.42 | 2750.0000 |  11.95 MB |        0.85 |
|                   |            |                |             |             |               |           |           |          |       |           |           |             |
| LightEnglish_Stem | ShortRun   | 3              | 1           | 3           | 20000         |  99.11 ms |  3.382 ms | 0.185 ms |  1.00 | 3500.0000 |     14 MB |        1.00 |
| Porter_Stem       | ShortRun   | 3              | 1           | 3           | 20000         | 137.85 ms | 14.151 ms | 0.776 ms |  1.39 | 2750.0000 |  11.95 MB |        0.85 |

## mlt

| Method                                      | Job        | IterationCount | LaunchCount | WarmupCount | MaxQueryTerms | DocumentCount | Mean      | Error     | StdDev   | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|-------------------------------------------- |----------- |--------------- |------------ |------------ |-------------- |-------------- |----------:|----------:|---------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_MoreLikeThisQuery_DefaultParams**  | **DefaultJob** | **Default**        | **Default**     | **Default**     | **10**            | **20000**         |  **34.84 μs** |  **0.104 μs** | **0.093 μs** |  **1.00** |  **5.3101** |      **-** |  **21.14 KB** |        **1.00** |
| LeanCorpus_MoreLikeThisQuery_HighMinDocFreq | DefaultJob | Default        | Default     | Default     | 10            | 20000         |  14.95 μs |  0.041 μs | 0.038 μs |  0.43 |  2.7161 |      - |     11 KB |        0.52 |
| LeanCorpus_MoreLikeThisQuery_NoBoost        | DefaultJob | Default        | Default     | Default     | 10            | 20000         |  35.46 μs |  0.188 μs | 0.175 μs |  1.02 |  5.3101 |      - |  21.15 KB |        1.00 |
|                                             |            |                |             |             |               |               |           |           |          |       |         |        |           |             |
| LeanCorpus_MoreLikeThisQuery_DefaultParams  | ShortRun   | 3              | 1           | 3           | 10            | 20000         |  34.40 μs |  1.231 μs | 0.067 μs |  1.00 |  5.3101 |      - |  21.11 KB |        1.00 |
| LeanCorpus_MoreLikeThisQuery_HighMinDocFreq | ShortRun   | 3              | 1           | 3           | 10            | 20000         |  14.80 μs |  0.799 μs | 0.044 μs |  0.43 |  2.7161 |      - |     11 KB |        0.52 |
| LeanCorpus_MoreLikeThisQuery_NoBoost        | ShortRun   | 3              | 1           | 3           | 10            | 20000         |  34.70 μs |  0.799 μs | 0.044 μs |  1.01 |  5.3101 |      - |  21.14 KB |        1.00 |
|                                             |            |                |             |             |               |               |           |           |          |       |         |        |           |             |
| **LeanCorpus_MoreLikeThisQuery_DefaultParams**  | **DefaultJob** | **Default**        | **Default**     | **Default**     | **25**            | **20000**         |  **94.68 μs** |  **0.707 μs** | **0.661 μs** |  **1.00** |  **7.9346** | **0.3662** |  **31.73 KB** |        **1.00** |
| LeanCorpus_MoreLikeThisQuery_HighMinDocFreq | DefaultJob | Default        | Default     | Default     | 25            | 20000         |  14.87 μs |  0.032 μs | 0.029 μs |  0.16 |  2.7161 |      - |     11 KB |        0.35 |
| LeanCorpus_MoreLikeThisQuery_NoBoost        | DefaultJob | Default        | Default     | Default     | 25            | 20000         |  95.30 μs |  0.535 μs | 0.501 μs |  1.01 |  7.9346 | 0.2441 |  31.73 KB |        1.00 |
|                                             |            |                |             |             |               |               |           |           |          |       |         |        |           |             |
| LeanCorpus_MoreLikeThisQuery_DefaultParams  | ShortRun   | 3              | 1           | 3           | 25            | 20000         |  95.37 μs | 10.294 μs | 0.564 μs |  1.00 |  7.9346 |      - |  31.73 KB |        1.00 |
| LeanCorpus_MoreLikeThisQuery_HighMinDocFreq | ShortRun   | 3              | 1           | 3           | 25            | 20000         |  14.85 μs |  0.690 μs | 0.038 μs |  0.16 |  2.7161 |      - |     11 KB |        0.35 |
| LeanCorpus_MoreLikeThisQuery_NoBoost        | ShortRun   | 3              | 1           | 3           | 25            | 20000         |  95.61 μs |  4.114 μs | 0.226 μs |  1.00 |  7.9346 | 0.2441 |  31.73 KB |        1.00 |
|                                             |            |                |             |             |               |               |           |           |          |       |         |        |           |             |
| **LeanCorpus_MoreLikeThisQuery_DefaultParams**  | **DefaultJob** | **Default**        | **Default**     | **Default**     | **50**            | **20000**         | **833.88 μs** |  **4.443 μs** | **4.156 μs** |  **1.00** | **13.6719** | **0.9766** |  **48.67 KB** |        **1.00** |
| LeanCorpus_MoreLikeThisQuery_HighMinDocFreq | DefaultJob | Default        | Default     | Default     | 50            | 20000         |  15.24 μs |  0.063 μs | 0.059 μs |  0.02 |  2.7161 |      - |     11 KB |        0.23 |
| LeanCorpus_MoreLikeThisQuery_NoBoost        | DefaultJob | Default        | Default     | Default     | 50            | 20000         | 831.93 μs |  4.418 μs | 4.132 μs |  1.00 | 12.6953 | 0.9766 |  48.67 KB |        1.00 |
|                                             |            |                |             |             |               |               |           |           |          |       |         |        |           |             |
| LeanCorpus_MoreLikeThisQuery_DefaultParams  | ShortRun   | 3              | 1           | 3           | 50            | 20000         | 830.52 μs | 43.700 μs | 2.395 μs |  1.00 | 12.6953 |      - |  48.67 KB |        1.00 |
| LeanCorpus_MoreLikeThisQuery_HighMinDocFreq | ShortRun   | 3              | 1           | 3           | 50            | 20000         |  14.92 μs |  1.901 μs | 0.104 μs |  0.02 |  2.7161 |      - |     11 KB |        0.23 |
| LeanCorpus_MoreLikeThisQuery_NoBoost        | ShortRun   | 3              | 1           | 3           | 50            | 20000         | 833.73 μs | 37.071 μs | 2.032 μs |  1.00 | 12.6953 |      - |  48.67 KB |        1.00 |

## multiphrase

| Method                      | Job        | IterationCount | LaunchCount | WarmupCount | DocumentCount | Mean     | Error    | StdDev   | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|---------------------------- |----------- |--------------- |------------ |------------ |-------------- |---------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| LeanCorpus_MultiPhraseQuery | DefaultJob | Default        | Default     | Default     | 20000         | 16.83 μs | 0.090 μs | 0.084 μs |  1.00 |  4.5776 |      - |  18.21 KB |        1.00 |
| LuceneNet_MultiPhraseQuery  | DefaultJob | Default        | Default     | Default     | 20000         | 36.56 μs | 0.097 μs | 0.086 μs |  2.17 | 21.6064 | 0.0610 |   88.4 KB |        4.85 |
|                             |            |                |             |             |               |          |          |          |       |         |        |           |             |
| LeanCorpus_MultiPhraseQuery | ShortRun   | 3              | 1           | 3           | 20000         | 16.62 μs | 0.225 μs | 0.012 μs |  1.00 |  4.5776 |      - |  18.21 KB |        1.00 |
| LuceneNet_MultiPhraseQuery  | ShortRun   | 3              | 1           | 3           | 20000         | 36.35 μs | 2.717 μs | 0.149 μs |  2.19 | 21.6064 | 0.0610 |   88.4 KB |        4.85 |

## ngram

| Method                                        | Job        | IterationCount | LaunchCount | WarmupCount | GramRange | DocumentCount | Mean        | Error     | StdDev   | Ratio | RatioSD | Gen0       | Allocated   | Alloc Ratio |
|---------------------------------------------- |----------- |--------------- |------------ |------------ |---------- |-------------- |------------:|----------:|---------:|------:|--------:|-----------:|------------:|------------:|
| **LeanCorpus_EdgeNGramTokeniser_SpanSink**        | **DefaultJob** | **Default**        | **Default**     | **Default**     | **2-3**       | **20000**         |    **32.17 ms** |  **0.015 ms** | **0.012 ms** |  **1.00** |    **0.00** |          **-** |           **-** |          **NA** |
| LeanCorpus_NGramTokeniser_SpanSink            | DefaultJob | Default        | Default     | Default     | 2-3       | 20000         |    32.99 ms |  0.137 ms | 0.128 ms |  1.03 |    0.00 |          - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_SpanSink  | DefaultJob | Default        | Default     | Default     | 2-3       | 20000         |    48.98 ms |  0.054 ms | 0.050 ms |  1.52 |    0.00 |          - |           - |          NA |
| LeanCorpus_EdgeNGramTokeniser_Streaming       | DefaultJob | Default        | Default     | Default     | 2-3       | 20000         |    44.23 ms |  0.041 ms | 0.034 ms |  1.37 |    0.00 |          - |           - |          NA |
| LeanCorpus_NGramTokeniser_Streaming           | DefaultJob | Default        | Default     | Default     | 2-3       | 20000         |   109.72 ms |  0.126 ms | 0.111 ms |  3.41 |    0.00 |          - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_Streaming | DefaultJob | Default        | Default     | Default     | 2-3       | 20000         |   109.20 ms |  0.049 ms | 0.041 ms |  3.39 |    0.00 |          - |           - |          NA |
| LuceneNet_EdgeNGramTokenizer                  | DefaultJob | Default        | Default     | Default     | 2-3       | 20000         |   121.46 ms |  0.502 ms | 0.469 ms |  3.78 |    0.01 | 42200.0000 | 177120000 B |          NA |
| LuceneNet_NGramTokenizer                      | DefaultJob | Default        | Default     | Default     | 2-3       | 20000         |   605.81 ms |  2.183 ms | 1.823 ms | 18.83 |    0.06 | 42000.0000 | 177120000 B |          NA |
|                                               |            |                |             |             |           |               |             |           |          |       |         |            |             |             |
| LeanCorpus_EdgeNGramTokeniser_SpanSink        | ShortRun   | 3              | 1           | 3           | 2-3       | 20000         |    32.47 ms |  0.162 ms | 0.009 ms |  1.00 |    0.00 |          - |           - |          NA |
| LeanCorpus_NGramTokeniser_SpanSink            | ShortRun   | 3              | 1           | 3           | 2-3       | 20000         |    32.49 ms |  1.799 ms | 0.099 ms |  1.00 |    0.00 |          - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_SpanSink  | ShortRun   | 3              | 1           | 3           | 2-3       | 20000         |    48.67 ms |  1.497 ms | 0.082 ms |  1.50 |    0.00 |          - |           - |          NA |
| LeanCorpus_EdgeNGramTokeniser_Streaming       | ShortRun   | 3              | 1           | 3           | 2-3       | 20000         |    44.36 ms |  1.221 ms | 0.067 ms |  1.37 |    0.00 |          - |           - |          NA |
| LeanCorpus_NGramTokeniser_Streaming           | ShortRun   | 3              | 1           | 3           | 2-3       | 20000         |   109.83 ms |  6.132 ms | 0.336 ms |  3.38 |    0.01 |          - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_Streaming | ShortRun   | 3              | 1           | 3           | 2-3       | 20000         |   109.13 ms |  3.489 ms | 0.191 ms |  3.36 |    0.01 |          - |           - |          NA |
| LuceneNet_EdgeNGramTokenizer                  | ShortRun   | 3              | 1           | 3           | 2-3       | 20000         |   119.20 ms | 10.379 ms | 0.569 ms |  3.67 |    0.02 | 42200.0000 | 177120000 B |          NA |
| LuceneNet_NGramTokenizer                      | ShortRun   | 3              | 1           | 3           | 2-3       | 20000         |   629.86 ms | 21.665 ms | 1.188 ms | 19.40 |    0.03 | 42000.0000 | 177120000 B |          NA |
|                                               |            |                |             |             |           |               |             |           |          |       |         |            |             |             |
| **LeanCorpus_EdgeNGramTokeniser_SpanSink**        | **DefaultJob** | **Default**        | **Default**     | **Default**     | **3-5**       | **20000**         |    **33.00 ms** |  **0.012 ms** | **0.009 ms** |  **1.00** |    **0.00** |          **-** |           **-** |          **NA** |
| LeanCorpus_NGramTokeniser_SpanSink            | DefaultJob | Default        | Default     | Default     | 3-5       | 20000         |    50.75 ms |  0.909 ms | 0.805 ms |  1.54 |    0.02 |          - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_SpanSink  | DefaultJob | Default        | Default     | Default     | 3-5       | 20000         |    49.82 ms |  0.052 ms | 0.041 ms |  1.51 |    0.00 |          - |           - |          NA |
| LeanCorpus_EdgeNGramTokeniser_Streaming       | DefaultJob | Default        | Default     | Default     | 3-5       | 20000         |    46.11 ms |  0.076 ms | 0.067 ms |  1.40 |    0.00 |          - |           - |          NA |
| LeanCorpus_NGramTokeniser_Streaming           | DefaultJob | Default        | Default     | Default     | 3-5       | 20000         |   155.60 ms |  0.204 ms | 0.170 ms |  4.72 |    0.01 |          - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_Streaming | DefaultJob | Default        | Default     | Default     | 3-5       | 20000         |   106.52 ms |  0.042 ms | 0.035 ms |  3.23 |    0.00 |          - |           - |          NA |
| LuceneNet_EdgeNGramTokenizer                  | DefaultJob | Default        | Default     | Default     | 3-5       | 20000         |   119.71 ms |  0.427 ms | 0.400 ms |  3.63 |    0.01 | 42400.0000 | 177600000 B |          NA |
| LuceneNet_NGramTokenizer                      | DefaultJob | Default        | Default     | Default     | 3-5       | 20000         | 1,017.64 ms |  7.541 ms | 7.054 ms | 30.84 |    0.21 | 42000.0000 | 177600000 B |          NA |
|                                               |            |                |             |             |           |               |             |           |          |       |         |            |             |             |
| LeanCorpus_EdgeNGramTokeniser_SpanSink        | ShortRun   | 3              | 1           | 3           | 3-5       | 20000         |    32.34 ms |  0.053 ms | 0.003 ms |  1.00 |    0.00 |          - |           - |          NA |
| LeanCorpus_NGramTokeniser_SpanSink            | ShortRun   | 3              | 1           | 3           | 3-5       | 20000         |    49.93 ms | 10.766 ms | 0.590 ms |  1.54 |    0.02 |          - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_SpanSink  | ShortRun   | 3              | 1           | 3           | 3-5       | 20000         |    49.18 ms |  1.340 ms | 0.073 ms |  1.52 |    0.00 |          - |           - |          NA |
| LeanCorpus_EdgeNGramTokeniser_Streaming       | ShortRun   | 3              | 1           | 3           | 3-5       | 20000         |    47.73 ms |  2.864 ms | 0.157 ms |  1.48 |    0.00 |          - |           - |          NA |
| LeanCorpus_NGramTokeniser_Streaming           | ShortRun   | 3              | 1           | 3           | 3-5       | 20000         |   155.82 ms |  2.579 ms | 0.141 ms |  4.82 |    0.00 |          - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_Streaming | ShortRun   | 3              | 1           | 3           | 3-5       | 20000         |   105.94 ms |  3.778 ms | 0.207 ms |  3.28 |    0.01 |          - |           - |          NA |
| LuceneNet_EdgeNGramTokenizer                  | ShortRun   | 3              | 1           | 3           | 3-5       | 20000         |   120.75 ms |  4.262 ms | 0.234 ms |  3.73 |    0.01 | 42400.0000 | 177600000 B |          NA |
| LuceneNet_NGramTokenizer                      | ShortRun   | 3              | 1           | 3           | 3-5       | 20000         | 1,014.02 ms | 27.073 ms | 1.484 ms | 31.36 |    0.04 | 42000.0000 | 177600000 B |          NA |

## parallel

| Method                                 | Job        | IterationCount | LaunchCount | WarmupCount | SegmentCount | DocumentCount | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------------------------- |----------- |--------------- |------------ |------------ |------------- |-------------- |----------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_SequentialSearch**            | **DefaultJob** | **Default**        | **Default**     | **Default**     | **4**            | **20000**         |  **3.169 μs** | **0.0026 μs** | **0.0023 μs** |  **1.00** |    **0.00** | **0.1373** |     **576 B** |        **1.00** |
| LeanCorpus_ParallelSearch              | DefaultJob | Default        | Default     | Default     | 4            | 20000         |  3.137 μs | 0.0034 μs | 0.0032 μs |  0.99 |    0.00 | 0.1373 |     576 B |        1.00 |
| LeanCorpus_ParallelSearch_BooleanQuery | DefaultJob | Default        | Default     | Default     | 4            | 20000         | 15.604 μs | 0.0899 μs | 0.0797 μs |  4.92 |    0.02 | 2.2278 |    9303 B |       16.15 |
|                                        |            |                |             |             |              |               |           |           |           |       |         |        |           |             |
| LeanCorpus_SequentialSearch            | ShortRun   | 3              | 1           | 3           | 4            | 20000         |  3.178 μs | 0.0385 μs | 0.0021 μs |  1.00 |    0.00 | 0.1373 |     576 B |        1.00 |
| LeanCorpus_ParallelSearch              | ShortRun   | 3              | 1           | 3           | 4            | 20000         |  3.149 μs | 0.0271 μs | 0.0015 μs |  0.99 |    0.00 | 0.1373 |     576 B |        1.00 |
| LeanCorpus_ParallelSearch_BooleanQuery | ShortRun   | 3              | 1           | 3           | 4            | 20000         | 16.041 μs | 2.8871 μs | 0.1583 μs |  5.05 |    0.04 | 2.2278 |    9304 B |       16.15 |
|                                        |            |                |             |             |              |               |           |           |           |       |         |        |           |             |
| **LeanCorpus_SequentialSearch**            | **DefaultJob** | **Default**        | **Default**     | **Default**     | **8**            | **20000**         |  **4.097 μs** | **0.0040 μs** | **0.0033 μs** |  **1.00** |    **0.00** | **0.1602** |     **672 B** |        **1.00** |
| LeanCorpus_ParallelSearch              | DefaultJob | Default        | Default     | Default     | 8            | 20000         |  4.124 μs | 0.0038 μs | 0.0032 μs |  1.01 |    0.00 | 0.1602 |     672 B |        1.00 |
| LeanCorpus_ParallelSearch_BooleanQuery | DefaultJob | Default        | Default     | Default     | 8            | 20000         | 17.890 μs | 0.1776 μs | 0.1662 μs |  4.37 |    0.04 | 3.5400 |   14648 B |       21.80 |
|                                        |            |                |             |             |              |               |           |           |           |       |         |        |           |             |
| LeanCorpus_SequentialSearch            | ShortRun   | 3              | 1           | 3           | 8            | 20000         |  4.333 μs | 0.1150 μs | 0.0063 μs |  1.00 |    0.00 | 0.1602 |     672 B |        1.00 |
| LeanCorpus_ParallelSearch              | ShortRun   | 3              | 1           | 3           | 8            | 20000         |  4.128 μs | 0.0891 μs | 0.0049 μs |  0.95 |    0.00 | 0.1602 |     672 B |        1.00 |
| LeanCorpus_ParallelSearch_BooleanQuery | ShortRun   | 3              | 1           | 3           | 8            | 20000         | 18.247 μs | 3.3549 μs | 0.1839 μs |  4.21 |    0.04 | 3.5400 |   14648 B |       21.80 |

## Phrase queries

| Method                 | Job        | IterationCount | LaunchCount | WarmupCount | PhraseType     | DocumentCount | Mean     | Error    | StdDev   | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|----------------------- |----------- |--------------- |------------ |------------ |--------------- |-------------- |---------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_PhraseQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **ExactThreeWord** | **20000**         | **21.71 μs** | **0.074 μs** | **0.066 μs** |  **1.00** |  **3.6011** |      **-** |  **14.37 KB** |        **1.00** |
| LuceneNet_PhraseQuery  | DefaultJob | Default        | Default     | Default     | ExactThreeWord | 20000         | 21.15 μs | 0.047 μs | 0.044 μs |  0.97 | 17.9443 | 1.9836 |  73.47 KB |        5.11 |
|                        |            |                |             |             |                |               |          |          |          |       |         |        |           |             |
| LeanCorpus_PhraseQuery | ShortRun   | 3              | 1           | 3           | ExactThreeWord | 20000         | 21.40 μs | 0.389 μs | 0.021 μs |  1.00 |  3.6011 |      - |  14.37 KB |        1.00 |
| LuceneNet_PhraseQuery  | ShortRun   | 3              | 1           | 3           | ExactThreeWord | 20000         | 20.84 μs | 0.580 μs | 0.032 μs |  0.97 | 17.9443 | 1.9836 |  73.47 KB |        5.11 |
|                        |            |                |             |             |                |               |          |          |          |       |         |        |           |             |
| **LeanCorpus_PhraseQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **ExactTwoWord**   | **20000**         | **21.10 μs** | **0.054 μs** | **0.048 μs** |  **1.00** |  **2.7771** |      **-** |  **11.08 KB** |        **1.00** |
| LuceneNet_PhraseQuery  | DefaultJob | Default        | Default     | Default     | ExactTwoWord   | 20000         | 24.83 μs | 0.091 μs | 0.085 μs |  1.18 | 15.1062 | 1.5869 |  61.84 KB |        5.58 |
|                        |            |                |             |             |                |               |          |          |          |       |         |        |           |             |
| LeanCorpus_PhraseQuery | ShortRun   | 3              | 1           | 3           | ExactTwoWord   | 20000         | 21.30 μs | 1.362 μs | 0.075 μs |  1.00 |  2.7771 |      - |  11.08 KB |        1.00 |
| LuceneNet_PhraseQuery  | ShortRun   | 3              | 1           | 3           | ExactTwoWord   | 20000         | 25.63 μs | 1.003 μs | 0.055 μs |  1.20 | 15.1062 | 1.5869 |  61.84 KB |        5.58 |
|                        |            |                |             |             |                |               |          |          |          |       |         |        |           |             |
| **LeanCorpus_PhraseQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **SlopTwoWord**    | **20000**         | **44.78 μs** | **0.121 μs** | **0.107 μs** |  **1.00** |  **2.8076** |      **-** |  **11.19 KB** |        **1.00** |
| LuceneNet_PhraseQuery  | DefaultJob | Default        | Default     | Default     | SlopTwoWord    | 20000         | 20.70 μs | 0.037 μs | 0.033 μs |  0.46 |  7.3242 |      - |     30 KB |        2.68 |
|                        |            |                |             |             |                |               |          |          |          |       |         |        |           |             |
| LeanCorpus_PhraseQuery | ShortRun   | 3              | 1           | 3           | SlopTwoWord    | 20000         | 44.90 μs | 1.922 μs | 0.105 μs |  1.00 |  2.8076 |      - |  11.19 KB |        1.00 |
| LuceneNet_PhraseQuery  | ShortRun   | 3              | 1           | 3           | SlopTwoWord    | 20000         | 21.32 μs | 0.744 μs | 0.041 μs |  0.47 |  7.3242 |      - |     30 KB |        2.68 |

## Prefix queries

| Method                 | Job        | IterationCount | LaunchCount | WarmupCount | QueryPrefix | DocumentCount | Mean     | Error    | StdDev   | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|----------------------- |----------- |--------------- |------------ |------------ |------------ |-------------- |---------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_PrefixQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **gov**         | **20000**         | **10.27 μs** | **0.045 μs** | **0.040 μs** |  **1.00** |  **1.0529** |      **-** |    **4.3 KB** |        **1.00** |
| LuceneNet_PrefixQuery  | DefaultJob | Default        | Default     | Default     | gov         | 20000         | 15.65 μs | 0.045 μs | 0.042 μs |  1.52 | 13.3057 | 0.0305 |  54.55 KB |       12.67 |
|                        |            |                |             |             |             |               |          |          |          |       |         |        |           |             |
| LeanCorpus_PrefixQuery | ShortRun   | 3              | 1           | 3           | gov         | 20000         | 10.42 μs | 1.067 μs | 0.059 μs |  1.00 |  1.0529 |      - |    4.3 KB |        1.00 |
| LuceneNet_PrefixQuery  | ShortRun   | 3              | 1           | 3           | gov         | 20000         | 15.83 μs | 0.635 μs | 0.035 μs |  1.52 | 13.3057 | 0.0305 |  54.55 KB |       12.67 |
|                        |            |                |             |             |             |               |          |          |          |       |         |        |           |             |
| **LeanCorpus_PrefixQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **mark**        | **20000**         | **19.83 μs** | **0.086 μs** | **0.081 μs** |  **1.00** |  **1.2512** |      **-** |   **5.13 KB** |        **1.00** |
| LuceneNet_PrefixQuery  | DefaultJob | Default        | Default     | Default     | mark        | 20000         | 23.84 μs | 0.038 μs | 0.036 μs |  1.20 | 14.6790 |      - |  60.09 KB |       11.72 |
|                        |            |                |             |             |             |               |          |          |          |       |         |        |           |             |
| LeanCorpus_PrefixQuery | ShortRun   | 3              | 1           | 3           | mark        | 20000         | 19.73 μs | 0.998 μs | 0.055 μs |  1.00 |  1.2512 |      - |   5.13 KB |        1.00 |
| LuceneNet_PrefixQuery  | ShortRun   | 3              | 1           | 3           | mark        | 20000         | 23.54 μs | 1.368 μs | 0.075 μs |  1.19 | 14.6790 |      - |  60.09 KB |       11.72 |
|                        |            |                |             |             |             |               |          |          |          |       |         |        |           |             |
| **LeanCorpus_PrefixQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **pres**        | **20000**         | **37.46 μs** | **0.180 μs** | **0.151 μs** |  **1.00** |  **1.9531** |      **-** |   **7.96 KB** |        **1.00** |
| LuceneNet_PrefixQuery  | DefaultJob | Default        | Default     | Default     | pres        | 20000         | 49.61 μs | 0.076 μs | 0.067 μs |  1.32 | 15.5029 |      - |  63.72 KB |        8.01 |
|                        |            |                |             |             |             |               |          |          |          |       |         |        |           |             |
| LeanCorpus_PrefixQuery | ShortRun   | 3              | 1           | 3           | pres        | 20000         | 36.94 μs | 5.349 μs | 0.293 μs |  1.00 |  1.9531 |      - |   7.96 KB |        1.00 |
| LuceneNet_PrefixQuery  | ShortRun   | 3              | 1           | 3           | pres        | 20000         | 49.29 μs | 1.408 μs | 0.077 μs |  1.33 | 15.5029 |      - |  63.72 KB |        8.01 |

## Term queries

| Method               | Job        | IterationCount | LaunchCount | WarmupCount | QueryTerm  | DocumentCount | Mean      | Error     | StdDev    | Ratio | Gen0   | Allocated | Alloc Ratio |
|--------------------- |----------- |--------------- |------------ |------------ |----------- |-------------- |----------:|----------:|----------:|------:|-------:|----------:|------------:|
| **LeanCorpus_TermQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **government** | **20000**         |  **2.631 μs** | **0.0035 μs** | **0.0031 μs** |  **1.00** | **0.1259** |     **528 B** |        **1.00** |
| LuceneNet_TermQuery  | DefaultJob | Default        | Default     | Default     | government | 20000         |  9.064 μs | 0.0199 μs | 0.0186 μs |  3.45 | 3.0823 |   12936 B |       24.50 |
|                      |            |                |             |             |            |               |           |           |           |       |        |           |             |
| LeanCorpus_TermQuery | ShortRun   | 3              | 1           | 3           | government | 20000         |  2.662 μs | 0.0115 μs | 0.0006 μs |  1.00 | 0.1259 |     528 B |        1.00 |
| LuceneNet_TermQuery  | ShortRun   | 3              | 1           | 3           | government | 20000         |  9.260 μs | 0.5673 μs | 0.0311 μs |  3.48 | 3.0823 |   12936 B |       24.50 |
|                      |            |                |             |             |            |               |           |           |           |       |        |           |             |
| **LeanCorpus_TermQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **people**     | **20000**         | **22.057 μs** | **0.0169 μs** | **0.0141 μs** |  **1.00** | **0.1221** |     **520 B** |        **1.00** |
| LuceneNet_TermQuery  | DefaultJob | Default        | Default     | Default     | people     | 20000         | 26.229 μs | 0.0321 μs | 0.0268 μs |  1.19 | 3.1738 |   13304 B |       25.58 |
|                      |            |                |             |             |            |               |           |           |           |       |        |           |             |
| LeanCorpus_TermQuery | ShortRun   | 3              | 1           | 3           | people     | 20000         | 21.909 μs | 0.3659 μs | 0.0201 μs |  1.00 | 0.1221 |     520 B |        1.00 |
| LuceneNet_TermQuery  | ShortRun   | 3              | 1           | 3           | people     | 20000         | 28.244 μs | 0.9210 μs | 0.0505 μs |  1.29 | 3.1738 |   13304 B |       25.58 |
|                      |            |                |             |             |            |               |           |           |           |       |        |           |             |
| **LeanCorpus_TermQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **said**       | **20000**         | **92.700 μs** | **0.0925 μs** | **0.0865 μs** |  **1.00** | **0.1221** |     **512 B** |        **1.00** |
| LuceneNet_TermQuery  | DefaultJob | Default        | Default     | Default     | said       | 20000         | 89.071 μs | 0.1996 μs | 0.1867 μs |  0.96 | 3.0518 |   13168 B |       25.72 |
|                      |            |                |             |             |            |               |           |           |           |       |        |           |             |
| LeanCorpus_TermQuery | ShortRun   | 3              | 1           | 3           | said       | 20000         | 92.730 μs | 2.4289 μs | 0.1331 μs |  1.00 | 0.1221 |     512 B |        1.00 |
| LuceneNet_TermQuery  | ShortRun   | 3              | 1           | 3           | said       | 20000         | 90.385 μs | 5.4236 μs | 0.2973 μs |  0.97 | 3.0518 |   13168 B |       25.72 |

## query-cache

| Method                            | Job        | IterationCount | LaunchCount | WarmupCount | DocumentCount | Mean        | Error       | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------------------------- |----------- |--------------- |------------ |------------ |-------------- |------------:|------------:|---------:|------:|--------:|-------:|----------:|------------:|
| LeanCorpus_NoCache                | DefaultJob | Default        | Default     | Default     | 20000         |  2,624.0 ns |     1.12 ns |  0.87 ns |  1.00 |    0.00 | 0.1259 |     528 B |        1.00 |
| LeanCorpus_WithCache              | DefaultJob | Default        | Default     | Default     | 20000         |    279.4 ns |     0.21 ns |  0.16 ns |  0.11 |    0.00 | 0.1183 |     496 B |        0.94 |
| LeanCorpus_WithCache_BooleanQuery | DefaultJob | Default        | Default     | Default     | 20000         |    732.0 ns |     1.38 ns |  1.29 ns |  0.28 |    0.00 | 0.2518 |    1056 B |        2.00 |
| LeanCorpus_NoCache_BooleanQuery   | DefaultJob | Default        | Default     | Default     | 20000         |  9,969.3 ns |    84.33 ns | 74.75 ns |  3.80 |    0.03 | 1.4191 |    5895 B |       11.16 |
|                                   |            |                |             |             |               |             |             |          |       |         |        |           |             |
| LeanCorpus_NoCache                | ShortRun   | 3              | 1           | 3           | 20000         |  2,620.9 ns |    21.98 ns |  1.20 ns |  1.00 |    0.00 | 0.1259 |     528 B |        1.00 |
| LeanCorpus_WithCache              | ShortRun   | 3              | 1           | 3           | 20000         |    278.6 ns |    10.61 ns |  0.58 ns |  0.11 |    0.00 | 0.1183 |     496 B |        0.94 |
| LeanCorpus_WithCache_BooleanQuery | ShortRun   | 3              | 1           | 3           | 20000         |    738.9 ns |    29.20 ns |  1.60 ns |  0.28 |    0.00 | 0.2518 |    1056 B |        2.00 |
| LeanCorpus_NoCache_BooleanQuery   | ShortRun   | 3              | 1           | 3           | 20000         | 10,004.5 ns | 1,012.99 ns | 55.53 ns |  3.82 |    0.02 | 1.4191 |    5895 B |       11.16 |

## range

| Method                      | Job        | IterationCount | LaunchCount | WarmupCount | RangeWidth | DocumentCount | Mean       | Error     | StdDev    | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|---------------------------- |----------- |--------------- |------------ |------------ |----------- |-------------- |-----------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_RangeQuery**       | **DefaultJob** | **Default**        | **Default**     | **Default**     | **0.01**       | **20000**         |   **7.759 μs** | **0.0205 μs** | **0.0182 μs** |  **1.00** |  **1.0071** |      **-** |   **4.13 KB** |        **1.00** |
| LuceneNet_NumericRangeQuery | DefaultJob | Default        | Default     | Default     | 0.01       | 20000         |  28.931 μs | 0.0679 μs | 0.0602 μs |  3.73 | 16.6626 |      - |  68.62 KB |       16.63 |
|                             |            |                |             |             |            |               |            |           |           |       |         |        |           |             |
| LeanCorpus_RangeQuery       | ShortRun   | 3              | 1           | 3           | 0.01       | 20000         |   7.978 μs | 0.4165 μs | 0.0228 μs |  1.00 |  1.0071 |      - |   4.13 KB |        1.00 |
| LuceneNet_NumericRangeQuery | ShortRun   | 3              | 1           | 3           | 0.01       | 20000         |  28.839 μs | 0.6391 μs | 0.0350 μs |  3.61 | 16.6626 |      - |  68.62 KB |       16.63 |
|                             |            |                |             |             |            |               |            |           |           |       |         |        |           |             |
| **LeanCorpus_RangeQuery**       | **DefaultJob** | **Default**        | **Default**     | **Default**     | **0.1**        | **20000**         |  **20.306 μs** | **0.0682 μs** | **0.0605 μs** |  **1.00** |  **1.0071** |      **-** |   **4.13 KB** |        **1.00** |
| LuceneNet_NumericRangeQuery | DefaultJob | Default        | Default     | Default     | 0.1        | 20000         |  63.552 μs | 0.1121 μs | 0.1048 μs |  3.13 | 16.2354 |      - |  66.73 KB |       16.18 |
|                             |            |                |             |             |            |               |            |           |           |       |         |        |           |             |
| LeanCorpus_RangeQuery       | ShortRun   | 3              | 1           | 3           | 0.1        | 20000         |  19.648 μs | 1.1293 μs | 0.0619 μs |  1.00 |  1.0071 |      - |   4.13 KB |        1.00 |
| LuceneNet_NumericRangeQuery | ShortRun   | 3              | 1           | 3           | 0.1        | 20000         |  63.686 μs | 3.1814 μs | 0.1744 μs |  3.24 | 16.2354 |      - |  66.73 KB |       16.18 |
|                             |            |                |             |             |            |               |            |           |           |       |         |        |           |             |
| **LeanCorpus_RangeQuery**       | **DefaultJob** | **Default**        | **Default**     | **Default**     | **0.5**        | **20000**         |  **67.850 μs** | **0.2137 μs** | **0.1999 μs** |  **1.00** |  **0.9766** |      **-** |   **4.11 KB** |        **1.00** |
| LuceneNet_NumericRangeQuery | DefaultJob | Default        | Default     | Default     | 0.5        | 20000         | 218.269 μs | 0.2611 μs | 0.2315 μs |  3.22 | 18.0664 | 0.4883 |  74.21 KB |       18.04 |
|                             |            |                |             |             |            |               |            |           |           |       |         |        |           |             |
| LeanCorpus_RangeQuery       | ShortRun   | 3              | 1           | 3           | 0.5        | 20000         |  67.860 μs | 1.2181 μs | 0.0668 μs |  1.00 |  0.9766 |      - |   4.11 KB |        1.00 |
| LuceneNet_NumericRangeQuery | ShortRun   | 3              | 1           | 3           | 0.5        | 20000         | 220.501 μs | 7.1696 μs | 0.3930 μs |  3.25 | 18.0664 | 0.4883 |  74.21 KB |       18.06 |

## regexp

| Method                 | Job        | IterationCount | LaunchCount | WarmupCount | Pattern    | DocumentCount | Mean        | Error        | StdDev    | Ratio | RatioSD | Gen0     | Gen1   | Allocated | Alloc Ratio |
|----------------------- |----------- |--------------- |------------ |------------ |----------- |-------------- |------------:|-------------:|----------:|------:|--------:|---------:|-------:|----------:|------------:|
| **LeanCorpus_RegexpQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **.*nation.*** | **20000**         | **3,883.28 μs** |     **5.159 μs** |  **4.028 μs** |  **1.00** |    **0.00** |        **-** |      **-** |  **11.83 KB** |        **1.00** |
| LuceneNet_RegexpQuery  | DefaultJob | Default        | Default     | Default     | .*nation.* | 20000         | 3,647.86 μs |     6.416 μs |  6.002 μs |  0.94 |    0.00 | 125.0000 | 3.9063 | 518.48 KB |       43.81 |
|                        |            |                |             |             |            |               |             |              |           |       |         |          |        |           |             |
| LeanCorpus_RegexpQuery | ShortRun   | 3              | 1           | 3           | .*nation.* | 20000         | 3,940.30 μs | 1,459.714 μs | 80.012 μs |  1.00 |    0.00 |        - |      - |  11.48 KB |        1.00 |
| LuceneNet_RegexpQuery  | ShortRun   | 3              | 1           | 3           | .*nation.* | 20000         | 3,679.37 μs |   150.070 μs |  8.226 μs |  0.93 |    0.02 | 125.0000 | 3.9063 | 518.48 KB |       45.15 |
|                        |            |                |             |             |            |               |             |              |           |       |         |          |        |           |             |
| **LeanCorpus_RegexpQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **gov.*ment**  | **20000**         |    **13.78 μs** |     **0.039 μs** |  **0.036 μs** |  **1.00** |    **0.00** |   **2.6703** |      **-** |  **10.84 KB** |        **1.00** |
| LuceneNet_RegexpQuery  | DefaultJob | Default        | Default     | Default     | gov.*ment  | 20000         |   254.31 μs |     0.559 μs |  0.523 μs | 18.46 |    0.06 |  77.6367 | 4.8828 | 319.12 KB |       29.43 |
|                        |            |                |             |             |            |               |             |              |           |       |         |          |        |           |             |
| LeanCorpus_RegexpQuery | ShortRun   | 3              | 1           | 3           | gov.*ment  | 20000         |    14.37 μs |     1.764 μs |  0.097 μs |  1.00 |    0.00 |   2.6703 |      - |  10.84 KB |        1.00 |
| LuceneNet_RegexpQuery  | ShortRun   | 3              | 1           | 3           | gov.*ment  | 20000         |   255.74 μs |    13.539 μs |  0.742 μs | 17.80 |    0.11 |  77.6367 | 4.8828 | 319.12 KB |       29.43 |
|                        |            |                |             |             |            |               |             |              |           |       |         |          |        |           |             |
| **LeanCorpus_RegexpQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **mark.***     | **20000**         |    **38.89 μs** |     **0.192 μs** |  **0.179 μs** |  **1.00** |    **0.00** |   **3.2959** |      **-** |  **13.39 KB** |        **1.00** |
| LuceneNet_RegexpQuery  | DefaultJob | Default        | Default     | Default     | mark.*     | 20000         |    66.70 μs |     0.143 μs |  0.134 μs |  1.72 |    0.01 |  26.9775 |      - | 110.44 KB |        8.25 |
|                        |            |                |             |             |            |               |             |              |           |       |         |          |        |           |             |
| LeanCorpus_RegexpQuery | ShortRun   | 3              | 1           | 3           | mark.*     | 20000         |    39.24 μs |     2.959 μs |  0.162 μs |  1.00 |    0.00 |   3.2959 |      - |  13.39 KB |        1.00 |
| LuceneNet_RegexpQuery  | ShortRun   | 3              | 1           | 3           | mark.*     | 20000         |    66.33 μs |     3.127 μs |  0.171 μs |  1.69 |    0.01 |  26.9775 |      - | 110.44 KB |        8.25 |

## Schema and JSON

| Method                      | Job        | IterationCount | LaunchCount | WarmupCount | DocumentCount | Mean        | Error      | StdDev    | Ratio | RatioSD | Gen0       | Gen1      | Allocated | Alloc Ratio |
|---------------------------- |----------- |--------------- |------------ |------------ |-------------- |------------:|-----------:|----------:|------:|--------:|-----------:|----------:|----------:|------------:|
| LeanCorpus_Index_NoSchema   | DefaultJob | Default        | Default     | Default     | 20000         |   983.13 ms |   5.215 ms |  4.878 ms |  1.00 |    0.00 | 18000.0000 | 9000.0000 | 133.69 MB |        1.00 |
| LeanCorpus_Index_WithSchema | DefaultJob | Default        | Default     | Default     | 20000         |   986.84 ms |   4.258 ms |  3.775 ms |  1.00 |    0.01 | 18000.0000 | 9000.0000 | 134.45 MB |        1.01 |
| LeanCorpus_JsonMapping      | DefaultJob | Default        | Default     | Default     | 20000         |    56.45 ms |   0.167 ms |  0.156 ms |  0.06 |    0.00 |  6666.6667 |         - |  27.94 MB |        0.21 |
|                             |            |                |             |             |               |             |            |           |       |         |            |           |           |             |
| LeanCorpus_Index_NoSchema   | ShortRun   | 3              | 1           | 3           | 20000         | 1,005.09 ms | 512.571 ms | 28.096 ms |  1.00 |    0.00 | 18000.0000 | 8000.0000 | 133.69 MB |        1.00 |
| LeanCorpus_Index_WithSchema | ShortRun   | 3              | 1           | 3           | 20000         |   993.69 ms | 532.140 ms | 29.168 ms |  0.99 |    0.03 | 18000.0000 | 9000.0000 | 134.46 MB |        1.01 |
| LeanCorpus_JsonMapping      | ShortRun   | 3              | 1           | 3           | 20000         |    56.95 ms |   3.236 ms |  0.177 ms |  0.06 |    0.00 |  6666.6667 |         - |  27.94 MB |        0.21 |

## searcher-mgr

| Method                                   | Job        | IterationCount | LaunchCount | WarmupCount | DocumentCount | Mean     | Error     | StdDev    | Ratio | Gen0   | Allocated | Alloc Ratio |
|----------------------------------------- |----------- |--------------- |------------ |------------ |-------------- |---------:|----------:|----------:|------:|-------:|----------:|------------:|
| LeanCorpus_SearcherManager_AcquireSearch | DefaultJob | Default        | Default     | Default     | 20000         | 2.686 μs | 0.0037 μs | 0.0035 μs |  1.00 | 0.1259 |     529 B |        1.00 |
| LeanCorpus_SearcherManager_AcquireLease  | DefaultJob | Default        | Default     | Default     | 20000         | 2.668 μs | 0.0035 μs | 0.0033 μs |  0.99 | 0.1411 |     592 B |        1.12 |
| LuceneNet_SearcherManager_AcquireSearch  | DefaultJob | Default        | Default     | Default     | 20000         | 9.277 μs | 0.0376 μs | 0.0351 μs |  3.45 | 3.0823 |   12936 B |       24.45 |
|                                          |            |                |             |             |               |          |           |           |       |        |           |             |
| LeanCorpus_SearcherManager_AcquireSearch | ShortRun   | 3              | 1           | 3           | 20000         | 2.653 μs | 0.0182 μs | 0.0010 μs |  1.00 | 0.1259 |     529 B |        1.00 |
| LeanCorpus_SearcherManager_AcquireLease  | ShortRun   | 3              | 1           | 3           | 20000         | 2.641 μs | 0.0765 μs | 0.0042 μs |  1.00 | 0.1411 |     593 B |        1.12 |
| LuceneNet_SearcherManager_AcquireSearch  | ShortRun   | 3              | 1           | 3           | 20000         | 9.448 μs | 0.2881 μs | 0.0158 μs |  3.56 | 3.0823 |   12938 B |       24.46 |

## similarity

| Method                        | Job        | IterationCount | LaunchCount | WarmupCount | DocumentCount | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------------------ |----------- |--------------- |------------ |------------ |-------------- |----------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| LeanCorpus_Bm25_TermQuery     | DefaultJob | Default        | Default     | Default     | 20000         |  2.640 μs | 0.0010 μs | 0.0008 μs |  1.00 |    0.00 | 0.1259 |     528 B |        1.00 |
| LeanCorpus_TfIdf_TermQuery    | DefaultJob | Default        | Default     | Default     | 20000         |  2.636 μs | 0.0047 μs | 0.0039 μs |  1.00 |    0.00 | 0.1259 |     528 B |        1.00 |
| LeanCorpus_Bm25_BooleanQuery  | DefaultJob | Default        | Default     | Default     | 20000         |  9.918 μs | 0.0496 μs | 0.0464 μs |  3.76 |    0.02 | 1.4191 |    5895 B |       11.16 |
| LeanCorpus_TfIdf_BooleanQuery | DefaultJob | Default        | Default     | Default     | 20000         |  9.995 μs | 0.0592 μs | 0.0525 μs |  3.79 |    0.02 | 1.4191 |    5895 B |       11.16 |
|                               |            |                |             |             |               |           |           |           |       |         |        |           |             |
| LeanCorpus_Bm25_TermQuery     | ShortRun   | 3              | 1           | 3           | 20000         |  2.651 μs | 0.0285 μs | 0.0016 μs |  1.00 |    0.00 | 0.1259 |     528 B |        1.00 |
| LeanCorpus_TfIdf_TermQuery    | ShortRun   | 3              | 1           | 3           | 20000         |  2.691 μs | 0.1284 μs | 0.0070 μs |  1.01 |    0.00 | 0.1259 |     528 B |        1.00 |
| LeanCorpus_Bm25_BooleanQuery  | ShortRun   | 3              | 1           | 3           | 20000         |  9.948 μs | 0.4734 μs | 0.0259 μs |  3.75 |    0.01 | 1.4191 |    5894 B |       11.16 |
| LeanCorpus_TfIdf_BooleanQuery | ShortRun   | 3              | 1           | 3           | 20000         | 10.083 μs | 1.3342 μs | 0.0731 μs |  3.80 |    0.02 | 1.4191 |    5895 B |       11.16 |

## span

| Method               | Job        | IterationCount | LaunchCount | WarmupCount | SpanType | DocumentCount | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|--------------------- |----------- |--------------- |------------ |------------ |--------- |-------------- |---------:|---------:|---------:|------:|--------:|--------:|----------:|------------:|
| **LeanCorpus_SpanQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **Near**     | **20000**         | **19.65 μs** | **0.082 μs** | **0.077 μs** |  **1.00** |    **0.00** |  **2.6550** |  **10.62 KB** |        **1.00** |
| LuceneNet_SpanQuery  | DefaultJob | Default        | Default     | Default     | Near     | 20000         | 24.78 μs | 0.065 μs | 0.061 μs |  1.26 |    0.01 |  7.7209 |  31.55 KB |        2.97 |
|                      |            |                |             |             |          |               |          |          |          |       |         |         |           |             |
| LeanCorpus_SpanQuery | ShortRun   | 3              | 1           | 3           | Near     | 20000         | 19.68 μs | 1.937 μs | 0.106 μs |  1.00 |    0.00 |  2.6550 |  10.62 KB |        1.00 |
| LuceneNet_SpanQuery  | ShortRun   | 3              | 1           | 3           | Near     | 20000         | 23.91 μs | 0.953 μs | 0.052 μs |  1.22 |    0.01 |  7.7209 |  31.55 KB |        2.97 |
|                      |            |                |             |             |          |               |          |          |          |       |         |         |           |             |
| **LeanCorpus_SpanQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **Not**      | **20000**         | **21.14 μs** | **0.086 μs** | **0.081 μs** |  **1.00** |    **0.00** |  **2.6855** |  **10.75 KB** |        **1.00** |
| LuceneNet_SpanQuery  | DefaultJob | Default        | Default     | Default     | Not      | 20000         | 30.84 μs | 0.074 μs | 0.062 μs |  1.46 |    0.01 | 11.5967 |  47.41 KB |        4.41 |
|                      |            |                |             |             |          |               |          |          |          |       |         |         |           |             |
| LeanCorpus_SpanQuery | ShortRun   | 3              | 1           | 3           | Not      | 20000         | 21.13 μs | 1.300 μs | 0.071 μs |  1.00 |    0.00 |  2.6855 |  10.75 KB |        1.00 |
| LuceneNet_SpanQuery  | ShortRun   | 3              | 1           | 3           | Not      | 20000         | 31.97 μs | 1.654 μs | 0.091 μs |  1.51 |    0.01 | 11.5967 |  47.41 KB |        4.41 |
|                      |            |                |             |             |          |               |          |          |          |       |         |         |           |             |
| **LeanCorpus_SpanQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **Or**       | **20000**         | **17.45 μs** | **0.066 μs** | **0.055 μs** |  **1.00** |    **0.00** |  **1.0376** |   **4.27 KB** |        **1.00** |
| LuceneNet_SpanQuery  | DefaultJob | Default        | Default     | Default     | Or       | 20000         | 95.10 μs | 0.171 μs | 0.160 μs |  5.45 |    0.02 | 10.9863 |  45.02 KB |       10.53 |
|                      |            |                |             |             |          |               |          |          |          |       |         |         |           |             |
| LeanCorpus_SpanQuery | ShortRun   | 3              | 1           | 3           | Or       | 20000         | 17.56 μs | 0.515 μs | 0.028 μs |  1.00 |    0.00 |  1.0376 |   4.27 KB |        1.00 |
| LuceneNet_SpanQuery  | ShortRun   | 3              | 1           | 3           | Or       | 20000         | 96.10 μs | 3.821 μs | 0.209 μs |  5.47 |    0.01 | 10.9863 |  45.02 KB |       10.53 |

## stemmer

| Method                     | Job        | IterationCount | LaunchCount | WarmupCount | DocumentCount | Mean     | Error     | StdDev  | Ratio | RatioSD | Gen0       | Allocated | Alloc Ratio |
|--------------------------- |----------- |--------------- |------------ |------------ |-------------- |---------:|----------:|--------:|------:|--------:|-----------:|----------:|------------:|
| LeanCorpus_StemmedAnalyser | DefaultJob | Default        | Default     | Default     | 20000         | 288.9 ms |   0.59 ms | 0.55 ms |  1.00 |    0.00 |  6500.0000 |  27.44 MB |        1.00 |
| LuceneNet_EnglishAnalyzer  | DefaultJob | Default        | Default     | Default     | 20000         | 378.5 ms |   1.63 ms | 1.52 ms |  1.31 |    0.01 | 15000.0000 |  62.62 MB |        2.28 |
|                            |            |                |             |             |               |          |           |         |       |         |            |           |             |
| LeanCorpus_StemmedAnalyser | ShortRun   | 3              | 1           | 3           | 20000         | 294.1 ms |  10.03 ms | 0.55 ms |  1.00 |    0.00 |  6500.0000 |  27.44 MB |        1.00 |
| LuceneNet_EnglishAnalyzer  | ShortRun   | 3              | 1           | 3           | 20000         | 380.5 ms | 145.03 ms | 7.95 ms |  1.29 |    0.02 | 15000.0000 |  62.62 MB |        2.28 |

## Suggester

| Method                 | Job        | IterationCount | LaunchCount | WarmupCount | DocumentCount | Mean     | Error     | StdDev    | Ratio | RatioSD | Gen0      | Gen1    | Allocated  | Alloc Ratio |
|----------------------- |----------- |--------------- |------------ |------------ |-------------- |---------:|----------:|----------:|------:|--------:|----------:|--------:|-----------:|------------:|
| LeanCorpus_DidYouMean  | DefaultJob | Default        | Default     | Default     | 20000         | 1.909 ms | 0.0047 ms | 0.0044 ms |  1.00 |    0.00 |    1.9531 |       - |    12.4 KB |        1.00 |
| LeanCorpus_SpellIndex  | DefaultJob | Default        | Default     | Default     | 20000         | 1.895 ms | 0.0031 ms | 0.0029 ms |  0.99 |    0.00 |    1.9531 |       - |   10.68 KB |        0.86 |
| LuceneNet_SpellChecker | DefaultJob | Default        | Default     | Default     | 20000         | 8.189 ms | 0.0169 ms | 0.0150 ms |  4.29 |    0.01 | 1281.2500 | 93.7500 | 5262.26 KB |      424.43 |
|                        |            |                |             |             |               |          |           |           |       |         |           |         |            |             |
| LeanCorpus_DidYouMean  | ShortRun   | 3              | 1           | 3           | 20000         | 1.899 ms | 0.0771 ms | 0.0042 ms |  1.00 |    0.00 |    1.9531 |       - |    12.4 KB |        1.00 |
| LeanCorpus_SpellIndex  | ShortRun   | 3              | 1           | 3           | 20000         | 1.900 ms | 0.1047 ms | 0.0057 ms |  1.00 |    0.00 |    1.9531 |       - |   10.68 KB |        0.86 |
| LuceneNet_SpellChecker | ShortRun   | 3              | 1           | 3           | 20000         | 8.286 ms | 0.4996 ms | 0.0274 ms |  4.36 |    0.02 | 1281.2500 | 93.7500 | 5262.26 KB |      424.43 |

## synonym

| Method                  | Job        | IterationCount | LaunchCount | WarmupCount | SynonymCount | DocumentCount | Mean     | Error    | StdDev  | Ratio | Gen0       | Gen1      | Allocated | Alloc Ratio |
|------------------------ |----------- |--------------- |------------ |------------ |------------- |-------------- |---------:|---------:|--------:|------:|-----------:|----------:|----------:|------------:|
| **LeanCorpus_NoSynonyms**   | **DefaultJob** | **Default**        | **Default**     | **Default**     | **10**           | **20000**         | **168.0 ms** |  **0.31 ms** | **0.29 ms** |  **1.00** |  **3333.3333** |         **-** |  **13.65 MB** |        **1.00** |
| LeanCorpus_WithSynonyms | DefaultJob | Default        | Default     | Default     | 10           | 20000         | 284.3 ms |  0.63 ms | 0.56 ms |  1.69 | 33000.0000 | 3000.0000 | 150.32 MB |       11.01 |
|                         |            |                |             |             |              |               |          |          |         |       |            |           |           |             |
| LeanCorpus_NoSynonyms   | ShortRun   | 3              | 1           | 3           | 10           | 20000         | 176.8 ms |  9.95 ms | 0.55 ms |  1.00 |  3333.3333 |         - |  13.65 MB |        1.00 |
| LeanCorpus_WithSynonyms | ShortRun   | 3              | 1           | 3           | 10           | 20000         | 289.4 ms | 16.11 ms | 0.88 ms |  1.64 | 33000.0000 | 3000.0000 | 150.32 MB |       11.01 |
|                         |            |                |             |             |              |               |          |          |         |       |            |           |           |             |
| **LeanCorpus_NoSynonyms**   | **DefaultJob** | **Default**        | **Default**     | **Default**     | **50**           | **20000**         | **171.0 ms** |  **0.20 ms** | **0.19 ms** |  **1.00** |  **3333.3333** |         **-** |  **13.65 MB** |        **1.00** |
| LeanCorpus_WithSynonyms | DefaultJob | Default        | Default     | Default     | 50           | 20000         | 298.8 ms |  0.71 ms | 0.66 ms |  1.75 | 35500.0000 | 3000.0000 | 160.86 MB |       11.78 |
|                         |            |                |             |             |              |               |          |          |         |       |            |           |           |             |
| LeanCorpus_NoSynonyms   | ShortRun   | 3              | 1           | 3           | 50           | 20000         | 169.2 ms |  2.68 ms | 0.15 ms |  1.00 |  3333.3333 |         - |  13.65 MB |        1.00 |
| LeanCorpus_WithSynonyms | ShortRun   | 3              | 1           | 3           | 50           | 20000         | 296.3 ms |  7.87 ms | 0.43 ms |  1.75 | 35500.0000 | 3000.0000 | 160.86 MB |       11.78 |
|                         |            |                |             |             |              |               |          |          |         |       |            |           |           |             |
| **LeanCorpus_NoSynonyms**   | **DefaultJob** | **Default**        | **Default**     | **Default**     | **200**          | **20000**         | **167.1 ms** |  **0.20 ms** | **0.19 ms** |  **1.00** |  **3333.3333** |         **-** |  **13.65 MB** |        **1.00** |
| LeanCorpus_WithSynonyms | DefaultJob | Default        | Default     | Default     | 200          | 20000         | 308.5 ms |  0.75 ms | 0.62 ms |  1.85 | 42500.0000 | 4000.0000 | 188.31 MB |       13.79 |
|                         |            |                |             |             |              |               |          |          |         |       |            |           |           |             |
| LeanCorpus_NoSynonyms   | ShortRun   | 3              | 1           | 3           | 200          | 20000         | 179.2 ms |  6.50 ms | 0.36 ms |  1.00 |  3333.3333 |         - |  13.65 MB |        1.00 |
| LeanCorpus_WithSynonyms | ShortRun   | 3              | 1           | 3           | 200          | 20000         | 309.6 ms | 12.80 ms | 0.70 ms |  1.73 | 42500.0000 | 4000.0000 | 188.31 MB |       13.79 |

## terminset

| Method                         | Job        | IterationCount | LaunchCount | WarmupCount | SetSize | DocumentCount | Mean        | Error      | StdDev    | Ratio | RatioSD | Gen0    | Gen1    | Allocated | Alloc Ratio |
|------------------------------- |----------- |--------------- |------------ |------------ |-------- |-------------- |------------:|-----------:|----------:|------:|--------:|--------:|--------:|----------:|------------:|
| **LeanCorpus_TermInSetQuery**      | **DefaultJob** | **Default**        | **Default**     | **Default**     | **5**       | **20000**         |    **57.16 μs** |   **0.088 μs** |  **0.078 μs** |  **1.00** |    **0.00** |  **1.1597** |       **-** |   **4.74 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_Should | DefaultJob | Default        | Default     | Default     | 5       | 20000         |   114.88 μs |   0.455 μs |  0.426 μs |  2.01 |    0.01 |  1.9531 |       - |   7.86 KB |        1.66 |
|                                |            |                |             |             |         |               |             |            |           |       |         |         |         |           |             |
| LeanCorpus_TermInSetQuery      | ShortRun   | 3              | 1           | 3           | 5       | 20000         |    57.17 μs |   0.273 μs |  0.015 μs |  1.00 |    0.00 |  1.1597 |       - |   4.74 KB |        1.00 |
| LeanCorpus_BooleanQuery_Should | ShortRun   | 3              | 1           | 3           | 5       | 20000         |   115.46 μs |  12.917 μs |  0.708 μs |  2.02 |    0.01 |  1.9531 |       - |   7.86 KB |        1.66 |
|                                |            |                |             |             |         |               |             |            |           |       |         |         |         |           |             |
| **LeanCorpus_TermInSetQuery**      | **DefaultJob** | **Default**        | **Default**     | **Default**     | **20**      | **20000**         |   **134.00 μs** |   **0.798 μs** |  **0.746 μs** |  **1.00** |    **0.00** |  **1.7090** |       **-** |   **7.17 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_Should | DefaultJob | Default        | Default     | Default     | 20      | 20000         |   407.37 μs |   0.981 μs |  0.870 μs |  3.04 |    0.02 |  4.8828 |       - |  18.63 KB |        2.60 |
|                                |            |                |             |             |         |               |             |            |           |       |         |         |         |           |             |
| LeanCorpus_TermInSetQuery      | ShortRun   | 3              | 1           | 3           | 20      | 20000         |   134.73 μs |   5.437 μs |  0.298 μs |  1.00 |    0.00 |  1.7090 |       - |   7.17 KB |        1.00 |
| LeanCorpus_BooleanQuery_Should | ShortRun   | 3              | 1           | 3           | 20      | 20000         |   410.03 μs |  11.624 μs |  0.637 μs |  3.04 |    0.01 |  4.8828 |       - |  18.63 KB |        2.60 |
|                                |            |                |             |             |         |               |             |            |           |       |         |         |         |           |             |
| **LeanCorpus_TermInSetQuery**      | **DefaultJob** | **Default**        | **Default**     | **Default**     | **100**     | **20000**         |   **266.37 μs** |   **3.498 μs** |  **3.101 μs** |  **1.00** |    **0.00** |  **4.8828** |       **-** |  **21.55 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_Should | DefaultJob | Default        | Default     | Default     | 100     | 20000         | 1,549.73 μs |   4.877 μs |  3.808 μs |  5.82 |    0.07 | 50.7813 | 15.6250 | 230.41 KB |       10.69 |
|                                |            |                |             |             |         |               |             |            |           |       |         |         |         |           |             |
| LeanCorpus_TermInSetQuery      | ShortRun   | 3              | 1           | 3           | 100     | 20000         |   266.02 μs |   9.463 μs |  0.519 μs |  1.00 |    0.00 |  4.8828 |       - |  21.51 KB |        1.00 |
| LeanCorpus_BooleanQuery_Should | ShortRun   | 3              | 1           | 3           | 100     | 20000         | 1,557.41 μs | 229.119 μs | 12.559 μs |  5.85 |    0.04 | 50.7813 | 15.6250 | 230.41 KB |       10.71 |

## Wildcard queries

| Method                   | Job        | IterationCount | LaunchCount | WarmupCount | WildcardPattern | DocumentCount | Mean      | Error     | StdDev   | Ratio | RatioSD | Gen0    | Gen1    | Allocated | Alloc Ratio |
|------------------------- |----------- |--------------- |------------ |------------ |---------------- |-------------- |----------:|----------:|---------:|------:|--------:|--------:|--------:|----------:|------------:|
| **LeanCorpus_WildcardQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **gov***            | **20000**         |  **10.49 μs** |  **0.023 μs** | **0.020 μs** |  **1.00** |    **0.00** |  **1.0986** |       **-** |   **4.45 KB** |        **1.00** |
| LuceneNet_WildcardQuery  | DefaultJob | Default        | Default     | Default     | gov*            | 20000         |  28.63 μs |  0.075 μs | 0.070 μs |  2.73 |    0.01 | 17.8528 |  0.0610 |  73.57 KB |       16.55 |
|                          |            |                |             |             |                 |               |           |           |          |       |         |         |         |           |             |
| LeanCorpus_WildcardQuery | ShortRun   | 3              | 1           | 3           | gov*            | 20000         |  10.38 μs |  0.602 μs | 0.033 μs |  1.00 |    0.00 |  1.0986 |       - |   4.45 KB |        1.00 |
| LuceneNet_WildcardQuery  | ShortRun   | 3              | 1           | 3           | gov*            | 20000         |  28.88 μs |  1.107 μs | 0.061 μs |  2.78 |    0.01 | 17.8528 |  0.0610 |  73.57 KB |       16.55 |
|                          |            |                |             |             |                 |               |           |           |          |       |         |         |         |           |             |
| **LeanCorpus_WildcardQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **m*rket**          | **20000**         | **242.76 μs** |  **3.668 μs** | **3.431 μs** |  **1.00** |    **0.00** |  **0.7324** |       **-** |   **3.92 KB** |        **1.00** |
| LuceneNet_WildcardQuery  | DefaultJob | Default        | Default     | Default     | m*rket          | 20000         | 317.36 μs |  0.593 μs | 0.555 μs |  1.31 |    0.02 | 70.8008 |  9.2773 | 290.52 KB |       74.08 |
|                          |            |                |             |             |                 |               |           |           |          |       |         |         |         |           |             |
| LeanCorpus_WildcardQuery | ShortRun   | 3              | 1           | 3           | m*rket          | 20000         | 240.61 μs | 11.362 μs | 0.623 μs |  1.00 |    0.00 |  0.4883 |       - |   3.92 KB |        1.00 |
| LuceneNet_WildcardQuery  | ShortRun   | 3              | 1           | 3           | m*rket          | 20000         | 314.82 μs | 12.913 μs | 0.708 μs |  1.31 |    0.00 | 70.8008 |  9.2773 | 290.52 KB |       74.08 |
|                          |            |                |             |             |                 |               |           |           |          |       |         |         |         |           |             |
| **LeanCorpus_WildcardQuery** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **pre*dent**        | **20000**         |  **22.75 μs** |  **0.072 μs** | **0.067 μs** |  **1.00** |    **0.00** |  **0.9155** |       **-** |    **3.8 KB** |        **1.00** |
| LuceneNet_WildcardQuery  | DefaultJob | Default        | Default     | Default     | pre*dent        | 20000         | 234.51 μs |  0.524 μs | 0.490 μs | 10.31 |    0.04 | 73.2422 | 10.4980 | 299.79 KB |       78.94 |
|                          |            |                |             |             |                 |               |           |           |          |       |         |         |         |           |             |
| LeanCorpus_WildcardQuery | ShortRun   | 3              | 1           | 3           | pre*dent        | 20000         |  22.90 μs |  3.036 μs | 0.166 μs |  1.00 |    0.00 |  0.9155 |       - |    3.8 KB |        1.00 |
| LuceneNet_WildcardQuery  | ShortRun   | 3              | 1           | 3           | pre*dent        | 20000         | 234.10 μs |  7.490 μs | 0.411 μs | 10.22 |    0.07 | 73.2422 | 10.4980 | 299.79 KB |       78.94 |

<details>
<summary>Full data (report.json)</summary>

<pre><code class="lang-json">{
  "schemaVersion": 2,
  "runId": "2026-05-29 09-00 (8df4f79e)",
  "runType": "full",
  "generatedAtUtc": "2026-05-29T09:00:01.2991812\u002B00:00",
  "commandLineArgs": [
    "--job",
    "short"
  ],
  "hostMachineName": "debian",
  "commitHash": "8df4f79e",
  "dotnetVersion": "10.0.3",
  "provenance": {
    "sourceCommit": "8df4f79e",
    "sourceRef": "",
    "sourceManifestPath": "",
    "gitCommitHash": "8df4f79e",
    "gitAvailable": true,
    "gitDirty": false,
    "benchmarkDotNetVersion": "0.16.0-nightly.20260427.506\u002Bc68dc1556c410c4bdfe21373c7689be5781fbaf9",
    "runtimeFramework": ".NET 10.0.3",
    "runtimeIdentifier": "linux-x64",
    "osDescription": "Debian GNU/Linux 13 (trixie)",
    "processArchitecture": "X64",
    "effectiveDocCount": 20000,
    "dataFingerprintSha256": "",
    "dataSources": []
  },
  "totalBenchmarkCount": 222,
  "suites": [
    {
      "suiteName": "aggregation",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.AggregationBenchmarks-20260529-122207",
      "benchmarkCount": 4,
      "benchmarks": [
        {
          "key": "AggregationBenchmarks.LeanCorpus_SearchOnly|DocumentCount=20000",
          "displayInfo": "AggregationBenchmarks.LeanCorpus_SearchOnly: DefaultJob [DocumentCount=20000]",
          "typeName": "AggregationBenchmarks",
          "methodName": "LeanCorpus_SearchOnly",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 2643.355362701416,
            "medianNanoseconds": 2641.628879547119,
            "minNanoseconds": 2640.467430114746,
            "maxNanoseconds": 2649.52836227417,
            "standardDeviationNanoseconds": 3.160492461680491,
            "operationsPerSecond": 378307.0615893412
          },
          "gc": {
            "bytesAllocatedPerOperation": 528,
            "gen0Collections": 33,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "AggregationBenchmarks.LeanCorpus_SearchWithHistogram|DocumentCount=20000",
          "displayInfo": "AggregationBenchmarks.LeanCorpus_SearchWithHistogram: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DocumentCount=20000]",
          "typeName": "AggregationBenchmarks",
          "methodName": "LeanCorpus_SearchWithHistogram",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 10182.29345703125,
            "medianNanoseconds": 10173.361038208008,
            "minNanoseconds": 10167.849655151367,
            "maxNanoseconds": 10205.669677734375,
            "standardDeviationNanoseconds": 20.431094110941874,
            "operationsPerSecond": 98209.70140174688
          },
          "gc": {
            "bytesAllocatedPerOperation": 6952,
            "gen0Collections": 108,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "AggregationBenchmarks.LeanCorpus_SearchWithStatsAndHistogram|DocumentCount=20000",
          "displayInfo": "AggregationBenchmarks.LeanCorpus_SearchWithStatsAndHistogram: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DocumentCount=20000]",
          "typeName": "AggregationBenchmarks",
          "methodName": "LeanCorpus_SearchWithStatsAndHistogram",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 12160.461288452148,
            "medianNanoseconds": 12160.792083740234,
            "minNanoseconds": 12159.224014282227,
            "maxNanoseconds": 12161.367767333984,
            "standardDeviationNanoseconds": 1.1094991792223765,
            "operationsPerSecond": 82233.72257675971
          },
          "gc": {
            "bytesAllocatedPerOperation": 8672,
            "gen0Collections": 135,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "AggregationBenchmarks.LeanCorpus_SearchWithStats|DocumentCount=20000",
          "displayInfo": "AggregationBenchmarks.LeanCorpus_SearchWithStats: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DocumentCount=20000]",
          "typeName": "AggregationBenchmarks",
          "methodName": "LeanCorpus_SearchWithStats",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 9503.048848470053,
            "medianNanoseconds": 9496.17155456543,
            "minNanoseconds": 9495.537567138672,
            "maxNanoseconds": 9517.437423706055,
            "standardDeviationNanoseconds": 12.464903048395614,
            "operationsPerSecond": 105229.38647852952
          },
          "gc": {
            "bytesAllocatedPerOperation": 5832,
            "gen0Collections": 91,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "analysis",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.AnalysisBenchmarks-20260529-100912",
      "benchmarkCount": 2,
      "benchmarks": [
        {
          "key": "AnalysisBenchmarks.LeanCorpus_Analyse|DocumentCount=20000",
          "displayInfo": "AnalysisBenchmarks.LeanCorpus_Analyse: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DocumentCount=20000]",
          "typeName": "AnalysisBenchmarks",
          "methodName": "LeanCorpus_Analyse",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 168925530.2222222,
            "medianNanoseconds": 168864230.33333334,
            "minNanoseconds": 168816683.66666666,
            "maxNanoseconds": 169095676.66666666,
            "standardDeviationNanoseconds": 149256.59380942397,
            "operationsPerSecond": 5.919768306688137
          },
          "gc": {
            "bytesAllocatedPerOperation": 14315256,
            "gen0Collections": 10,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "AnalysisBenchmarks.LuceneNet_Analyse|DocumentCount=20000",
          "displayInfo": "AnalysisBenchmarks.LuceneNet_Analyse: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DocumentCount=20000]",
          "typeName": "AnalysisBenchmarks",
          "methodName": "LuceneNet_Analyse",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 243488833.88888887,
            "medianNanoseconds": 243727234.33333334,
            "minNanoseconds": 242136525.66666666,
            "maxNanoseconds": 244602741.66666666,
            "standardDeviationNanoseconds": 1250272.5377282691,
            "operationsPerSecond": 4.106964512616334
          },
          "gc": {
            "bytesAllocatedPerOperation": 66465536,
            "gen0Collections": 47,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "analysis-filters",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.TokenFilterBenchmarks-20260529-101552",
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
            "meanNanoseconds": 152.92994390215193,
            "medianNanoseconds": 152.87346351146698,
            "minNanoseconds": 152.76629400253296,
            "maxNanoseconds": 153.31528782844543,
            "standardDeviationNanoseconds": 0.1521320901094294,
            "operationsPerSecond": 6538941.782649334
          },
          "gc": {
            "bytesAllocatedPerOperation": 168,
            "gen0Collections": 168,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "TokenFilterBenchmarks.Apply|Scenario=elision-mutating",
          "displayInfo": "TokenFilterBenchmarks.Apply: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [Scenario=elision-mutating]",
          "typeName": "TokenFilterBenchmarks",
          "methodName": "Apply",
          "parameters": {
            "Scenario": "elision-mutating"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 211.74744844436646,
            "medianNanoseconds": 211.69949626922607,
            "minNanoseconds": 211.34035420417786,
            "maxNanoseconds": 212.20249485969543,
            "standardDeviationNanoseconds": 0.43306602937611044,
            "operationsPerSecond": 4722607.083800282
          },
          "gc": {
            "bytesAllocatedPerOperation": 200,
            "gen0Collections": 200,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "TokenFilterBenchmarks.Apply|Scenario=length-mutating",
          "displayInfo": "TokenFilterBenchmarks.Apply: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [Scenario=length-mutating]",
          "typeName": "TokenFilterBenchmarks",
          "methodName": "Apply",
          "parameters": {
            "Scenario": "length-mutating"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 121.02790379524231,
            "medianNanoseconds": 120.9369421005249,
            "minNanoseconds": 120.91823434829712,
            "maxNanoseconds": 121.22853493690491,
            "standardDeviationNanoseconds": 0.1740032650612684,
            "operationsPerSecond": 8262557.382567099
          },
          "gc": {
            "bytesAllocatedPerOperation": 176,
            "gen0Collections": 176,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "TokenFilterBenchmarks.Apply|Scenario=length-noop",
          "displayInfo": "TokenFilterBenchmarks.Apply: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [Scenario=length-noop]",
          "typeName": "TokenFilterBenchmarks",
          "methodName": "Apply",
          "parameters": {
            "Scenario": "length-noop"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 103.2566757996877,
            "medianNanoseconds": 103.20555937290192,
            "minNanoseconds": 103.13733267784119,
            "maxNanoseconds": 103.42713534832001,
            "standardDeviationNanoseconds": 0.1515125861757829,
            "operationsPerSecond": 9684603.850117598
          },
          "gc": {
            "bytesAllocatedPerOperation": 176,
            "gen0Collections": 353,
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
            "meanNanoseconds": 139.36942733128865,
            "medianNanoseconds": 139.3935513496399,
            "minNanoseconds": 139.09311389923096,
            "maxNanoseconds": 139.68042159080505,
            "standardDeviationNanoseconds": 0.16079018919002536,
            "operationsPerSecond": 7175174.77935061
          },
          "gc": {
            "bytesAllocatedPerOperation": 208,
            "gen0Collections": 208,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "TokenFilterBenchmarks.Apply|Scenario=shingle-mutating",
          "displayInfo": "TokenFilterBenchmarks.Apply: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [Scenario=shingle-mutating]",
          "typeName": "TokenFilterBenchmarks",
          "methodName": "Apply",
          "parameters": {
            "Scenario": "shingle-mutating"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 397.9325753847758,
            "medianNanoseconds": 397.83730697631836,
            "minNanoseconds": 397.6489520072937,
            "maxNanoseconds": 398.31146717071533,
            "standardDeviationNanoseconds": 0.34137755883831544,
            "operationsPerSecond": 2512988.5358921993
          },
          "gc": {
            "bytesAllocatedPerOperation": 864,
            "gen0Collections": 433,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "TokenFilterBenchmarks.Apply|Scenario=truncate-mutating",
          "displayInfo": "TokenFilterBenchmarks.Apply: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [Scenario=truncate-mutating]",
          "typeName": "TokenFilterBenchmarks",
          "methodName": "Apply",
          "parameters": {
            "Scenario": "truncate-mutating"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 116.50640698273976,
            "medianNanoseconds": 116.43893837928772,
            "minNanoseconds": 116.37043488025665,
            "maxNanoseconds": 116.70984768867493,
            "standardDeviationNanoseconds": 0.17948335011477284,
            "operationsPerSecond": 8583218.948191823
          },
          "gc": {
            "bytesAllocatedPerOperation": 176,
            "gen0Collections": 353,
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
            "meanNanoseconds": 101.6605009317398,
            "medianNanoseconds": 101.57823276519775,
            "minNanoseconds": 101.50199913978577,
            "maxNanoseconds": 102.00700461864471,
            "standardDeviationNanoseconds": 0.1538955731676844,
            "operationsPerSecond": 9836662.133619158
          },
          "gc": {
            "bytesAllocatedPerOperation": 176,
            "gen0Collections": 353,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "TokenFilterBenchmarks.Apply|Scenario=unique-mutating",
          "displayInfo": "TokenFilterBenchmarks.Apply: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [Scenario=unique-mutating]",
          "typeName": "TokenFilterBenchmarks",
          "methodName": "Apply",
          "parameters": {
            "Scenario": "unique-mutating"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 221.2804470062256,
            "medianNanoseconds": 221.60898613929749,
            "minNanoseconds": 220.41866898536682,
            "maxNanoseconds": 221.81368589401245,
            "standardDeviationNanoseconds": 0.7533070524835997,
            "operationsPerSecond": 4519152.114564671
          },
          "gc": {
            "bytesAllocatedPerOperation": 392,
            "gen0Collections": 393,
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
            "meanNanoseconds": 590.1543685277303,
            "medianNanoseconds": 589.972975730896,
            "minNanoseconds": 587.7579774856567,
            "maxNanoseconds": 592.3030776977539,
            "standardDeviationNanoseconds": 1.2523391022557566,
            "operationsPerSecond": 1694471.9099423422
          },
          "gc": {
            "bytesAllocatedPerOperation": 1432,
            "gen0Collections": 359,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "analysis-parity",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.AnalyserParityBenchmarks-20260529-101158",
      "benchmarkCount": 6,
      "benchmarks": [
        {
          "key": "AnalyserParityBenchmarks.LeanCorpus_Keyword",
          "displayInfo": "AnalyserParityBenchmarks.LeanCorpus_Keyword: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)",
          "typeName": "AnalyserParityBenchmarks",
          "methodName": "LeanCorpus_Keyword",
          "parameters": {},
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 4099.489649454753,
            "medianNanoseconds": 4099.736236572266,
            "minNanoseconds": 4098.33260345459,
            "maxNanoseconds": 4100.400108337402,
            "standardDeviationNanoseconds": 1.0555794688254967,
            "operationsPerSecond": 243932.80274119088
          },
          "gc": {
            "bytesAllocatedPerOperation": 0,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "AnalyserParityBenchmarks.LeanCorpus_Simple",
          "displayInfo": "AnalyserParityBenchmarks.LeanCorpus_Simple: DefaultJob",
          "typeName": "AnalyserParityBenchmarks",
          "methodName": "LeanCorpus_Simple",
          "parameters": {},
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 42165.4948445638,
            "medianNanoseconds": 42178.618408203125,
            "minNanoseconds": 41661.970275878906,
            "maxNanoseconds": 42546.65399169922,
            "standardDeviationNanoseconds": 228.12198780519816,
            "operationsPerSecond": 23716.074095331656
          },
          "gc": {
            "bytesAllocatedPerOperation": 0,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "AnalyserParityBenchmarks.LeanCorpus_Whitespace",
          "displayInfo": "AnalyserParityBenchmarks.LeanCorpus_Whitespace: DefaultJob",
          "typeName": "AnalyserParityBenchmarks",
          "methodName": "LeanCorpus_Whitespace",
          "parameters": {},
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 48158.60496419271,
            "medianNanoseconds": 48145.52380371094,
            "minNanoseconds": 48108.95056152344,
            "maxNanoseconds": 48250.171875,
            "standardDeviationNanoseconds": 43.80184217768755,
            "operationsPerSecond": 20764.72108657484
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
          "displayInfo": "AnalyserParityBenchmarks.LuceneNet_Keyword: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)",
          "typeName": "AnalyserParityBenchmarks",
          "methodName": "LuceneNet_Keyword",
          "parameters": {},
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 12096.263326009115,
            "medianNanoseconds": 12125.098541259766,
            "minNanoseconds": 12031.783798217773,
            "maxNanoseconds": 12131.907638549805,
            "standardDeviationNanoseconds": 55.94459830602107,
            "operationsPerSecond": 82670.15796934764
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
            "meanNanoseconds": 82776.81731770834,
            "medianNanoseconds": 82738.66088867188,
            "minNanoseconds": 82710.02722167969,
            "maxNanoseconds": 82933.82836914062,
            "standardDeviationNanoseconds": 82.05257326849622,
            "operationsPerSecond": 12080.677083317521
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
            "sampleCount": 12,
            "meanNanoseconds": 74583.49682617188,
            "medianNanoseconds": 74588.00653076172,
            "minNanoseconds": 74482.99353027344,
            "maxNanoseconds": 74633.55798339844,
            "standardDeviationNanoseconds": 39.74025843330291,
            "operationsPerSecond": 13407.791838061057
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
      "suiteName": "async-index",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.AsyncIndexingBenchmarks-20260529-131713",
      "benchmarkCount": 3,
      "benchmarks": [
        {
          "key": "AsyncIndexingBenchmarks.LeanCorpus_AddDocumentAsync_Sequential|DocumentCount=20000",
          "displayInfo": "AsyncIndexingBenchmarks.LeanCorpus_AddDocumentAsync_Sequential: DefaultJob [DocumentCount=20000]",
          "typeName": "AsyncIndexingBenchmarks",
          "methodName": "LeanCorpus_AddDocumentAsync_Sequential",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 990916315.2142857,
            "medianNanoseconds": 990059453.5,
            "minNanoseconds": 983393273,
            "maxNanoseconds": 998909639,
            "standardDeviationNanoseconds": 4752080.09817154,
            "operationsPerSecond": 1.0091669545109365
          },
          "gc": {
            "bytesAllocatedPerOperation": 135733232,
            "gen0Collections": 18,
            "gen1Collections": 9,
            "gen2Collections": 0
          }
        },
        {
          "key": "AsyncIndexingBenchmarks.LeanCorpus_AddDocument_Sync|DocumentCount=20000",
          "displayInfo": "AsyncIndexingBenchmarks.LeanCorpus_AddDocument_Sync: DefaultJob [DocumentCount=20000]",
          "typeName": "AsyncIndexingBenchmarks",
          "methodName": "LeanCorpus_AddDocument_Sync",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 993454481.2857143,
            "medianNanoseconds": 992834650.5,
            "minNanoseconds": 983969133,
            "maxNanoseconds": 1005645492,
            "standardDeviationNanoseconds": 6338615.348045738,
            "operationsPerSecond": 1.006588644812206
          },
          "gc": {
            "bytesAllocatedPerOperation": 135715032,
            "gen0Collections": 17,
            "gen1Collections": 8,
            "gen2Collections": 0
          }
        },
        {
          "key": "AsyncIndexingBenchmarks.LeanCorpus_AddDocumentsAsync_Batch|DocumentCount=20000",
          "displayInfo": "AsyncIndexingBenchmarks.LeanCorpus_AddDocumentsAsync_Batch: DefaultJob [DocumentCount=20000]",
          "typeName": "AsyncIndexingBenchmarks",
          "methodName": "LeanCorpus_AddDocumentsAsync_Batch",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 974158668.2,
            "medianNanoseconds": 972805239,
            "minNanoseconds": 965795399,
            "maxNanoseconds": 990073077,
            "standardDeviationNanoseconds": 6792708.940678361,
            "operationsPerSecond": 1.0265268201613893
          },
          "gc": {
            "bytesAllocatedPerOperation": 135733168,
            "gen0Collections": 17,
            "gen1Collections": 8,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "blockjoin-index",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.BlockJoinIndexBenchmarks-20260529-111310",
      "benchmarkCount": 2,
      "benchmarks": [
        {
          "key": "BlockJoinIndexBenchmarks.LeanLucene_IndexBlocks|BlockCount=500",
          "displayInfo": "BlockJoinIndexBenchmarks.LeanLucene_IndexBlocks: Job-CNUJVU(InvocationCount=1, UnrollFactor=1) [BlockCount=500]",
          "typeName": "BlockJoinIndexBenchmarks",
          "methodName": "LeanLucene_IndexBlocks",
          "parameters": {
            "BlockCount": "500"
          },
          "statistics": {
            "sampleCount": 12,
            "meanNanoseconds": 61693372.166666664,
            "medianNanoseconds": 61613127,
            "minNanoseconds": 60781675,
            "maxNanoseconds": 62354676,
            "standardDeviationNanoseconds": 448728.346810048,
            "operationsPerSecond": 16.209196626478242
          },
          "gc": {
            "bytesAllocatedPerOperation": 13141856,
            "gen0Collections": 1,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "BlockJoinIndexBenchmarks.LuceneNet_IndexBlocks|BlockCount=500",
          "displayInfo": "BlockJoinIndexBenchmarks.LuceneNet_IndexBlocks: ShortRun(InvocationCount=1, IterationCount=3, LaunchCount=1, UnrollFactor=1, WarmupCount=3) [BlockCount=500]",
          "typeName": "BlockJoinIndexBenchmarks",
          "methodName": "LuceneNet_IndexBlocks",
          "parameters": {
            "BlockCount": "500"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 55389229,
            "medianNanoseconds": 55020041,
            "minNanoseconds": 54842267,
            "maxNanoseconds": 56305379,
            "standardDeviationNanoseconds": 798372.7297722537,
            "operationsPerSecond": 18.054051627979874
          },
          "gc": {
            "bytesAllocatedPerOperation": 28366360,
            "gen0Collections": 5,
            "gen1Collections": 1,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "blockjoin-search",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.BlockJoinSearchBenchmarks-20260529-111502",
      "benchmarkCount": 2,
      "benchmarks": [
        {
          "key": "BlockJoinSearchBenchmarks.LeanLucene_BlockJoinQuery|BlockCount=500",
          "displayInfo": "BlockJoinSearchBenchmarks.LeanLucene_BlockJoinQuery: DefaultJob [BlockCount=500]",
          "typeName": "BlockJoinSearchBenchmarks",
          "methodName": "LeanLucene_BlockJoinQuery",
          "parameters": {
            "BlockCount": "500"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 7078.186760965983,
            "medianNanoseconds": 7071.323921203613,
            "minNanoseconds": 7061.405990600586,
            "maxNanoseconds": 7109.8163986206055,
            "standardDeviationNanoseconds": 14.465640684890095,
            "operationsPerSecond": 141279.12045422304
          },
          "gc": {
            "bytesAllocatedPerOperation": 744,
            "gen0Collections": 23,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "BlockJoinSearchBenchmarks.LuceneNet_ToParentBlockJoinQuery|BlockCount=500",
          "displayInfo": "BlockJoinSearchBenchmarks.LuceneNet_ToParentBlockJoinQuery: DefaultJob [BlockCount=500]",
          "typeName": "BlockJoinSearchBenchmarks",
          "methodName": "LuceneNet_ToParentBlockJoinQuery",
          "parameters": {
            "BlockCount": "500"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 24841.07027791341,
            "medianNanoseconds": 24893.984283447266,
            "minNanoseconds": 24623.60174560547,
            "maxNanoseconds": 24951.173858642578,
            "standardDeviationNanoseconds": 122.42542157019322,
            "operationsPerSecond": 40255.91445184694
          },
          "gc": {
            "bytesAllocatedPerOperation": 10941,
            "gen0Collections": 84,
            "gen1Collections": 1,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "boolean",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.BooleanQueryBenchmarks-20260529-102159",
      "benchmarkCount": 10,
      "benchmarks": [
        {
          "key": "BooleanQueryBenchmarks.LeanCorpus_BooleanQuery|BooleanShape=Must2Common, DocumentCount=20000",
          "displayInfo": "BooleanQueryBenchmarks.LeanCorpus_BooleanQuery: DefaultJob [BooleanShape=Must2Common, DocumentCount=20000]",
          "typeName": "BooleanQueryBenchmarks",
          "methodName": "LeanCorpus_BooleanQuery",
          "parameters": {
            "BooleanShape": "Must2Common",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 10220.539220537457,
            "medianNanoseconds": 10216.558990478516,
            "minNanoseconds": 10185.239013671875,
            "maxNanoseconds": 10249.222732543945,
            "standardDeviationNanoseconds": 22.144966412875245,
            "operationsPerSecond": 97842.19583939076
          },
          "gc": {
            "bytesAllocatedPerOperation": 4840,
            "gen0Collections": 76,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "BooleanQueryBenchmarks.LeanCorpus_BooleanQuery|BooleanShape=Must3Mixed, DocumentCount=20000",
          "displayInfo": "BooleanQueryBenchmarks.LeanCorpus_BooleanQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [BooleanShape=Must3Mixed, DocumentCount=20000]",
          "typeName": "BooleanQueryBenchmarks",
          "methodName": "LeanCorpus_BooleanQuery",
          "parameters": {
            "BooleanShape": "Must3Mixed",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 7854.89155069987,
            "medianNanoseconds": 7826.758087158203,
            "minNanoseconds": 7797.609619140625,
            "maxNanoseconds": 7940.306945800781,
            "standardDeviationNanoseconds": 75.39396916946707,
            "operationsPerSecond": 127309.20516794406
          },
          "gc": {
            "bytesAllocatedPerOperation": 5303,
            "gen0Collections": 84,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "BooleanQueryBenchmarks.LeanCorpus_BooleanQuery|BooleanShape=MustNotCommon, DocumentCount=20000",
          "displayInfo": "BooleanQueryBenchmarks.LeanCorpus_BooleanQuery: DefaultJob [BooleanShape=MustNotCommon, DocumentCount=20000]",
          "typeName": "BooleanQueryBenchmarks",
          "methodName": "LeanCorpus_BooleanQuery",
          "parameters": {
            "BooleanShape": "MustNotCommon",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 9229.702310180664,
            "medianNanoseconds": 9233.473266601562,
            "minNanoseconds": 9188.75912475586,
            "maxNanoseconds": 9299.680419921875,
            "standardDeviationNanoseconds": 31.5923295965933,
            "operationsPerSecond": 108345.85627934795
          },
          "gc": {
            "bytesAllocatedPerOperation": 5149,
            "gen0Collections": 81,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "BooleanQueryBenchmarks.LeanCorpus_BooleanQuery|BooleanShape=Should2Common, DocumentCount=20000",
          "displayInfo": "BooleanQueryBenchmarks.LeanCorpus_BooleanQuery: DefaultJob [BooleanShape=Should2Common, DocumentCount=20000]",
          "typeName": "BooleanQueryBenchmarks",
          "methodName": "LeanCorpus_BooleanQuery",
          "parameters": {
            "BooleanShape": "Should2Common",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 31396.311079915366,
            "medianNanoseconds": 31403.79559326172,
            "minNanoseconds": 31043.65704345703,
            "maxNanoseconds": 31717.13751220703,
            "standardDeviationNanoseconds": 170.269908397098,
            "operationsPerSecond": 31850.87564760795
          },
          "gc": {
            "bytesAllocatedPerOperation": 5395,
            "gen0Collections": 21,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "BooleanQueryBenchmarks.LeanCorpus_BooleanQuery|BooleanShape=Should4Mixed, DocumentCount=20000",
          "displayInfo": "BooleanQueryBenchmarks.LeanCorpus_BooleanQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [BooleanShape=Should4Mixed, DocumentCount=20000]",
          "typeName": "BooleanQueryBenchmarks",
          "methodName": "LeanCorpus_BooleanQuery",
          "parameters": {
            "BooleanShape": "Should4Mixed",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 44856.950703938805,
            "medianNanoseconds": 44828.46533203125,
            "minNanoseconds": 44794.30340576172,
            "maxNanoseconds": 44948.08337402344,
            "standardDeviationNanoseconds": 80.75043014856004,
            "operationsPerSecond": 22293.089126813782
          },
          "gc": {
            "bytesAllocatedPerOperation": 6644,
            "gen0Collections": 26,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "BooleanQueryBenchmarks.LuceneNet_BooleanQuery|BooleanShape=Must2Common, DocumentCount=20000",
          "displayInfo": "BooleanQueryBenchmarks.LuceneNet_BooleanQuery: DefaultJob [BooleanShape=Must2Common, DocumentCount=20000]",
          "typeName": "BooleanQueryBenchmarks",
          "methodName": "LuceneNet_BooleanQuery",
          "parameters": {
            "BooleanShape": "Must2Common",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 32348.611067708334,
            "medianNanoseconds": 32594.540100097656,
            "minNanoseconds": 31976.01824951172,
            "maxNanoseconds": 32664.00164794922,
            "standardDeviationNanoseconds": 322.61434042682373,
            "operationsPerSecond": 30913.228327080775
          },
          "gc": {
            "bytesAllocatedPerOperation": 23099,
            "gen0Collections": 89,
            "gen1Collections": 2,
            "gen2Collections": 0
          }
        },
        {
          "key": "BooleanQueryBenchmarks.LuceneNet_BooleanQuery|BooleanShape=Must3Mixed, DocumentCount=20000",
          "displayInfo": "BooleanQueryBenchmarks.LuceneNet_BooleanQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [BooleanShape=Must3Mixed, DocumentCount=20000]",
          "typeName": "BooleanQueryBenchmarks",
          "methodName": "LuceneNet_BooleanQuery",
          "parameters": {
            "BooleanShape": "Must3Mixed",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 35719.9045715332,
            "medianNanoseconds": 35691.422271728516,
            "minNanoseconds": 35662.58108520508,
            "maxNanoseconds": 35805.710357666016,
            "standardDeviationNanoseconds": 75.69628927776048,
            "operationsPerSecond": 27995.595508867762
          },
          "gc": {
            "bytesAllocatedPerOperation": 32046,
            "gen0Collections": 249,
            "gen1Collections": 2,
            "gen2Collections": 0
          }
        },
        {
          "key": "BooleanQueryBenchmarks.LuceneNet_BooleanQuery|BooleanShape=MustNotCommon, DocumentCount=20000",
          "displayInfo": "BooleanQueryBenchmarks.LuceneNet_BooleanQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [BooleanShape=MustNotCommon, DocumentCount=20000]",
          "typeName": "BooleanQueryBenchmarks",
          "methodName": "LuceneNet_BooleanQuery",
          "parameters": {
            "BooleanShape": "MustNotCommon",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 29820.92046101888,
            "medianNanoseconds": 29725.47149658203,
            "minNanoseconds": 29535.20883178711,
            "maxNanoseconds": 30202.0810546875,
            "standardDeviationNanoseconds": 343.5295024129328,
            "operationsPerSecond": 33533.505490119715
          },
          "gc": {
            "bytesAllocatedPerOperation": 24689,
            "gen0Collections": 192,
            "gen1Collections": 2,
            "gen2Collections": 0
          }
        },
        {
          "key": "BooleanQueryBenchmarks.LuceneNet_BooleanQuery|BooleanShape=Should2Common, DocumentCount=20000",
          "displayInfo": "BooleanQueryBenchmarks.LuceneNet_BooleanQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [BooleanShape=Should2Common, DocumentCount=20000]",
          "typeName": "BooleanQueryBenchmarks",
          "methodName": "LuceneNet_BooleanQuery",
          "parameters": {
            "BooleanShape": "Should2Common",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 82188.20336914062,
            "medianNanoseconds": 82176.0166015625,
            "minNanoseconds": 82002.89978027344,
            "maxNanoseconds": 82385.69372558594,
            "standardDeviationNanoseconds": 191.68773857495356,
            "operationsPerSecond": 12167.196252101941
          },
          "gc": {
            "bytesAllocatedPerOperation": 139632,
            "gen0Collections": 271,
            "gen1Collections": 11,
            "gen2Collections": 0
          }
        },
        {
          "key": "BooleanQueryBenchmarks.LuceneNet_BooleanQuery|BooleanShape=Should4Mixed, DocumentCount=20000",
          "displayInfo": "BooleanQueryBenchmarks.LuceneNet_BooleanQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [BooleanShape=Should4Mixed, DocumentCount=20000]",
          "typeName": "BooleanQueryBenchmarks",
          "methodName": "LuceneNet_BooleanQuery",
          "parameters": {
            "BooleanShape": "Should4Mixed",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 104784.328125,
            "medianNanoseconds": 104799.38354492188,
            "minNanoseconds": 104602.03247070312,
            "maxNanoseconds": 104951.568359375,
            "standardDeviationNanoseconds": 175.2536265507146,
            "operationsPerSecond": 9543.411862192535
          },
          "gc": {
            "bytesAllocatedPerOperation": 162512,
            "gen0Collections": 316,
            "gen1Collections": 8,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "collapse-facet",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.CollapseAndFacetBenchmarks-20260529-124243",
      "benchmarkCount": 4,
      "benchmarks": [
        {
          "key": "CollapseAndFacetBenchmarks.LeanCorpus_BaseSearch|DocumentCount=20000",
          "displayInfo": "CollapseAndFacetBenchmarks.LeanCorpus_BaseSearch: DefaultJob [DocumentCount=20000]",
          "typeName": "CollapseAndFacetBenchmarks",
          "methodName": "LeanCorpus_BaseSearch",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 2629.5374855041505,
            "medianNanoseconds": 2629.536449432373,
            "minNanoseconds": 2618.6049728393555,
            "maxNanoseconds": 2640.189067840576,
            "standardDeviationNanoseconds": 6.495381784434048,
            "operationsPerSecond": 380295.0159534516
          },
          "gc": {
            "bytesAllocatedPerOperation": 528,
            "gen0Collections": 33,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "CollapseAndFacetBenchmarks.LeanCorpus_SearchWithCollapseAndFacets|DocumentCount=20000",
          "displayInfo": "CollapseAndFacetBenchmarks.LeanCorpus_SearchWithCollapseAndFacets: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DocumentCount=20000]",
          "typeName": "CollapseAndFacetBenchmarks",
          "methodName": "LeanCorpus_SearchWithCollapseAndFacets",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 9733.802591959635,
            "medianNanoseconds": 9741.50163269043,
            "minNanoseconds": 9709.326461791992,
            "maxNanoseconds": 9750.579681396484,
            "standardDeviationNanoseconds": 21.677487240566805,
            "operationsPerSecond": 102734.77302960973
          },
          "gc": {
            "bytesAllocatedPerOperation": 2712,
            "gen0Collections": 42,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "CollapseAndFacetBenchmarks.LeanCorpus_SearchWithCollapse|DocumentCount=20000",
          "displayInfo": "CollapseAndFacetBenchmarks.LeanCorpus_SearchWithCollapse: DefaultJob [DocumentCount=20000]",
          "typeName": "CollapseAndFacetBenchmarks",
          "methodName": "LeanCorpus_SearchWithCollapse",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 9558.511925252278,
            "medianNanoseconds": 9552.570724487305,
            "minNanoseconds": 9535.694122314453,
            "maxNanoseconds": 9590.874160766602,
            "standardDeviationNanoseconds": 16.73549208495964,
            "operationsPerSecond": 104618.79504048503
          },
          "gc": {
            "bytesAllocatedPerOperation": 2712,
            "gen0Collections": 42,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "CollapseAndFacetBenchmarks.LeanCorpus_SearchWithFacets|DocumentCount=20000",
          "displayInfo": "CollapseAndFacetBenchmarks.LeanCorpus_SearchWithFacets: DefaultJob [DocumentCount=20000]",
          "typeName": "CollapseAndFacetBenchmarks",
          "methodName": "LeanCorpus_SearchWithFacets",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 12705.596937052409,
            "medianNanoseconds": 12698.120513916016,
            "minNanoseconds": 12682.691970825195,
            "maxNanoseconds": 12740.382705688477,
            "standardDeviationNanoseconds": 19.88593876091226,
            "operationsPerSecond": 78705.47168734534
          },
          "gc": {
            "bytesAllocatedPerOperation": 7048,
            "gen0Collections": 110,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "combined",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.CombinedFieldsQueryBenchmarks-20260529-121326",
      "benchmarkCount": 4,
      "benchmarks": [
        {
          "key": "CombinedFieldsQueryBenchmarks.LeanCorpus_BooleanQuery_MultiField|DocumentCount=20000, MinimumShouldMatch=1",
          "displayInfo": "CombinedFieldsQueryBenchmarks.LeanCorpus_BooleanQuery_MultiField: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [MinimumShouldMatch=1, DocumentCount=20000]",
          "typeName": "CombinedFieldsQueryBenchmarks",
          "methodName": "LeanCorpus_BooleanQuery_MultiField",
          "parameters": {
            "MinimumShouldMatch": "1",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 13919.490839640299,
            "medianNanoseconds": 13948.723236083984,
            "minNanoseconds": 13858.871368408203,
            "maxNanoseconds": 13950.877914428711,
            "standardDeviationNanoseconds": 52.50905520990191,
            "operationsPerSecond": 71841.7082579036
          },
          "gc": {
            "bytesAllocatedPerOperation": 7232,
            "gen0Collections": 115,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "CombinedFieldsQueryBenchmarks.LeanCorpus_BooleanQuery_MultiField|DocumentCount=20000, MinimumShouldMatch=2",
          "displayInfo": "CombinedFieldsQueryBenchmarks.LeanCorpus_BooleanQuery_MultiField: DefaultJob [MinimumShouldMatch=2, DocumentCount=20000]",
          "typeName": "CombinedFieldsQueryBenchmarks",
          "methodName": "LeanCorpus_BooleanQuery_MultiField",
          "parameters": {
            "MinimumShouldMatch": "2",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 14146.484579467773,
            "medianNanoseconds": 14126.639343261719,
            "minNanoseconds": 13969.804443359375,
            "maxNanoseconds": 14320.520263671875,
            "standardDeviationNanoseconds": 108.25392826048171,
            "operationsPerSecond": 70688.94002481729
          },
          "gc": {
            "bytesAllocatedPerOperation": 7232,
            "gen0Collections": 115,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "CombinedFieldsQueryBenchmarks.LeanCorpus_CombinedFieldsQuery|DocumentCount=20000, MinimumShouldMatch=1",
          "displayInfo": "CombinedFieldsQueryBenchmarks.LeanCorpus_CombinedFieldsQuery: DefaultJob [MinimumShouldMatch=1, DocumentCount=20000]",
          "typeName": "CombinedFieldsQueryBenchmarks",
          "methodName": "LeanCorpus_CombinedFieldsQuery",
          "parameters": {
            "MinimumShouldMatch": "1",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 21758.308460780554,
            "medianNanoseconds": 21742.535110473633,
            "minNanoseconds": 21616.087951660156,
            "maxNanoseconds": 21978.686553955078,
            "standardDeviationNanoseconds": 99.7569537176198,
            "operationsPerSecond": 45959.455065291695
          },
          "gc": {
            "bytesAllocatedPerOperation": 13629,
            "gen0Collections": 107,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "CombinedFieldsQueryBenchmarks.LeanCorpus_CombinedFieldsQuery|DocumentCount=20000, MinimumShouldMatch=2",
          "displayInfo": "CombinedFieldsQueryBenchmarks.LeanCorpus_CombinedFieldsQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [MinimumShouldMatch=2, DocumentCount=20000]",
          "typeName": "CombinedFieldsQueryBenchmarks",
          "methodName": "LeanCorpus_CombinedFieldsQuery",
          "parameters": {
            "MinimumShouldMatch": "2",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 19527.96245320638,
            "medianNanoseconds": 19511.379028320312,
            "minNanoseconds": 19388.02621459961,
            "maxNanoseconds": 19684.48211669922,
            "standardDeviationNanoseconds": 148.92207008411205,
            "operationsPerSecond": 51208.619557531245
          },
          "gc": {
            "bytesAllocatedPerOperation": 13159,
            "gen0Collections": 103,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "deletion-commit",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.DeletionCommitBenchmarks-20260529-105323",
      "benchmarkCount": 2,
      "benchmarks": [
        {
          "key": "DeletionCommitBenchmarks.LeanLucene_CommitDeletes|DocumentCount=20000",
          "displayInfo": "DeletionCommitBenchmarks.LeanLucene_CommitDeletes: Job-CNUJVU(InvocationCount=1, UnrollFactor=1) [DocumentCount=20000]",
          "typeName": "DeletionCommitBenchmarks",
          "methodName": "LeanLucene_CommitDeletes",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 4285034.6,
            "medianNanoseconds": 4275334,
            "minNanoseconds": 4213302,
            "maxNanoseconds": 4463374,
            "standardDeviationNanoseconds": 70805.31585451153,
            "operationsPerSecond": 233.3703443141393
          },
          "gc": {
            "bytesAllocatedPerOperation": 1857528,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "DeletionCommitBenchmarks.LuceneNet_CommitDeletes|DocumentCount=20000",
          "displayInfo": "DeletionCommitBenchmarks.LuceneNet_CommitDeletes: Job-CNUJVU(InvocationCount=1, UnrollFactor=1) [DocumentCount=20000]",
          "typeName": "DeletionCommitBenchmarks",
          "methodName": "LuceneNet_CommitDeletes",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 95,
            "meanNanoseconds": 2478587.094736842,
            "medianNanoseconds": 2385258,
            "minNanoseconds": 2312283,
            "maxNanoseconds": 2966219,
            "standardDeviationNanoseconds": 154312.46485863108,
            "operationsPerSecond": 403.4556631572281
          },
          "gc": {
            "bytesAllocatedPerOperation": 4058960,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "deletion-queue",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.DeletionQueueBenchmarks-20260529-105208",
      "benchmarkCount": 2,
      "benchmarks": [
        {
          "key": "DeletionQueueBenchmarks.LeanLucene_QueueDeletes|DocumentCount=20000",
          "displayInfo": "DeletionQueueBenchmarks.LeanLucene_QueueDeletes: Job-CNUJVU(InvocationCount=1, UnrollFactor=1) [DocumentCount=20000]",
          "typeName": "DeletionQueueBenchmarks",
          "methodName": "LeanLucene_QueueDeletes",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 92,
            "meanNanoseconds": 87322.22826086957,
            "medianNanoseconds": 82414,
            "minNanoseconds": 81038,
            "maxNanoseconds": 115973,
            "standardDeviationNanoseconds": 9258.87370634041,
            "operationsPerSecond": 11451.83786438161
          },
          "gc": {
            "bytesAllocatedPerOperation": 248848,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "DeletionQueueBenchmarks.LuceneNet_QueueDeletes|DocumentCount=20000",
          "displayInfo": "DeletionQueueBenchmarks.LuceneNet_QueueDeletes: Job-CNUJVU(InvocationCount=1, UnrollFactor=1) [DocumentCount=20000]",
          "typeName": "DeletionQueueBenchmarks",
          "methodName": "LuceneNet_QueueDeletes",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 13,
            "meanNanoseconds": 553897,
            "medianNanoseconds": 551605,
            "minNanoseconds": 545733,
            "maxNanoseconds": 570746,
            "standardDeviationNanoseconds": 8767.230339926818,
            "operationsPerSecond": 1805.3898107409861
          },
          "gc": {
            "bytesAllocatedPerOperation": 605160,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "dismax",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.DisjunctionMaxQueryBenchmarks-20260529-114506",
      "benchmarkCount": 6,
      "benchmarks": [
        {
          "key": "DisjunctionMaxQueryBenchmarks.LeanCorpus_DisjunctionMaxQuery|DocumentCount=20000, TieBreakerMultiplier=0",
          "displayInfo": "DisjunctionMaxQueryBenchmarks.LeanCorpus_DisjunctionMaxQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [TieBreakerMultiplier=0, DocumentCount=20000]",
          "typeName": "DisjunctionMaxQueryBenchmarks",
          "methodName": "LeanCorpus_DisjunctionMaxQuery",
          "parameters": {
            "TieBreakerMultiplier": "0",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 31826.591369628906,
            "medianNanoseconds": 31782.03759765625,
            "minNanoseconds": 31664.935668945312,
            "maxNanoseconds": 32032.800842285156,
            "standardDeviationNanoseconds": 187.9360938905348,
            "operationsPerSecond": 31420.26704607355
          },
          "gc": {
            "bytesAllocatedPerOperation": 4819,
            "gen0Collections": 19,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "DisjunctionMaxQueryBenchmarks.LeanCorpus_DisjunctionMaxQuery|DocumentCount=20000, TieBreakerMultiplier=0.1",
          "displayInfo": "DisjunctionMaxQueryBenchmarks.LeanCorpus_DisjunctionMaxQuery: DefaultJob [TieBreakerMultiplier=0.1, DocumentCount=20000]",
          "typeName": "DisjunctionMaxQueryBenchmarks",
          "methodName": "LeanCorpus_DisjunctionMaxQuery",
          "parameters": {
            "TieBreakerMultiplier": "0.1",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 30947.899701799666,
            "medianNanoseconds": 30937.37176513672,
            "minNanoseconds": 30631.171813964844,
            "maxNanoseconds": 31222.648071289062,
            "standardDeviationNanoseconds": 153.83038526110315,
            "operationsPerSecond": 32312.370456010252
          },
          "gc": {
            "bytesAllocatedPerOperation": 4817,
            "gen0Collections": 19,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "DisjunctionMaxQueryBenchmarks.LeanCorpus_DisjunctionMaxQuery|DocumentCount=20000, TieBreakerMultiplier=0.5",
          "displayInfo": "DisjunctionMaxQueryBenchmarks.LeanCorpus_DisjunctionMaxQuery: DefaultJob [TieBreakerMultiplier=0.5, DocumentCount=20000]",
          "typeName": "DisjunctionMaxQueryBenchmarks",
          "methodName": "LeanCorpus_DisjunctionMaxQuery",
          "parameters": {
            "TieBreakerMultiplier": "0.5",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 31763.372147623697,
            "medianNanoseconds": 31783.360961914062,
            "minNanoseconds": 31577.874633789062,
            "maxNanoseconds": 32053.91180419922,
            "standardDeviationNanoseconds": 137.78546727315828,
            "operationsPerSecond": 31482.8033797039
          },
          "gc": {
            "bytesAllocatedPerOperation": 4819,
            "gen0Collections": 19,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "DisjunctionMaxQueryBenchmarks.LuceneNet_DisjunctionMaxQuery|DocumentCount=20000, TieBreakerMultiplier=0",
          "displayInfo": "DisjunctionMaxQueryBenchmarks.LuceneNet_DisjunctionMaxQuery: DefaultJob [TieBreakerMultiplier=0, DocumentCount=20000]",
          "typeName": "DisjunctionMaxQueryBenchmarks",
          "methodName": "LuceneNet_DisjunctionMaxQuery",
          "parameters": {
            "TieBreakerMultiplier": "0",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 55887.845829264326,
            "medianNanoseconds": 55854.12841796875,
            "minNanoseconds": 55808.45617675781,
            "maxNanoseconds": 56030.36651611328,
            "standardDeviationNanoseconds": 80.62035852962642,
            "operationsPerSecond": 17892.978073532653
          },
          "gc": {
            "bytesAllocatedPerOperation": 40432,
            "gen0Collections": 158,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "DisjunctionMaxQueryBenchmarks.LuceneNet_DisjunctionMaxQuery|DocumentCount=20000, TieBreakerMultiplier=0.1",
          "displayInfo": "DisjunctionMaxQueryBenchmarks.LuceneNet_DisjunctionMaxQuery: DefaultJob [TieBreakerMultiplier=0.1, DocumentCount=20000]",
          "typeName": "DisjunctionMaxQueryBenchmarks",
          "methodName": "LuceneNet_DisjunctionMaxQuery",
          "parameters": {
            "TieBreakerMultiplier": "0.1",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 56234.55651855469,
            "medianNanoseconds": 56214.132080078125,
            "minNanoseconds": 56101.71643066406,
            "maxNanoseconds": 56510.18762207031,
            "standardDeviationNanoseconds": 121.94076437787507,
            "operationsPerSecond": 17782.66002097924
          },
          "gc": {
            "bytesAllocatedPerOperation": 40432,
            "gen0Collections": 158,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "DisjunctionMaxQueryBenchmarks.LuceneNet_DisjunctionMaxQuery|DocumentCount=20000, TieBreakerMultiplier=0.5",
          "displayInfo": "DisjunctionMaxQueryBenchmarks.LuceneNet_DisjunctionMaxQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [TieBreakerMultiplier=0.5, DocumentCount=20000]",
          "typeName": "DisjunctionMaxQueryBenchmarks",
          "methodName": "LuceneNet_DisjunctionMaxQuery",
          "parameters": {
            "TieBreakerMultiplier": "0.5",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 56080.376525878906,
            "medianNanoseconds": 56046.959411621094,
            "minNanoseconds": 55960.13787841797,
            "maxNanoseconds": 56234.032287597656,
            "standardDeviationNanoseconds": 139.97165601993245,
            "operationsPerSecond": 17831.54932168722
          },
          "gc": {
            "bytesAllocatedPerOperation": 40432,
            "gen0Collections": 158,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "function-score",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.FunctionScoreQueryBenchmarks-20260529-123325",
      "benchmarkCount": 8,
      "benchmarks": [
        {
          "key": "FunctionScoreQueryBenchmarks.LeanCorpus_BaseTermQuery|DocumentCount=20000, Mode=Max",
          "displayInfo": "FunctionScoreQueryBenchmarks.LeanCorpus_BaseTermQuery: DefaultJob [Mode=Max, DocumentCount=20000]",
          "typeName": "FunctionScoreQueryBenchmarks",
          "methodName": "LeanCorpus_BaseTermQuery",
          "parameters": {
            "Mode": "Max",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 2620.156334177653,
            "medianNanoseconds": 2618.072162628174,
            "minNanoseconds": 2616.523693084717,
            "maxNanoseconds": 2625.997386932373,
            "standardDeviationNanoseconds": 3.6729435072808476,
            "operationsPerSecond": 381656.6160407578
          },
          "gc": {
            "bytesAllocatedPerOperation": 528,
            "gen0Collections": 33,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "FunctionScoreQueryBenchmarks.LeanCorpus_BaseTermQuery|DocumentCount=20000, Mode=Multiply",
          "displayInfo": "FunctionScoreQueryBenchmarks.LeanCorpus_BaseTermQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [Mode=Multiply, DocumentCount=20000]",
          "typeName": "FunctionScoreQueryBenchmarks",
          "methodName": "LeanCorpus_BaseTermQuery",
          "parameters": {
            "Mode": "Multiply",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 2670.4874267578125,
            "medianNanoseconds": 2670.9845848083496,
            "minNanoseconds": 2668.544189453125,
            "maxNanoseconds": 2671.933506011963,
            "standardDeviationNanoseconds": 1.7484968628549422,
            "operationsPerSecond": 374463.4743381214
          },
          "gc": {
            "bytesAllocatedPerOperation": 528,
            "gen0Collections": 33,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "FunctionScoreQueryBenchmarks.LeanCorpus_BaseTermQuery|DocumentCount=20000, Mode=Replace",
          "displayInfo": "FunctionScoreQueryBenchmarks.LeanCorpus_BaseTermQuery: DefaultJob [Mode=Replace, DocumentCount=20000]",
          "typeName": "FunctionScoreQueryBenchmarks",
          "methodName": "LeanCorpus_BaseTermQuery",
          "parameters": {
            "Mode": "Replace",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 12,
            "meanNanoseconds": 2630.0884256362915,
            "medianNanoseconds": 2630.0669651031494,
            "minNanoseconds": 2628.121711730957,
            "maxNanoseconds": 2633.616024017334,
            "standardDeviationNanoseconds": 1.3846078516331515,
            "operationsPerSecond": 380215.35331386136
          },
          "gc": {
            "bytesAllocatedPerOperation": 528,
            "gen0Collections": 33,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "FunctionScoreQueryBenchmarks.LeanCorpus_BaseTermQuery|DocumentCount=20000, Mode=Sum",
          "displayInfo": "FunctionScoreQueryBenchmarks.LeanCorpus_BaseTermQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [Mode=Sum, DocumentCount=20000]",
          "typeName": "FunctionScoreQueryBenchmarks",
          "methodName": "LeanCorpus_BaseTermQuery",
          "parameters": {
            "Mode": "Sum",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 2635.283229827881,
            "medianNanoseconds": 2632.505039215088,
            "minNanoseconds": 2632.451499938965,
            "maxNanoseconds": 2640.89315032959,
            "standardDeviationNanoseconds": 4.858407418089083,
            "operationsPerSecond": 379465.85349207924
          },
          "gc": {
            "bytesAllocatedPerOperation": 528,
            "gen0Collections": 33,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "FunctionScoreQueryBenchmarks.LeanCorpus_FunctionScoreQuery|DocumentCount=20000, Mode=Max",
          "displayInfo": "FunctionScoreQueryBenchmarks.LeanCorpus_FunctionScoreQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [Mode=Max, DocumentCount=20000]",
          "typeName": "FunctionScoreQueryBenchmarks",
          "methodName": "LeanCorpus_FunctionScoreQuery",
          "parameters": {
            "Mode": "Max",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 23087.83967081706,
            "medianNanoseconds": 23089.589935302734,
            "minNanoseconds": 23065.264556884766,
            "maxNanoseconds": 23108.664520263672,
            "standardDeviationNanoseconds": 21.752856700901788,
            "operationsPerSecond": 43312.84408839673
          },
          "gc": {
            "bytesAllocatedPerOperation": 167440,
            "gen0Collections": 1365,
            "gen1Collections": 322,
            "gen2Collections": 0
          }
        },
        {
          "key": "FunctionScoreQueryBenchmarks.LeanCorpus_FunctionScoreQuery|DocumentCount=20000, Mode=Multiply",
          "displayInfo": "FunctionScoreQueryBenchmarks.LeanCorpus_FunctionScoreQuery: DefaultJob [Mode=Multiply, DocumentCount=20000]",
          "typeName": "FunctionScoreQueryBenchmarks",
          "methodName": "LeanCorpus_FunctionScoreQuery",
          "parameters": {
            "Mode": "Multiply",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 23085.615704345702,
            "medianNanoseconds": 23051.458129882812,
            "minNanoseconds": 22382.776977539062,
            "maxNanoseconds": 23674.880310058594,
            "standardDeviationNanoseconds": 351.8650762449549,
            "operationsPerSecond": 43317.016656902815
          },
          "gc": {
            "bytesAllocatedPerOperation": 167439,
            "gen0Collections": 1365,
            "gen1Collections": 379,
            "gen2Collections": 0
          }
        },
        {
          "key": "FunctionScoreQueryBenchmarks.LeanCorpus_FunctionScoreQuery|DocumentCount=20000, Mode=Replace",
          "displayInfo": "FunctionScoreQueryBenchmarks.LeanCorpus_FunctionScoreQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [Mode=Replace, DocumentCount=20000]",
          "typeName": "FunctionScoreQueryBenchmarks",
          "methodName": "LeanCorpus_FunctionScoreQuery",
          "parameters": {
            "Mode": "Replace",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 22838.896596272785,
            "medianNanoseconds": 22732.58432006836,
            "minNanoseconds": 22723.034698486328,
            "maxNanoseconds": 23061.070770263672,
            "standardDeviationNanoseconds": 192.46771549922124,
            "operationsPerSecond": 43784.95238527399
          },
          "gc": {
            "bytesAllocatedPerOperation": 167441,
            "gen0Collections": 1365,
            "gen1Collections": 357,
            "gen2Collections": 0
          }
        },
        {
          "key": "FunctionScoreQueryBenchmarks.LeanCorpus_FunctionScoreQuery|DocumentCount=20000, Mode=Sum",
          "displayInfo": "FunctionScoreQueryBenchmarks.LeanCorpus_FunctionScoreQuery: DefaultJob [Mode=Sum, DocumentCount=20000]",
          "typeName": "FunctionScoreQueryBenchmarks",
          "methodName": "LeanCorpus_FunctionScoreQuery",
          "parameters": {
            "Mode": "Sum",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 23466.97295328776,
            "medianNanoseconds": 23504.335998535156,
            "minNanoseconds": 23011.403350830078,
            "maxNanoseconds": 24054.67007446289,
            "standardDeviationNanoseconds": 304.3041303406566,
            "operationsPerSecond": 42613.08017828087
          },
          "gc": {
            "bytesAllocatedPerOperation": 167438,
            "gen0Collections": 1365,
            "gen1Collections": 454,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "fuzzy",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.FuzzyQueryBenchmarks-20260529-103927",
      "benchmarkCount": 10,
      "benchmarks": [
        {
          "key": "FuzzyQueryBenchmarks.LeanCorpus_FuzzyQuery|DocumentCount=20000, Scenario=long-edit1-common",
          "displayInfo": "FuzzyQueryBenchmarks.LeanCorpus_FuzzyQuery: DefaultJob [Scenario=long-edit1-common, DocumentCount=20000]",
          "typeName": "FuzzyQueryBenchmarks",
          "methodName": "LeanCorpus_FuzzyQuery",
          "parameters": {
            "Scenario": "long-edit1-common",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 13,
            "meanNanoseconds": 2407.7430783785308,
            "medianNanoseconds": 2406.02201461792,
            "minNanoseconds": 2394.425189971924,
            "maxNanoseconds": 2430.928798675537,
            "standardDeviationNanoseconds": 9.053770223900454,
            "operationsPerSecond": 415326.70532000426
          },
          "gc": {
            "bytesAllocatedPerOperation": 3102,
            "gen0Collections": 195,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "FuzzyQueryBenchmarks.LeanCorpus_FuzzyQuery|DocumentCount=20000, Scenario=medium-edit1-common",
          "displayInfo": "FuzzyQueryBenchmarks.LeanCorpus_FuzzyQuery: DefaultJob [Scenario=medium-edit1-common, DocumentCount=20000]",
          "typeName": "FuzzyQueryBenchmarks",
          "methodName": "LeanCorpus_FuzzyQuery",
          "parameters": {
            "Scenario": "medium-edit1-common",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 13,
            "meanNanoseconds": 5704.544439462515,
            "medianNanoseconds": 5708.768157958984,
            "minNanoseconds": 5667.200347900391,
            "maxNanoseconds": 5743.9108963012695,
            "standardDeviationNanoseconds": 20.09566053180537,
            "operationsPerSecond": 175298.8359740468
          },
          "gc": {
            "bytesAllocatedPerOperation": 3541,
            "gen0Collections": 112,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "FuzzyQueryBenchmarks.LeanCorpus_FuzzyQuery|DocumentCount=20000, Scenario=medium-edit2-common",
          "displayInfo": "FuzzyQueryBenchmarks.LeanCorpus_FuzzyQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [Scenario=medium-edit2-common, DocumentCount=20000]",
          "typeName": "FuzzyQueryBenchmarks",
          "methodName": "LeanCorpus_FuzzyQuery",
          "parameters": {
            "Scenario": "medium-edit2-common",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 14991.207021077475,
            "medianNanoseconds": 14967.511276245117,
            "minNanoseconds": 14953.507873535156,
            "maxNanoseconds": 15052.601913452148,
            "standardDeviationNanoseconds": 53.62856915057246,
            "operationsPerSecond": 66705.76949501203
          },
          "gc": {
            "bytesAllocatedPerOperation": 4008,
            "gen0Collections": 63,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "FuzzyQueryBenchmarks.LeanCorpus_FuzzyQuery|DocumentCount=20000, Scenario=nohit-edit2",
          "displayInfo": "FuzzyQueryBenchmarks.LeanCorpus_FuzzyQuery: DefaultJob [Scenario=nohit-edit2, DocumentCount=20000]",
          "typeName": "FuzzyQueryBenchmarks",
          "methodName": "LeanCorpus_FuzzyQuery",
          "parameters": {
            "Scenario": "nohit-edit2",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 2079.599787902832,
            "medianNanoseconds": 2081.2642822265625,
            "minNanoseconds": 2060.1625747680664,
            "maxNanoseconds": 2090.4793548583984,
            "standardDeviationNanoseconds": 8.440707237282814,
            "operationsPerSecond": 480861.75321668404
          },
          "gc": {
            "bytesAllocatedPerOperation": 2952,
            "gen0Collections": 186,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "FuzzyQueryBenchmarks.LeanCorpus_FuzzyQuery|DocumentCount=20000, Scenario=short-edit1-common",
          "displayInfo": "FuzzyQueryBenchmarks.LeanCorpus_FuzzyQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [Scenario=short-edit1-common, DocumentCount=20000]",
          "typeName": "FuzzyQueryBenchmarks",
          "methodName": "LeanCorpus_FuzzyQuery",
          "parameters": {
            "Scenario": "short-edit1-common",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 31742.303466796875,
            "medianNanoseconds": 31677.490966796875,
            "minNanoseconds": 31557.752563476562,
            "maxNanoseconds": 31991.666870117188,
            "standardDeviationNanoseconds": 224.10020413654487,
            "operationsPerSecond": 31503.699819580557
          },
          "gc": {
            "bytesAllocatedPerOperation": 4235,
            "gen0Collections": 16,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "FuzzyQueryBenchmarks.LuceneNet_FuzzyQuery|DocumentCount=20000, Scenario=long-edit1-common",
          "displayInfo": "FuzzyQueryBenchmarks.LuceneNet_FuzzyQuery: DefaultJob [Scenario=long-edit1-common, DocumentCount=20000]",
          "typeName": "FuzzyQueryBenchmarks",
          "methodName": "LuceneNet_FuzzyQuery",
          "parameters": {
            "Scenario": "long-edit1-common",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 286407.7923060826,
            "medianNanoseconds": 286381.7097167969,
            "minNanoseconds": 286023.4462890625,
            "maxNanoseconds": 287104.4091796875,
            "standardDeviationNanoseconds": 323.7190241062003,
            "operationsPerSecond": 3491.525115110363
          },
          "gc": {
            "bytesAllocatedPerOperation": 222806,
            "gen0Collections": 109,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "FuzzyQueryBenchmarks.LuceneNet_FuzzyQuery|DocumentCount=20000, Scenario=medium-edit1-common",
          "displayInfo": "FuzzyQueryBenchmarks.LuceneNet_FuzzyQuery: DefaultJob [Scenario=medium-edit1-common, DocumentCount=20000]",
          "typeName": "FuzzyQueryBenchmarks",
          "methodName": "LuceneNet_FuzzyQuery",
          "parameters": {
            "Scenario": "medium-edit1-common",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 12,
            "meanNanoseconds": 355150.7356770833,
            "medianNanoseconds": 355125.3786621094,
            "minNanoseconds": 355010.49755859375,
            "maxNanoseconds": 355313.916015625,
            "standardDeviationNanoseconds": 104.9902159787952,
            "operationsPerSecond": 2815.705838518207
          },
          "gc": {
            "bytesAllocatedPerOperation": 275319,
            "gen0Collections": 134,
            "gen1Collections": 13,
            "gen2Collections": 0
          }
        },
        {
          "key": "FuzzyQueryBenchmarks.LuceneNet_FuzzyQuery|DocumentCount=20000, Scenario=medium-edit2-common",
          "displayInfo": "FuzzyQueryBenchmarks.LuceneNet_FuzzyQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [Scenario=medium-edit2-common, DocumentCount=20000]",
          "typeName": "FuzzyQueryBenchmarks",
          "methodName": "LuceneNet_FuzzyQuery",
          "parameters": {
            "Scenario": "medium-edit2-common",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 2292845.7552083335,
            "medianNanoseconds": 2296476.1328125,
            "minNanoseconds": 2267797.42578125,
            "maxNanoseconds": 2314263.70703125,
            "standardDeviationNanoseconds": 23444.904658851818,
            "operationsPerSecond": 436.13923776967613
          },
          "gc": {
            "bytesAllocatedPerOperation": 1420679,
            "gen0Collections": 78,
            "gen1Collections": 26,
            "gen2Collections": 0
          }
        },
        {
          "key": "FuzzyQueryBenchmarks.LuceneNet_FuzzyQuery|DocumentCount=20000, Scenario=nohit-edit2",
          "displayInfo": "FuzzyQueryBenchmarks.LuceneNet_FuzzyQuery: DefaultJob [Scenario=nohit-edit2, DocumentCount=20000]",
          "typeName": "FuzzyQueryBenchmarks",
          "methodName": "LuceneNet_FuzzyQuery",
          "parameters": {
            "Scenario": "nohit-edit2",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 2205983.78046875,
            "medianNanoseconds": 2205981.44921875,
            "minNanoseconds": 2197645.8046875,
            "maxNanoseconds": 2211080.08984375,
            "standardDeviationNanoseconds": 3575.7025599594413,
            "operationsPerSecond": 453.31248980783977
          },
          "gc": {
            "bytesAllocatedPerOperation": 1844660,
            "gen0Collections": 95,
            "gen1Collections": 34,
            "gen2Collections": 0
          }
        },
        {
          "key": "FuzzyQueryBenchmarks.LuceneNet_FuzzyQuery|DocumentCount=20000, Scenario=short-edit1-common",
          "displayInfo": "FuzzyQueryBenchmarks.LuceneNet_FuzzyQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [Scenario=short-edit1-common, DocumentCount=20000]",
          "typeName": "FuzzyQueryBenchmarks",
          "methodName": "LuceneNet_FuzzyQuery",
          "parameters": {
            "Scenario": "short-edit1-common",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 445414.8776041667,
            "medianNanoseconds": 446077.1484375,
            "minNanoseconds": 444011.01123046875,
            "maxNanoseconds": 446156.47314453125,
            "standardDeviationNanoseconds": 1216.430720874067,
            "operationsPerSecond": 2245.097885770858
          },
          "gc": {
            "bytesAllocatedPerOperation": 345502,
            "gen0Collections": 168,
            "gen1Collections": 2,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "geo",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.GeoQueryBenchmarks-20260529-123922",
      "benchmarkCount": 4,
      "benchmarks": [
        {
          "key": "GeoQueryBenchmarks.LeanCorpus_GeoBoundingBoxQuery|DocumentCount=20000, GeoQueryType=BoundingBox",
          "displayInfo": "GeoQueryBenchmarks.LeanCorpus_GeoBoundingBoxQuery: DefaultJob [GeoQueryType=BoundingBox, DocumentCount=20000]",
          "typeName": "GeoQueryBenchmarks",
          "methodName": "LeanCorpus_GeoBoundingBoxQuery",
          "parameters": {
            "GeoQueryType": "BoundingBox",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 18976.66246948242,
            "medianNanoseconds": 18995.407775878906,
            "minNanoseconds": 18893.013305664062,
            "maxNanoseconds": 19059.61669921875,
            "standardDeviationNanoseconds": 51.775268579531456,
            "operationsPerSecond": 52696.30534917105
          },
          "gc": {
            "bytesAllocatedPerOperation": 41992,
            "gen0Collections": 341,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GeoQueryBenchmarks.LeanCorpus_GeoBoundingBoxQuery|DocumentCount=20000, GeoQueryType=Distance",
          "displayInfo": "GeoQueryBenchmarks.LeanCorpus_GeoBoundingBoxQuery: DefaultJob [GeoQueryType=Distance, DocumentCount=20000]",
          "typeName": "GeoQueryBenchmarks",
          "methodName": "LeanCorpus_GeoBoundingBoxQuery",
          "parameters": {
            "GeoQueryType": "Distance",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 19040.792390950523,
            "medianNanoseconds": 19047.139373779297,
            "minNanoseconds": 18948.991119384766,
            "maxNanoseconds": 19145.262084960938,
            "standardDeviationNanoseconds": 63.83489318865423,
            "operationsPerSecond": 52518.82271849505
          },
          "gc": {
            "bytesAllocatedPerOperation": 41992,
            "gen0Collections": 341,
            "gen1Collections": 1,
            "gen2Collections": 0
          }
        },
        {
          "key": "GeoQueryBenchmarks.LeanCorpus_GeoDistanceQuery|DocumentCount=20000, GeoQueryType=BoundingBox",
          "displayInfo": "GeoQueryBenchmarks.LeanCorpus_GeoDistanceQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [GeoQueryType=BoundingBox, DocumentCount=20000]",
          "typeName": "GeoQueryBenchmarks",
          "methodName": "LeanCorpus_GeoDistanceQuery",
          "parameters": {
            "GeoQueryType": "BoundingBox",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 10066.130228678385,
            "medianNanoseconds": 10055.09164428711,
            "minNanoseconds": 10039.227020263672,
            "maxNanoseconds": 10104.072021484375,
            "standardDeviationNanoseconds": 33.80246004140438,
            "operationsPerSecond": 99343.04219023533
          },
          "gc": {
            "bytesAllocatedPerOperation": 14472,
            "gen0Collections": 233,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GeoQueryBenchmarks.LeanCorpus_GeoDistanceQuery|DocumentCount=20000, GeoQueryType=Distance",
          "displayInfo": "GeoQueryBenchmarks.LeanCorpus_GeoDistanceQuery: DefaultJob [GeoQueryType=Distance, DocumentCount=20000]",
          "typeName": "GeoQueryBenchmarks",
          "methodName": "LeanCorpus_GeoDistanceQuery",
          "parameters": {
            "GeoQueryType": "Distance",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 10005.8650197347,
            "medianNanoseconds": 10002.329010009766,
            "minNanoseconds": 9950.461471557617,
            "maxNanoseconds": 10044.855072021484,
            "standardDeviationNanoseconds": 24.628487222115645,
            "operationsPerSecond": 99941.38418094655
          },
          "gc": {
            "bytesAllocatedPerOperation": 14472,
            "gen0Collections": 233,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "gutenberg-analysis",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.GutenbergAnalysisBenchmarks-20260529-111731",
      "benchmarkCount": 2,
      "benchmarks": [
        {
          "key": "GutenbergAnalysisBenchmarks.LeanCorpus_English_Analyse",
          "displayInfo": "GutenbergAnalysisBenchmarks.LeanCorpus_English_Analyse: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)",
          "typeName": "GutenbergAnalysisBenchmarks",
          "methodName": "LeanCorpus_English_Analyse",
          "parameters": {},
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 449171156.3333333,
            "medianNanoseconds": 427049397,
            "minNanoseconds": 418439134,
            "maxNanoseconds": 502024938,
            "standardDeviationNanoseconds": 45974730.39162975,
            "operationsPerSecond": 2.226322830172765
          },
          "gc": {
            "bytesAllocatedPerOperation": 208671528,
            "gen0Collections": 11,
            "gen1Collections": 7,
            "gen2Collections": 3
          }
        },
        {
          "key": "GutenbergAnalysisBenchmarks.LeanCorpus_Standard_Analyse",
          "displayInfo": "GutenbergAnalysisBenchmarks.LeanCorpus_Standard_Analyse: DefaultJob",
          "typeName": "GutenbergAnalysisBenchmarks",
          "methodName": "LeanCorpus_Standard_Analyse",
          "parameters": {},
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 126874816.35,
            "medianNanoseconds": 127057737.75,
            "minNanoseconds": 126248259.25,
            "maxNanoseconds": 127424119.25,
            "standardDeviationNanoseconds": 415103.66894993186,
            "operationsPerSecond": 7.881784807801222
          },
          "gc": {
            "bytesAllocatedPerOperation": 7619280,
            "gen0Collections": 5,
            "gen1Collections": 3,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "gutenberg-index",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.GutenbergIndexingBenchmarks-20260529-112033",
      "benchmarkCount": 3,
      "benchmarks": [
        {
          "key": "GutenbergIndexingBenchmarks.LeanCorpus_English_Index",
          "displayInfo": "GutenbergIndexingBenchmarks.LeanCorpus_English_Index: DefaultJob",
          "typeName": "GutenbergIndexingBenchmarks",
          "methodName": "LeanCorpus_English_Index",
          "parameters": {},
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 967497338.6666666,
            "medianNanoseconds": 967865591,
            "minNanoseconds": 960302927,
            "maxNanoseconds": 977054094,
            "standardDeviationNanoseconds": 5505497.504194029,
            "operationsPerSecond": 1.0335945744079524
          },
          "gc": {
            "bytesAllocatedPerOperation": 310744264,
            "gen0Collections": 47,
            "gen1Collections": 14,
            "gen2Collections": 1
          }
        },
        {
          "key": "GutenbergIndexingBenchmarks.LeanCorpus_Standard_Index",
          "displayInfo": "GutenbergIndexingBenchmarks.LeanCorpus_Standard_Index: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)",
          "typeName": "GutenbergIndexingBenchmarks",
          "methodName": "LeanCorpus_Standard_Index",
          "parameters": {},
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 736625154.6666666,
            "medianNanoseconds": 731878789,
            "minNanoseconds": 730107696,
            "maxNanoseconds": 747888979,
            "standardDeviationNanoseconds": 9794870.931401106,
            "operationsPerSecond": 1.3575425624074897
          },
          "gc": {
            "bytesAllocatedPerOperation": 117380952,
            "gen0Collections": 16,
            "gen1Collections": 8,
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
            "meanNanoseconds": 612546842.1428572,
            "medianNanoseconds": 611921645.5,
            "minNanoseconds": 605855163,
            "maxNanoseconds": 621545972,
            "standardDeviationNanoseconds": 4179611.125888074,
            "operationsPerSecond": 1.6325282104168968
          },
          "gc": {
            "bytesAllocatedPerOperation": 218236288,
            "gen0Collections": 42,
            "gen1Collections": 3,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "gutenberg-search",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.GutenbergSearchBenchmarks-20260529-112412",
      "benchmarkCount": 15,
      "benchmarks": [
        {
          "key": "GutenbergSearchBenchmarks.LeanCorpus_English_Search|SearchTerm=death",
          "displayInfo": "GutenbergSearchBenchmarks.LeanCorpus_English_Search: DefaultJob [SearchTerm=death]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LeanCorpus_English_Search",
          "parameters": {
            "SearchTerm": "death"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 13088.694285801479,
            "medianNanoseconds": 13089.593544006348,
            "minNanoseconds": 13042.222640991211,
            "maxNanoseconds": 13140.455993652344,
            "standardDeviationNanoseconds": 27.436117960987403,
            "operationsPerSecond": 76401.81504466743
          },
          "gc": {
            "bytesAllocatedPerOperation": 520,
            "gen0Collections": 8,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LeanCorpus_English_Search|SearchTerm=love",
          "displayInfo": "GutenbergSearchBenchmarks.LeanCorpus_English_Search: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [SearchTerm=love]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LeanCorpus_English_Search",
          "parameters": {
            "SearchTerm": "love"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 23106.254364013672,
            "medianNanoseconds": 23089.013793945312,
            "minNanoseconds": 23088.37921142578,
            "maxNanoseconds": 23141.370086669922,
            "standardDeviationNanoseconds": 30.41276306086847,
            "operationsPerSecond": 43278.32561029139
          },
          "gc": {
            "bytesAllocatedPerOperation": 512,
            "gen0Collections": 4,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LeanCorpus_English_Search|SearchTerm=man",
          "displayInfo": "GutenbergSearchBenchmarks.LeanCorpus_English_Search: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [SearchTerm=man]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LeanCorpus_English_Search",
          "parameters": {
            "SearchTerm": "man"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 45504.8046468099,
            "medianNanoseconds": 45498.34686279297,
            "minNanoseconds": 45456.56018066406,
            "maxNanoseconds": 45559.506896972656,
            "standardDeviationNanoseconds": 51.776286372117106,
            "operationsPerSecond": 21975.701417940374
          },
          "gc": {
            "bytesAllocatedPerOperation": 512,
            "gen0Collections": 2,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LeanCorpus_English_Search|SearchTerm=night",
          "displayInfo": "GutenbergSearchBenchmarks.LeanCorpus_English_Search: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [SearchTerm=night]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LeanCorpus_English_Search",
          "parameters": {
            "SearchTerm": "night"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 29901.63542683919,
            "medianNanoseconds": 29895.696014404297,
            "minNanoseconds": 29881.655487060547,
            "maxNanoseconds": 29927.554779052734,
            "standardDeviationNanoseconds": 23.519007555589337,
            "operationsPerSecond": 33442.986837516495
          },
          "gc": {
            "bytesAllocatedPerOperation": 520,
            "gen0Collections": 4,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LeanCorpus_English_Search|SearchTerm=sea",
          "displayInfo": "GutenbergSearchBenchmarks.LeanCorpus_English_Search: DefaultJob [SearchTerm=sea]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LeanCorpus_English_Search",
          "parameters": {
            "SearchTerm": "sea"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 15929.240973336357,
            "medianNanoseconds": 15928.297576904297,
            "minNanoseconds": 15899.553253173828,
            "maxNanoseconds": 15950.546936035156,
            "standardDeviationNanoseconds": 13.301630441119983,
            "operationsPerSecond": 62777.63025079979
          },
          "gc": {
            "bytesAllocatedPerOperation": 512,
            "gen0Collections": 4,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LeanCorpus_Standard_Search|SearchTerm=death",
          "displayInfo": "GutenbergSearchBenchmarks.LeanCorpus_Standard_Search: DefaultJob [SearchTerm=death]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LeanCorpus_Standard_Search",
          "parameters": {
            "SearchTerm": "death"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 13077.747075398764,
            "medianNanoseconds": 13067.15104675293,
            "minNanoseconds": 13040.339828491211,
            "maxNanoseconds": 13130.367401123047,
            "standardDeviationNanoseconds": 28.767692237502562,
            "operationsPerSecond": 76465.7700011
          },
          "gc": {
            "bytesAllocatedPerOperation": 520,
            "gen0Collections": 8,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LeanCorpus_Standard_Search|SearchTerm=love",
          "displayInfo": "GutenbergSearchBenchmarks.LeanCorpus_Standard_Search: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [SearchTerm=love]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LeanCorpus_Standard_Search",
          "parameters": {
            "SearchTerm": "love"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 17366.721771240234,
            "medianNanoseconds": 17347.88946533203,
            "minNanoseconds": 17335.400756835938,
            "maxNanoseconds": 17416.875091552734,
            "standardDeviationNanoseconds": 43.880617745780086,
            "operationsPerSecond": 57581.39119013395
          },
          "gc": {
            "bytesAllocatedPerOperation": 512,
            "gen0Collections": 4,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LeanCorpus_Standard_Search|SearchTerm=man",
          "displayInfo": "GutenbergSearchBenchmarks.LeanCorpus_Standard_Search: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [SearchTerm=man]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LeanCorpus_Standard_Search",
          "parameters": {
            "SearchTerm": "man"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 45917.015604654945,
            "medianNanoseconds": 45912.15460205078,
            "minNanoseconds": 45869.31311035156,
            "maxNanoseconds": 45969.5791015625,
            "standardDeviationNanoseconds": 50.309435080472085,
            "operationsPerSecond": 21778.418889633205
          },
          "gc": {
            "bytesAllocatedPerOperation": 512,
            "gen0Collections": 2,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LeanCorpus_Standard_Search|SearchTerm=night",
          "displayInfo": "GutenbergSearchBenchmarks.LeanCorpus_Standard_Search: DefaultJob [SearchTerm=night]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LeanCorpus_Standard_Search",
          "parameters": {
            "SearchTerm": "night"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 28930.303846086776,
            "medianNanoseconds": 28928.595153808594,
            "minNanoseconds": 28897.997344970703,
            "maxNanoseconds": 28967.56134033203,
            "standardDeviationNanoseconds": 22.76133368416567,
            "operationsPerSecond": 34565.83122390067
          },
          "gc": {
            "bytesAllocatedPerOperation": 520,
            "gen0Collections": 4,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LeanCorpus_Standard_Search|SearchTerm=sea",
          "displayInfo": "GutenbergSearchBenchmarks.LeanCorpus_Standard_Search: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [SearchTerm=sea]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LeanCorpus_Standard_Search",
          "parameters": {
            "SearchTerm": "sea"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 14458.484736124674,
            "medianNanoseconds": 14463.134994506836,
            "minNanoseconds": 14445.524215698242,
            "maxNanoseconds": 14466.794998168945,
            "standardDeviationNanoseconds": 11.372344700836322,
            "operationsPerSecond": 69163.54087240482
          },
          "gc": {
            "bytesAllocatedPerOperation": 512,
            "gen0Collections": 8,
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
            "meanNanoseconds": 22427.591910807292,
            "medianNanoseconds": 22630.81640625,
            "minNanoseconds": 21759.430419921875,
            "maxNanoseconds": 22794.914337158203,
            "standardDeviationNanoseconds": 397.82193156498494,
            "operationsPerSecond": 44587.934539602764
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
            "meanNanoseconds": 28685.11327718099,
            "medianNanoseconds": 28684.293090820312,
            "minNanoseconds": 28644.677154541016,
            "maxNanoseconds": 28749.295837402344,
            "standardDeviationNanoseconds": 32.453848052260504,
            "operationsPerSecond": 34861.288164948615
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
          "displayInfo": "GutenbergSearchBenchmarks.LuceneNet_Search: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [SearchTerm=man]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LuceneNet_Search",
          "parameters": {
            "SearchTerm": "man"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 49561.908528645836,
            "medianNanoseconds": 49564.31268310547,
            "minNanoseconds": 49549.482849121094,
            "maxNanoseconds": 49571.93005371094,
            "standardDeviationNanoseconds": 11.415087274038436,
            "operationsPerSecond": 20176.785553405778
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
            "meanNanoseconds": 36204.4063680013,
            "medianNanoseconds": 36194.00994873047,
            "minNanoseconds": 36151.97882080078,
            "maxNanoseconds": 36273.083557128906,
            "standardDeviationNanoseconds": 40.2638577562227,
            "operationsPerSecond": 27620.947291206918
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
          "displayInfo": "GutenbergSearchBenchmarks.LuceneNet_Search: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [SearchTerm=sea]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LuceneNet_Search",
          "parameters": {
            "SearchTerm": "sea"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 25898.465250651043,
            "medianNanoseconds": 25902.279663085938,
            "minNanoseconds": 25890.08056640625,
            "maxNanoseconds": 25903.035522460938,
            "standardDeviationNanoseconds": 7.271177914364155,
            "operationsPerSecond": 38612.32665031615
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
      "suiteName": "highlighter",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.HighlighterBenchmarks-20260529-120419",
      "benchmarkCount": 6,
      "benchmarks": [
        {
          "key": "HighlighterBenchmarks.LeanCorpus_Highlight_FiveTerms|DocumentCount=20000, MaxSnippetLength=100",
          "displayInfo": "HighlighterBenchmarks.LeanCorpus_Highlight_FiveTerms: DefaultJob [MaxSnippetLength=100, DocumentCount=20000]",
          "typeName": "HighlighterBenchmarks",
          "methodName": "LeanCorpus_Highlight_FiveTerms",
          "parameters": {
            "MaxSnippetLength": "100",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 210388536.6190476,
            "medianNanoseconds": 210315818.3333333,
            "minNanoseconds": 210106218.33333334,
            "maxNanoseconds": 211035539.33333334,
            "standardDeviationNanoseconds": 267037.6541830533,
            "operationsPerSecond": 4.7531106783194605
          },
          "gc": {
            "bytesAllocatedPerOperation": 26502144,
            "gen0Collections": 19,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "HighlighterBenchmarks.LeanCorpus_Highlight_FiveTerms|DocumentCount=20000, MaxSnippetLength=200",
          "displayInfo": "HighlighterBenchmarks.LeanCorpus_Highlight_FiveTerms: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [MaxSnippetLength=200, DocumentCount=20000]",
          "typeName": "HighlighterBenchmarks",
          "methodName": "LeanCorpus_Highlight_FiveTerms",
          "parameters": {
            "MaxSnippetLength": "200",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 217278936.11111113,
            "medianNanoseconds": 217276271.66666666,
            "minNanoseconds": 217096553.33333334,
            "maxNanoseconds": 217463983.33333334,
            "standardDeviationNanoseconds": 183729.49048301473,
            "operationsPerSecond": 4.6023789415492375
          },
          "gc": {
            "bytesAllocatedPerOperation": 31901056,
            "gen0Collections": 22,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "HighlighterBenchmarks.LeanCorpus_Highlight_FiveTerms|DocumentCount=20000, MaxSnippetLength=500",
          "displayInfo": "HighlighterBenchmarks.LeanCorpus_Highlight_FiveTerms: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [MaxSnippetLength=500, DocumentCount=20000]",
          "typeName": "HighlighterBenchmarks",
          "methodName": "LeanCorpus_Highlight_FiveTerms",
          "parameters": {
            "MaxSnippetLength": "500",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 229028024.33333334,
            "medianNanoseconds": 229026920,
            "minNanoseconds": 228507946.33333334,
            "maxNanoseconds": 229549206.66666666,
            "standardDeviationNanoseconds": 520631.0450860898,
            "operationsPerSecond": 4.366277895077914
          },
          "gc": {
            "bytesAllocatedPerOperation": 41449912,
            "gen0Collections": 29,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "HighlighterBenchmarks.LeanCorpus_Highlight_TwoTerms|DocumentCount=20000, MaxSnippetLength=100",
          "displayInfo": "HighlighterBenchmarks.LeanCorpus_Highlight_TwoTerms: DefaultJob [MaxSnippetLength=100, DocumentCount=20000]",
          "typeName": "HighlighterBenchmarks",
          "methodName": "LeanCorpus_Highlight_TwoTerms",
          "parameters": {
            "MaxSnippetLength": "100",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 205248144.24444446,
            "medianNanoseconds": 205164908,
            "minNanoseconds": 204848247,
            "maxNanoseconds": 205859744,
            "standardDeviationNanoseconds": 311325.25833076966,
            "operationsPerSecond": 4.87215123762108
          },
          "gc": {
            "bytesAllocatedPerOperation": 25442344,
            "gen0Collections": 18,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "HighlighterBenchmarks.LeanCorpus_Highlight_TwoTerms|DocumentCount=20000, MaxSnippetLength=200",
          "displayInfo": "HighlighterBenchmarks.LeanCorpus_Highlight_TwoTerms: DefaultJob [MaxSnippetLength=200, DocumentCount=20000]",
          "typeName": "HighlighterBenchmarks",
          "methodName": "LeanCorpus_Highlight_TwoTerms",
          "parameters": {
            "MaxSnippetLength": "200",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 213903026.57777774,
            "medianNanoseconds": 213715238.66666666,
            "minNanoseconds": 213422518,
            "maxNanoseconds": 214938820.66666666,
            "standardDeviationNanoseconds": 500880.969235162,
            "operationsPerSecond": 4.675015664803545
          },
          "gc": {
            "bytesAllocatedPerOperation": 30991760,
            "gen0Collections": 21,
            "gen1Collections": 1,
            "gen2Collections": 0
          }
        },
        {
          "key": "HighlighterBenchmarks.LeanCorpus_Highlight_TwoTerms|DocumentCount=20000, MaxSnippetLength=500",
          "displayInfo": "HighlighterBenchmarks.LeanCorpus_Highlight_TwoTerms: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [MaxSnippetLength=500, DocumentCount=20000]",
          "typeName": "HighlighterBenchmarks",
          "methodName": "LeanCorpus_Highlight_TwoTerms",
          "parameters": {
            "MaxSnippetLength": "500",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 219969146.7777778,
            "medianNanoseconds": 219841569.33333334,
            "minNanoseconds": 219781915,
            "maxNanoseconds": 220283956,
            "standardDeviationNanoseconds": 274259.5388832998,
            "operationsPerSecond": 4.546092098135211
          },
          "gc": {
            "bytesAllocatedPerOperation": 40881120,
            "gen0Collections": 29,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "hunspell",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.HunspellBenchmarks-20260529-125645",
      "benchmarkCount": 2,
      "benchmarks": [
        {
          "key": "HunspellBenchmarks.Parse_Dictionary",
          "displayInfo": "HunspellBenchmarks.Parse_Dictionary: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3)",
          "typeName": "HunspellBenchmarks",
          "methodName": "Parse_Dictionary",
          "parameters": {},
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 294.96844577789307,
            "medianNanoseconds": 294.7187991142273,
            "minNanoseconds": 294.6052966117859,
            "maxNanoseconds": 295.581241607666,
            "standardDeviationNanoseconds": 0.5337225414533785,
            "operationsPerSecond": 3390193.1352785625
          },
          "gc": {
            "bytesAllocatedPerOperation": 176,
            "gen0Collections": 88,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "HunspellBenchmarks.Stem_Words",
          "displayInfo": "HunspellBenchmarks.Stem_Words: DefaultJob",
          "typeName": "HunspellBenchmarks",
          "methodName": "Stem_Words",
          "parameters": {},
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 102.45626644293468,
            "medianNanoseconds": 102.40557599067688,
            "minNanoseconds": 102.36411774158478,
            "maxNanoseconds": 102.64367198944092,
            "standardDeviationNanoseconds": 0.09257830504160613,
            "operationsPerSecond": 9760261.960716404
          },
          "gc": {
            "bytesAllocatedPerOperation": 0,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "index",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.IndexingBenchmarks-20260529-100613",
      "benchmarkCount": 2,
      "benchmarks": [
        {
          "key": "IndexingBenchmarks.LeanCorpus_IndexDocuments|DocumentCount=20000",
          "displayInfo": "IndexingBenchmarks.LeanCorpus_IndexDocuments: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DocumentCount=20000]",
          "typeName": "IndexingBenchmarks",
          "methodName": "LeanCorpus_IndexDocuments",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 1006426995,
            "medianNanoseconds": 997310512,
            "minNanoseconds": 985609686,
            "maxNanoseconds": 1036360787,
            "standardDeviationNanoseconds": 26575388.236016743,
            "operationsPerSecond": 0.9936140474848849
          },
          "gc": {
            "bytesAllocatedPerOperation": 140186656,
            "gen0Collections": 18,
            "gen1Collections": 9,
            "gen2Collections": 0
          }
        },
        {
          "key": "IndexingBenchmarks.LuceneNet_IndexDocuments|DocumentCount=20000",
          "displayInfo": "IndexingBenchmarks.LuceneNet_IndexDocuments: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DocumentCount=20000]",
          "typeName": "IndexingBenchmarks",
          "methodName": "LuceneNet_IndexDocuments",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 797376756.6666666,
            "medianNanoseconds": 797918508,
            "minNanoseconds": 795907352,
            "maxNanoseconds": 798304410,
            "standardDeviationNanoseconds": 1287086.8829326688,
            "operationsPerSecond": 1.254112302169898
          },
          "gc": {
            "bytesAllocatedPerOperation": 250949624,
            "gen0Collections": 46,
            "gen1Collections": 4,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "indexsort-index",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.IndexSortIndexBenchmarks-20260529-110721",
      "benchmarkCount": 2,
      "benchmarks": [
        {
          "key": "IndexSortIndexBenchmarks.LeanCorpus_Index_Sorted|DocumentCount=20000",
          "displayInfo": "IndexSortIndexBenchmarks.LeanCorpus_Index_Sorted: DefaultJob [DocumentCount=20000]",
          "typeName": "IndexSortIndexBenchmarks",
          "methodName": "LeanCorpus_Index_Sorted",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 1179536632.9285715,
            "medianNanoseconds": 1179840969.5,
            "minNanoseconds": 1171249384,
            "maxNanoseconds": 1188029364,
            "standardDeviationNanoseconds": 5155031.8048846135,
            "operationsPerSecond": 0.8477905408644959
          },
          "gc": {
            "bytesAllocatedPerOperation": 162099656,
            "gen0Collections": 21,
            "gen1Collections": 11,
            "gen2Collections": 1
          }
        },
        {
          "key": "IndexSortIndexBenchmarks.LeanCorpus_Index_Unsorted|DocumentCount=20000",
          "displayInfo": "IndexSortIndexBenchmarks.LeanCorpus_Index_Unsorted: DefaultJob [DocumentCount=20000]",
          "typeName": "IndexSortIndexBenchmarks",
          "methodName": "LeanCorpus_Index_Unsorted",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 1055601303.6,
            "medianNanoseconds": 1055963483,
            "minNanoseconds": 1044285244,
            "maxNanoseconds": 1068772098,
            "standardDeviationNanoseconds": 7058229.029987265,
            "operationsPerSecond": 0.9473273636453664
          },
          "gc": {
            "bytesAllocatedPerOperation": 157172136,
            "gen0Collections": 21,
            "gen1Collections": 11,
            "gen2Collections": 1
          }
        }
      ]
    },
    {
      "suiteName": "indexsort-search",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.IndexSortSearchBenchmarks-20260529-111038",
      "benchmarkCount": 2,
      "benchmarks": [
        {
          "key": "IndexSortSearchBenchmarks.LeanCorpus_SortedSearch_EarlyTermination|DocumentCount=20000",
          "displayInfo": "IndexSortSearchBenchmarks.LeanCorpus_SortedSearch_EarlyTermination: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DocumentCount=20000]",
          "typeName": "IndexSortSearchBenchmarks",
          "methodName": "LeanCorpus_SortedSearch_EarlyTermination",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 6299.103950500488,
            "medianNanoseconds": 6299.971450805664,
            "minNanoseconds": 6294.413703918457,
            "maxNanoseconds": 6302.926696777344,
            "standardDeviationNanoseconds": 4.322288680603613,
            "operationsPerSecond": 158752.73814469218
          },
          "gc": {
            "bytesAllocatedPerOperation": 5176,
            "gen0Collections": 162,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "IndexSortSearchBenchmarks.LeanCorpus_SortedSearch_PostSort|DocumentCount=20000",
          "displayInfo": "IndexSortSearchBenchmarks.LeanCorpus_SortedSearch_PostSort: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DocumentCount=20000]",
          "typeName": "IndexSortSearchBenchmarks",
          "methodName": "LeanCorpus_SortedSearch_PostSort",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 6310.276634216309,
            "medianNanoseconds": 6307.137306213379,
            "minNanoseconds": 6306.956886291504,
            "maxNanoseconds": 6316.735710144043,
            "standardDeviationNanoseconds": 5.594451198931902,
            "operationsPerSecond": 158471.6578949463
          },
          "gc": {
            "bytesAllocatedPerOperation": 5176,
            "gen0Collections": 162,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "kstemmer",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.KStemmerParityBenchmarks-20260529-125207",
      "benchmarkCount": 1,
      "benchmarks": [
        {
          "key": "KStemmerParityBenchmarks.KStem_Analyse|DocumentCount=20000",
          "displayInfo": "KStemmerParityBenchmarks.KStem_Analyse: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DocumentCount=20000]",
          "typeName": "KStemmerParityBenchmarks",
          "methodName": "KStem_Analyse",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 443330215,
            "medianNanoseconds": 444399739,
            "minNanoseconds": 440674923,
            "maxNanoseconds": 444915983,
            "standardDeviationNanoseconds": 2313991.9340464436,
            "operationsPerSecond": 2.2556549636482592
          },
          "gc": {
            "bytesAllocatedPerOperation": 281343168,
            "gen0Collections": 61,
            "gen1Collections": 5,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "lightenglish",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.LightEnglishStemmerBenchmarks-20260529-125356",
      "benchmarkCount": 2,
      "benchmarks": [
        {
          "key": "LightEnglishStemmerBenchmarks.LightEnglish_Stem|DocumentCount=20000",
          "displayInfo": "LightEnglishStemmerBenchmarks.LightEnglish_Stem: DefaultJob [DocumentCount=20000]",
          "typeName": "LightEnglishStemmerBenchmarks",
          "methodName": "LightEnglish_Stem",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 97934531.7222222,
            "medianNanoseconds": 97974402.83333333,
            "minNanoseconds": 97575592.83333333,
            "maxNanoseconds": 98286977.83333333,
            "standardDeviationNanoseconds": 241791.71957785342,
            "operationsPerSecond": 10.210902961545395
          },
          "gc": {
            "bytesAllocatedPerOperation": 14675432,
            "gen0Collections": 21,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "LightEnglishStemmerBenchmarks.Porter_Stem|DocumentCount=20000",
          "displayInfo": "LightEnglishStemmerBenchmarks.Porter_Stem: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DocumentCount=20000]",
          "typeName": "LightEnglishStemmerBenchmarks",
          "methodName": "Porter_Stem",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 137849716,
            "medianNanoseconds": 137411270.25,
            "minNanoseconds": 137392597.75,
            "maxNanoseconds": 138745280,
            "standardDeviationNanoseconds": 775637.3663871941,
            "operationsPerSecond": 7.2542768241901925
          },
          "gc": {
            "bytesAllocatedPerOperation": 12529072,
            "gen0Collections": 11,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "mlt",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.MoreLikeThisBenchmarks-20260529-115707",
      "benchmarkCount": 9,
      "benchmarks": [
        {
          "key": "MoreLikeThisBenchmarks.LeanCorpus_MoreLikeThisQuery_DefaultParams|DocumentCount=20000, MaxQueryTerms=10",
          "displayInfo": "MoreLikeThisBenchmarks.LeanCorpus_MoreLikeThisQuery_DefaultParams: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [MaxQueryTerms=10, DocumentCount=20000]",
          "typeName": "MoreLikeThisBenchmarks",
          "methodName": "LeanCorpus_MoreLikeThisQuery_DefaultParams",
          "parameters": {
            "MaxQueryTerms": "10",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 34397.25272623698,
            "medianNanoseconds": 34380.546813964844,
            "minNanoseconds": 34339.69940185547,
            "maxNanoseconds": 34471.511962890625,
            "standardDeviationNanoseconds": 67.47557662069225,
            "operationsPerSecond": 29072.08921476558
          },
          "gc": {
            "bytesAllocatedPerOperation": 21620,
            "gen0Collections": 87,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "MoreLikeThisBenchmarks.LeanCorpus_MoreLikeThisQuery_DefaultParams|DocumentCount=20000, MaxQueryTerms=25",
          "displayInfo": "MoreLikeThisBenchmarks.LeanCorpus_MoreLikeThisQuery_DefaultParams: DefaultJob [MaxQueryTerms=25, DocumentCount=20000]",
          "typeName": "MoreLikeThisBenchmarks",
          "methodName": "LeanCorpus_MoreLikeThisQuery_DefaultParams",
          "parameters": {
            "MaxQueryTerms": "25",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 94679.30778808593,
            "medianNanoseconds": 94734.34167480469,
            "minNanoseconds": 93504.69226074219,
            "maxNanoseconds": 95597.45056152344,
            "standardDeviationNanoseconds": 660.9174140632425,
            "operationsPerSecond": 10561.969910450021
          },
          "gc": {
            "bytesAllocatedPerOperation": 32496,
            "gen0Collections": 65,
            "gen1Collections": 3,
            "gen2Collections": 0
          }
        },
        {
          "key": "MoreLikeThisBenchmarks.LeanCorpus_MoreLikeThisQuery_DefaultParams|DocumentCount=20000, MaxQueryTerms=50",
          "displayInfo": "MoreLikeThisBenchmarks.LeanCorpus_MoreLikeThisQuery_DefaultParams: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [MaxQueryTerms=50, DocumentCount=20000]",
          "typeName": "MoreLikeThisBenchmarks",
          "methodName": "LeanCorpus_MoreLikeThisQuery_DefaultParams",
          "parameters": {
            "MaxQueryTerms": "50",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 830521.38671875,
            "medianNanoseconds": 830199.0146484375,
            "minNanoseconds": 828303.5654296875,
            "maxNanoseconds": 833061.580078125,
            "standardDeviationNanoseconds": 2395.3326830473115,
            "operationsPerSecond": 1204.0629127575287
          },
          "gc": {
            "bytesAllocatedPerOperation": 49839,
            "gen0Collections": 13,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "MoreLikeThisBenchmarks.LeanCorpus_MoreLikeThisQuery_HighMinDocFreq|DocumentCount=20000, MaxQueryTerms=10",
          "displayInfo": "MoreLikeThisBenchmarks.LeanCorpus_MoreLikeThisQuery_HighMinDocFreq: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [MaxQueryTerms=10, DocumentCount=20000]",
          "typeName": "MoreLikeThisBenchmarks",
          "methodName": "LeanCorpus_MoreLikeThisQuery_HighMinDocFreq",
          "parameters": {
            "MaxQueryTerms": "10",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 14802.580383300781,
            "medianNanoseconds": 14816.475601196289,
            "minNanoseconds": 14753.508285522461,
            "maxNanoseconds": 14837.757263183594,
            "standardDeviationNanoseconds": 43.8095922088901,
            "operationsPerSecond": 67555.78920065376
          },
          "gc": {
            "bytesAllocatedPerOperation": 11262,
            "gen0Collections": 178,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "MoreLikeThisBenchmarks.LeanCorpus_MoreLikeThisQuery_HighMinDocFreq|DocumentCount=20000, MaxQueryTerms=25",
          "displayInfo": "MoreLikeThisBenchmarks.LeanCorpus_MoreLikeThisQuery_HighMinDocFreq: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [MaxQueryTerms=25, DocumentCount=20000]",
          "typeName": "MoreLikeThisBenchmarks",
          "methodName": "LeanCorpus_MoreLikeThisQuery_HighMinDocFreq",
          "parameters": {
            "MaxQueryTerms": "25",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 14846.348449707031,
            "medianNanoseconds": 14855.858871459961,
            "minNanoseconds": 14804.690307617188,
            "maxNanoseconds": 14878.496170043945,
            "standardDeviationNanoseconds": 37.81087705380985,
            "operationsPerSecond": 67356.6300418965
          },
          "gc": {
            "bytesAllocatedPerOperation": 11262,
            "gen0Collections": 178,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "MoreLikeThisBenchmarks.LeanCorpus_MoreLikeThisQuery_HighMinDocFreq|DocumentCount=20000, MaxQueryTerms=50",
          "displayInfo": "MoreLikeThisBenchmarks.LeanCorpus_MoreLikeThisQuery_HighMinDocFreq: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [MaxQueryTerms=50, DocumentCount=20000]",
          "typeName": "MoreLikeThisBenchmarks",
          "methodName": "LeanCorpus_MoreLikeThisQuery_HighMinDocFreq",
          "parameters": {
            "MaxQueryTerms": "50",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 14918.934443155924,
            "medianNanoseconds": 14877.500076293945,
            "minNanoseconds": 14841.826171875,
            "maxNanoseconds": 15037.477081298828,
            "standardDeviationNanoseconds": 104.19896668169295,
            "operationsPerSecond": 67028.91575870896
          },
          "gc": {
            "bytesAllocatedPerOperation": 11263,
            "gen0Collections": 178,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "MoreLikeThisBenchmarks.LeanCorpus_MoreLikeThisQuery_NoBoost|DocumentCount=20000, MaxQueryTerms=10",
          "displayInfo": "MoreLikeThisBenchmarks.LeanCorpus_MoreLikeThisQuery_NoBoost: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [MaxQueryTerms=10, DocumentCount=20000]",
          "typeName": "MoreLikeThisBenchmarks",
          "methodName": "LeanCorpus_MoreLikeThisQuery_NoBoost",
          "parameters": {
            "MaxQueryTerms": "10",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 34696.287089029945,
            "medianNanoseconds": 34696.229919433594,
            "minNanoseconds": 34652.50402832031,
            "maxNanoseconds": 34740.12731933594,
            "standardDeviationNanoseconds": 43.81167348292334,
            "operationsPerSecond": 28821.527716611894
          },
          "gc": {
            "bytesAllocatedPerOperation": 21651,
            "gen0Collections": 87,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "MoreLikeThisBenchmarks.LeanCorpus_MoreLikeThisQuery_NoBoost|DocumentCount=20000, MaxQueryTerms=25",
          "displayInfo": "MoreLikeThisBenchmarks.LeanCorpus_MoreLikeThisQuery_NoBoost: DefaultJob [MaxQueryTerms=25, DocumentCount=20000]",
          "typeName": "MoreLikeThisBenchmarks",
          "methodName": "LeanCorpus_MoreLikeThisQuery_NoBoost",
          "parameters": {
            "MaxQueryTerms": "25",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 95303.81318359375,
            "medianNanoseconds": 95330.75109863281,
            "minNanoseconds": 94212.9775390625,
            "maxNanoseconds": 96229.95129394531,
            "standardDeviationNanoseconds": 500.52879584259193,
            "operationsPerSecond": 10492.75959266808
          },
          "gc": {
            "bytesAllocatedPerOperation": 32492,
            "gen0Collections": 65,
            "gen1Collections": 2,
            "gen2Collections": 0
          }
        },
        {
          "key": "MoreLikeThisBenchmarks.LeanCorpus_MoreLikeThisQuery_NoBoost|DocumentCount=20000, MaxQueryTerms=50",
          "displayInfo": "MoreLikeThisBenchmarks.LeanCorpus_MoreLikeThisQuery_NoBoost: DefaultJob [MaxQueryTerms=50, DocumentCount=20000]",
          "typeName": "MoreLikeThisBenchmarks",
          "methodName": "LeanCorpus_MoreLikeThisQuery_NoBoost",
          "parameters": {
            "MaxQueryTerms": "50",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 831928.2132161459,
            "medianNanoseconds": 831757.7666015625,
            "minNanoseconds": 825754.7470703125,
            "maxNanoseconds": 839346.2998046875,
            "standardDeviationNanoseconds": 4132.448255391603,
            "operationsPerSecond": 1202.026790429557
          },
          "gc": {
            "bytesAllocatedPerOperation": 49840,
            "gen0Collections": 13,
            "gen1Collections": 1,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "multiphrase",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.MultiPhraseQueryBenchmarks-20260529-115005",
      "benchmarkCount": 2,
      "benchmarks": [
        {
          "key": "MultiPhraseQueryBenchmarks.LeanCorpus_MultiPhraseQuery|DocumentCount=20000",
          "displayInfo": "MultiPhraseQueryBenchmarks.LeanCorpus_MultiPhraseQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DocumentCount=20000]",
          "typeName": "MultiPhraseQueryBenchmarks",
          "methodName": "LeanCorpus_MultiPhraseQuery",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 16623.291025797527,
            "medianNanoseconds": 16625.181579589844,
            "minNanoseconds": 16610.10614013672,
            "maxNanoseconds": 16634.585357666016,
            "standardDeviationNanoseconds": 12.348630205123724,
            "operationsPerSecond": 60156.55975992417
          },
          "gc": {
            "bytesAllocatedPerOperation": 18649,
            "gen0Collections": 150,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "MultiPhraseQueryBenchmarks.LuceneNet_MultiPhraseQuery|DocumentCount=20000",
          "displayInfo": "MultiPhraseQueryBenchmarks.LuceneNet_MultiPhraseQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DocumentCount=20000]",
          "typeName": "MultiPhraseQueryBenchmarks",
          "methodName": "LuceneNet_MultiPhraseQuery",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 36350.821838378906,
            "medianNanoseconds": 36360.958251953125,
            "minNanoseconds": 36197.10412597656,
            "maxNanoseconds": 36494.40313720703,
            "standardDeviationNanoseconds": 148.90848088595996,
            "operationsPerSecond": 27509.69440102749
          },
          "gc": {
            "bytesAllocatedPerOperation": 90520,
            "gen0Collections": 354,
            "gen1Collections": 1,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "ngram",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.NGramTokeniserBenchmarks-20260529-125848",
      "benchmarkCount": 16,
      "benchmarks": [
        {
          "key": "NGramTokeniserBenchmarks.LeanCorpus_EdgeNGramTokeniser_SpanSink|DocumentCount=20000, GramRange=2-3",
          "displayInfo": "NGramTokeniserBenchmarks.LeanCorpus_EdgeNGramTokeniser_SpanSink: DefaultJob [GramRange=2-3, DocumentCount=20000]",
          "typeName": "NGramTokeniserBenchmarks",
          "methodName": "LeanCorpus_EdgeNGramTokeniser_SpanSink",
          "parameters": {
            "GramRange": "2-3",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 13,
            "meanNanoseconds": 32173986.125,
            "medianNanoseconds": 32171047.4375,
            "minNanoseconds": 32158103.75,
            "maxNanoseconds": 32200297.9375,
            "standardDeviationNanoseconds": 12403.37335843035,
            "operationsPerSecond": 31.08101048203582
          },
          "gc": {
            "bytesAllocatedPerOperation": 0,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "NGramTokeniserBenchmarks.LeanCorpus_EdgeNGramTokeniser_SpanSink|DocumentCount=20000, GramRange=3-5",
          "displayInfo": "NGramTokeniserBenchmarks.LeanCorpus_EdgeNGramTokeniser_SpanSink: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [GramRange=3-5, DocumentCount=20000]",
          "typeName": "NGramTokeniserBenchmarks",
          "methodName": "LeanCorpus_EdgeNGramTokeniser_SpanSink",
          "parameters": {
            "GramRange": "3-5",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 32336727.354166668,
            "medianNanoseconds": 32335932.9375,
            "minNanoseconds": 32334280.9375,
            "maxNanoseconds": 32339968.1875,
            "standardDeviationNanoseconds": 2925.6668506228343,
            "operationsPerSecond": 30.924588906216187
          },
          "gc": {
            "bytesAllocatedPerOperation": 0,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "NGramTokeniserBenchmarks.LeanCorpus_EdgeNGramTokeniser_Streaming|DocumentCount=20000, GramRange=2-3",
          "displayInfo": "NGramTokeniserBenchmarks.LeanCorpus_EdgeNGramTokeniser_Streaming: DefaultJob [GramRange=2-3, DocumentCount=20000]",
          "typeName": "NGramTokeniserBenchmarks",
          "methodName": "LeanCorpus_EdgeNGramTokeniser_Streaming",
          "parameters": {
            "GramRange": "2-3",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 13,
            "meanNanoseconds": 44230876.73717949,
            "medianNanoseconds": 44231198.916666664,
            "minNanoseconds": 44167123.833333336,
            "maxNanoseconds": 44308908.25,
            "standardDeviationNanoseconds": 34056.08559668576,
            "operationsPerSecond": 22.608640700070552
          },
          "gc": {
            "bytesAllocatedPerOperation": 0,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "NGramTokeniserBenchmarks.LeanCorpus_EdgeNGramTokeniser_Streaming|DocumentCount=20000, GramRange=3-5",
          "displayInfo": "NGramTokeniserBenchmarks.LeanCorpus_EdgeNGramTokeniser_Streaming: DefaultJob [GramRange=3-5, DocumentCount=20000]",
          "typeName": "NGramTokeniserBenchmarks",
          "methodName": "LeanCorpus_EdgeNGramTokeniser_Streaming",
          "parameters": {
            "GramRange": "3-5",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 46113684.47402598,
            "medianNanoseconds": 46085077.36363636,
            "minNanoseconds": 46052819.36363637,
            "maxNanoseconds": 46253835.72727273,
            "standardDeviationNanoseconds": 66991.5169177874,
            "operationsPerSecond": 21.68553676432558
          },
          "gc": {
            "bytesAllocatedPerOperation": 0,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "NGramTokeniserBenchmarks.LeanCorpus_NGramTokeniser_SpanSink|DocumentCount=20000, GramRange=2-3",
          "displayInfo": "NGramTokeniserBenchmarks.LeanCorpus_NGramTokeniser_SpanSink: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [GramRange=2-3, DocumentCount=20000]",
          "typeName": "NGramTokeniserBenchmarks",
          "methodName": "LeanCorpus_NGramTokeniser_SpanSink",
          "parameters": {
            "GramRange": "2-3",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 32487782.604166668,
            "medianNanoseconds": 32503083.375,
            "minNanoseconds": 32382438.3125,
            "maxNanoseconds": 32577826.125,
            "standardDeviationNanoseconds": 98588.46032601598,
            "operationsPerSecond": 30.78080188432887
          },
          "gc": {
            "bytesAllocatedPerOperation": 0,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "NGramTokeniserBenchmarks.LeanCorpus_NGramTokeniser_SpanSink|DocumentCount=20000, GramRange=3-5",
          "displayInfo": "NGramTokeniserBenchmarks.LeanCorpus_NGramTokeniser_SpanSink: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [GramRange=3-5, DocumentCount=20000]",
          "typeName": "NGramTokeniserBenchmarks",
          "methodName": "LeanCorpus_NGramTokeniser_SpanSink",
          "parameters": {
            "GramRange": "3-5",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 49925960.76666667,
            "medianNanoseconds": 49765058.5,
            "minNanoseconds": 49432956.8,
            "maxNanoseconds": 50579867,
            "standardDeviationNanoseconds": 590142.2763028123,
            "operationsPerSecond": 20.029659612833232
          },
          "gc": {
            "bytesAllocatedPerOperation": 0,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "NGramTokeniserBenchmarks.LeanCorpus_NGramTokeniser_Streaming|DocumentCount=20000, GramRange=2-3",
          "displayInfo": "NGramTokeniserBenchmarks.LeanCorpus_NGramTokeniser_Streaming: DefaultJob [GramRange=2-3, DocumentCount=20000]",
          "typeName": "NGramTokeniserBenchmarks",
          "methodName": "LeanCorpus_NGramTokeniser_Streaming",
          "parameters": {
            "GramRange": "2-3",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 109715295.25714286,
            "medianNanoseconds": 109683031,
            "minNanoseconds": 109565191.8,
            "maxNanoseconds": 109986178.2,
            "standardDeviationNanoseconds": 111268.96606540465,
            "operationsPerSecond": 9.114499465696843
          },
          "gc": {
            "bytesAllocatedPerOperation": 0,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "NGramTokeniserBenchmarks.LeanCorpus_NGramTokeniser_Streaming|DocumentCount=20000, GramRange=3-5",
          "displayInfo": "NGramTokeniserBenchmarks.LeanCorpus_NGramTokeniser_Streaming: DefaultJob [GramRange=3-5, DocumentCount=20000]",
          "typeName": "NGramTokeniserBenchmarks",
          "methodName": "LeanCorpus_NGramTokeniser_Streaming",
          "parameters": {
            "GramRange": "3-5",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 13,
            "meanNanoseconds": 155599578.51923078,
            "medianNanoseconds": 155526848.25,
            "minNanoseconds": 155434387.75,
            "maxNanoseconds": 155949150.75,
            "standardDeviationNanoseconds": 170489.23056336472,
            "operationsPerSecond": 6.426752626944992
          },
          "gc": {
            "bytesAllocatedPerOperation": 0,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "NGramTokeniserBenchmarks.LeanCorpus_NGramTokeniser_WordSplit_SpanSink|DocumentCount=20000, GramRange=2-3",
          "displayInfo": "NGramTokeniserBenchmarks.LeanCorpus_NGramTokeniser_WordSplit_SpanSink: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [GramRange=2-3, DocumentCount=20000]",
          "typeName": "NGramTokeniserBenchmarks",
          "methodName": "LeanCorpus_NGramTokeniser_WordSplit_SpanSink",
          "parameters": {
            "GramRange": "2-3",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 48669941.48484849,
            "medianNanoseconds": 48633133.36363637,
            "minNanoseconds": 48612761.54545455,
            "maxNanoseconds": 48763929.54545455,
            "standardDeviationNanoseconds": 82030.90512955822,
            "operationsPerSecond": 20.546562611161377
          },
          "gc": {
            "bytesAllocatedPerOperation": 0,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "NGramTokeniserBenchmarks.LeanCorpus_NGramTokeniser_WordSplit_SpanSink|DocumentCount=20000, GramRange=3-5",
          "displayInfo": "NGramTokeniserBenchmarks.LeanCorpus_NGramTokeniser_WordSplit_SpanSink: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [GramRange=3-5, DocumentCount=20000]",
          "typeName": "NGramTokeniserBenchmarks",
          "methodName": "LeanCorpus_NGramTokeniser_WordSplit_SpanSink",
          "parameters": {
            "GramRange": "3-5",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 49183453.39393938,
            "medianNanoseconds": 49142410.90909091,
            "minNanoseconds": 49139722.63636363,
            "maxNanoseconds": 49268226.63636363,
            "standardDeviationNanoseconds": 73428.08506221276,
            "operationsPerSecond": 20.33204118446926
          },
          "gc": {
            "bytesAllocatedPerOperation": 0,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "NGramTokeniserBenchmarks.LeanCorpus_NGramTokeniser_WordSplit_Streaming|DocumentCount=20000, GramRange=2-3",
          "displayInfo": "NGramTokeniserBenchmarks.LeanCorpus_NGramTokeniser_WordSplit_Streaming: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [GramRange=2-3, DocumentCount=20000]",
          "typeName": "NGramTokeniserBenchmarks",
          "methodName": "LeanCorpus_NGramTokeniser_WordSplit_Streaming",
          "parameters": {
            "GramRange": "2-3",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 109131306.53333335,
            "medianNanoseconds": 109038690,
            "minNanoseconds": 109003999.6,
            "maxNanoseconds": 109351230,
            "standardDeviationNanoseconds": 191247.49503210257,
            "operationsPerSecond": 9.163273415906163
          },
          "gc": {
            "bytesAllocatedPerOperation": 0,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "NGramTokeniserBenchmarks.LeanCorpus_NGramTokeniser_WordSplit_Streaming|DocumentCount=20000, GramRange=3-5",
          "displayInfo": "NGramTokeniserBenchmarks.LeanCorpus_NGramTokeniser_WordSplit_Streaming: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [GramRange=3-5, DocumentCount=20000]",
          "typeName": "NGramTokeniserBenchmarks",
          "methodName": "LeanCorpus_NGramTokeniser_WordSplit_Streaming",
          "parameters": {
            "GramRange": "3-5",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 105940510.26666667,
            "medianNanoseconds": 105880939.4,
            "minNanoseconds": 105769763,
            "maxNanoseconds": 106170828.4,
            "standardDeviationNanoseconds": 207062.50236547986,
            "operationsPerSecond": 9.439259802344392
          },
          "gc": {
            "bytesAllocatedPerOperation": 0,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "NGramTokeniserBenchmarks.LuceneNet_EdgeNGramTokenizer|DocumentCount=20000, GramRange=2-3",
          "displayInfo": "NGramTokeniserBenchmarks.LuceneNet_EdgeNGramTokenizer: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [GramRange=2-3, DocumentCount=20000]",
          "typeName": "NGramTokeniserBenchmarks",
          "methodName": "LuceneNet_EdgeNGramTokenizer",
          "parameters": {
            "GramRange": "2-3",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 119202077,
            "medianNanoseconds": 119056798,
            "minNanoseconds": 118719884.2,
            "maxNanoseconds": 119829548.8,
            "standardDeviationNanoseconds": 568918.598750328,
            "operationsPerSecond": 8.389115568850364
          },
          "gc": {
            "bytesAllocatedPerOperation": 177120000,
            "gen0Collections": 211,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "NGramTokeniserBenchmarks.LuceneNet_EdgeNGramTokenizer|DocumentCount=20000, GramRange=3-5",
          "displayInfo": "NGramTokeniserBenchmarks.LuceneNet_EdgeNGramTokenizer: DefaultJob [GramRange=3-5, DocumentCount=20000]",
          "typeName": "NGramTokeniserBenchmarks",
          "methodName": "LuceneNet_EdgeNGramTokenizer",
          "parameters": {
            "GramRange": "3-5",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 119710362.74666667,
            "medianNanoseconds": 119818748.8,
            "minNanoseconds": 119248057.4,
            "maxNanoseconds": 120294176,
            "standardDeviationNanoseconds": 399855.3624492894,
            "operationsPerSecond": 8.353495696243264
          },
          "gc": {
            "bytesAllocatedPerOperation": 177600000,
            "gen0Collections": 212,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "NGramTokeniserBenchmarks.LuceneNet_NGramTokenizer|DocumentCount=20000, GramRange=2-3",
          "displayInfo": "NGramTokeniserBenchmarks.LuceneNet_NGramTokenizer: DefaultJob [GramRange=2-3, DocumentCount=20000]",
          "typeName": "NGramTokeniserBenchmarks",
          "methodName": "LuceneNet_NGramTokenizer",
          "parameters": {
            "GramRange": "2-3",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 13,
            "meanNanoseconds": 605812104,
            "medianNanoseconds": 605565306,
            "minNanoseconds": 602457780,
            "maxNanoseconds": 609260477,
            "standardDeviationNanoseconds": 1822506.973198411,
            "operationsPerSecond": 1.6506768243772165
          },
          "gc": {
            "bytesAllocatedPerOperation": 177120000,
            "gen0Collections": 42,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "NGramTokeniserBenchmarks.LuceneNet_NGramTokenizer|DocumentCount=20000, GramRange=3-5",
          "displayInfo": "NGramTokeniserBenchmarks.LuceneNet_NGramTokenizer: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [GramRange=3-5, DocumentCount=20000]",
          "typeName": "NGramTokeniserBenchmarks",
          "methodName": "LuceneNet_NGramTokenizer",
          "parameters": {
            "GramRange": "3-5",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 1014022224,
            "medianNanoseconds": 1013552474,
            "minNanoseconds": 1012829998,
            "maxNanoseconds": 1015684200,
            "standardDeviationNanoseconds": 1483952.8500178165,
            "operationsPerSecond": 0.9861716798033413
          },
          "gc": {
            "bytesAllocatedPerOperation": 177600000,
            "gen0Collections": 42,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "parallel",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.ParallelSearchBenchmarks-20260529-122900",
      "benchmarkCount": 6,
      "benchmarks": [
        {
          "key": "ParallelSearchBenchmarks.LeanCorpus_ParallelSearch_BooleanQuery|DocumentCount=20000, SegmentCount=4",
          "displayInfo": "ParallelSearchBenchmarks.LeanCorpus_ParallelSearch_BooleanQuery: DefaultJob [SegmentCount=4, DocumentCount=20000]",
          "typeName": "ParallelSearchBenchmarks",
          "methodName": "LeanCorpus_ParallelSearch_BooleanQuery",
          "parameters": {
            "SegmentCount": "4",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 15604.459221976143,
            "medianNanoseconds": 15625.914505004883,
            "minNanoseconds": 15387.36880493164,
            "maxNanoseconds": 15682.873046875,
            "standardDeviationNanoseconds": 79.73633726955259,
            "operationsPerSecond": 64084.24577710937
          },
          "gc": {
            "bytesAllocatedPerOperation": 9303,
            "gen0Collections": 73,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "ParallelSearchBenchmarks.LeanCorpus_ParallelSearch_BooleanQuery|DocumentCount=20000, SegmentCount=8",
          "displayInfo": "ParallelSearchBenchmarks.LeanCorpus_ParallelSearch_BooleanQuery: DefaultJob [SegmentCount=8, DocumentCount=20000]",
          "typeName": "ParallelSearchBenchmarks",
          "methodName": "LeanCorpus_ParallelSearch_BooleanQuery",
          "parameters": {
            "SegmentCount": "8",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 17890.00975748698,
            "medianNanoseconds": 17880.210021972656,
            "minNanoseconds": 17671.8251953125,
            "maxNanoseconds": 18244.31689453125,
            "standardDeviationNanoseconds": 166.15042553108592,
            "operationsPerSecond": 55897.11875822199
          },
          "gc": {
            "bytesAllocatedPerOperation": 14648,
            "gen0Collections": 116,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "ParallelSearchBenchmarks.LeanCorpus_ParallelSearch|DocumentCount=20000, SegmentCount=4",
          "displayInfo": "ParallelSearchBenchmarks.LeanCorpus_ParallelSearch: DefaultJob [SegmentCount=4, DocumentCount=20000]",
          "typeName": "ParallelSearchBenchmarks",
          "methodName": "LeanCorpus_ParallelSearch",
          "parameters": {
            "SegmentCount": "4",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 3137.289494832357,
            "medianNanoseconds": 3135.829261779785,
            "minNanoseconds": 3134.162425994873,
            "maxNanoseconds": 3143.248149871826,
            "standardDeviationNanoseconds": 3.1565391747575458,
            "operationsPerSecond": 318746.4853489511
          },
          "gc": {
            "bytesAllocatedPerOperation": 576,
            "gen0Collections": 36,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "ParallelSearchBenchmarks.LeanCorpus_ParallelSearch|DocumentCount=20000, SegmentCount=8",
          "displayInfo": "ParallelSearchBenchmarks.LeanCorpus_ParallelSearch: DefaultJob [SegmentCount=8, DocumentCount=20000]",
          "typeName": "ParallelSearchBenchmarks",
          "methodName": "LeanCorpus_ParallelSearch",
          "parameters": {
            "SegmentCount": "8",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 13,
            "meanNanoseconds": 4123.544073838454,
            "medianNanoseconds": 4122.975234985352,
            "minNanoseconds": 4118.295875549316,
            "maxNanoseconds": 4129.101333618164,
            "standardDeviationNanoseconds": 3.214398243357144,
            "operationsPerSecond": 242509.83670683485
          },
          "gc": {
            "bytesAllocatedPerOperation": 672,
            "gen0Collections": 21,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "ParallelSearchBenchmarks.LeanCorpus_SequentialSearch|DocumentCount=20000, SegmentCount=4",
          "displayInfo": "ParallelSearchBenchmarks.LeanCorpus_SequentialSearch: DefaultJob [SegmentCount=4, DocumentCount=20000]",
          "typeName": "ParallelSearchBenchmarks",
          "methodName": "LeanCorpus_SequentialSearch",
          "parameters": {
            "SegmentCount": "4",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 3169.3207866123744,
            "medianNanoseconds": 3169.114501953125,
            "minNanoseconds": 3166.1462631225586,
            "maxNanoseconds": 3174.2528076171875,
            "standardDeviationNanoseconds": 2.325681006470249,
            "operationsPerSecond": 315525.0185541744
          },
          "gc": {
            "bytesAllocatedPerOperation": 576,
            "gen0Collections": 36,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "ParallelSearchBenchmarks.LeanCorpus_SequentialSearch|DocumentCount=20000, SegmentCount=8",
          "displayInfo": "ParallelSearchBenchmarks.LeanCorpus_SequentialSearch: DefaultJob [SegmentCount=8, DocumentCount=20000]",
          "typeName": "ParallelSearchBenchmarks",
          "methodName": "LeanCorpus_SequentialSearch",
          "parameters": {
            "SegmentCount": "8",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 13,
            "meanNanoseconds": 4097.02174612192,
            "medianNanoseconds": 4097.56135559082,
            "minNanoseconds": 4092.2035903930664,
            "maxNanoseconds": 4103.7463455200195,
            "standardDeviationNanoseconds": 3.3180613504035716,
            "operationsPerSecond": 244079.7393732559
          },
          "gc": {
            "bytesAllocatedPerOperation": 672,
            "gen0Collections": 21,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "phrase",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.PhraseQueryBenchmarks-20260529-102938",
      "benchmarkCount": 6,
      "benchmarks": [
        {
          "key": "PhraseQueryBenchmarks.LeanCorpus_PhraseQuery|DocumentCount=20000, PhraseType=ExactThreeWord",
          "displayInfo": "PhraseQueryBenchmarks.LeanCorpus_PhraseQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [PhraseType=ExactThreeWord, DocumentCount=20000]",
          "typeName": "PhraseQueryBenchmarks",
          "methodName": "LeanCorpus_PhraseQuery",
          "parameters": {
            "PhraseType": "ExactThreeWord",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 21397.873799641926,
            "medianNanoseconds": 21394.348602294922,
            "minNanoseconds": 21378.512969970703,
            "maxNanoseconds": 21420.759826660156,
            "standardDeviationNanoseconds": 21.34290250379027,
            "operationsPerSecond": 46733.615188287265
          },
          "gc": {
            "bytesAllocatedPerOperation": 14711,
            "gen0Collections": 118,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "PhraseQueryBenchmarks.LeanCorpus_PhraseQuery|DocumentCount=20000, PhraseType=ExactTwoWord",
          "displayInfo": "PhraseQueryBenchmarks.LeanCorpus_PhraseQuery: DefaultJob [PhraseType=ExactTwoWord, DocumentCount=20000]",
          "typeName": "PhraseQueryBenchmarks",
          "methodName": "LeanCorpus_PhraseQuery",
          "parameters": {
            "PhraseType": "ExactTwoWord",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 21096.50866481236,
            "medianNanoseconds": 21093.043685913086,
            "minNanoseconds": 21013.752807617188,
            "maxNanoseconds": 21189.680267333984,
            "standardDeviationNanoseconds": 47.93730965319546,
            "operationsPerSecond": 47401.20822304292
          },
          "gc": {
            "bytesAllocatedPerOperation": 11349,
            "gen0Collections": 91,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "PhraseQueryBenchmarks.LeanCorpus_PhraseQuery|DocumentCount=20000, PhraseType=SlopTwoWord",
          "displayInfo": "PhraseQueryBenchmarks.LeanCorpus_PhraseQuery: DefaultJob [PhraseType=SlopTwoWord, DocumentCount=20000]",
          "typeName": "PhraseQueryBenchmarks",
          "methodName": "LeanCorpus_PhraseQuery",
          "parameters": {
            "PhraseType": "SlopTwoWord",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 44779.05324445452,
            "medianNanoseconds": 44780.13595581055,
            "minNanoseconds": 44537.14990234375,
            "maxNanoseconds": 45004.48352050781,
            "standardDeviationNanoseconds": 106.99467709399882,
            "operationsPerSecond": 22331.870094279875
          },
          "gc": {
            "bytesAllocatedPerOperation": 11457,
            "gen0Collections": 46,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "PhraseQueryBenchmarks.LuceneNet_PhraseQuery|DocumentCount=20000, PhraseType=ExactThreeWord",
          "displayInfo": "PhraseQueryBenchmarks.LuceneNet_PhraseQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [PhraseType=ExactThreeWord, DocumentCount=20000]",
          "typeName": "PhraseQueryBenchmarks",
          "methodName": "LuceneNet_PhraseQuery",
          "parameters": {
            "PhraseType": "ExactThreeWord",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 20844.360280354816,
            "medianNanoseconds": 20860.49285888672,
            "minNanoseconds": 20807.706420898438,
            "maxNanoseconds": 20864.881561279297,
            "standardDeviationNanoseconds": 31.8189289096712,
            "operationsPerSecond": 47974.60735422377
          },
          "gc": {
            "bytesAllocatedPerOperation": 75232,
            "gen0Collections": 588,
            "gen1Collections": 65,
            "gen2Collections": 0
          }
        },
        {
          "key": "PhraseQueryBenchmarks.LuceneNet_PhraseQuery|DocumentCount=20000, PhraseType=ExactTwoWord",
          "displayInfo": "PhraseQueryBenchmarks.LuceneNet_PhraseQuery: DefaultJob [PhraseType=ExactTwoWord, DocumentCount=20000]",
          "typeName": "PhraseQueryBenchmarks",
          "methodName": "LuceneNet_PhraseQuery",
          "parameters": {
            "PhraseType": "ExactTwoWord",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 24827.605954996743,
            "medianNanoseconds": 24795.89779663086,
            "minNanoseconds": 24731.915100097656,
            "maxNanoseconds": 24997.224365234375,
            "standardDeviationNanoseconds": 84.8376129787082,
            "operationsPerSecond": 40277.745740472514
          },
          "gc": {
            "bytesAllocatedPerOperation": 63320,
            "gen0Collections": 495,
            "gen1Collections": 52,
            "gen2Collections": 0
          }
        },
        {
          "key": "PhraseQueryBenchmarks.LuceneNet_PhraseQuery|DocumentCount=20000, PhraseType=SlopTwoWord",
          "displayInfo": "PhraseQueryBenchmarks.LuceneNet_PhraseQuery: DefaultJob [PhraseType=SlopTwoWord, DocumentCount=20000]",
          "typeName": "PhraseQueryBenchmarks",
          "methodName": "LuceneNet_PhraseQuery",
          "parameters": {
            "PhraseType": "SlopTwoWord",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 20698.848220825195,
            "medianNanoseconds": 20684.94285583496,
            "minNanoseconds": 20670.048431396484,
            "maxNanoseconds": 20761.298248291016,
            "standardDeviationNanoseconds": 33.17212829607027,
            "operationsPerSecond": 48311.86688899414
          },
          "gc": {
            "bytesAllocatedPerOperation": 30720,
            "gen0Collections": 240,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "prefix",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.PrefixQueryBenchmarks-20260529-103444",
      "benchmarkCount": 6,
      "benchmarks": [
        {
          "key": "PrefixQueryBenchmarks.LeanCorpus_PrefixQuery|DocumentCount=20000, QueryPrefix=gov",
          "displayInfo": "PrefixQueryBenchmarks.LeanCorpus_PrefixQuery: DefaultJob [QueryPrefix=gov, DocumentCount=20000]",
          "typeName": "PrefixQueryBenchmarks",
          "methodName": "LeanCorpus_PrefixQuery",
          "parameters": {
            "QueryPrefix": "gov",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 10270.527075631278,
            "medianNanoseconds": 10272.124168395996,
            "minNanoseconds": 10215.031692504883,
            "maxNanoseconds": 10360.653244018555,
            "standardDeviationNanoseconds": 40.07838918793907,
            "operationsPerSecond": 97365.98644218412
          },
          "gc": {
            "bytesAllocatedPerOperation": 4408,
            "gen0Collections": 69,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "PrefixQueryBenchmarks.LeanCorpus_PrefixQuery|DocumentCount=20000, QueryPrefix=mark",
          "displayInfo": "PrefixQueryBenchmarks.LeanCorpus_PrefixQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [QueryPrefix=mark, DocumentCount=20000]",
          "typeName": "PrefixQueryBenchmarks",
          "methodName": "LeanCorpus_PrefixQuery",
          "parameters": {
            "QueryPrefix": "mark",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 19725.77178955078,
            "medianNanoseconds": 19696.526916503906,
            "minNanoseconds": 19691.923309326172,
            "maxNanoseconds": 19788.865142822266,
            "standardDeviationNanoseconds": 54.68890856510091,
            "operationsPerSecond": 50695.101346033225
          },
          "gc": {
            "bytesAllocatedPerOperation": 5249,
            "gen0Collections": 41,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "PrefixQueryBenchmarks.LeanCorpus_PrefixQuery|DocumentCount=20000, QueryPrefix=pres",
          "displayInfo": "PrefixQueryBenchmarks.LeanCorpus_PrefixQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [QueryPrefix=pres, DocumentCount=20000]",
          "typeName": "PrefixQueryBenchmarks",
          "methodName": "LeanCorpus_PrefixQuery",
          "parameters": {
            "QueryPrefix": "pres",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 36942.7255859375,
            "medianNanoseconds": 36821.738708496094,
            "minNanoseconds": 36729.362365722656,
            "maxNanoseconds": 37277.07568359375,
            "standardDeviationNanoseconds": 293.2163672582191,
            "operationsPerSecond": 27068.928568190346
          },
          "gc": {
            "bytesAllocatedPerOperation": 8146,
            "gen0Collections": 32,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "PrefixQueryBenchmarks.LuceneNet_PrefixQuery|DocumentCount=20000, QueryPrefix=gov",
          "displayInfo": "PrefixQueryBenchmarks.LuceneNet_PrefixQuery: DefaultJob [QueryPrefix=gov, DocumentCount=20000]",
          "typeName": "PrefixQueryBenchmarks",
          "methodName": "LuceneNet_PrefixQuery",
          "parameters": {
            "QueryPrefix": "gov",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 15647.69633992513,
            "medianNanoseconds": 15630.813507080078,
            "minNanoseconds": 15607.812072753906,
            "maxNanoseconds": 15752.911895751953,
            "standardDeviationNanoseconds": 42.263263282181434,
            "operationsPerSecond": 63907.17063242708
          },
          "gc": {
            "bytesAllocatedPerOperation": 55856,
            "gen0Collections": 436,
            "gen1Collections": 1,
            "gen2Collections": 0
          }
        },
        {
          "key": "PrefixQueryBenchmarks.LuceneNet_PrefixQuery|DocumentCount=20000, QueryPrefix=mark",
          "displayInfo": "PrefixQueryBenchmarks.LuceneNet_PrefixQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [QueryPrefix=mark, DocumentCount=20000]",
          "typeName": "PrefixQueryBenchmarks",
          "methodName": "LuceneNet_PrefixQuery",
          "parameters": {
            "QueryPrefix": "mark",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 23539.06263224284,
            "medianNanoseconds": 23504.552612304688,
            "minNanoseconds": 23487.570220947266,
            "maxNanoseconds": 23625.065063476562,
            "standardDeviationNanoseconds": 74.96275100099864,
            "operationsPerSecond": 42482.57526747225
          },
          "gc": {
            "bytesAllocatedPerOperation": 61528,
            "gen0Collections": 481,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "PrefixQueryBenchmarks.LuceneNet_PrefixQuery|DocumentCount=20000, QueryPrefix=pres",
          "displayInfo": "PrefixQueryBenchmarks.LuceneNet_PrefixQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [QueryPrefix=pres, DocumentCount=20000]",
          "typeName": "PrefixQueryBenchmarks",
          "methodName": "LuceneNet_PrefixQuery",
          "parameters": {
            "QueryPrefix": "pres",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 49292.160970052086,
            "medianNanoseconds": 49311.62023925781,
            "minNanoseconds": 49207.09924316406,
            "maxNanoseconds": 49357.763427734375,
            "standardDeviationNanoseconds": 77.19405091440063,
            "operationsPerSecond": 20287.201460036606
          },
          "gc": {
            "bytesAllocatedPerOperation": 65248,
            "gen0Collections": 254,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "query",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.TermQueryBenchmarks-20260529-100111",
      "benchmarkCount": 6,
      "benchmarks": [
        {
          "key": "TermQueryBenchmarks.LeanCorpus_TermQuery|DocumentCount=20000, QueryTerm=government",
          "displayInfo": "TermQueryBenchmarks.LeanCorpus_TermQuery: DefaultJob [QueryTerm=government, DocumentCount=20000]",
          "typeName": "TermQueryBenchmarks",
          "methodName": "LeanCorpus_TermQuery",
          "parameters": {
            "QueryTerm": "government",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 2630.6678979056223,
            "medianNanoseconds": 2630.168430328369,
            "minNanoseconds": 2627.1591148376465,
            "maxNanoseconds": 2637.2294883728027,
            "standardDeviationNanoseconds": 3.1012498172372562,
            "operationsPerSecond": 380131.60110257135
          },
          "gc": {
            "bytesAllocatedPerOperation": 528,
            "gen0Collections": 33,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "TermQueryBenchmarks.LeanCorpus_TermQuery|DocumentCount=20000, QueryTerm=people",
          "displayInfo": "TermQueryBenchmarks.LeanCorpus_TermQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [QueryTerm=people, DocumentCount=20000]",
          "typeName": "TermQueryBenchmarks",
          "methodName": "LeanCorpus_TermQuery",
          "parameters": {
            "QueryTerm": "people",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 21909.153361002605,
            "medianNanoseconds": 21916.205047607422,
            "minNanoseconds": 21886.525421142578,
            "maxNanoseconds": 21924.729614257812,
            "standardDeviationNanoseconds": 20.054545765884466,
            "operationsPerSecond": 45643.02342143257
          },
          "gc": {
            "bytesAllocatedPerOperation": 520,
            "gen0Collections": 4,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "TermQueryBenchmarks.LeanCorpus_TermQuery|DocumentCount=20000, QueryTerm=said",
          "displayInfo": "TermQueryBenchmarks.LeanCorpus_TermQuery: DefaultJob [QueryTerm=said, DocumentCount=20000]",
          "typeName": "TermQueryBenchmarks",
          "methodName": "LeanCorpus_TermQuery",
          "parameters": {
            "QueryTerm": "said",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 92700.1298828125,
            "medianNanoseconds": 92667.02587890625,
            "minNanoseconds": 92595.44653320312,
            "maxNanoseconds": 92859.98901367188,
            "standardDeviationNanoseconds": 86.51308184003861,
            "operationsPerSecond": 10787.471401217634
          },
          "gc": {
            "bytesAllocatedPerOperation": 512,
            "gen0Collections": 1,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "TermQueryBenchmarks.LuceneNet_TermQuery|DocumentCount=20000, QueryTerm=government",
          "displayInfo": "TermQueryBenchmarks.LuceneNet_TermQuery: DefaultJob [QueryTerm=government, DocumentCount=20000]",
          "typeName": "TermQueryBenchmarks",
          "methodName": "LuceneNet_TermQuery",
          "parameters": {
            "QueryTerm": "government",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 9064.351802571615,
            "medianNanoseconds": 9065.643661499023,
            "minNanoseconds": 9031.949920654297,
            "maxNanoseconds": 9092.902099609375,
            "standardDeviationNanoseconds": 18.59510766321585,
            "operationsPerSecond": 110322.28467967159
          },
          "gc": {
            "bytesAllocatedPerOperation": 12936,
            "gen0Collections": 202,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "TermQueryBenchmarks.LuceneNet_TermQuery|DocumentCount=20000, QueryTerm=people",
          "displayInfo": "TermQueryBenchmarks.LuceneNet_TermQuery: DefaultJob [QueryTerm=people, DocumentCount=20000]",
          "typeName": "TermQueryBenchmarks",
          "methodName": "LuceneNet_TermQuery",
          "parameters": {
            "QueryTerm": "people",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 13,
            "meanNanoseconds": 26229.376434326172,
            "medianNanoseconds": 26221.558197021484,
            "minNanoseconds": 26203.875762939453,
            "maxNanoseconds": 26298.92562866211,
            "standardDeviationNanoseconds": 26.7981567372763,
            "operationsPerSecond": 38125.191519662214
          },
          "gc": {
            "bytesAllocatedPerOperation": 13304,
            "gen0Collections": 104,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "TermQueryBenchmarks.LuceneNet_TermQuery|DocumentCount=20000, QueryTerm=said",
          "displayInfo": "TermQueryBenchmarks.LuceneNet_TermQuery: DefaultJob [QueryTerm=said, DocumentCount=20000]",
          "typeName": "TermQueryBenchmarks",
          "methodName": "LuceneNet_TermQuery",
          "parameters": {
            "QueryTerm": "said",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 89071.32638346354,
            "medianNanoseconds": 88968.40856933594,
            "minNanoseconds": 88826.18737792969,
            "maxNanoseconds": 89447.42272949219,
            "standardDeviationNanoseconds": 186.71901328116442,
            "operationsPerSecond": 11226.957547425207
          },
          "gc": {
            "bytesAllocatedPerOperation": 13168,
            "gen0Collections": 25,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "query-cache",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.QueryCacheBenchmarks-20260529-122535",
      "benchmarkCount": 4,
      "benchmarks": [
        {
          "key": "QueryCacheBenchmarks.LeanCorpus_NoCache_BooleanQuery|DocumentCount=20000",
          "displayInfo": "QueryCacheBenchmarks.LeanCorpus_NoCache_BooleanQuery: DefaultJob [DocumentCount=20000]",
          "typeName": "QueryCacheBenchmarks",
          "methodName": "LeanCorpus_NoCache_BooleanQuery",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 9969.266205923897,
            "medianNanoseconds": 9941.697700500488,
            "minNanoseconds": 9895.792114257812,
            "maxNanoseconds": 10146.92594909668,
            "standardDeviationNanoseconds": 74.75444883895409,
            "operationsPerSecond": 100308.2854188189
          },
          "gc": {
            "bytesAllocatedPerOperation": 5895,
            "gen0Collections": 93,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "QueryCacheBenchmarks.LeanCorpus_NoCache|DocumentCount=20000",
          "displayInfo": "QueryCacheBenchmarks.LeanCorpus_NoCache: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DocumentCount=20000]",
          "typeName": "QueryCacheBenchmarks",
          "methodName": "LeanCorpus_NoCache",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 2620.859303792318,
            "medianNanoseconds": 2620.314743041992,
            "minNanoseconds": 2620.0230827331543,
            "maxNanoseconds": 2622.2400856018066,
            "standardDeviationNanoseconds": 1.2046515006421985,
            "operationsPerSecond": 381554.24770533276
          },
          "gc": {
            "bytesAllocatedPerOperation": 528,
            "gen0Collections": 33,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "QueryCacheBenchmarks.LeanCorpus_WithCache_BooleanQuery|DocumentCount=20000",
          "displayInfo": "QueryCacheBenchmarks.LeanCorpus_WithCache_BooleanQuery: DefaultJob [DocumentCount=20000]",
          "typeName": "QueryCacheBenchmarks",
          "methodName": "LeanCorpus_WithCache_BooleanQuery",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 732.0264626820882,
            "medianNanoseconds": 731.4069309234619,
            "minNanoseconds": 730.8041706085205,
            "maxNanoseconds": 734.7157344818115,
            "standardDeviationNanoseconds": 1.28657388812783,
            "operationsPerSecond": 1366070.833472437
          },
          "gc": {
            "bytesAllocatedPerOperation": 1056,
            "gen0Collections": 264,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "QueryCacheBenchmarks.LeanCorpus_WithCache|DocumentCount=20000",
          "displayInfo": "QueryCacheBenchmarks.LeanCorpus_WithCache: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DocumentCount=20000]",
          "typeName": "QueryCacheBenchmarks",
          "methodName": "LeanCorpus_WithCache",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 278.64390023549396,
            "medianNanoseconds": 278.37215757369995,
            "minNanoseconds": 278.2479758262634,
            "maxNanoseconds": 279.31156730651855,
            "standardDeviationNanoseconds": 0.581540854022312,
            "operationsPerSecond": 3588809.943999696
          },
          "gc": {
            "bytesAllocatedPerOperation": 496,
            "gen0Collections": 248,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "range",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.RangeQueryBenchmarks-20260529-113452",
      "benchmarkCount": 6,
      "benchmarks": [
        {
          "key": "RangeQueryBenchmarks.LeanCorpus_RangeQuery|DocumentCount=20000, RangeWidth=0.01",
          "displayInfo": "RangeQueryBenchmarks.LeanCorpus_RangeQuery: DefaultJob [RangeWidth=0.01, DocumentCount=20000]",
          "typeName": "RangeQueryBenchmarks",
          "methodName": "LeanCorpus_RangeQuery",
          "parameters": {
            "RangeWidth": "0.01",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 7758.632531302316,
            "medianNanoseconds": 7758.311874389648,
            "minNanoseconds": 7721.118240356445,
            "maxNanoseconds": 7785.161849975586,
            "standardDeviationNanoseconds": 18.205185290163104,
            "operationsPerSecond": 128888.69217165337
          },
          "gc": {
            "bytesAllocatedPerOperation": 4224,
            "gen0Collections": 66,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "RangeQueryBenchmarks.LeanCorpus_RangeQuery|DocumentCount=20000, RangeWidth=0.1",
          "displayInfo": "RangeQueryBenchmarks.LeanCorpus_RangeQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [RangeWidth=0.1, DocumentCount=20000]",
          "typeName": "RangeQueryBenchmarks",
          "methodName": "LeanCorpus_RangeQuery",
          "parameters": {
            "RangeWidth": "0.1",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 19648.436686197918,
            "medianNanoseconds": 19674.407257080078,
            "minNanoseconds": 19577.783294677734,
            "maxNanoseconds": 19693.119506835938,
            "standardDeviationNanoseconds": 61.898815600215976,
            "operationsPerSecond": 50894.63431472143
          },
          "gc": {
            "bytesAllocatedPerOperation": 4224,
            "gen0Collections": 33,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "RangeQueryBenchmarks.LeanCorpus_RangeQuery|DocumentCount=20000, RangeWidth=0.5",
          "displayInfo": "RangeQueryBenchmarks.LeanCorpus_RangeQuery: DefaultJob [RangeWidth=0.5, DocumentCount=20000]",
          "typeName": "RangeQueryBenchmarks",
          "methodName": "LeanCorpus_RangeQuery",
          "parameters": {
            "RangeWidth": "0.5",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 67850.14021809895,
            "medianNanoseconds": 67897.40930175781,
            "minNanoseconds": 67515.89440917969,
            "maxNanoseconds": 68298.150390625,
            "standardDeviationNanoseconds": 199.91631418612843,
            "operationsPerSecond": 14738.363056960214
          },
          "gc": {
            "bytesAllocatedPerOperation": 4212,
            "gen0Collections": 8,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "RangeQueryBenchmarks.LuceneNet_NumericRangeQuery|DocumentCount=20000, RangeWidth=0.01",
          "displayInfo": "RangeQueryBenchmarks.LuceneNet_NumericRangeQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [RangeWidth=0.01, DocumentCount=20000]",
          "typeName": "RangeQueryBenchmarks",
          "methodName": "LuceneNet_NumericRangeQuery",
          "parameters": {
            "RangeWidth": "0.01",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 28839.08318074544,
            "medianNanoseconds": 28845.328399658203,
            "minNanoseconds": 28801.348358154297,
            "maxNanoseconds": 28870.572784423828,
            "standardDeviationNanoseconds": 35.03223326506129,
            "operationsPerSecond": 34675.16611858365
          },
          "gc": {
            "bytesAllocatedPerOperation": 70264,
            "gen0Collections": 546,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "RangeQueryBenchmarks.LuceneNet_NumericRangeQuery|DocumentCount=20000, RangeWidth=0.1",
          "displayInfo": "RangeQueryBenchmarks.LuceneNet_NumericRangeQuery: DefaultJob [RangeWidth=0.1, DocumentCount=20000]",
          "typeName": "RangeQueryBenchmarks",
          "methodName": "LuceneNet_NumericRangeQuery",
          "parameters": {
            "RangeWidth": "0.1",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 63551.994466145836,
            "medianNanoseconds": 63504.40100097656,
            "minNanoseconds": 63430.408203125,
            "maxNanoseconds": 63769.783447265625,
            "standardDeviationNanoseconds": 104.81192071968566,
            "operationsPerSecond": 15735.147392308832
          },
          "gc": {
            "bytesAllocatedPerOperation": 68336,
            "gen0Collections": 133,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "RangeQueryBenchmarks.LuceneNet_NumericRangeQuery|DocumentCount=20000, RangeWidth=0.5",
          "displayInfo": "RangeQueryBenchmarks.LuceneNet_NumericRangeQuery: DefaultJob [RangeWidth=0.5, DocumentCount=20000]",
          "typeName": "RangeQueryBenchmarks",
          "methodName": "LuceneNet_NumericRangeQuery",
          "parameters": {
            "RangeWidth": "0.5",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 218269.12513950892,
            "medianNanoseconds": 218193.8992919922,
            "minNanoseconds": 217991.33715820312,
            "maxNanoseconds": 218638.98022460938,
            "standardDeviationNanoseconds": 231.47300917382313,
            "operationsPerSecond": 4581.500014538657
          },
          "gc": {
            "bytesAllocatedPerOperation": 75992,
            "gen0Collections": 74,
            "gen1Collections": 2,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "regexp",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.RegexpQueryBenchmarks-20260529-113952",
      "benchmarkCount": 6,
      "benchmarks": [
        {
          "key": "RegexpQueryBenchmarks.LeanCorpus_RegexpQuery|DocumentCount=20000, Pattern=.*nation.*",
          "displayInfo": "RegexpQueryBenchmarks.LeanCorpus_RegexpQuery: DefaultJob [Pattern=.*nation.*, DocumentCount=20000]",
          "typeName": "RegexpQueryBenchmarks",
          "methodName": "LeanCorpus_RegexpQuery",
          "parameters": {
            "Pattern": ".*nation.*",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 12,
            "meanNanoseconds": 3883276.4694010415,
            "medianNanoseconds": 3882957.845703125,
            "minNanoseconds": 3877846.265625,
            "maxNanoseconds": 3888884.16796875,
            "standardDeviationNanoseconds": 4027.7426217781535,
            "operationsPerSecond": 257.51450041728305
          },
          "gc": {
            "bytesAllocatedPerOperation": 12118,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "RegexpQueryBenchmarks.LeanCorpus_RegexpQuery|DocumentCount=20000, Pattern=gov.*ment",
          "displayInfo": "RegexpQueryBenchmarks.LeanCorpus_RegexpQuery: DefaultJob [Pattern=gov.*ment, DocumentCount=20000]",
          "typeName": "RegexpQueryBenchmarks",
          "methodName": "LeanCorpus_RegexpQuery",
          "parameters": {
            "Pattern": "gov.*ment",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 13777.624266560872,
            "medianNanoseconds": 13781.468444824219,
            "minNanoseconds": 13687.153442382812,
            "maxNanoseconds": 13814.263320922852,
            "standardDeviationNanoseconds": 36.40011505891763,
            "operationsPerSecond": 72581.45385972389
          },
          "gc": {
            "bytesAllocatedPerOperation": 11105,
            "gen0Collections": 175,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "RegexpQueryBenchmarks.LeanCorpus_RegexpQuery|DocumentCount=20000, Pattern=mark.*",
          "displayInfo": "RegexpQueryBenchmarks.LeanCorpus_RegexpQuery: DefaultJob [Pattern=mark.*, DocumentCount=20000]",
          "typeName": "RegexpQueryBenchmarks",
          "methodName": "LeanCorpus_RegexpQuery",
          "parameters": {
            "Pattern": "mark.*",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 38890.18157145182,
            "medianNanoseconds": 38857.63720703125,
            "minNanoseconds": 38654.12756347656,
            "maxNanoseconds": 39229.85388183594,
            "standardDeviationNanoseconds": 179.15646193650835,
            "operationsPerSecond": 25713.430989328976
          },
          "gc": {
            "bytesAllocatedPerOperation": 13715,
            "gen0Collections": 54,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "RegexpQueryBenchmarks.LuceneNet_RegexpQuery|DocumentCount=20000, Pattern=.*nation.*",
          "displayInfo": "RegexpQueryBenchmarks.LuceneNet_RegexpQuery: DefaultJob [Pattern=.*nation.*, DocumentCount=20000]",
          "typeName": "RegexpQueryBenchmarks",
          "methodName": "LuceneNet_RegexpQuery",
          "parameters": {
            "Pattern": ".*nation.*",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 3647863.4364583334,
            "medianNanoseconds": 3645108.66015625,
            "minNanoseconds": 3641985.69921875,
            "maxNanoseconds": 3660756.1328125,
            "standardDeviationNanoseconds": 6001.731146927516,
            "operationsPerSecond": 274.1330692387125
          },
          "gc": {
            "bytesAllocatedPerOperation": 530928,
            "gen0Collections": 32,
            "gen1Collections": 1,
            "gen2Collections": 0
          }
        },
        {
          "key": "RegexpQueryBenchmarks.LuceneNet_RegexpQuery|DocumentCount=20000, Pattern=gov.*ment",
          "displayInfo": "RegexpQueryBenchmarks.LuceneNet_RegexpQuery: DefaultJob [Pattern=gov.*ment, DocumentCount=20000]",
          "typeName": "RegexpQueryBenchmarks",
          "methodName": "LuceneNet_RegexpQuery",
          "parameters": {
            "Pattern": "gov.*ment",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 254310.70432942707,
            "medianNanoseconds": 254234.40966796875,
            "minNanoseconds": 253700.294921875,
            "maxNanoseconds": 255225.677734375,
            "standardDeviationNanoseconds": 522.5479986798936,
            "operationsPerSecond": 3932.1978311405546
          },
          "gc": {
            "bytesAllocatedPerOperation": 326776,
            "gen0Collections": 159,
            "gen1Collections": 10,
            "gen2Collections": 0
          }
        },
        {
          "key": "RegexpQueryBenchmarks.LuceneNet_RegexpQuery|DocumentCount=20000, Pattern=mark.*",
          "displayInfo": "RegexpQueryBenchmarks.LuceneNet_RegexpQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [Pattern=mark.*, DocumentCount=20000]",
          "typeName": "RegexpQueryBenchmarks",
          "methodName": "LuceneNet_RegexpQuery",
          "parameters": {
            "Pattern": "mark.*",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 66325.58776855469,
            "medianNanoseconds": 66257.70483398438,
            "minNanoseconds": 66198.52880859375,
            "maxNanoseconds": 66520.52966308594,
            "standardDeviationNanoseconds": 171.3978038885934,
            "operationsPerSecond": 15077.13740117212
          },
          "gc": {
            "bytesAllocatedPerOperation": 113088,
            "gen0Collections": 221,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "schemajson",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.SchemaAndJsonBenchmarks-20260529-110333",
      "benchmarkCount": 3,
      "benchmarks": [
        {
          "key": "SchemaAndJsonBenchmarks.LeanCorpus_Index_NoSchema|DocumentCount=20000",
          "displayInfo": "SchemaAndJsonBenchmarks.LeanCorpus_Index_NoSchema: DefaultJob [DocumentCount=20000]",
          "typeName": "SchemaAndJsonBenchmarks",
          "methodName": "LeanCorpus_Index_NoSchema",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 983129937.4666667,
            "medianNanoseconds": 982873511,
            "minNanoseconds": 976199023,
            "maxNanoseconds": 993884261,
            "standardDeviationNanoseconds": 4877755.318385334,
            "operationsPerSecond": 1.0171595451327666
          },
          "gc": {
            "bytesAllocatedPerOperation": 140184904,
            "gen0Collections": 18,
            "gen1Collections": 9,
            "gen2Collections": 0
          }
        },
        {
          "key": "SchemaAndJsonBenchmarks.LeanCorpus_Index_WithSchema|DocumentCount=20000",
          "displayInfo": "SchemaAndJsonBenchmarks.LeanCorpus_Index_WithSchema: DefaultJob [DocumentCount=20000]",
          "typeName": "SchemaAndJsonBenchmarks",
          "methodName": "LeanCorpus_Index_WithSchema",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 986839569.7142857,
            "medianNanoseconds": 987869013,
            "minNanoseconds": 979601214,
            "maxNanoseconds": 991794762,
            "standardDeviationNanoseconds": 3774723.176559053,
            "operationsPerSecond": 1.013335936954296
          },
          "gc": {
            "bytesAllocatedPerOperation": 140985024,
            "gen0Collections": 18,
            "gen1Collections": 9,
            "gen2Collections": 0
          }
        },
        {
          "key": "SchemaAndJsonBenchmarks.LeanCorpus_JsonMapping|DocumentCount=20000",
          "displayInfo": "SchemaAndJsonBenchmarks.LeanCorpus_JsonMapping: DefaultJob [DocumentCount=20000]",
          "typeName": "SchemaAndJsonBenchmarks",
          "methodName": "LeanCorpus_JsonMapping",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 56454965.51851852,
            "medianNanoseconds": 56557977.666666664,
            "minNanoseconds": 56270072.88888889,
            "maxNanoseconds": 56697134.88888889,
            "standardDeviationNanoseconds": 155773.7060047178,
            "operationsPerSecond": 17.713233739766913
          },
          "gc": {
            "bytesAllocatedPerOperation": 29301936,
            "gen0Collections": 60,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "searcher-mgr",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.SearcherManagerBenchmarks-20260529-121025",
      "benchmarkCount": 3,
      "benchmarks": [
        {
          "key": "SearcherManagerBenchmarks.LeanCorpus_SearcherManager_AcquireLease|DocumentCount=20000",
          "displayInfo": "SearcherManagerBenchmarks.LeanCorpus_SearcherManager_AcquireLease: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DocumentCount=20000]",
          "typeName": "SearcherManagerBenchmarks",
          "methodName": "LeanCorpus_SearcherManager_AcquireLease",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 2641.4691988627114,
            "medianNanoseconds": 2639.2980880737305,
            "minNanoseconds": 2638.8086471557617,
            "maxNanoseconds": 2646.3008613586426,
            "standardDeviationNanoseconds": 4.191492569358209,
            "operationsPerSecond": 378577.1950059276
          },
          "gc": {
            "bytesAllocatedPerOperation": 593,
            "gen0Collections": 37,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "SearcherManagerBenchmarks.LeanCorpus_SearcherManager_AcquireSearch|DocumentCount=20000",
          "displayInfo": "SearcherManagerBenchmarks.LeanCorpus_SearcherManager_AcquireSearch: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DocumentCount=20000]",
          "typeName": "SearcherManagerBenchmarks",
          "methodName": "LeanCorpus_SearcherManager_AcquireSearch",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 2653.4379844665527,
            "medianNanoseconds": 2653.0421295166016,
            "minNanoseconds": 2652.6969985961914,
            "maxNanoseconds": 2654.5748252868652,
            "standardDeviationNanoseconds": 0.9995419581500262,
            "operationsPerSecond": 376869.5578544075
          },
          "gc": {
            "bytesAllocatedPerOperation": 529,
            "gen0Collections": 33,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "SearcherManagerBenchmarks.LuceneNet_SearcherManager_AcquireSearch|DocumentCount=20000",
          "displayInfo": "SearcherManagerBenchmarks.LuceneNet_SearcherManager_AcquireSearch: DefaultJob [DocumentCount=20000]",
          "typeName": "SearcherManagerBenchmarks",
          "methodName": "LuceneNet_SearcherManager_AcquireSearch",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 9277.353255208332,
            "medianNanoseconds": 9279.45915222168,
            "minNanoseconds": 9213.241958618164,
            "maxNanoseconds": 9326.97412109375,
            "standardDeviationNanoseconds": 35.13663938211592,
            "operationsPerSecond": 107789.36324738925
          },
          "gc": {
            "bytesAllocatedPerOperation": 12936,
            "gen0Collections": 202,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "similarity",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.SimilarityBenchmarks-20260529-124613",
      "benchmarkCount": 4,
      "benchmarks": [
        {
          "key": "SimilarityBenchmarks.LeanCorpus_Bm25_BooleanQuery|DocumentCount=20000",
          "displayInfo": "SimilarityBenchmarks.LeanCorpus_Bm25_BooleanQuery: DefaultJob [DocumentCount=20000]",
          "typeName": "SimilarityBenchmarks",
          "methodName": "LeanCorpus_Bm25_BooleanQuery",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 9918.275011189779,
            "medianNanoseconds": 9913.607131958008,
            "minNanoseconds": 9863.060531616211,
            "maxNanoseconds": 10003.709426879883,
            "standardDeviationNanoseconds": 46.44052057659028,
            "operationsPerSecond": 100823.98389556671
          },
          "gc": {
            "bytesAllocatedPerOperation": 5895,
            "gen0Collections": 93,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "SimilarityBenchmarks.LeanCorpus_Bm25_TermQuery|DocumentCount=20000",
          "displayInfo": "SimilarityBenchmarks.LeanCorpus_Bm25_TermQuery: DefaultJob [DocumentCount=20000]",
          "typeName": "SimilarityBenchmarks",
          "methodName": "LeanCorpus_Bm25_TermQuery",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 13,
            "meanNanoseconds": 2639.633399083064,
            "medianNanoseconds": 2639.5722274780273,
            "minNanoseconds": 2638.7255363464355,
            "maxNanoseconds": 2641.696434020996,
            "standardDeviationNanoseconds": 0.8371242809939623,
            "operationsPerSecond": 378840.4860869591
          },
          "gc": {
            "bytesAllocatedPerOperation": 528,
            "gen0Collections": 33,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "SimilarityBenchmarks.LeanCorpus_TfIdf_BooleanQuery|DocumentCount=20000",
          "displayInfo": "SimilarityBenchmarks.LeanCorpus_TfIdf_BooleanQuery: DefaultJob [DocumentCount=20000]",
          "typeName": "SimilarityBenchmarks",
          "methodName": "LeanCorpus_TfIdf_BooleanQuery",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 9995.182985578265,
            "medianNanoseconds": 9983.699806213379,
            "minNanoseconds": 9898.188705444336,
            "maxNanoseconds": 10093.846145629883,
            "standardDeviationNanoseconds": 52.46996584346748,
            "operationsPerSecond": 100048.1933590279
          },
          "gc": {
            "bytesAllocatedPerOperation": 5895,
            "gen0Collections": 93,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "SimilarityBenchmarks.LeanCorpus_TfIdf_TermQuery|DocumentCount=20000",
          "displayInfo": "SimilarityBenchmarks.LeanCorpus_TfIdf_TermQuery: DefaultJob [DocumentCount=20000]",
          "typeName": "SimilarityBenchmarks",
          "methodName": "LeanCorpus_TfIdf_TermQuery",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 13,
            "meanNanoseconds": 2636.1198786222017,
            "medianNanoseconds": 2636.886127471924,
            "minNanoseconds": 2625.078227996826,
            "maxNanoseconds": 2639.7774047851562,
            "standardDeviationNanoseconds": 3.8926653242842044,
            "operationsPerSecond": 379345.41904166416
          },
          "gc": {
            "bytesAllocatedPerOperation": 528,
            "gen0Collections": 33,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "span",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.SpanQueryBenchmarks-20260529-115219",
      "benchmarkCount": 6,
      "benchmarks": [
        {
          "key": "SpanQueryBenchmarks.LeanCorpus_SpanQuery|DocumentCount=20000, SpanType=Near",
          "displayInfo": "SpanQueryBenchmarks.LeanCorpus_SpanQuery: DefaultJob [SpanType=Near, DocumentCount=20000]",
          "typeName": "SpanQueryBenchmarks",
          "methodName": "LeanCorpus_SpanQuery",
          "parameters": {
            "SpanType": "Near",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 19650.093528238933,
            "medianNanoseconds": 19659.513763427734,
            "minNanoseconds": 19493.52813720703,
            "maxNanoseconds": 19769.725555419922,
            "standardDeviationNanoseconds": 77.13340822355072,
            "operationsPerSecond": 50890.34301861775
          },
          "gc": {
            "bytesAllocatedPerOperation": 10875,
            "gen0Collections": 87,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "SpanQueryBenchmarks.LeanCorpus_SpanQuery|DocumentCount=20000, SpanType=Not",
          "displayInfo": "SpanQueryBenchmarks.LeanCorpus_SpanQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [SpanType=Not, DocumentCount=20000]",
          "typeName": "SpanQueryBenchmarks",
          "methodName": "LeanCorpus_SpanQuery",
          "parameters": {
            "SpanType": "Not",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 21131.894856770832,
            "medianNanoseconds": 21108.880828857422,
            "minNanoseconds": 21075.013671875,
            "maxNanoseconds": 21211.790069580078,
            "standardDeviationNanoseconds": 71.23327770707934,
            "operationsPerSecond": 47321.833029071306
          },
          "gc": {
            "bytesAllocatedPerOperation": 11010,
            "gen0Collections": 88,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "SpanQueryBenchmarks.LeanCorpus_SpanQuery|DocumentCount=20000, SpanType=Or",
          "displayInfo": "SpanQueryBenchmarks.LeanCorpus_SpanQuery: DefaultJob [SpanType=Or, DocumentCount=20000]",
          "typeName": "SpanQueryBenchmarks",
          "methodName": "LeanCorpus_SpanQuery",
          "parameters": {
            "SpanType": "Or",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 13,
            "meanNanoseconds": 17446.86568274865,
            "medianNanoseconds": 17431.14273071289,
            "minNanoseconds": 17351.817779541016,
            "maxNanoseconds": 17553.912689208984,
            "standardDeviationNanoseconds": 54.82498317303161,
            "operationsPerSecond": 57316.88534685022
          },
          "gc": {
            "bytesAllocatedPerOperation": 4377,
            "gen0Collections": 34,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "SpanQueryBenchmarks.LuceneNet_SpanQuery|DocumentCount=20000, SpanType=Near",
          "displayInfo": "SpanQueryBenchmarks.LuceneNet_SpanQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [SpanType=Near, DocumentCount=20000]",
          "typeName": "SpanQueryBenchmarks",
          "methodName": "LuceneNet_SpanQuery",
          "parameters": {
            "SpanType": "Near",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 23911.024932861328,
            "medianNanoseconds": 23927.982452392578,
            "minNanoseconds": 23852.410095214844,
            "maxNanoseconds": 23952.682250976562,
            "standardDeviationNanoseconds": 52.24264930844473,
            "operationsPerSecond": 41821.71206829712
          },
          "gc": {
            "bytesAllocatedPerOperation": 32304,
            "gen0Collections": 253,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "SpanQueryBenchmarks.LuceneNet_SpanQuery|DocumentCount=20000, SpanType=Not",
          "displayInfo": "SpanQueryBenchmarks.LuceneNet_SpanQuery: DefaultJob [SpanType=Not, DocumentCount=20000]",
          "typeName": "SpanQueryBenchmarks",
          "methodName": "LuceneNet_SpanQuery",
          "parameters": {
            "SpanType": "Not",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 13,
            "meanNanoseconds": 30841.65932523287,
            "medianNanoseconds": 30820.875854492188,
            "minNanoseconds": 30780.869750976562,
            "maxNanoseconds": 30986.524475097656,
            "standardDeviationNanoseconds": 61.95104008743387,
            "operationsPerSecond": 32423.676996582915
          },
          "gc": {
            "bytesAllocatedPerOperation": 48552,
            "gen0Collections": 190,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "SpanQueryBenchmarks.LuceneNet_SpanQuery|DocumentCount=20000, SpanType=Or",
          "displayInfo": "SpanQueryBenchmarks.LuceneNet_SpanQuery: DefaultJob [SpanType=Or, DocumentCount=20000]",
          "typeName": "SpanQueryBenchmarks",
          "methodName": "LuceneNet_SpanQuery",
          "parameters": {
            "SpanType": "Or",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 95100.67701822917,
            "medianNanoseconds": 95075.20642089844,
            "minNanoseconds": 94886.775390625,
            "maxNanoseconds": 95358.97705078125,
            "standardDeviationNanoseconds": 159.98659141803637,
            "operationsPerSecond": 10515.172250648828
          },
          "gc": {
            "bytesAllocatedPerOperation": 46104,
            "gen0Collections": 90,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "stemmer",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.StemmerParityBenchmarks-20260529-124937",
      "benchmarkCount": 2,
      "benchmarks": [
        {
          "key": "StemmerParityBenchmarks.LeanCorpus_StemmedAnalyser|DocumentCount=20000",
          "displayInfo": "StemmerParityBenchmarks.LeanCorpus_StemmedAnalyser: DefaultJob [DocumentCount=20000]",
          "typeName": "StemmerParityBenchmarks",
          "methodName": "LeanCorpus_StemmedAnalyser",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 288936547.5,
            "medianNanoseconds": 288775020,
            "minNanoseconds": 287728355,
            "maxNanoseconds": 289835742,
            "standardDeviationNanoseconds": 551793.7482174775,
            "operationsPerSecond": 3.4609674984089716
          },
          "gc": {
            "bytesAllocatedPerOperation": 28770792,
            "gen0Collections": 13,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "StemmerParityBenchmarks.LuceneNet_EnglishAnalyzer|DocumentCount=20000",
          "displayInfo": "StemmerParityBenchmarks.LuceneNet_EnglishAnalyzer: DefaultJob [DocumentCount=20000]",
          "typeName": "StemmerParityBenchmarks",
          "methodName": "LuceneNet_EnglishAnalyzer",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 378471207.73333335,
            "medianNanoseconds": 378696718,
            "minNanoseconds": 376095681,
            "maxNanoseconds": 381263172,
            "standardDeviationNanoseconds": 1523764.3806250957,
            "operationsPerSecond": 2.642208917262179
          },
          "gc": {
            "bytesAllocatedPerOperation": 65661096,
            "gen0Collections": 15,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "suggester",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.SuggesterBenchmarks-20260529-105952",
      "benchmarkCount": 3,
      "benchmarks": [
        {
          "key": "SuggesterBenchmarks.LeanCorpus_DidYouMean|DocumentCount=20000",
          "displayInfo": "SuggesterBenchmarks.LeanCorpus_DidYouMean: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [DocumentCount=20000]",
          "typeName": "SuggesterBenchmarks",
          "methodName": "LeanCorpus_DidYouMean",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 1898918.0130208333,
            "medianNanoseconds": 1896723.42578125,
            "minNanoseconds": 1896241.76171875,
            "maxNanoseconds": 1903788.8515625,
            "standardDeviationNanoseconds": 4225.139186052347,
            "operationsPerSecond": 526.6156796359953
          },
          "gc": {
            "bytesAllocatedPerOperation": 12696,
            "gen0Collections": 1,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "SuggesterBenchmarks.LeanCorpus_SpellIndex|DocumentCount=20000",
          "displayInfo": "SuggesterBenchmarks.LeanCorpus_SpellIndex: DefaultJob [DocumentCount=20000]",
          "typeName": "SuggesterBenchmarks",
          "methodName": "LeanCorpus_SpellIndex",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 1894599.2298177083,
            "medianNanoseconds": 1893145.388671875,
            "minNanoseconds": 1891386.646484375,
            "maxNanoseconds": 1899909.158203125,
            "standardDeviationNanoseconds": 2875.0565647228013,
            "operationsPerSecond": 527.8161123797229
          },
          "gc": {
            "bytesAllocatedPerOperation": 10936,
            "gen0Collections": 1,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "SuggesterBenchmarks.LuceneNet_SpellChecker|DocumentCount=20000",
          "displayInfo": "SuggesterBenchmarks.LuceneNet_SpellChecker: DefaultJob [DocumentCount=20000]",
          "typeName": "SuggesterBenchmarks",
          "methodName": "LuceneNet_SpellChecker",
          "parameters": {
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 8189215.580357143,
            "medianNanoseconds": 8182116.5859375,
            "minNanoseconds": 8177337.234375,
            "maxNanoseconds": 8217994.640625,
            "standardDeviationNanoseconds": 15011.556279471757,
            "operationsPerSecond": 122.11181769333622
          },
          "gc": {
            "bytesAllocatedPerOperation": 5388552,
            "gen0Collections": 82,
            "gen1Collections": 6,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "synonym",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.SynonymBenchmarks-20260529-131130",
      "benchmarkCount": 6,
      "benchmarks": [
        {
          "key": "SynonymBenchmarks.LeanCorpus_NoSynonyms|DocumentCount=20000, SynonymCount=10",
          "displayInfo": "SynonymBenchmarks.LeanCorpus_NoSynonyms: DefaultJob [SynonymCount=10, DocumentCount=20000]",
          "typeName": "SynonymBenchmarks",
          "methodName": "LeanCorpus_NoSynonyms",
          "parameters": {
            "SynonymCount": "10",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 167995968.1111111,
            "medianNanoseconds": 168080600,
            "minNanoseconds": 167657478.66666666,
            "maxNanoseconds": 168635469.66666666,
            "standardDeviationNanoseconds": 290615.3698017526,
            "operationsPerSecond": 5.952523809015514
          },
          "gc": {
            "bytesAllocatedPerOperation": 14315256,
            "gen0Collections": 10,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "SynonymBenchmarks.LeanCorpus_NoSynonyms|DocumentCount=20000, SynonymCount=200",
          "displayInfo": "SynonymBenchmarks.LeanCorpus_NoSynonyms: DefaultJob [SynonymCount=200, DocumentCount=20000]",
          "typeName": "SynonymBenchmarks",
          "methodName": "LeanCorpus_NoSynonyms",
          "parameters": {
            "SynonymCount": "200",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 167058977.26666662,
            "medianNanoseconds": 167070793,
            "minNanoseconds": 166781324.33333334,
            "maxNanoseconds": 167348734.66666666,
            "standardDeviationNanoseconds": 190748.13140706692,
            "operationsPerSecond": 5.985909984374905
          },
          "gc": {
            "bytesAllocatedPerOperation": 14315256,
            "gen0Collections": 10,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "SynonymBenchmarks.LeanCorpus_NoSynonyms|DocumentCount=20000, SynonymCount=50",
          "displayInfo": "SynonymBenchmarks.LeanCorpus_NoSynonyms: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [SynonymCount=50, DocumentCount=20000]",
          "typeName": "SynonymBenchmarks",
          "methodName": "LeanCorpus_NoSynonyms",
          "parameters": {
            "SynonymCount": "50",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 169181645.33333334,
            "medianNanoseconds": 169115696.66666666,
            "minNanoseconds": 169079374,
            "maxNanoseconds": 169349865.33333334,
            "standardDeviationNanoseconds": 146810.45714950544,
            "operationsPerSecond": 5.910806683725832
          },
          "gc": {
            "bytesAllocatedPerOperation": 14315256,
            "gen0Collections": 10,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "SynonymBenchmarks.LeanCorpus_WithSynonyms|DocumentCount=20000, SynonymCount=10",
          "displayInfo": "SynonymBenchmarks.LeanCorpus_WithSynonyms: DefaultJob [SynonymCount=10, DocumentCount=20000]",
          "typeName": "SynonymBenchmarks",
          "methodName": "LeanCorpus_WithSynonyms",
          "parameters": {
            "SynonymCount": "10",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 284321357.28571427,
            "medianNanoseconds": 284383870.5,
            "minNanoseconds": 283659980.5,
            "maxNanoseconds": 285697941,
            "standardDeviationNanoseconds": 557928.7691399915,
            "operationsPerSecond": 3.517146969002054
          },
          "gc": {
            "bytesAllocatedPerOperation": 157617728,
            "gen0Collections": 66,
            "gen1Collections": 6,
            "gen2Collections": 0
          }
        },
        {
          "key": "SynonymBenchmarks.LeanCorpus_WithSynonyms|DocumentCount=20000, SynonymCount=200",
          "displayInfo": "SynonymBenchmarks.LeanCorpus_WithSynonyms: DefaultJob [SynonymCount=200, DocumentCount=20000]",
          "typeName": "SynonymBenchmarks",
          "methodName": "LeanCorpus_WithSynonyms",
          "parameters": {
            "SynonymCount": "200",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 13,
            "meanNanoseconds": 308485759.7692308,
            "medianNanoseconds": 308434505.5,
            "minNanoseconds": 307052196.5,
            "maxNanoseconds": 309568816,
            "standardDeviationNanoseconds": 624331.2465974299,
            "operationsPerSecond": 3.2416407186771634
          },
          "gc": {
            "bytesAllocatedPerOperation": 197458912,
            "gen0Collections": 85,
            "gen1Collections": 8,
            "gen2Collections": 0
          }
        },
        {
          "key": "SynonymBenchmarks.LeanCorpus_WithSynonyms|DocumentCount=20000, SynonymCount=50",
          "displayInfo": "SynonymBenchmarks.LeanCorpus_WithSynonyms: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [SynonymCount=50, DocumentCount=20000]",
          "typeName": "SynonymBenchmarks",
          "methodName": "LeanCorpus_WithSynonyms",
          "parameters": {
            "SynonymCount": "50",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 296256867.3333333,
            "medianNanoseconds": 296130108.5,
            "minNanoseconds": 295903205.5,
            "maxNanoseconds": 296737288,
            "standardDeviationNanoseconds": 431247.3252932513,
            "operationsPerSecond": 3.3754491803049085
          },
          "gc": {
            "bytesAllocatedPerOperation": 168670928,
            "gen0Collections": 71,
            "gen1Collections": 6,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "terminset",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.TermInSetQueryBenchmarks-20260529-121712",
      "benchmarkCount": 6,
      "benchmarks": [
        {
          "key": "TermInSetQueryBenchmarks.LeanCorpus_BooleanQuery_Should|DocumentCount=20000, SetSize=100",
          "displayInfo": "TermInSetQueryBenchmarks.LeanCorpus_BooleanQuery_Should: DefaultJob [SetSize=100, DocumentCount=20000]",
          "typeName": "TermInSetQueryBenchmarks",
          "methodName": "LeanCorpus_BooleanQuery_Should",
          "parameters": {
            "SetSize": "100",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 12,
            "meanNanoseconds": 1549732.5647786458,
            "medianNanoseconds": 1549727.6650390625,
            "minNanoseconds": 1541641.994140625,
            "maxNanoseconds": 1555045.833984375,
            "standardDeviationNanoseconds": 3807.988512554505,
            "operationsPerSecond": 645.2726249208255
          },
          "gc": {
            "bytesAllocatedPerOperation": 235936,
            "gen0Collections": 26,
            "gen1Collections": 8,
            "gen2Collections": 0
          }
        },
        {
          "key": "TermInSetQueryBenchmarks.LeanCorpus_BooleanQuery_Should|DocumentCount=20000, SetSize=20",
          "displayInfo": "TermInSetQueryBenchmarks.LeanCorpus_BooleanQuery_Should: DefaultJob [SetSize=20, DocumentCount=20000]",
          "typeName": "TermInSetQueryBenchmarks",
          "methodName": "LeanCorpus_BooleanQuery_Should",
          "parameters": {
            "SetSize": "20",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 407367.96522739955,
            "medianNanoseconds": 407353.97607421875,
            "minNanoseconds": 405134.76904296875,
            "maxNanoseconds": 408632.78271484375,
            "standardDeviationNanoseconds": 869.9267594601761,
            "operationsPerSecond": 2454.783108538698
          },
          "gc": {
            "bytesAllocatedPerOperation": 19080,
            "gen0Collections": 10,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "TermInSetQueryBenchmarks.LeanCorpus_BooleanQuery_Should|DocumentCount=20000, SetSize=5",
          "displayInfo": "TermInSetQueryBenchmarks.LeanCorpus_BooleanQuery_Should: DefaultJob [SetSize=5, DocumentCount=20000]",
          "typeName": "TermInSetQueryBenchmarks",
          "methodName": "LeanCorpus_BooleanQuery_Should",
          "parameters": {
            "SetSize": "5",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 114881.24180501302,
            "medianNanoseconds": 114851.10424804688,
            "minNanoseconds": 113901.73400878906,
            "maxNanoseconds": 115473.337890625,
            "standardDeviationNanoseconds": 425.53069271743067,
            "operationsPerSecond": 8704.6412824932
          },
          "gc": {
            "bytesAllocatedPerOperation": 8050,
            "gen0Collections": 16,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "TermInSetQueryBenchmarks.LeanCorpus_TermInSetQuery|DocumentCount=20000, SetSize=100",
          "displayInfo": "TermInSetQueryBenchmarks.LeanCorpus_TermInSetQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [SetSize=100, DocumentCount=20000]",
          "typeName": "TermInSetQueryBenchmarks",
          "methodName": "LeanCorpus_TermInSetQuery",
          "parameters": {
            "SetSize": "100",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 266023.4658203125,
            "medianNanoseconds": 266195.162109375,
            "minNanoseconds": 265440.71337890625,
            "maxNanoseconds": 266434.52197265625,
            "standardDeviationNanoseconds": 518.6748422771374,
            "operationsPerSecond": 3759.0668812482027
          },
          "gc": {
            "bytesAllocatedPerOperation": 22027,
            "gen0Collections": 10,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "TermInSetQueryBenchmarks.LeanCorpus_TermInSetQuery|DocumentCount=20000, SetSize=20",
          "displayInfo": "TermInSetQueryBenchmarks.LeanCorpus_TermInSetQuery: DefaultJob [SetSize=20, DocumentCount=20000]",
          "typeName": "TermInSetQueryBenchmarks",
          "methodName": "LeanCorpus_TermInSetQuery",
          "parameters": {
            "SetSize": "20",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 134002.58510742188,
            "medianNanoseconds": 133848.68701171875,
            "minNanoseconds": 132320.13427734375,
            "maxNanoseconds": 135367.80517578125,
            "standardDeviationNanoseconds": 746.2113251436997,
            "operationsPerSecond": 7462.54260093833
          },
          "gc": {
            "bytesAllocatedPerOperation": 7347,
            "gen0Collections": 7,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "TermInSetQueryBenchmarks.LeanCorpus_TermInSetQuery|DocumentCount=20000, SetSize=5",
          "displayInfo": "TermInSetQueryBenchmarks.LeanCorpus_TermInSetQuery: DefaultJob [SetSize=5, DocumentCount=20000]",
          "typeName": "TermInSetQueryBenchmarks",
          "methodName": "LeanCorpus_TermInSetQuery",
          "parameters": {
            "SetSize": "5",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 57164.20241001674,
            "medianNanoseconds": 57165.41439819336,
            "minNanoseconds": 57063.493225097656,
            "maxNanoseconds": 57285.67077636719,
            "standardDeviationNanoseconds": 78.2964237930539,
            "operationsPerSecond": 17493.465452861325
          },
          "gc": {
            "bytesAllocatedPerOperation": 4849,
            "gen0Collections": 19,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    },
    {
      "suiteName": "wildcard",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.WildcardQueryBenchmarks-20260529-104648",
      "benchmarkCount": 6,
      "benchmarks": [
        {
          "key": "WildcardQueryBenchmarks.LeanCorpus_WildcardQuery|DocumentCount=20000, WildcardPattern=gov*",
          "displayInfo": "WildcardQueryBenchmarks.LeanCorpus_WildcardQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [WildcardPattern=gov*, DocumentCount=20000]",
          "typeName": "WildcardQueryBenchmarks",
          "methodName": "LeanCorpus_WildcardQuery",
          "parameters": {
            "WildcardPattern": "gov*",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 10376.680221557617,
            "medianNanoseconds": 10383.853271484375,
            "minNanoseconds": 10340.66616821289,
            "maxNanoseconds": 10405.521224975586,
            "standardDeviationNanoseconds": 33.01717857209983,
            "operationsPerSecond": 96369.93514770686
          },
          "gc": {
            "bytesAllocatedPerOperation": 4552,
            "gen0Collections": 72,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "WildcardQueryBenchmarks.LeanCorpus_WildcardQuery|DocumentCount=20000, WildcardPattern=m*rket",
          "displayInfo": "WildcardQueryBenchmarks.LeanCorpus_WildcardQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [WildcardPattern=m*rket, DocumentCount=20000]",
          "typeName": "WildcardQueryBenchmarks",
          "methodName": "LeanCorpus_WildcardQuery",
          "parameters": {
            "WildcardPattern": "m*rket",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 240605.3289388021,
            "medianNanoseconds": 240306.931640625,
            "minNanoseconds": 240187.8544921875,
            "maxNanoseconds": 241321.20068359375,
            "standardDeviationNanoseconds": 622.8154687090715,
            "operationsPerSecond": 4156.183923317632
          },
          "gc": {
            "bytesAllocatedPerOperation": 4016,
            "gen0Collections": 1,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "WildcardQueryBenchmarks.LeanCorpus_WildcardQuery|DocumentCount=20000, WildcardPattern=pre*dent",
          "displayInfo": "WildcardQueryBenchmarks.LeanCorpus_WildcardQuery: DefaultJob [WildcardPattern=pre*dent, DocumentCount=20000]",
          "typeName": "WildcardQueryBenchmarks",
          "methodName": "LeanCorpus_WildcardQuery",
          "parameters": {
            "WildcardPattern": "pre*dent",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 22748.00551147461,
            "medianNanoseconds": 22734.967407226562,
            "minNanoseconds": 22633.004516601562,
            "maxNanoseconds": 22865.842895507812,
            "standardDeviationNanoseconds": 67.37147879302273,
            "operationsPerSecond": 43959.8979126138
          },
          "gc": {
            "bytesAllocatedPerOperation": 3889,
            "gen0Collections": 30,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "WildcardQueryBenchmarks.LuceneNet_WildcardQuery|DocumentCount=20000, WildcardPattern=gov*",
          "displayInfo": "WildcardQueryBenchmarks.LuceneNet_WildcardQuery: DefaultJob [WildcardPattern=gov*, DocumentCount=20000]",
          "typeName": "WildcardQueryBenchmarks",
          "methodName": "LuceneNet_WildcardQuery",
          "parameters": {
            "WildcardPattern": "gov*",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 28634.029770914713,
            "medianNanoseconds": 28628.585205078125,
            "minNanoseconds": 28556.32977294922,
            "maxNanoseconds": 28774.775268554688,
            "standardDeviationNanoseconds": 69.75181744277921,
            "operationsPerSecond": 34923.48118656213
          },
          "gc": {
            "bytesAllocatedPerOperation": 75336,
            "gen0Collections": 585,
            "gen1Collections": 2,
            "gen2Collections": 0
          }
        },
        {
          "key": "WildcardQueryBenchmarks.LuceneNet_WildcardQuery|DocumentCount=20000, WildcardPattern=m*rket",
          "displayInfo": "WildcardQueryBenchmarks.LuceneNet_WildcardQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [WildcardPattern=m*rket, DocumentCount=20000]",
          "typeName": "WildcardQueryBenchmarks",
          "methodName": "LuceneNet_WildcardQuery",
          "parameters": {
            "WildcardPattern": "m*rket",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 314823.7698567708,
            "medianNanoseconds": 314646.79833984375,
            "minNanoseconds": 314221.244140625,
            "maxNanoseconds": 315603.26708984375,
            "standardDeviationNanoseconds": 707.8036778616141,
            "operationsPerSecond": 3176.380234741965
          },
          "gc": {
            "bytesAllocatedPerOperation": 297496,
            "gen0Collections": 145,
            "gen1Collections": 19,
            "gen2Collections": 0
          }
        },
        {
          "key": "WildcardQueryBenchmarks.LuceneNet_WildcardQuery|DocumentCount=20000, WildcardPattern=pre*dent",
          "displayInfo": "WildcardQueryBenchmarks.LuceneNet_WildcardQuery: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [WildcardPattern=pre*dent, DocumentCount=20000]",
          "typeName": "WildcardQueryBenchmarks",
          "methodName": "LuceneNet_WildcardQuery",
          "parameters": {
            "WildcardPattern": "pre*dent",
            "DocumentCount": "20000"
          },
          "statistics": {
            "sampleCount": 3,
            "meanNanoseconds": 234102.06388346353,
            "medianNanoseconds": 234252.71435546875,
            "minNanoseconds": 233637.47436523438,
            "maxNanoseconds": 234416.0029296875,
            "standardDeviationNanoseconds": 410.54641023141943,
            "operationsPerSecond": 4271.641109912649
          },
          "gc": {
            "bytesAllocatedPerOperation": 306984,
            "gen0Collections": 300,
            "gen1Collections": 43,
            "gen2Collections": 0
          }
        }
      ]
    }
  ]
}</code></pre>

</details>

