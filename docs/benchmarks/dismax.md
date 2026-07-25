---
title: Benchmarks - Disjunction max
---

# Disjunction max

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                         | TieBreakerMultiplier | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------------- |--------------------- |-------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_DisjunctionMaxQuery** | **0**                    | **100000**        | **716.8 μs** | **0.31 μs** | **0.25 μs** |  **1.00** |       **-** |      **-** |   **3.38 KB** |        **1.00** |
| LuceneNet_DisjunctionMaxQuery  | 0                    | 100000        | 940.7 μs | 2.13 μs | 2.00 μs |  1.31 | 39.0625 | 0.9766 | 162.64 KB |       48.19 |
|                                |                      |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_DisjunctionMaxQuery** | **0.1**                  | **100000**        | **723.1 μs** | **0.26 μs** | **0.20 μs** |  **1.00** |       **-** |      **-** |   **3.38 KB** |        **1.00** |
| LuceneNet_DisjunctionMaxQuery  | 0.1                  | 100000        | 929.3 μs | 1.99 μs | 1.86 μs |  1.29 | 39.0625 | 0.9766 | 162.64 KB |       48.19 |
|                                |                      |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_DisjunctionMaxQuery** | **0.5**                  | **100000**        | **715.8 μs** | **0.85 μs** | **0.75 μs** |  **1.00** |       **-** |      **-** |   **3.38 KB** |        **1.00** |
| LuceneNet_DisjunctionMaxQuery  | 0.5                  | 100000        | 960.5 μs | 2.64 μs | 2.47 μs |  1.34 | 39.0625 | 0.9766 | 162.64 KB |       48.19 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-dismax"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-dismax" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-dismax" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-dismax" style="max-width:960px"><canvas id="chart-bench-dismax" style="height:500px"></canvas></div>
<p><a href="dismax.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


