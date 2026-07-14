---
title: Benchmarks - Prefix queries
---

# Prefix queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                 | QueryPrefix | DocumentCount | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|----------------------- |------------ |-------------- |----------:|---------:|---------:|------:|--------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_PrefixQuery** | **gov**         | **100000**        |  **53.12 μs** | **1.035 μs** | **1.063 μs** |  **1.00** |    **0.00** |  **2.9907** |      **-** |  **12.29 KB** |        **1.00** |
| LuceneNet_PrefixQuery  | gov         | 100000        | 153.37 μs | 0.225 μs | 0.211 μs |  2.89 |    0.06 | 17.8223 | 0.4883 |     74 KB |        6.02 |
|                        |             |               |           |          |          |       |         |         |        |           |             |
| **LeanCorpus_PrefixQuery** | **mark**        | **100000**        | **106.25 μs** | **2.115 μs** | **5.228 μs** |  **1.00** |    **0.00** |  **3.2959** |      **-** |  **13.39 KB** |        **1.00** |
| LuceneNet_PrefixQuery  | mark        | 100000        | 227.04 μs | 0.224 μs | 0.210 μs |  2.14 |    0.10 | 19.2871 | 0.2441 |  79.64 KB |        5.95 |
|                        |             |               |           |          |          |       |         |         |        |           |             |
| **LeanCorpus_PrefixQuery** | **pres**        | **100000**        |  **90.35 μs** | **1.796 μs** | **4.635 μs** |  **1.00** |    **0.00** |  **4.6387** |      **-** |  **18.83 KB** |        **1.00** |
| LuceneNet_PrefixQuery  | pres        | 100000        | 223.61 μs | 0.329 μs | 0.308 μs |  2.48 |    0.13 | 19.7754 | 0.4883 |  81.95 KB |        4.35 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-prefix"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-prefix" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-prefix" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-prefix" style="max-width:960px"><canvas id="chart-bench-prefix" style="height:500px"></canvas></div>
<p><a href="debian-prefix.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


