---
title: Benchmarks - Wildcard queries
---

# Wildcard queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                   | WildcardPattern | DocumentCount | Mean      | Error    | StdDev    | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------- |---------------- |-------------- |----------:|---------:|----------:|------:|--------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_WildcardQuery** | **gov***            | **100000**        |  **55.33 μs** | **1.105 μs** |  **1.183 μs** |  **1.00** |    **0.00** |  **3.1738** |      **-** |  **12.99 KB** |        **1.00** |
| LuceneNet_WildcardQuery  | gov*            | 100000        | 169.43 μs | 0.129 μs |  0.108 μs |  3.06 |    0.06 | 22.4609 | 0.4883 |  93.05 KB |        7.16 |
|                          |                 |               |           |          |           |       |         |         |        |           |             |
| **LeanCorpus_WildcardQuery** | **m*rket**          | **100000**        | **278.52 μs** | **5.573 μs** | **16.345 μs** |  **1.00** |    **0.00** |  **1.9531** |      **-** |   **8.73 KB** |        **1.00** |
| LuceneNet_WildcardQuery  | m*rket          | 100000        | 465.22 μs | 0.399 μs |  0.333 μs |  1.68 |    0.10 | 75.6836 | 0.4883 | 310.58 KB |       35.58 |
|                          |                 |               |           |          |           |       |         |         |        |           |             |
| **LeanCorpus_WildcardQuery** | **pre*dent**        | **100000**        |  **56.14 μs** | **1.114 μs** |  **2.692 μs** |  **1.00** |    **0.00** |  **2.1973** |      **-** |   **9.24 KB** |        **1.00** |
| LuceneNet_WildcardQuery  | pre*dent        | 100000        | 341.76 μs | 0.211 μs |  0.176 μs |  6.10 |    0.28 | 78.1250 | 1.9531 | 319.79 KB |       34.61 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-wildcard"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-wildcard" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-wildcard" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-wildcard" style="max-width:960px"><canvas id="chart-bench-wildcard" style="height:500px"></canvas></div>
<p><a href="debian-wildcard.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


