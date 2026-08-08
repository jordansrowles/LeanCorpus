---
title: Benchmarks - Wildcard queries
---

# Wildcard queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                   | WildcardPattern | DocumentCount | Mean       | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------- |---------------- |-------------- |-----------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_WildcardQuery** | **gov***            | **100000**        |   **240.2 μs** | **0.28 μs** | **0.26 μs** |  **1.00** |  **2.9297** |      **-** |  **12.37 KB** |        **1.00** |
| LuceneNet_WildcardQuery  | gov*            | 100000        |   267.4 μs | 0.54 μs | 0.45 μs |  1.11 | 28.8086 | 0.9766 | 119.67 KB |        9.68 |
|                          |                 |               |            |         |         |       |         |        |           |             |
| **LeanCorpus_WildcardQuery** | **m*rket**          | **100000**        | **2,440.1 μs** | **2.22 μs** | **1.85 μs** |  **1.00** |       **-** |      **-** |    **3.2 KB** |        **1.00** |
| LuceneNet_WildcardQuery  | m*rket          | 100000        | 1,374.1 μs | 2.14 μs | 1.90 μs |  0.56 | 95.7031 | 3.9063 | 396.38 KB |      123.75 |
|                          |                 |               |            |         |         |       |         |        |           |             |
| **LeanCorpus_WildcardQuery** | **pre*dent**        | **100000**        |   **313.7 μs** | **1.06 μs** | **1.00 μs** |  **1.00** |  **0.9766** |      **-** |    **4.2 KB** |        **1.00** |
| LuceneNet_WildcardQuery  | pre*dent        | 100000        |   430.4 μs | 0.52 μs | 0.46 μs |  1.37 | 90.3320 | 1.4648 | 370.48 KB |       88.31 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-wildcard"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-wildcard" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-wildcard" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-wildcard" style="max-width:960px"><canvas id="chart-bench-wildcard" style="height:500px"></canvas></div>
<p><a href="debian-wildcard.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


