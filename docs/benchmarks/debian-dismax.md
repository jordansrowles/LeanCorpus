---
title: Benchmarks - Disjunction max
---

# Disjunction max

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `f3305a5` &nbsp;&middot;&nbsp; 16 July 2026 20:51 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                         | TieBreakerMultiplier | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------------- |--------------------- |-------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_DisjunctionMaxQuery** | **0**                    | **100000**        | **372.0 μs** | **2.47 μs** | **2.31 μs** |  **1.00** |  **1.9531** |      **-** |   **9.47 KB** |        **1.00** |
| LuceneNet_DisjunctionMaxQuery  | 0                    | 100000        | 908.7 μs | 1.38 μs | 1.15 μs |  2.44 | 23.4375 | 0.9766 |  96.47 KB |       10.18 |
|                                |                      |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_DisjunctionMaxQuery** | **0.1**                  | **100000**        | **369.6 μs** | **2.29 μs** | **2.14 μs** |  **1.00** |  **1.9531** |      **-** |   **9.48 KB** |        **1.00** |
| LuceneNet_DisjunctionMaxQuery  | 0.1                  | 100000        | 922.6 μs | 0.99 μs | 0.93 μs |  2.50 | 23.4375 | 0.9766 |  96.47 KB |       10.18 |
|                                |                      |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_DisjunctionMaxQuery** | **0.5**                  | **100000**        | **360.8 μs** | **1.80 μs** | **1.69 μs** |  **1.00** |  **1.9531** |      **-** |   **9.48 KB** |        **1.00** |
| LuceneNet_DisjunctionMaxQuery  | 0.5                  | 100000        | 989.9 μs | 1.05 μs | 0.93 μs |  2.74 | 23.4375 | 1.9531 |  96.42 KB |       10.17 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-dismax"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-dismax" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-dismax" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-dismax" style="max-width:960px"><canvas id="chart-bench-dismax" style="height:500px"></canvas></div>
<p><a href="debian-dismax.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


