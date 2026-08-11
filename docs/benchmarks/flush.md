---
title: Benchmarks - flush
---

# flush

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c4ff69e` &nbsp;&middot;&nbsp; 6 August 2026 20:14 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                         | DocsPerFlush | Mean       | Error     | StdDev   | Ratio | RatioSD | Gen0       | Gen1       | Gen2      | Allocated | Alloc Ratio |
|------------------------------- |------------- |-----------:|----------:|---------:|------:|--------:|-----------:|-----------:|----------:|----------:|------------:|
| **&#39;Flush text-only docs&#39;**         | **100**          |   **121.1 ms** |  **38.89 ms** |  **6.02 ms** |  **1.00** |    **0.00** |   **400.0000** |   **200.0000** |         **-** |   **5.88 MB** |        **1.00** |
| &#39;Flush mixed-field docs&#39;       | 100          |   125.5 ms |  23.74 ms |  6.16 ms |  1.04 |    0.07 |   500.0000 |   250.0000 |         - |   6.35 MB |        1.08 |
| &#39;Flush docs with vectors&#39;      | 100          |   127.9 ms |  35.88 ms |  9.32 ms |  1.06 |    0.09 |   600.0000 |   400.0000 |         - |   6.48 MB |        1.10 |
| &#39;Flush docs with term vectors&#39; | 100          |   147.2 ms |  29.21 ms |  4.52 ms |  1.22 |    0.07 |   500.0000 |   250.0000 |         - |   6.91 MB |        1.18 |
|                                |              |            |           |          |       |         |            |            |           |           |             |
| **&#39;Flush text-only docs&#39;**         | **1000**         |   **198.5 ms** |  **32.09 ms** |  **4.97 ms** |  **1.00** |    **0.00** |  **1333.3333** |   **666.6667** |         **-** |  **11.83 MB** |        **1.00** |
| &#39;Flush mixed-field docs&#39;       | 1000         |   198.5 ms |  33.24 ms |  5.14 ms |  1.00 |    0.03 |  1666.6667 |   666.6667 |         - |  14.07 MB |        1.19 |
| &#39;Flush docs with vectors&#39;      | 1000         |   308.4 ms |  25.61 ms |  3.96 ms |  1.55 |    0.04 |  2500.0000 |  1000.0000 |         - |  19.84 MB |        1.68 |
| &#39;Flush docs with term vectors&#39; | 1000         |   247.7 ms |  80.10 ms | 20.80 ms |  1.25 |    0.10 |  2500.0000 |  1000.0000 |         - |  17.84 MB |        1.51 |
|                                |              |            |           |          |       |         |            |            |           |           |             |
| **&#39;Flush text-only docs&#39;**         | **10000**        |   **510.7 ms** | **128.37 ms** | **33.34 ms** |  **1.00** |    **0.00** |  **8000.0000** |  **4000.0000** |         **-** |  **58.96 MB** |        **1.00** |
| &#39;Flush mixed-field docs&#39;       | 10000        |   571.3 ms |  36.48 ms |  9.47 ms |  1.12 |    0.07 | 10000.0000 |  5000.0000 |         - |  80.04 MB |        1.36 |
| &#39;Flush docs with vectors&#39;      | 10000        | 2,272.2 ms |  64.38 ms |  9.96 ms |  4.46 |    0.26 | 30000.0000 | 11000.0000 |         - | 172.48 MB |        2.93 |
| &#39;Flush docs with term vectors&#39; | 10000        |   856.2 ms | 127.53 ms | 19.73 ms |  1.68 |    0.11 | 21000.0000 | 10000.0000 | 1000.0000 | 130.43 MB |        2.21 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-flush"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-flush" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-flush" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-flush" style="max-width:960px"><canvas id="chart-bench-flush" style="height:500px"></canvas></div>
<p><a href="flush.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


