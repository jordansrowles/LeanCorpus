---
title: Benchmarks - Wildcard queries
---

# Wildcard queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                   | WildcardPattern | DocumentCount | Mean       | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------- |---------------- |-------------- |-----------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_WildcardQuery** | **gov***            | **100000**        |   **245.3 μs** | **0.22 μs** | **0.19 μs** |  **1.00** |  **2.4414** |      **-** |  **11.13 KB** |        **1.00** |
| LuceneNet_WildcardQuery  | gov*            | 100000        |   267.2 μs | 0.43 μs | 0.40 μs |  1.09 | 28.8086 | 0.9766 | 119.67 KB |       10.76 |
|                          |                 |               |            |         |         |       |         |        |           |             |
| **LeanCorpus_WildcardQuery** | **m*rket**          | **100000**        | **2,397.6 μs** | **2.53 μs** | **2.25 μs** |  **1.00** |       **-** |      **-** |   **3.12 KB** |        **1.00** |
| LuceneNet_WildcardQuery  | m*rket          | 100000        | 1,374.2 μs | 2.33 μs | 2.18 μs |  0.57 | 95.7031 | 3.9063 | 396.38 KB |      127.16 |
|                          |                 |               |            |         |         |       |         |        |           |             |
| **LeanCorpus_WildcardQuery** | **pre*dent**        | **100000**        |   **321.8 μs** | **0.22 μs** | **0.18 μs** |  **1.00** |  **0.4883** |      **-** |   **3.96 KB** |        **1.00** |
| LuceneNet_WildcardQuery  | pre*dent        | 100000        |   434.1 μs | 0.63 μs | 0.59 μs |  1.35 | 90.3320 | 0.4883 | 370.49 KB |       93.54 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-wildcard"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-wildcard" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-wildcard" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-wildcard" style="max-width:960px"><canvas id="chart-bench-wildcard" style="height:500px"></canvas></div>
<p><a href="wildcard.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


