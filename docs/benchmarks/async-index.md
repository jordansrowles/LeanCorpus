---
title: Benchmarks - Async index
---

# Async index

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `50d0c68` &nbsp;&middot;&nbsp; 16 June 2026 23:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                 | DocumentCount | Mean    | Error   | StdDev  | Ratio | Gen0        | Gen1       | Gen2      | Allocated | Alloc Ratio |
|--------------------------------------- |-------------- |--------:|--------:|--------:|------:|------------:|-----------:|----------:|----------:|------------:|
| LeanCorpus_AddDocument_Sync            | 100000        | 10.74 s | 0.236 s | 0.061 s |  1.00 | 153000.0000 | 61000.0000 | 6000.0000 |    1.2 GB |        1.00 |
| LeanCorpus_AddDocumentAsync_Sequential | 100000        | 10.80 s | 0.302 s | 0.078 s |  1.01 | 153000.0000 | 61000.0000 | 6000.0000 |    1.2 GB |        1.00 |
| LeanCorpus_AddDocumentsAsync_Batch     | 100000        | 10.94 s | 0.431 s | 0.112 s |  1.02 | 153000.0000 | 61000.0000 | 6000.0000 |    1.2 GB |        1.00 |

