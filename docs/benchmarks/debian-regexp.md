---
title: Benchmarks - Regexp queries
---

# Regexp queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                 | Pattern    | DocumentCount | Mean        | Error    | StdDev   | Ratio | Gen0     | Gen1   | Allocated  | Alloc Ratio |
|----------------------- |----------- |-------------- |------------:|---------:|---------:|------:|---------:|-------:|-----------:|------------:|
| **LeanCorpus_RegexpQuery** | **.*nation.*** | **100000**        | **38,220.4 μs** | **66.03 μs** | **58.53 μs** |  **1.00** |        **-** |      **-** |   **51.45 KB** |        **1.00** |
| LuceneNet_RegexpQuery  | .*nation.* | 100000        | 30,253.3 μs | 65.08 μs | 60.87 μs |  0.79 | 312.5000 |      - | 1342.77 KB |       26.10 |
|                        |            |               |             |          |          |       |          |        |            |             |
| **LeanCorpus_RegexpQuery** | **gov.*ment**  | **100000**        |    **254.2 μs** |  **0.17 μs** |  **0.14 μs** |  **1.00** |  **10.2539** |      **-** |   **43.24 KB** |        **1.00** |
| LuceneNet_RegexpQuery  | gov.*ment  | 100000        |    420.5 μs |  3.26 μs |  3.05 μs |  1.65 |  89.8438 | 0.9766 |  369.11 KB |        8.54 |
|                        |            |               |             |          |          |       |          |        |            |             |
| **LeanCorpus_RegexpQuery** | **mark.***     | **100000**        |    **493.6 μs** |  **0.67 μs** |  **0.62 μs** |  **1.00** |  **17.5781** |      **-** |   **74.57 KB** |        **1.00** |
| LuceneNet_RegexpQuery  | mark.*     | 100000        |    417.4 μs |  4.18 μs |  3.91 μs |  0.85 |  40.5273 | 0.9766 |  166.43 KB |        2.23 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-regexp"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-regexp" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-regexp" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-regexp" style="max-width:960px"><canvas id="chart-bench-regexp" style="height:500px"></canvas></div>
<p><a href="debian-regexp.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


