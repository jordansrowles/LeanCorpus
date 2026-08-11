---
title: Benchmarks - Disjunction max
---

# Disjunction max

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                         | TieBreakerMultiplier | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------------- |--------------------- |-------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_DisjunctionMaxQuery** | **0**                    | **100000**        | **681.4 μs** | **0.39 μs** | **0.30 μs** |  **1.00** |       **-** |      **-** |   **3.15 KB** |        **1.00** |
| LuceneNet_DisjunctionMaxQuery  | 0                    | 100000        | 933.7 μs | 2.38 μs | 2.23 μs |  1.37 | 39.0625 | 0.9766 | 162.64 KB |       51.66 |
|                                |                      |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_DisjunctionMaxQuery** | **0.1**                  | **100000**        | **677.9 μs** | **0.64 μs** | **0.54 μs** |  **1.00** |       **-** |      **-** |   **3.15 KB** |        **1.00** |
| LuceneNet_DisjunctionMaxQuery  | 0.1                  | 100000        | 958.1 μs | 1.89 μs | 1.77 μs |  1.41 | 39.0625 | 0.9766 | 162.64 KB |       51.66 |
|                                |                      |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_DisjunctionMaxQuery** | **0.5**                  | **100000**        | **682.0 μs** | **0.22 μs** | **0.17 μs** |  **1.00** |       **-** |      **-** |   **3.15 KB** |        **1.00** |
| LuceneNet_DisjunctionMaxQuery  | 0.5                  | 100000        | 933.2 μs | 2.83 μs | 2.65 μs |  1.37 | 39.0625 | 0.9766 | 162.64 KB |       51.66 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-dismax"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-dismax" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-dismax" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-dismax" style="max-width:960px"><canvas id="chart-bench-dismax" style="height:500px"></canvas></div>
<p><a href="dismax.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


