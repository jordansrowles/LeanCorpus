---
title: Benchmarks - concurrent-write
---

# concurrent-write

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `cbcd9de` &nbsp;&middot;&nbsp; 16 June 2026 22:54 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                            | BatchSize | Mean     | Error     | StdDev   | Ratio | RatioSD | Gen0       | Gen1      | Allocated | Alloc Ratio |
|---------------------------------- |---------- |---------:|----------:|---------:|------:|--------:|-----------:|----------:|----------:|------------:|
| **Sequential_AddDocument**            | **100**       | **136.3 ms** |  **37.76 ms** |  **5.84 ms** |  **1.00** |    **0.00** |   **500.0000** |         **-** |   **4.22 MB** |        **1.00** |
| Concurrent_AddDocumentsConcurrent | 100       | 141.2 ms |  26.52 ms |  6.89 ms |  1.04 |    0.06 |   750.0000 |  500.0000 |   5.95 MB |        1.41 |
| Concurrent_AddDocumentLockFree    | 100       | 144.5 ms |  36.60 ms |  9.50 ms |  1.06 |    0.08 |   750.0000 |  500.0000 |   6.19 MB |        1.47 |
|                                   |           |          |           |          |       |         |            |           |           |             |
| **Sequential_AddDocument**            | **1000**      | **213.2 ms** |  **31.82 ms** |  **8.26 ms** |  **1.00** |    **0.00** |  **1333.3333** | **1000.0000** |  **11.12 MB** |        **1.00** |
| Concurrent_AddDocumentsConcurrent | 1000      | 233.9 ms |  12.96 ms |  2.00 ms |  1.10 |    0.04 |  2666.6667 | 1333.3333 |  19.54 MB |        1.76 |
| Concurrent_AddDocumentLockFree    | 1000      | 237.8 ms |  45.01 ms |  6.97 ms |  1.12 |    0.05 |  2666.6667 | 1333.3333 |  20.38 MB |        1.83 |
|                                   |           |          |           |          |       |         |            |           |           |             |
| **Sequential_AddDocument**            | **10000**     | **641.2 ms** | **222.97 ms** | **34.51 ms** |  **1.00** |    **0.00** |  **7000.0000** | **3000.0000** |  **73.89 MB** |        **1.00** |
| Concurrent_AddDocumentsConcurrent | 10000     | 722.2 ms | 144.19 ms | 37.45 ms |  1.13 |    0.07 | 16000.0000 | 8000.0000 | 133.05 MB |        1.80 |
| Concurrent_AddDocumentLockFree    | 10000     | 869.6 ms |  93.35 ms | 24.24 ms |  1.36 |    0.07 | 15000.0000 | 7000.0000 | 128.38 MB |        1.74 |

