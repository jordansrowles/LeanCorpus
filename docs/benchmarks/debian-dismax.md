---
title: Benchmarks - Disjunction max
---

# Disjunction max

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                         | TieBreakerMultiplier | DocumentCount | Mean     | Error   | StdDev  | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------------- |--------------------- |-------------- |---------:|--------:|--------:|------:|--------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_DisjunctionMaxQuery** | **0**                    | **100000**        | **194.1 μs** | **3.35 μs** | **3.13 μs** |  **1.00** |    **0.00** |  **1.9531** |      **-** |   **8.78 KB** |        **1.00** |
| LuceneNet_DisjunctionMaxQuery  | 0                    | 100000        | 504.2 μs | 0.25 μs | 0.20 μs |  2.60 |    0.04 | 16.6016 | 0.9766 |  69.65 KB |        7.94 |
|                                |                      |               |          |         |         |       |         |         |        |           |             |
| **LeanCorpus_DisjunctionMaxQuery** | **0.1**                  | **100000**        | **199.4 μs** | **2.32 μs** | **2.17 μs** |  **1.00** |    **0.00** |  **1.9531** |      **-** |   **8.78 KB** |        **1.00** |
| LuceneNet_DisjunctionMaxQuery  | 0.1                  | 100000        | 508.2 μs | 0.25 μs | 0.21 μs |  2.55 |    0.03 | 16.6016 | 0.9766 |  69.65 KB |        7.94 |
|                                |                      |               |          |         |         |       |         |         |        |           |             |
| **LeanCorpus_DisjunctionMaxQuery** | **0.5**                  | **100000**        | **196.8 μs** | **2.49 μs** | **2.33 μs** |  **1.00** |    **0.00** |  **1.9531** |      **-** |   **8.75 KB** |        **1.00** |
| LuceneNet_DisjunctionMaxQuery  | 0.5                  | 100000        | 512.3 μs | 0.35 μs | 0.31 μs |  2.60 |    0.03 | 16.6016 | 0.9766 |  69.65 KB |        7.96 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-dismax"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-dismax" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-dismax" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-dismax" style="max-width:960px"><canvas id="chart-bench-dismax" style="height:500px"></canvas></div>
<p><a href="debian-dismax.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


