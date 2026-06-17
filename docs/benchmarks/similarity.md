---
title: Benchmarks - Similarity
---

# Similarity

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                   | DocumentCount | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|----------------------------------------- |-------------- |---------:|---------:|---------:|------:|--------:|--------:|-------:|----------:|------------:|
| LeanCorpus_Bm25_TermQuery                | 100000        | 120.1 μs |  0.49 μs |  0.46 μs |  1.00 |    0.00 |  0.1221 |      - |     720 B |        1.00 |
| LeanCorpus_TfIdf_TermQuery               | 100000        | 122.6 μs |  0.78 μs |  0.73 μs |  1.02 |    0.01 |       - |      - |     720 B |        1.00 |
| LeanCorpus_Bm25_BooleanQuery             | 100000        | 232.9 μs |  3.53 μs |  2.94 μs |  1.94 |    0.02 |  3.6621 |      - |   15998 B |       22.22 |
| LeanCorpus_TfIdf_BooleanQuery            | 100000        | 230.2 μs |  3.26 μs |  3.05 μs |  1.92 |    0.03 |  3.6621 |      - |   16001 B |       22.22 |
| LeanCorpus_Dirichlet_TermQuery           | 100000        | 215.8 μs |  1.99 μs |  1.86 μs |  1.80 |    0.02 |       - |      - |     960 B |        1.33 |
| LuceneNet_Dirichlet_TermQuery            | 100000        | 329.1 μs |  2.23 μs |  2.08 μs |  2.74 |    0.02 | 11.7188 | 0.4883 |   50975 B |       70.80 |
| LeanCorpus_JelinekMercer_TermQuery       | 100000        | 228.2 μs |  1.04 μs |  0.98 μs |  1.90 |    0.01 |       - |      - |     960 B |        1.33 |
| LuceneNet_JelinekMercer_TermQuery        | 100000        | 257.6 μs |  1.25 μs |  1.05 μs |  2.14 |    0.01 | 11.7188 | 0.4883 |   50975 B |       70.80 |
| LeanCorpus_AbsoluteDiscounting_TermQuery | 100000        | 233.7 μs |  1.30 μs |  1.09 μs |  1.95 |    0.01 |       - |      - |     960 B |        1.33 |
| LeanCorpus_Dirichlet_BooleanQuery        | 100000        | 886.5 μs | 17.26 μs | 17.72 μs |  7.38 |    0.15 |  4.8828 |      - |   20786 B |       28.87 |
| LeanCorpus_Bm25Plus_TermQuery            | 100000        | 122.3 μs |  0.74 μs |  0.69 μs |  1.02 |    0.01 |       - |      - |     720 B |        1.00 |
| LeanCorpus_Bm25L_TermQuery               | 100000        | 122.6 μs |  0.71 μs |  0.66 μs |  1.02 |    0.01 |       - |      - |     720 B |        1.00 |
| LeanCorpus_TfIdfAugmented_TermQuery      | 100000        | 121.6 μs |  0.59 μs |  0.55 μs |  1.01 |    0.01 |  0.1221 |      - |     720 B |        1.00 |
| LeanCorpus_TfIdfPivoted_TermQuery        | 100000        | 121.0 μs |  0.52 μs |  0.49 μs |  1.01 |    0.01 |  0.1221 |      - |     720 B |        1.00 |
| LeanCorpus_TfIdfDoubleNorm_TermQuery     | 100000        | 125.4 μs |  0.88 μs |  0.82 μs |  1.04 |    0.01 |       - |      - |     720 B |        1.00 |
| LeanCorpus_Bm25Plus_BooleanQuery         | 100000        | 229.2 μs |  4.39 μs |  4.69 μs |  1.91 |    0.04 |  3.6621 |      - |   15994 B |       22.21 |

