---
title: Benchmarks - Analysis
---

# Analysis

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method             | DocumentCount | Mean       | Error   | StdDev  | Ratio | Gen0        | Allocated   | Alloc Ratio |
|------------------- |-------------- |-----------:|--------:|--------:|------:|------------:|------------:|------------:|
| LeanCorpus_Analyse | 100000        |   878.4 ms | 3.30 ms | 2.93 ms |  1.00 |           - |           - |          NA |
| LuceneNet_Analyse  | 100000        | 2,212.4 ms | 4.08 ms | 3.82 ms |  2.52 | 144000.0000 | 605066968 B |          NA |

