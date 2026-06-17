---
title: Benchmarks - Deletion
---

# Deletion

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c89728e` &nbsp;&middot;&nbsp; 15 May 2026 21:48 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                     | DocumentCount | Mean     | Error    | StdDev   | Ratio | Gen0        | Gen1       | Gen2      | Allocated | Alloc Ratio |
|--------------------------- |-------------- |---------:|---------:|---------:|------:|------------:|-----------:|----------:|----------:|------------:|
| LeanCorpus_DeleteDocuments | 100000        | 10.273 s | 0.0472 s | 0.0442 s |  1.00 | 180000.0000 | 77000.0000 | 6000.0000 |   1.09 GB |        1.00 |
| LuceneNet_DeleteDocuments  | 100000        |  6.684 s | 0.0312 s | 0.0292 s |  0.65 | 338000.0000 | 32000.0000 | 1000.0000 |   1.91 GB |        1.75 |

