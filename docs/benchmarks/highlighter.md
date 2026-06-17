---
title: Benchmarks - Highlighter
---

# Highlighter

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                         | MaxSnippetLength | DocumentCount | Mean    | Error    | StdDev   | Ratio | RatioSD | Gen0         | Allocated  | Alloc Ratio |
|------------------------------- |----------------- |-------------- |--------:|---------:|---------:|------:|--------:|-------------:|-----------:|------------:|
| **LeanCorpus_Highlight_TwoTerms**  | **100**              | **100000**        | **1.160 s** | **0.0042 s** | **0.0039 s** |  **1.00** |    **0.00** |    **9000.0000** |   **37.57 MB** |        **1.00** |
| LeanCorpus_Highlight_FiveTerms | 100              | 100000        | 1.174 s | 0.0051 s | 0.0048 s |  1.01 |    0.01 |    7000.0000 |   31.56 MB |        0.84 |
| LuceneNet_Highlight_TwoTerms   | 100              | 100000        | 4.343 s | 0.0120 s | 0.0112 s |  3.74 |    0.02 | 1054000.0000 | 4205.61 MB |      111.94 |
| LuceneNet_Highlight_FiveTerms  | 100              | 100000        | 4.616 s | 0.0189 s | 0.0177 s |  3.98 |    0.02 | 1108000.0000 | 4419.97 MB |      117.64 |
|                                |                  |               |         |          |          |       |         |              |            |             |
| **LeanCorpus_Highlight_TwoTerms**  | **200**              | **100000**        | **1.230 s** | **0.0051 s** | **0.0048 s** |  **1.00** |    **0.00** |   **14000.0000** |   **59.81 MB** |        **1.00** |
| LeanCorpus_Highlight_FiveTerms | 200              | 100000        | 1.193 s | 0.0056 s | 0.0052 s |  0.97 |    0.01 |   12000.0000 |   50.06 MB |        0.84 |
| LuceneNet_Highlight_TwoTerms   | 200              | 100000        | 4.322 s | 0.0149 s | 0.0139 s |  3.52 |    0.02 | 1054000.0000 | 4205.61 MB |       70.32 |
| LuceneNet_Highlight_FiveTerms  | 200              | 100000        | 4.458 s | 0.0147 s | 0.0138 s |  3.63 |    0.02 | 1108000.0000 | 4419.97 MB |       73.90 |
|                                |                  |               |         |          |          |       |         |              |            |             |
| **LeanCorpus_Highlight_TwoTerms**  | **500**              | **100000**        | **1.301 s** | **0.0042 s** | **0.0040 s** |  **1.00** |    **0.00** |   **22000.0000** |   **90.16 MB** |        **1.00** |
| LeanCorpus_Highlight_FiveTerms | 500              | 100000        | 1.195 s | 0.0049 s | 0.0046 s |  0.92 |    0.00 |   20000.0000 |   80.05 MB |        0.89 |
| LuceneNet_Highlight_TwoTerms   | 500              | 100000        | 4.307 s | 0.0145 s | 0.0135 s |  3.31 |    0.01 | 1054000.0000 | 4205.61 MB |       46.64 |
| LuceneNet_Highlight_FiveTerms  | 500              | 100000        | 4.477 s | 0.0273 s | 0.0255 s |  3.44 |    0.02 | 1108000.0000 | 4419.97 MB |       49.02 |

