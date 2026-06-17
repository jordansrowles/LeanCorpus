---
title: Benchmarks - Gutenberg index
---

# Gutenberg index

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                    | Mean       | Error     | StdDev   | Ratio | RatioSD | Gen0       | Gen1      | Gen2      | Allocated | Alloc Ratio |
|-------------------------- |-----------:|----------:|---------:|------:|--------:|-----------:|----------:|----------:|----------:|------------:|
| LeanCorpus_Standard_Index |   940.9 ms |  82.27 ms | 12.73 ms |  1.00 |    0.00 | 18000.0000 | 9000.0000 | 1000.0000 | 153.26 MB |        1.00 |
| LeanCorpus_English_Index  |   965.8 ms | 106.80 ms | 27.74 ms |  1.03 |    0.03 | 16000.0000 | 8000.0000 | 1000.0000 |  138.2 MB |        0.90 |
| LuceneNet_Index           | 1,433.1 ms | 151.78 ms | 39.42 ms |  1.52 |    0.04 | 42000.0000 | 3000.0000 |         - | 208.13 MB |        1.36 |

