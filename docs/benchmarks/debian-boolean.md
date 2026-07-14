---
title: Benchmarks - Boolean queries
---

# Boolean queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                  | BooleanShape  | DocumentCount | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------ |-------------- |-------------- |----------:|---------:|---------:|------:|--------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_BooleanQuery** | **Must2Common**   | **100000**        | **231.62 μs** | **1.210 μs** | **1.132 μs** |  **1.00** |    **0.00** |  **3.1738** |      **-** |   **13.8 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Must2Common   | 100000        | 447.75 μs | 0.445 μs | 0.395 μs |  1.93 |    0.01 | 12.2070 | 0.4883 |  51.32 KB |        3.72 |
|                         |               |               |           |          |          |       |         |         |        |           |             |
| **LeanCorpus_BooleanQuery** | **Must3Mixed**    | **100000**        |  **31.29 μs** | **0.625 μs** | **0.855 μs** |  **1.00** |    **0.00** |  **3.9673** |      **-** |  **15.88 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Must3Mixed    | 100000        | 128.67 μs | 0.279 μs | 0.261 μs |  4.11 |    0.11 | 17.3340 | 0.4883 |  73.16 KB |        4.61 |
|                         |               |               |           |          |          |       |         |         |        |           |             |
| **LeanCorpus_BooleanQuery** | **MustNotCommon** | **100000**        |  **78.75 μs** | **1.568 μs** | **4.266 μs** |  **1.00** |    **0.00** |  **3.2959** |      **-** |  **13.73 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | MustNotCommon | 100000        | 288.51 μs | 0.477 μs | 0.423 μs |  3.67 |    0.20 | 13.1836 | 0.4883 |  54.31 KB |        3.95 |
|                         |               |               |           |          |          |       |         |         |        |           |             |
| **LeanCorpus_BooleanQuery** | **Should2Common** | **100000**        |  **83.25 μs** | **1.662 μs** | **3.918 μs** |  **1.00** |    **0.00** |  **3.4180** |      **-** |  **13.95 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Should2Common | 100000        | 274.06 μs | 0.388 μs | 0.324 μs |  3.30 |    0.15 | 65.9180 | 2.4414 | 273.42 KB |       19.60 |
|                         |               |               |           |          |          |       |         |         |        |           |             |
| **LeanCorpus_BooleanQuery** | **Should4Mixed**  | **100000**        | **346.57 μs** | **1.390 μs** | **1.300 μs** |  **1.00** |    **0.00** |  **4.3945** |      **-** |  **19.44 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Should4Mixed  | 100000        | 502.28 μs | 1.508 μs | 1.410 μs |  1.45 |    0.01 | 78.1250 | 3.9063 | 321.28 KB |       16.52 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-boolean"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-boolean" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-boolean" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-boolean" style="max-width:960px"><canvas id="chart-bench-boolean" style="height:500px"></canvas></div>
<p><a href="debian-boolean.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


