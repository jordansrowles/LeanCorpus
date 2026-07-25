---
title: Benchmarks - Prefix queries
---

# Prefix queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                 | QueryPrefix | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|----------------------- |------------ |-------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_PrefixQuery** | **gov**         | **100000**        | **239.8 μs** | **0.25 μs** | **0.23 μs** |  **1.00** |  **2.6855** |      **-** |  **11.66 KB** |        **1.00** |
| LuceneNet_PrefixQuery  | gov         | 100000        | 259.3 μs | 0.28 μs | 0.24 μs |  1.08 | 24.4141 | 0.9766 | 100.59 KB |        8.62 |
|                        |             |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_PrefixQuery** | **mark**        | **100000**        | **379.1 μs** | **0.39 μs** | **0.37 μs** |  **1.00** |  **4.3945** |      **-** |  **19.22 KB** |        **1.00** |
| LuceneNet_PrefixQuery  | mark        | 100000        | 372.0 μs | 0.23 μs | 0.19 μs |  0.98 | 27.8320 | 0.4883 | 116.13 KB |        6.04 |
|                        |             |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_PrefixQuery** | **pres**        | **100000**        | **540.6 μs** | **0.17 μs** | **0.14 μs** |  **1.00** |  **8.7891** |      **-** |  **37.09 KB** |        **1.00** |
| LuceneNet_PrefixQuery  | pres        | 100000        | 512.3 μs | 0.62 μs | 0.58 μs |  0.95 | 29.2969 | 0.9766 | 122.78 KB |        3.31 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-prefix"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-prefix" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-prefix" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-prefix" style="max-width:960px"><canvas id="chart-bench-prefix" style="height:500px"></canvas></div>
<p><a href="prefix.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


