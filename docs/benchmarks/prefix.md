---
title: Benchmarks - Prefix queries
---

# Prefix queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                 | QueryPrefix | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|----------------------- |------------ |-------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_PrefixQuery** | **gov**         | **100000**        | **245.3 μs** | **0.17 μs** | **0.15 μs** |  **1.00** |  **2.4414** |      **-** |  **10.42 KB** |        **1.00** |
| LuceneNet_PrefixQuery  | gov         | 100000        | 250.2 μs | 0.29 μs | 0.26 μs |  1.02 | 24.4141 | 0.9766 | 100.59 KB |        9.65 |
|                        |             |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_PrefixQuery** | **mark**        | **100000**        | **386.1 μs** | **0.44 μs** | **0.39 μs** |  **1.00** |  **3.9063** |      **-** |  **17.02 KB** |        **1.00** |
| LuceneNet_PrefixQuery  | mark        | 100000        | 372.0 μs | 0.49 μs | 0.46 μs |  0.96 | 27.8320 | 0.4883 | 116.13 KB |        6.82 |
|                        |             |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_PrefixQuery** | **pres**        | **100000**        | **544.8 μs** | **1.86 μs** | **1.74 μs** |  **1.00** |  **7.8125** |      **-** |  **32.52 KB** |        **1.00** |
| LuceneNet_PrefixQuery  | pres        | 100000        | 514.3 μs | 0.77 μs | 0.72 μs |  0.94 | 29.2969 | 0.9766 | 122.78 KB |        3.77 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-prefix"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-prefix" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-prefix" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-prefix" style="max-width:960px"><canvas id="chart-bench-prefix" style="height:500px"></canvas></div>
<p><a href="prefix.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


