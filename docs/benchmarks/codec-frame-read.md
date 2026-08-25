---
title: Benchmarks - codec-frame-read
---

# codec-frame-read

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `bf6ca39` &nbsp;&middot;&nbsp; 14 August 2026 08:30 UTC &nbsp;&middot;&nbsp; 500 docs

| Method           | BodyMiB | Mean      | Error | Ratio | Allocated | Alloc Ratio |
|----------------- |-------- |----------:|------:|------:|----------:|------------:|
| **OpenFrame**        | **1**       |  **5.914 ms** |    **NA** |  **1.00** |   **1.97 KB** |        **1.00** |
| ValidateChecksum | 1       |  7.494 ms |    NA |  1.27 |   2.09 KB |        1.06 |
|                  |         |           |       |       |           |             |
| **OpenFrame**        | **16**      |  **6.087 ms** |    **NA** |  **1.00** |   **1.97 KB** |        **1.00** |
| ValidateChecksum | 16      | 22.161 ms |    NA |  3.64 |   2.09 KB |        1.06 |



