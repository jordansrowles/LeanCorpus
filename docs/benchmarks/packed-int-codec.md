---
title: Benchmarks - packed-int-codec
---

# packed-int-codec

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c1bfdfd` &nbsp;&middot;&nbsp; 16 June 2026 17:44 UTC &nbsp;&middot;&nbsp; 20,000 docs

| Method | Job        | IterationCount | LaunchCount | WarmupCount | BitsPerValue | Mean      | Error     | StdDev   | Allocated |
|------- |----------- |--------------- |------------ |------------ |------------- |----------:|----------:|---------:|----------:|
| **Pack**   | **DefaultJob** | **Default**        | **Default**     | **Default**     | **3**            | **221.98 ns** |  **1.199 ns** | **1.122 ns** |         **-** |
| Unpack | DefaultJob | Default        | Default     | Default     | 3            |  14.38 ns |  0.057 ns | 0.053 ns |         - |
| Pack   | ShortRun   | 3              | 1           | 3           | 3            | 210.22 ns | 21.759 ns | 1.193 ns |         - |
| Unpack | ShortRun   | 3              | 1           | 3           | 3            |  21.78 ns |  2.648 ns | 0.145 ns |         - |
| **Pack**   | **DefaultJob** | **Default**        | **Default**     | **Default**     | **5**            | **249.35 ns** |  **0.982 ns** | **0.820 ns** |         **-** |
| Unpack | DefaultJob | Default        | Default     | Default     | 5            |  16.29 ns |  0.075 ns | 0.070 ns |         - |
| Pack   | ShortRun   | 3              | 1           | 3           | 5            | 250.11 ns | 21.900 ns | 1.200 ns |         - |
| Unpack | ShortRun   | 3              | 1           | 3           | 5            |  13.01 ns |  0.981 ns | 0.054 ns |         - |
| **Pack**   | **DefaultJob** | **Default**        | **Default**     | **Default**     | **7**            | **246.77 ns** |  **1.352 ns** | **1.265 ns** |         **-** |
| Unpack | DefaultJob | Default        | Default     | Default     | 7            |  15.06 ns |  0.061 ns | 0.051 ns |         - |
| Pack   | ShortRun   | 3              | 1           | 3           | 7            | 246.52 ns | 25.010 ns | 1.371 ns |         - |
| Unpack | ShortRun   | 3              | 1           | 3           | 7            |  19.13 ns |  0.579 ns | 0.032 ns |         - |
| **Pack**   | **DefaultJob** | **Default**        | **Default**     | **Default**     | **9**            | **305.49 ns** |  **1.197 ns** | **1.120 ns** |         **-** |
| Unpack | DefaultJob | Default        | Default     | Default     | 9            |  13.67 ns |  0.042 ns | 0.039 ns |         - |
| Pack   | ShortRun   | 3              | 1           | 3           | 9            | 271.78 ns | 30.673 ns | 1.681 ns |         - |
| Unpack | ShortRun   | 3              | 1           | 3           | 9            |  14.77 ns |  1.139 ns | 0.062 ns |         - |
| **Pack**   | **DefaultJob** | **Default**        | **Default**     | **Default**     | **11**           | **296.35 ns** |  **1.302 ns** | **1.155 ns** |         **-** |
| Unpack | DefaultJob | Default        | Default     | Default     | 11           |  13.09 ns |  0.046 ns | 0.043 ns |         - |
| Pack   | ShortRun   | 3              | 1           | 3           | 11           | 298.63 ns | 31.349 ns | 1.718 ns |         - |
| Unpack | ShortRun   | 3              | 1           | 3           | 11           |  14.43 ns |  0.992 ns | 0.054 ns |         - |

