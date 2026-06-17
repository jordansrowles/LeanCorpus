---
title: Benchmarks - Stemmer
---

# Stemmer

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                     | DocumentCount | Mean    | Error    | StdDev   | Ratio | Gen0        | Allocated | Alloc Ratio |
|--------------------------- |-------------- |--------:|---------:|---------:|------:|------------:|----------:|------------:|
| LeanCorpus_StemmedAnalyser | 100000        | 2.112 s | 0.0044 s | 0.0041 s |  1.00 |           - |   2.29 MB |        1.00 |
| LuceneNet_EnglishAnalyzer  | 100000        | 3.685 s | 0.0069 s | 0.0061 s |  1.74 | 143000.0000 |  573.3 MB |      250.48 |

