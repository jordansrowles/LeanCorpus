---
title: Benchmarks - Prefix queries
---

# Prefix queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `66ba120` &nbsp;&middot;&nbsp; 24 August 2026 21:47 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                 | QueryPrefix | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|----------------------- |------------ |-------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_PrefixQuery** | **gov**         | **100000**        | **285.5 μs** | **0.87 μs** | **0.82 μs** |  **1.00** |  **4.8828** |      **-** |  **19.95 KB** |        **1.00** |
| LuceneNet_PrefixQuery  | gov         | 100000        | 243.2 μs | 0.52 μs | 0.49 μs |  0.85 | 24.1699 | 0.4883 | 100.59 KB |        5.04 |
|                        |             |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_PrefixQuery** | **mark**        | **100000**        | **455.3 μs** | **1.21 μs** | **1.13 μs** |  **1.00** |  **7.8125** |      **-** |  **33.23 KB** |        **1.00** |
| LuceneNet_PrefixQuery  | mark        | 100000        | 351.3 μs | 0.69 μs | 0.65 μs |  0.77 | 27.8320 | 0.4883 | 116.13 KB |        3.49 |
|                        |             |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_PrefixQuery** | **pres**        | **100000**        | **668.0 μs** | **0.79 μs** | **0.74 μs** |  **1.00** | **15.6250** |      **-** |   **65.3 KB** |        **1.00** |
| LuceneNet_PrefixQuery  | pres        | 100000        | 522.2 μs | 1.14 μs | 1.07 μs |  0.78 | 29.2969 | 0.9766 | 122.77 KB |        1.88 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-prefix"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-prefix" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-prefix" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-prefix" style="max-width:960px"><canvas id="chart-bench-prefix" style="height:500px"></canvas></div>
<p><a href="prefix.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


