---
title: Benchmarks - KStemmer
---

# KStemmer

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                   | DocumentCount | Mean    | Error    | StdDev   | Ratio | RatioSD | Gen0        | Allocated | Alloc Ratio |
|------------------------- |-------------- |--------:|---------:|---------:|------:|--------:|------------:|----------:|------------:|
| LeanCorpus_KStem_Analyse | 100000        | 2.077 s | 0.0153 s | 0.0143 s |  1.00 |    0.00 |           - |   2.29 MB |        1.00 |
| LuceneNet_KStem_Analyse  | 100000        | 3.159 s | 0.0329 s | 0.0308 s |  1.52 |    0.02 | 146000.0000 | 582.78 MB |      254.62 |

