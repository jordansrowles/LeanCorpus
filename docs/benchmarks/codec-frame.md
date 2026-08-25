---
title: Benchmarks - codec-frame
---

# codec-frame

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 11 August 2026 11:26 UTC &nbsp;&middot;&nbsp; 500 docs

| Method                     | BodyMiB | Mean     | Error | Ratio | Allocated | Alloc Ratio |
|--------------------------- |-------- |---------:|------:|------:|----------:|------------:|
| **&#39;Legacy trailer&#39;**           | **1**       | **16.26 ms** |    **NA** |  **1.00** |  **64.73 KB** |        **1.00** |
| &#39;Canonical xxHash64 frame&#39; | 1       | 22.92 ms |    NA |  1.41 |  65.32 KB |        1.01 |
|                            |         |          |       |       |           |             |
| **&#39;Legacy trailer&#39;**           | **16**      | **21.64 ms** |    **NA** |  **1.00** |  **64.76 KB** |        **1.00** |
| &#39;Canonical xxHash64 frame&#39; | 16      | 43.99 ms |    NA |  2.03 |  65.32 KB |        1.01 |



