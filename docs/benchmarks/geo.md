---
title: Benchmarks - Geo queries
---

# Geo queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                         | GeoQueryType | DocumentCount | Mean        | Error    | StdDev   | Ratio | RatioSD | Gen0     | Gen1   | Allocated | Alloc Ratio |
|------------------------------- |------------- |-------------- |------------:|---------:|---------:|------:|--------:|---------:|-------:|----------:|------------:|
| **LeanCorpus_GeoDistanceQuery**    | **BoundingBox**  | **100000**        |    **66.33 μs** | **0.162 μs** | **0.143 μs** |  **1.00** |    **0.00** |  **14.4043** |      **-** |   **58.9 KB** |        **1.00** |
| LeanCorpus_GeoBoundingBoxQuery | BoundingBox  | 100000        |   165.71 μs | 0.462 μs | 0.386 μs |  2.50 |    0.01 |  46.3867 |      - | 190.04 KB |        3.23 |
| LuceneNet_GeoDistanceQuery     | BoundingBox  | 100000        | 2,187.89 μs | 0.939 μs | 0.833 μs | 32.98 |    0.07 |  35.1563 |      - | 147.77 KB |        2.51 |
| LuceneNet_GeoBoundingBoxQuery  | BoundingBox  | 100000        |   612.70 μs | 1.112 μs | 1.040 μs |  9.24 |    0.02 | 161.1328 | 1.9531 | 660.55 KB |       11.22 |
|                                |              |               |             |          |          |       |         |          |        |           |             |
| **LeanCorpus_GeoDistanceQuery**    | **Distance**     | **100000**        |    **96.46 μs** | **0.154 μs** | **0.144 μs** |  **1.00** |    **0.00** |  **14.4043** |      **-** |   **58.9 KB** |        **1.00** |
| LeanCorpus_GeoBoundingBoxQuery | Distance     | 100000        |   165.08 μs | 0.116 μs | 0.090 μs |  1.71 |    0.00 |  46.3867 |      - | 190.04 KB |        3.23 |
| LuceneNet_GeoDistanceQuery     | Distance     | 100000        | 2,181.43 μs | 1.081 μs | 0.903 μs | 22.62 |    0.03 |  35.1563 |      - | 147.77 KB |        2.51 |
| LuceneNet_GeoBoundingBoxQuery  | Distance     | 100000        |   604.75 μs | 1.327 μs | 1.108 μs |  6.27 |    0.01 | 161.1328 | 1.9531 | 660.55 KB |       11.22 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-geo"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-geo" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-geo" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-geo" style="max-width:960px"><canvas id="chart-bench-geo" style="height:500px"></canvas></div>
<p><a href="geo.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


