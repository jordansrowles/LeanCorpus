---
title: Benchmarks - simd-cosine
---

# simd-cosine

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c1bfdfd` &nbsp;&middot;&nbsp; 16 June 2026 17:32 UTC &nbsp;&middot;&nbsp; 20,000 docs

| Method                   | Job        | IterationCount | LaunchCount | WarmupCount | Dimension | Mean      | Error     | StdDev   | Ratio | Allocated | Alloc Ratio |
|------------------------- |----------- |--------------- |------------ |------------ |---------- |----------:|----------:|---------:|------:|----------:|------------:|
| **System.Numerics.Vector**   | **DefaultJob** | **Default**        | **Default**     | **Default**     | **64**        |  **26.47 μs** |  **0.097 μs** | **0.091 μs** |  **1.00** |         **-** |          **NA** |
| Runtime.Intrinsics       | DefaultJob | Default        | Default     | Default     | 64        |  35.76 μs |  0.176 μs | 0.164 μs |  1.35 |         - |          NA |
| &#39;Numerics dot product&#39;   | DefaultJob | Default        | Default     | Default     | 64        |  12.78 μs |  0.062 μs | 0.058 μs |  0.48 |         - |          NA |
| &#39;Intrinsics dot product&#39; | DefaultJob | Default        | Default     | Default     | 64        |  17.42 μs |  0.098 μs | 0.087 μs |  0.66 |         - |          NA |
|                          |            |                |             |             |           |           |           |          |       |           |             |
| System.Numerics.Vector   | ShortRun   | 3              | 1           | 3           | 64        |  25.71 μs |  2.132 μs | 0.117 μs |  1.00 |         - |          NA |
| Runtime.Intrinsics       | ShortRun   | 3              | 1           | 3           | 64        |  35.76 μs |  2.897 μs | 0.159 μs |  1.39 |         - |          NA |
| &#39;Numerics dot product&#39;   | ShortRun   | 3              | 1           | 3           | 64        |  12.84 μs |  1.667 μs | 0.091 μs |  0.50 |         - |          NA |
| &#39;Intrinsics dot product&#39; | ShortRun   | 3              | 1           | 3           | 64        |  13.76 μs |  2.236 μs | 0.123 μs |  0.54 |         - |          NA |
|                          |            |                |             |             |           |           |           |          |       |           |             |
| **System.Numerics.Vector**   | **DefaultJob** | **Default**        | **Default**     | **Default**     | **128**       |  **41.14 μs** |  **0.233 μs** | **0.218 μs** |  **1.00** |         **-** |          **NA** |
| Runtime.Intrinsics       | DefaultJob | Default        | Default     | Default     | 128       |  55.52 μs |  0.276 μs | 0.258 μs |  1.35 |         - |          NA |
| &#39;Numerics dot product&#39;   | DefaultJob | Default        | Default     | Default     | 128       |  30.50 μs |  0.128 μs | 0.120 μs |  0.74 |         - |          NA |
| &#39;Intrinsics dot product&#39; | DefaultJob | Default        | Default     | Default     | 128       |  26.11 μs |  0.143 μs | 0.134 μs |  0.63 |         - |          NA |
|                          |            |                |             |             |           |           |           |          |       |           |             |
| System.Numerics.Vector   | ShortRun   | 3              | 1           | 3           | 128       |  41.14 μs |  0.775 μs | 0.042 μs |  1.00 |         - |          NA |
| Runtime.Intrinsics       | ShortRun   | 3              | 1           | 3           | 128       |  50.71 μs |  3.944 μs | 0.216 μs |  1.23 |         - |          NA |
| &#39;Numerics dot product&#39;   | ShortRun   | 3              | 1           | 3           | 128       |  30.59 μs |  4.011 μs | 0.220 μs |  0.74 |         - |          NA |
| &#39;Intrinsics dot product&#39; | ShortRun   | 3              | 1           | 3           | 128       |  26.35 μs |  2.391 μs | 0.131 μs |  0.64 |         - |          NA |
|                          |            |                |             |             |           |           |           |          |       |           |             |
| **System.Numerics.Vector**   | **DefaultJob** | **Default**        | **Default**     | **Default**     | **256**       |  **79.77 μs** |  **0.395 μs** | **0.370 μs** |  **1.00** |         **-** |          **NA** |
| Runtime.Intrinsics       | DefaultJob | Default        | Default     | Default     | 256       |  92.47 μs |  0.388 μs | 0.363 μs |  1.16 |         - |          NA |
| &#39;Numerics dot product&#39;   | DefaultJob | Default        | Default     | Default     | 256       |  55.19 μs |  0.234 μs | 0.219 μs |  0.69 |         - |          NA |
| &#39;Intrinsics dot product&#39; | DefaultJob | Default        | Default     | Default     | 256       |  58.26 μs |  0.227 μs | 0.213 μs |  0.73 |         - |          NA |
|                          |            |                |             |             |           |           |           |          |       |           |             |
| System.Numerics.Vector   | ShortRun   | 3              | 1           | 3           | 256       |  70.38 μs |  6.436 μs | 0.353 μs |  1.00 |         - |          NA |
| Runtime.Intrinsics       | ShortRun   | 3              | 1           | 3           | 256       |  81.28 μs |  9.014 μs | 0.494 μs |  1.15 |         - |          NA |
| &#39;Numerics dot product&#39;   | ShortRun   | 3              | 1           | 3           | 256       |  55.16 μs |  3.474 μs | 0.190 μs |  0.78 |         - |          NA |
| &#39;Intrinsics dot product&#39; | ShortRun   | 3              | 1           | 3           | 256       |  57.28 μs |  3.987 μs | 0.219 μs |  0.81 |         - |          NA |
|                          |            |                |             |             |           |           |           |          |       |           |             |
| **System.Numerics.Vector**   | **DefaultJob** | **Default**        | **Default**     | **Default**     | **512**       | **125.04 μs** |  **0.767 μs** | **0.717 μs** |  **1.00** |         **-** |          **NA** |
| Runtime.Intrinsics       | DefaultJob | Default        | Default     | Default     | 512       | 177.74 μs |  0.834 μs | 0.780 μs |  1.42 |         - |          NA |
| &#39;Numerics dot product&#39;   | DefaultJob | Default        | Default     | Default     | 512       | 113.73 μs |  0.466 μs | 0.436 μs |  0.91 |         - |          NA |
| &#39;Intrinsics dot product&#39; | DefaultJob | Default        | Default     | Default     | 512       | 116.58 μs |  0.479 μs | 0.448 μs |  0.93 |         - |          NA |
|                          |            |                |             |             |           |           |           |          |       |           |             |
| System.Numerics.Vector   | ShortRun   | 3              | 1           | 3           | 512       | 142.54 μs | 16.688 μs | 0.915 μs |  1.00 |         - |          NA |
| Runtime.Intrinsics       | ShortRun   | 3              | 1           | 3           | 512       | 177.30 μs | 21.729 μs | 1.191 μs |  1.24 |         - |          NA |
| &#39;Numerics dot product&#39;   | ShortRun   | 3              | 1           | 3           | 512       | 105.58 μs | 10.389 μs | 0.569 μs |  0.74 |         - |          NA |
| &#39;Intrinsics dot product&#39; | ShortRun   | 3              | 1           | 3           | 512       | 126.84 μs |  2.428 μs | 0.133 μs |  0.89 |         - |          NA |
|                          |            |                |             |             |           |           |           |          |       |           |             |
| **System.Numerics.Vector**   | **DefaultJob** | **Default**        | **Default**     | **Default**     | **1024**      | **270.89 μs** |  **1.887 μs** | **1.765 μs** |  **1.00** |         **-** |          **NA** |
| Runtime.Intrinsics       | DefaultJob | Default        | Default     | Default     | 1024      | 293.39 μs |  1.749 μs | 1.636 μs |  1.08 |         - |          NA |
| &#39;Numerics dot product&#39;   | DefaultJob | Default        | Default     | Default     | 1024      | 228.44 μs |  1.025 μs | 0.959 μs |  0.84 |         - |          NA |
| &#39;Intrinsics dot product&#39; | DefaultJob | Default        | Default     | Default     | 1024      | 233.45 μs |  1.048 μs | 0.981 μs |  0.86 |         - |          NA |
|                          |            |                |             |             |           |           |           |          |       |           |             |
| System.Numerics.Vector   | ShortRun   | 3              | 1           | 3           | 1024      | 255.31 μs | 20.933 μs | 1.147 μs |  1.00 |         - |          NA |
| Runtime.Intrinsics       | ShortRun   | 3              | 1           | 3           | 1024      | 247.84 μs | 29.668 μs | 1.626 μs |  0.97 |         - |          NA |
| &#39;Numerics dot product&#39;   | ShortRun   | 3              | 1           | 3           | 1024      | 215.67 μs | 13.130 μs | 0.720 μs |  0.84 |         - |          NA |
| &#39;Intrinsics dot product&#39; | ShortRun   | 3              | 1           | 3           | 1024      | 220.40 μs | 25.824 μs | 1.416 μs |  0.86 |         - |          NA |

