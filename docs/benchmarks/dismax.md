---
title: Benchmarks - Disjunction max
---

# Disjunction max

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `66ba120` &nbsp;&middot;&nbsp; 24 August 2026 21:47 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                         | TieBreakerMultiplier | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------------- |--------------------- |-------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_DisjunctionMaxQuery** | **0**                    | **100000**        | **692.0 μs** | **1.08 μs** | **1.01 μs** |  **1.00** |  **1.9531** |      **-** |   **9.09 KB** |        **1.00** |
| LuceneNet_DisjunctionMaxQuery  | 0                    | 100000        | 938.3 μs | 2.10 μs | 1.86 μs |  1.36 | 39.0625 | 0.9766 | 162.64 KB |       17.90 |
|                                |                      |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_DisjunctionMaxQuery** | **0.1**                  | **100000**        | **693.9 μs** | **1.21 μs** | **1.01 μs** |  **1.00** |  **1.9531** |      **-** |   **9.09 KB** |        **1.00** |
| LuceneNet_DisjunctionMaxQuery  | 0.1                  | 100000        | 961.7 μs | 2.66 μs | 2.49 μs |  1.39 | 39.0625 | 0.9766 | 162.64 KB |       17.90 |
|                                |                      |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_DisjunctionMaxQuery** | **0.5**                  | **100000**        | **694.0 μs** | **1.27 μs** | **1.19 μs** |  **1.00** |  **1.9531** |      **-** |   **9.09 KB** |        **1.00** |
| LuceneNet_DisjunctionMaxQuery  | 0.5                  | 100000        | 937.7 μs | 2.11 μs | 1.98 μs |  1.35 | 39.0625 | 0.9766 | 162.64 KB |       17.90 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-dismax"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-dismax" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-dismax" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-dismax" style="max-width:960px"><canvas id="chart-bench-dismax" style="height:500px"></canvas></div>
<p><a href="dismax.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


