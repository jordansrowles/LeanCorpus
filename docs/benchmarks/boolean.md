---
title: Benchmarks - Boolean queries
---

# Boolean queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `66ba120` &nbsp;&middot;&nbsp; 24 August 2026 21:47 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                  | BooleanShape  | DocumentCount | Mean       | Error   | StdDev  | Ratio | RatioSD | Gen0     | Gen1   | Allocated | Alloc Ratio |
|------------------------ |-------------- |-------------- |-----------:|--------:|--------:|------:|--------:|---------:|-------:|----------:|------------:|
| **LeanCorpus_BooleanQuery** | **Must2Common**   | **100000**        |   **506.0 μs** | **3.91 μs** | **3.66 μs** |  **1.00** |    **0.00** |   **3.9063** |      **-** |  **18.31 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Must2Common   | 100000        |   562.7 μs | 2.67 μs | 2.50 μs |  1.11 |    0.01 |  27.3438 | 0.9766 | 117.53 KB |        6.42 |
|                         |               |               |            |         |         |       |         |          |        |           |             |
| **LeanCorpus_BooleanQuery** | **Must3Mixed**    | **100000**        |   **174.3 μs** | **1.59 μs** | **1.33 μs** |  **1.00** |    **0.00** |   **5.3711** |      **-** |  **21.95 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Must3Mixed    | 100000        |   275.4 μs | 5.40 μs | 7.40 μs |  1.58 |    0.04 |  39.5508 | 0.9766 | 166.58 KB |        7.59 |
|                         |               |               |            |         |         |       |         |          |        |           |             |
| **LeanCorpus_BooleanQuery** | **MustNotCommon** | **100000**        |   **396.5 μs** | **3.15 μs** | **2.95 μs** |  **1.00** |    **0.00** |   **4.3945** |      **-** |  **18.47 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | MustNotCommon | 100000        |   427.7 μs | 3.69 μs | 3.45 μs |  1.08 |    0.01 |  30.2734 | 0.4883 | 125.85 KB |        6.81 |
|                         |               |               |            |         |         |       |         |          |        |           |             |
| **LeanCorpus_BooleanQuery** | **Should2Common** | **100000**        |   **482.4 μs** | **4.16 μs** | **3.89 μs** |  **1.00** |    **0.00** |   **4.3945** |      **-** |     **19 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Should2Common | 100000        |   619.2 μs | 0.77 μs | 0.64 μs |  1.28 |    0.01 | 164.0625 | 5.8594 | 675.76 KB |       35.56 |
|                         |               |               |            |         |         |       |         |          |        |           |             |
| **LeanCorpus_BooleanQuery** | **Should4Mixed**  | **100000**        |   **843.4 μs** | **5.88 μs** | **4.91 μs** |  **1.00** |    **0.00** |   **6.8359** |      **-** |  **28.19 KB** |        **1.00** |
| LuceneNet_BooleanQuery  | Should4Mixed  | 100000        | 1,026.1 μs | 2.44 μs | 2.16 μs |  1.22 |    0.01 | 191.4063 | 5.8594 | 789.83 KB |       28.01 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-boolean"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-boolean" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-boolean" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-boolean" style="max-width:960px"><canvas id="chart-bench-boolean" style="height:500px"></canvas></div>
<p><a href="boolean.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


