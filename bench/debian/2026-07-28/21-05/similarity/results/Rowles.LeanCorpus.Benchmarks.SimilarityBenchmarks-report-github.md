```

BenchmarkDotNet v0.16.0-nightly.20260427.506, Linux Debian GNU/Linux 13 (trixie)
Intel Xeon CPU E3-1220 V2 3.10GHz (Max: 3.26GHz), 1 CPU, 4 logical and 4 physical cores
Memory: 23.45 GB Total, 1 GB Available
.NET SDK 11.0.100-preview.1.26104.118
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2
  DefaultJob : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v2


```
| Method                                   | DocumentCount | Mean     | Error   | StdDev  | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|----------------------------------------- |-------------- |---------:|--------:|--------:|------:|--------:|--------:|-------:|----------:|------------:|
| LeanCorpus_TfIdf_TermQuery               | 100000        | 122.4 μs | 0.82 μs | 0.77 μs |  1.12 |    0.01 |  0.1221 |      - |     888 B |        1.00 |
| LuceneNet_TfIdf_TermQuery                | 100000        | 149.4 μs | 1.08 μs | 1.01 μs |  1.36 |    0.01 | 11.9629 | 0.2441 |   51119 B |       57.57 |
| LeanCorpus_TfIdf_BooleanQuery            | 100000        | 416.9 μs | 7.27 μs | 6.80 μs |  3.80 |    0.06 |  3.9063 |      - |   16592 B |       18.68 |
| LeanCorpus_Bm25_TermQuery                | 100000        | 109.7 μs | 0.39 μs | 0.37 μs |  1.00 |    0.00 |  0.1221 |      - |     888 B |        1.00 |
| LuceneNet_Bm25_TermQuery                 | 100000        | 147.7 μs | 0.89 μs | 0.84 μs |  1.35 |    0.01 | 12.2070 | 0.2441 |   52221 B |       58.81 |
| LeanCorpus_Bm25_BooleanQuery             | 100000        | 409.4 μs | 7.24 μs | 6.77 μs |  3.73 |    0.06 |  3.9063 |      - |   16577 B |       18.67 |
| LuceneNet_Bm25_BooleanQuery              | 100000        | 375.6 μs | 3.63 μs | 3.40 μs |  3.42 |    0.03 | 30.7617 | 0.9766 |  130718 B |      147.20 |
| LeanCorpus_Dirichlet_TermQuery           | 100000        | 163.6 μs | 1.04 μs | 0.97 μs |  1.49 |    0.01 |       - |      - |     888 B |        1.00 |
| LuceneNet_Dirichlet_TermQuery            | 100000        | 319.3 μs | 2.09 μs | 1.95 μs |  2.91 |    0.02 | 11.7188 | 0.4883 |   50879 B |       57.30 |
| LeanCorpus_JelinekMercer_TermQuery       | 100000        | 168.5 μs | 0.83 μs | 0.78 μs |  1.54 |    0.01 |       - |      - |     888 B |        1.00 |
| LuceneNet_JelinekMercer_TermQuery        | 100000        | 250.0 μs | 1.49 μs | 1.39 μs |  2.28 |    0.01 | 11.7188 | 0.4883 |   50879 B |       57.30 |
| LeanCorpus_AbsoluteDiscounting_TermQuery | 100000        | 182.3 μs | 0.32 μs | 0.28 μs |  1.66 |    0.01 |       - |      - |     888 B |        1.00 |
| LeanCorpus_Dirichlet_BooleanQuery        | 100000        | 418.4 μs | 6.27 μs | 5.86 μs |  3.81 |    0.05 |  3.9063 |      - |   16596 B |       18.69 |
| LeanCorpus_Bm25Plus_TermQuery            | 100000        | 120.6 μs | 0.54 μs | 0.51 μs |  1.10 |    0.01 |  0.1221 |      - |     888 B |        1.00 |
| LeanCorpus_Bm25L_TermQuery               | 100000        | 123.3 μs | 0.77 μs | 0.72 μs |  1.12 |    0.01 |  0.1221 |      - |     888 B |        1.00 |
| LeanCorpus_TfIdfAugmented_TermQuery      | 100000        | 121.1 μs | 1.02 μs | 0.96 μs |  1.10 |    0.01 |       - |      - |     888 B |        1.00 |
| LeanCorpus_TfIdfPivoted_TermQuery        | 100000        | 118.4 μs | 0.72 μs | 0.68 μs |  1.08 |    0.01 |  0.1221 |      - |     888 B |        1.00 |
| LeanCorpus_TfIdfDoubleNorm_TermQuery     | 100000        | 121.4 μs | 0.75 μs | 0.70 μs |  1.11 |    0.01 |  0.1221 |      - |     888 B |        1.00 |
| LeanCorpus_Bm25Plus_BooleanQuery         | 100000        | 412.7 μs | 5.88 μs | 5.50 μs |  3.76 |    0.05 |  3.9063 |      - |   16596 B |       18.69 |
