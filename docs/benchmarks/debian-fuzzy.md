---
title: Benchmarks - Fuzzy queries
---

# Fuzzy queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                | Scenario            | DocumentCount | Mean         | Error     | StdDev    | Ratio  | RatioSD | Gen0     | Gen1     | Allocated  | Alloc Ratio |
|---------------------- |-------------------- |-------------- |-------------:|----------:|----------:|-------:|--------:|---------:|---------:|-----------:|------------:|
| **LeanCorpus_FuzzyQuery** | **long-edit1-common**   | **100000**        |    **24.963 μs** | **0.3731 μs** | **0.3307 μs** |   **1.00** |    **0.00** |   **1.8005** |        **-** |    **7.36 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | long-edit1-common   | 100000        |   616.740 μs | 0.9807 μs | 0.9173 μs |  24.71 |    0.32 |  66.4063 |   1.9531 |  274.85 KB |       37.35 |
|                       |                     |               |              |           |           |        |         |          |          |            |             |
| **LeanCorpus_FuzzyQuery** | **medium-edit1-common** | **100000**        |    **40.423 μs** | **0.7727 μs** | **0.8268 μs** |   **1.00** |    **0.00** |   **1.8921** |        **-** |    **7.76 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | medium-edit1-common | 100000        |   613.505 μs | 1.0259 μs | 0.9095 μs |  15.18 |    0.30 | 134.7656 |   5.8594 |  552.02 KB |       71.12 |
|                       |                     |               |              |           |           |        |         |          |          |            |             |
| **LeanCorpus_FuzzyQuery** | **medium-edit2-common** | **100000**        |    **43.507 μs** | **0.8654 μs** | **1.3974 μs** |   **1.00** |    **0.00** |   **1.9531** |        **-** |     **8.1 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | medium-edit2-common | 100000        | 3,625.947 μs | 7.0737 μs | 6.6167 μs |  83.42 |    2.60 | 378.9063 |  78.1250 | 1642.54 KB |      202.82 |
|                       |                     |               |              |           |           |        |         |          |          |            |             |
| **LeanCorpus_FuzzyQuery** | **nohit-edit2**         | **100000**        |     **3.445 μs** | **0.0244 μs** | **0.0229 μs** |   **1.00** |    **0.00** |   **1.3809** |        **-** |    **5.59 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | nohit-edit2         | 100000        | 3,151.948 μs | 5.4881 μs | 4.8650 μs | 915.07 |    6.01 | 410.1563 | 175.7813 | 1991.09 KB |      355.95 |
|                       |                     |               |              |           |           |        |         |          |          |            |             |
| **LeanCorpus_FuzzyQuery** | **short-edit1-common**  | **100000**        |    **82.410 μs** | **1.5138 μs** | **2.2190 μs** |   **1.00** |    **0.00** |   **2.0752** |        **-** |    **8.56 KB** |        **1.00** |
| LuceneNet_FuzzyQuery  | short-edit1-common  | 100000        |   849.260 μs | 1.9224 μs | 1.7982 μs |  10.31 |    0.27 | 128.9063 |   4.8828 |  534.64 KB |       62.48 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-fuzzy"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-fuzzy" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-fuzzy" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-fuzzy" style="max-width:960px"><canvas id="chart-bench-fuzzy" style="height:500px"></canvas></div>
<p><a href="debian-fuzzy.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


