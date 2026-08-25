---
title: Benchmarks - bkd
---

# bkd

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c4ff69e` &nbsp;&middot;&nbsp; 7 August 2026 08:49 UTC &nbsp;&middot;&nbsp; 20,000 docs

| Method      | PointCount | RangeWidth | Mean     | Error     | StdDev    | Median    | Ratio | Allocated | Alloc Ratio |
|------------ |----------- |----------- |---------:|----------:|----------:|----------:|------:|----------:|------------:|
| **&#39;BKD range&#39;** | **10000**      | **0.01**       | **5.116 ms** | **43.362 ms** | **11.261 ms** | **0.0700 ms** |  **1.00** |   **1.94 KB** |        **1.00** |
|             |            |            |          |           |           |           |       |           |             |
| **&#39;BKD range&#39;** | **10000**      | **0.1**        | **6.495 ms** | **53.358 ms** | **13.857 ms** | **0.2879 ms** |  **1.00** |   **2.12 KB** |        **1.00** |
|             |            |            |          |           |           |           |       |           |             |
| **&#39;BKD range&#39;** | **10000**      | **0.5**        | **6.009 ms** | **43.764 ms** | **11.365 ms** | **0.9428 ms** |  **1.00** |   **2.12 KB** |        **1.00** |
|             |            |            |          |           |           |           |       |           |             |
| **&#39;BKD range&#39;** | **100000**     | **0.01**       | **5.949 ms** | **48.962 ms** | **12.715 ms** | **0.2600 ms** |  **1.00** |   **3.02 KB** |        **1.00** |
|             |            |            |          |           |           |           |       |           |             |
| **&#39;BKD range&#39;** | **100000**     | **0.1**        | **6.844 ms** | **48.561 ms** | **12.611 ms** | **1.2093 ms** |  **1.00** |   **3.02 KB** |        **1.00** |
|             |            |            |          |           |           |           |       |           |             |
| **&#39;BKD range&#39;** | **100000**     | **0.5**        | **9.997 ms** | **48.566 ms** | **12.612 ms** | **4.3561 ms** |  **1.00** |   **3.02 KB** |        **1.00** |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-bkd"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-bkd" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-bkd" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-bkd" style="max-width:960px"><canvas id="chart-bench-bkd" style="height:500px"></canvas></div>
<p><a href="bkd.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


