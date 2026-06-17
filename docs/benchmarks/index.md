---
title: Benchmarks - Indexing
---

# Indexing

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                    | DocumentCount | Mean    | Error   | StdDev  | Ratio | Gen0        | Gen1       | Gen2       | Allocated | Alloc Ratio |
|-------------------------- |-------------- |--------:|--------:|--------:|------:|------------:|-----------:|-----------:|----------:|------------:|
| LeanCorpus_IndexDocuments | 100000        | 11.09 s | 0.523 s | 0.081 s |  1.00 | 162000.0000 | 67000.0000 | 10000.0000 |   1.23 GB |        1.00 |
| LuceneNet_IndexDocuments  | 100000        | 11.57 s | 0.277 s | 0.043 s |  1.04 | 369000.0000 | 19000.0000 |  2000.0000 |   1.82 GB |        1.48 |

