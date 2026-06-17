---
title: Benchmarks - index-writer
---

# index-writer

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c1bfdfd` &nbsp;&middot;&nbsp; 16 June 2026 17:56 UTC &nbsp;&middot;&nbsp; 20,000 docs

| Method                | Job        | IterationCount | LaunchCount | WarmupCount | WriterCount | Mean       | Error     | StdDev    | Gen0       | Gen1      | Gen2      | Allocated |
|---------------------- |----------- |--------------- |------------ |------------ |------------ |-----------:|----------:|----------:|-----------:|----------:|----------:|----------:|
| **&#39;Concurrent indexing&#39;** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **1**           | **1,236.5 ms** | **124.32 ms** | **366.55 ms** | **18000.0000** | **7000.0000** | **2000.0000** | **122.33 MB** |
| &#39;Concurrent indexing&#39; | ShortRun   | 3              | 1           | 3           | 1           |   699.7 ms | 379.26 ms |  20.79 ms | 15000.0000 | 9000.0000 | 3000.0000 |  99.53 MB |
| **&#39;Concurrent indexing&#39;** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **2**           | **1,236.2 ms** | **127.73 ms** | **376.63 ms** | **17000.0000** | **7000.0000** | **2000.0000** | **112.68 MB** |
| &#39;Concurrent indexing&#39; | ShortRun   | 3              | 1           | 3           | 2           |   685.5 ms |  14.00 ms |   0.77 ms | 13000.0000 | 8000.0000 | 2000.0000 |  90.62 MB |
| **&#39;Concurrent indexing&#39;** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **4**           | **1,209.7 ms** | **129.41 ms** | **381.57 ms** | **19000.0000** | **8000.0000** | **3000.0000** |  **111.4 MB** |
| &#39;Concurrent indexing&#39; | ShortRun   | 3              | 1           | 3           | 4           |   655.5 ms | 363.35 ms |  19.92 ms | 13000.0000 | 8000.0000 | 2000.0000 |  89.11 MB |
| **&#39;Concurrent indexing&#39;** | **DefaultJob** | **Default**        | **Default**     | **Default**     | **8**           | **1,202.4 ms** | **126.17 ms** | **372.03 ms** | **17000.0000** | **8000.0000** | **2000.0000** | **108.11 MB** |
| &#39;Concurrent indexing&#39; | ShortRun   | 3              | 1           | 3           | 8           |   668.0 ms | 373.47 ms |  20.47 ms | 13000.0000 | 9000.0000 | 3000.0000 |  85.99 MB |

