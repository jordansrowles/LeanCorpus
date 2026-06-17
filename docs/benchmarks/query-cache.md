---
title: Benchmarks - Query cache
---

# Query cache

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                            | DocumentCount | Mean         | Error       | StdDev      | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------------------------- |-------------- |-------------:|------------:|------------:|------:|--------:|-------:|----------:|------------:|
| LeanCorpus_NoCache                | 100000        | 121,429.0 ns |   697.95 ns |   618.72 ns | 1.000 |    0.00 |      - |     720 B |        1.00 |
| LeanCorpus_WithCache              | 100000        |     261.3 ns |     2.96 ns |     2.63 ns | 0.002 |    0.00 | 0.1183 |     496 B |        0.69 |
| LeanCorpus_WithCache_BooleanQuery | 100000        |     745.0 ns |     7.24 ns |     6.77 ns | 0.006 |    0.00 | 0.2518 |    1056 B |        1.47 |
| LeanCorpus_NoCache_BooleanQuery   | 100000        | 227,765.3 ns | 3,744.13 ns | 3,502.26 ns | 1.876 |    0.03 | 3.6621 |   15997 B |       22.22 |

