---
title: Benchmarks - packed-int-codec
---

# packed-int-codec

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c4ff69e` &nbsp;&middot;&nbsp; 6 August 2026 20:14 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method | BitsPerValue | Mean      | Error    | StdDev   | Allocated |
|------- |------------- |----------:|---------:|---------:|----------:|
| **Pack**   | **3**            | **219.95 ns** | **0.278 ns** | **0.260 ns** |         **-** |
| Unpack | 3            |  13.12 ns | 0.011 ns | 0.010 ns |         - |
| **Pack**   | **5**            | **248.46 ns** | **0.300 ns** | **0.266 ns** |         **-** |
| Unpack | 5            |  13.13 ns | 0.022 ns | 0.021 ns |         - |
| **Pack**   | **7**            | **275.67 ns** | **0.281 ns** | **0.249 ns** |         **-** |
| Unpack | 7            |  12.87 ns | 0.011 ns | 0.009 ns |         - |
| **Pack**   | **9**            | **267.25 ns** | **0.290 ns** | **0.257 ns** |         **-** |
| Unpack | 9            |  12.94 ns | 0.010 ns | 0.009 ns |         - |
| **Pack**   | **11**           | **330.27 ns** | **0.451 ns** | **0.400 ns** |         **-** |
| Unpack | 11           |  13.12 ns | 0.017 ns | 0.016 ns |         - |



