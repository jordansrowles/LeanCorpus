---
title: Benchmarks - Light English stemmer
---

# Light English stemmer

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method            | DocumentCount | Mean    | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|------------------ |-------------- |--------:|---------:|---------:|------:|----------:|------------:|
| LightEnglish_Stem | 100000        | 1.007 s | 0.0062 s | 0.0058 s |  1.00 |         - |          NA |
| Porter_Stem       | 100000        | 1.021 s | 0.0038 s | 0.0035 s |  1.01 |  404360 B |          NA |

