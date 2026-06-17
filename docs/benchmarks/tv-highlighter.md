---
title: Benchmarks - Term-vector highlighter
---

# Term-vector highlighter

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                 | DocumentCount | Mean       | Error    | StdDev   | Gen0      | Gen1    | Allocated  |
|--------------------------------------- |-------------- |-----------:|---------:|---------:|----------:|--------:|-----------:|
| LeanCorpus_HybridHighlighter_NoOffsets | 100000        | 1,535.0 μs |  9.76 μs |  9.13 μs |   15.6250 |       - |   65.13 KB |
| LeanCorpus_Highlighter                 | 100000        | 1,430.6 μs |  8.39 μs |  7.85 μs |    9.7656 |       - |   44.77 KB |
| LuceneNet_Highlighter                  | 100000        |   130.5 μs |  1.12 μs |  0.99 μs |   56.3965 |       - |  230.47 KB |
| LeanCorpus_TermVectorHighlighter       | 100000        | 1,936.7 μs | 15.34 μs | 13.60 μs |   23.4375 |       - |   101.8 KB |
| LuceneNet_FastVectorHighlighter        | 100000        | 7,967.5 μs | 97.36 μs | 91.08 μs | 1109.3750 | 15.6250 | 4561.99 KB |

