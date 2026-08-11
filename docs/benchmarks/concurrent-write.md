---
title: Benchmarks - concurrent-write
---

# concurrent-write

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c4ff69e` &nbsp;&middot;&nbsp; 6 August 2026 20:14 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                            | BatchSize | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0      | Gen1      | Allocated | Alloc Ratio |
|---------------------------------- |---------- |---------:|---------:|---------:|------:|--------:|----------:|----------:|----------:|------------:|
| **Sequential_AddDocument**            | **100**       | **123.8 ms** | **32.25 ms** |  **8.38 ms** |  **1.00** |    **0.00** |  **600.0000** |  **400.0000** |   **6.22 MB** |        **1.00** |
| Concurrent_AddDocumentsConcurrent | 100       | 122.1 ms | 48.18 ms |  7.46 ms |  0.99 |    0.08 |  500.0000 |  250.0000 |   6.22 MB |        1.00 |
| Concurrent_AddDocumentLockFree    | 100       | 125.3 ms | 17.58 ms |  2.72 ms |  1.02 |    0.07 |  600.0000 |  400.0000 |   6.22 MB |        1.00 |
|                                   |           |          |          |          |       |         |           |           |           |             |
| **Sequential_AddDocument**            | **1000**      | **190.0 ms** | **41.57 ms** | **10.80 ms** |  **1.00** |    **0.00** | **1666.6667** |  **666.6667** |  **12.75 MB** |        **1.00** |
| Concurrent_AddDocumentsConcurrent | 1000      | 194.7 ms | 40.89 ms | 10.62 ms |  1.03 |    0.07 | 1333.3333 |  666.6667 |  12.75 MB |        1.00 |
| Concurrent_AddDocumentLockFree    | 1000      | 171.6 ms | 15.44 ms |  2.39 ms |  0.91 |    0.05 | 1500.0000 |  500.0000 |  12.74 MB |        1.00 |
|                                   |           |          |          |          |       |         |           |           |           |             |
| **Sequential_AddDocument**            | **10000**     | **505.0 ms** | **66.91 ms** | **17.38 ms** |  **1.00** |    **0.00** | **8000.0000** | **4000.0000** |  **64.38 MB** |        **1.00** |
| Concurrent_AddDocumentsConcurrent | 10000     | 495.0 ms | 12.40 ms |  3.22 ms |  0.98 |    0.03 | 8000.0000 | 4000.0000 |  64.38 MB |        1.00 |
| Concurrent_AddDocumentLockFree    | 10000     | 504.2 ms | 52.17 ms |  8.07 ms |  1.00 |    0.03 | 8000.0000 | 4000.0000 |  64.38 MB |        1.00 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-concurrent-write"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-concurrent-write" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-concurrent-write" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-concurrent-write" style="max-width:960px"><canvas id="chart-bench-concurrent-write" style="height:500px"></canvas></div>
<p><a href="concurrent-write.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


