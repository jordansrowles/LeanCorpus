---
title: Benchmarks - Collapse and facet
---

# Collapse and facet

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                 | DocumentCount | Mean       | Error    | StdDev   | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|--------------------------------------- |-------------- |-----------:|---------:|---------:|------:|--------:|--------:|-------:|----------:|------------:|
| LeanCorpus_BaseSearch                  | 100000        |   120.1 μs |  0.56 μs |  0.52 μs |  1.00 |    0.00 |  0.1221 |      - |     720 B |        1.00 |
| LeanCorpus_SearchWithCollapse          | 100000        | 1,048.2 μs | 11.07 μs | 10.36 μs |  8.73 |    0.09 |  9.7656 |      - |   42216 B |       58.63 |
| LeanCorpus_SearchWithFacets            | 100000        | 1,413.6 μs | 27.06 μs | 34.22 μs | 11.77 |    0.28 | 83.9844 | 5.8594 |  458472 B |      636.77 |
| LeanCorpus_SearchWithCollapseAndFacets | 100000        | 1,067.9 μs |  5.60 μs |  4.37 μs |  8.89 |    0.05 |  9.7656 |      - |   42216 B |       58.63 |

