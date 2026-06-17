---
title: Benchmarks - Block-Join (index)
---

# Block-Join (index)

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                 | BlockCount | Mean    | Error   | StdDev  | Ratio | Gen0         | Gen1        | Gen2       | Allocated | Alloc Ratio |
|----------------------- |----------- |--------:|--------:|--------:|------:|-------------:|------------:|-----------:|----------:|------------:|
| LeanLucene_IndexBlocks | 100000     | 34.17 s | 3.831 s | 0.210 s |  1.00 |  512000.0000 | 224000.0000 | 30000.0000 |   3.87 GB |        1.00 |
| LuceneNet_IndexBlocks  | 100000     | 34.96 s | 8.615 s | 0.472 s |  1.02 | 1287000.0000 |  47000.0000 |  4000.0000 |   6.27 GB |        1.62 |

