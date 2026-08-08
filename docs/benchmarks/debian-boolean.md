---
title: Benchmarks - Boolean queries
---

# Boolean queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                  | BooleanShape  | DocumentCount | Mean       | Error   | StdDev  | Ratio | RatioSD | Gen0     | Gen1   | Allocated | Alloc Ratio |
|------------------------ |-------------- |-------------- |-----------:|--------:|--------:|------:|--------:|---------:|-------:|----------:|------------:|
| **LeanCorpus_BooleanQuery** | **Must2Common**   | **100000**        |   **469.5 μs** | **3.97 μs** | **3.71 μs** |  **1.00** |    **0.00** |   **3.4180** |      **-** |  **14.24 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Must2Common   | 100000        |   563.9 μs | 2.78 μs | 2.60 μs |  1.20 |    0.01 |  28.3203 | 0.9766 | 117.53 KB |        8.26 |
|                         |               |               |            |         |         |       |         |          |        |           |             |
| **LeanCorpus_BooleanQuery** | **Must3Mixed**    | **100000**        |   **156.0 μs** | **0.82 μs** | **0.72 μs** |  **1.00** |    **0.00** |   **3.9063** |      **-** |  **16.41 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Must3Mixed    | 100000        |   279.6 μs | 2.38 μs | 2.23 μs |  1.79 |    0.02 |  40.0391 | 0.9766 | 166.58 KB |       10.15 |
|                         |               |               |            |         |         |       |         |          |        |           |             |
| **LeanCorpus_BooleanQuery** | **MustNotCommon** | **100000**        |   **390.4 μs** | **2.60 μs** | **2.43 μs** |  **1.00** |    **0.00** |   **3.4180** |      **-** |  **14.36 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | MustNotCommon | 100000        |   404.6 μs | 2.64 μs | 2.47 μs |  1.04 |    0.01 |  30.2734 | 0.4883 | 125.85 KB |        8.76 |
|                         |               |               |            |         |         |       |         |          |        |           |             |
| **LeanCorpus_BooleanQuery** | **Should2Common** | **100000**        |   **443.4 μs** | **8.80 μs** | **9.04 μs** |  **1.00** |    **0.00** |   **3.4180** |      **-** |  **14.92 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Should2Common | 100000        |   618.9 μs | 1.58 μs | 1.48 μs |  1.40 |    0.03 | 164.0625 | 5.8594 | 675.76 KB |       45.29 |
|                         |               |               |            |         |         |       |         |          |        |           |             |
| **LeanCorpus_BooleanQuery** | **Should4Mixed**  | **100000**        |   **830.7 μs** | **9.12 μs** | **8.53 μs** |  **1.00** |    **0.00** |   **4.8828** |      **-** |  **20.88 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Should4Mixed  | 100000        | 1,002.0 μs | 1.75 μs | 1.55 μs |  1.21 |    0.01 | 191.4063 | 5.8594 | 789.94 KB |       37.84 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-boolean"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-boolean" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-boolean" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-boolean" style="max-width:960px"><canvas id="chart-bench-boolean" style="height:500px"></canvas></div>
<p><a href="debian-boolean.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


