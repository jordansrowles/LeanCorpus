---
title: Benchmarks - Boolean queries
---

# Boolean queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                  | BooleanShape  | DocumentCount | Mean       | Error   | StdDev  | Ratio | RatioSD | Gen0     | Gen1   | Allocated | Alloc Ratio |
|------------------------ |-------------- |-------------- |-----------:|--------:|--------:|------:|--------:|---------:|-------:|----------:|------------:|
| **LeanCorpus_BooleanQuery** | **Must2Common**   | **100000**        |   **480.9 μs** | **6.29 μs** | **5.88 μs** |  **1.00** |    **0.00** |   **3.4180** |      **-** |  **14.09 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Must2Common   | 100000        |   564.7 μs | 2.08 μs | 1.95 μs |  1.17 |    0.01 |  28.3203 | 0.9766 |  117.5 KB |        8.34 |
|                         |               |               |            |         |         |       |         |          |        |           |             |
| **LeanCorpus_BooleanQuery** | **Must3Mixed**    | **100000**        |   **158.5 μs** | **0.52 μs** | **0.49 μs** |  **1.00** |    **0.00** |   **3.9063** |      **-** |   **16.2 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Must3Mixed    | 100000        |   272.4 μs | 5.32 μs | 7.10 μs |  1.72 |    0.04 |  40.0391 | 0.4883 | 166.58 KB |       10.28 |
|                         |               |               |            |         |         |       |         |          |        |           |             |
| **LeanCorpus_BooleanQuery** | **MustNotCommon** | **100000**        |   **394.8 μs** | **3.17 μs** | **2.96 μs** |  **1.00** |    **0.00** |   **3.4180** |      **-** |  **14.22 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | MustNotCommon | 100000        |   434.0 μs | 2.79 μs | 2.47 μs |  1.10 |    0.01 |  30.2734 | 0.4883 | 125.85 KB |        8.85 |
|                         |               |               |            |         |         |       |         |          |        |           |             |
| **LeanCorpus_BooleanQuery** | **Should2Common** | **100000**        |   **449.2 μs** | **8.78 μs** | **8.21 μs** |  **1.00** |    **0.00** |   **3.4180** |      **-** |  **14.79 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Should2Common | 100000        |   613.9 μs | 1.62 μs | 1.51 μs |  1.37 |    0.02 | 165.0391 | 5.8594 | 675.76 KB |       45.69 |
|                         |               |               |            |         |         |       |         |          |        |           |             |
| **LeanCorpus_BooleanQuery** | **Should4Mixed**  | **100000**        |   **847.0 μs** | **6.90 μs** | **6.46 μs** |  **1.00** |    **0.00** |   **4.8828** |      **-** |  **20.58 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Should4Mixed  | 100000        | 1,007.4 μs | 1.79 μs | 1.50 μs |  1.19 |    0.01 | 191.4063 | 5.8594 | 789.94 KB |       38.39 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-boolean"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-boolean" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-boolean" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-boolean" style="max-width:960px"><canvas id="chart-bench-boolean" style="height:500px"></canvas></div>
<p><a href="boolean.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


