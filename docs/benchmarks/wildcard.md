---
title: Benchmarks - Wildcard queries
---

# Wildcard queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `66ba120` &nbsp;&middot;&nbsp; 24 August 2026 21:47 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                   | WildcardPattern | DocumentCount | Mean       | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------- |---------------- |-------------- |-----------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_WildcardQuery** | **gov***            | **100000**        |   **287.1 μs** | **0.42 μs** | **0.40 μs** |  **1.00** |  **4.8828** |      **-** |  **20.66 KB** |        **1.00** |
| LuceneNet_WildcardQuery  | gov*            | 100000        |   268.9 μs | 0.46 μs | 0.43 μs |  0.94 | 28.8086 | 0.9766 | 119.67 KB |        5.79 |
|                          |                 |               |            |         |         |       |         |        |           |             |
| **LeanCorpus_WildcardQuery** | **m*rket**          | **100000**        | **2,381.4 μs** | **4.68 μs** | **4.15 μs** |  **1.00** |       **-** |      **-** |   **4.55 KB** |        **1.00** |
| LuceneNet_WildcardQuery  | m*rket          | 100000        | 1,373.4 μs | 1.61 μs | 1.34 μs |  0.58 | 95.7031 | 3.9063 | 396.38 KB |       87.03 |
|                          |                 |               |            |         |         |       |         |        |           |             |
| **LeanCorpus_WildcardQuery** | **pre*dent**        | **100000**        |   **306.8 μs** | **0.48 μs** | **0.45 μs** |  **1.00** |  **1.4648** |      **-** |   **6.44 KB** |        **1.00** |
| LuceneNet_WildcardQuery  | pre*dent        | 100000        |   433.1 μs | 0.38 μs | 0.31 μs |  1.41 | 89.8438 | 0.4883 | 370.47 KB |       57.55 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-wildcard"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-wildcard" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-wildcard" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-wildcard" style="max-width:960px"><canvas id="chart-bench-wildcard" style="height:500px"></canvas></div>
<p><a href="wildcard.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


