---
title: Benchmarks - Regexp queries
---

# Regexp queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                 | Pattern    | DocumentCount | Mean         | Error      | StdDev     | Ratio | RatioSD | Gen0     | Gen1   | Allocated | Alloc Ratio |
|----------------------- |----------- |-------------- |-------------:|-----------:|-----------:|------:|--------:|---------:|-------:|----------:|------------:|
| **LeanCorpus_RegexpQuery** | **.*nation.*** | **100000**        | **36,444.14 μs** | **397.612 μs** | **371.927 μs** |  **1.00** |    **0.00** |        **-** |      **-** |  **26.17 KB** |        **1.00** |
| LuceneNet_RegexpQuery  | .*nation.* | 100000        | 25,875.44 μs |  35.351 μs |  33.067 μs |  0.71 |    0.01 | 156.2500 |      - | 749.09 KB |       28.63 |
|                        |            |               |              |            |            |       |         |          |        |           |             |
| **LeanCorpus_RegexpQuery** | **gov.*ment**  | **100000**        |     **66.84 μs** |   **1.292 μs** |   **1.680 μs** |  **1.00** |    **0.00** |  **12.2070** |      **-** |  **49.57 KB** |        **1.00** |
| LuceneNet_RegexpQuery  | gov.*ment  | 100000        |    353.52 μs |   0.550 μs |   0.514 μs |  5.29 |    0.13 |  83.0078 | 0.9766 | 340.36 KB |        6.87 |
|                        |            |               |              |            |            |       |         |          |        |           |             |
| **LeanCorpus_RegexpQuery** | **mark.***     | **100000**        |    **124.10 μs** |   **2.849 μs** |   **8.312 μs** |  **1.00** |    **0.00** |  **13.9160** |      **-** |  **56.38 KB** |        **1.00** |
| LuceneNet_RegexpQuery  | mark.*     | 100000        |    274.66 μs |   0.263 μs |   0.233 μs |  2.22 |    0.15 |  31.7383 |      - | 130.01 KB |        2.31 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-regexp"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-regexp" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-regexp" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-regexp" style="max-width:960px"><canvas id="chart-bench-regexp" style="height:500px"></canvas></div>
<p><a href="debian-regexp.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


