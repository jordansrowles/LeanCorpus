---
title: Benchmarks - Geo queries
---

# Geo queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                         | GeoQueryType | DocumentCount | Mean        | Error    | StdDev   | Ratio | RatioSD | Gen0     | Gen1   | Allocated | Alloc Ratio |
|------------------------------- |------------- |-------------- |------------:|---------:|---------:|------:|--------:|---------:|-------:|----------:|------------:|
| **LeanCorpus_GeoDistanceQuery**    | **BoundingBox**  | **100000**        |    **30.68 μs** | **0.264 μs** | **0.247 μs** |  **1.00** |    **0.00** |  **15.8081** |      **-** |  **63.38 KB** |        **1.00** |
| LeanCorpus_GeoBoundingBoxQuery | BoundingBox  | 100000        |    81.00 μs | 0.785 μs | 0.734 μs |  2.64 |    0.03 |  50.2930 | 0.1221 | 194.86 KB |        3.07 |
| LuceneNet_GeoDistanceQuery     | BoundingBox  | 100000        | 1,747.84 μs | 1.931 μs | 1.806 μs | 56.97 |    0.45 |  23.4375 | 1.9531 | 100.29 KB |        1.58 |
| LuceneNet_GeoBoundingBoxQuery  | BoundingBox  | 100000        |   439.33 μs | 1.387 μs | 1.298 μs | 14.32 |    0.12 | 119.1406 | 0.4883 | 487.53 KB |        7.69 |
|                                |              |               |             |          |          |       |         |          |        |           |             |
| **LeanCorpus_GeoDistanceQuery**    | **Distance**     | **100000**        |    **29.99 μs** | **0.211 μs** | **0.198 μs** |  **1.00** |    **0.00** |  **15.8386** |      **-** |  **63.38 KB** |        **1.00** |
| LeanCorpus_GeoBoundingBoxQuery | Distance     | 100000        |    81.99 μs | 0.556 μs | 0.520 μs |  2.73 |    0.02 |  50.1709 |      - | 194.86 KB |        3.07 |
| LuceneNet_GeoDistanceQuery     | Distance     | 100000        | 1,752.91 μs | 1.804 μs | 1.688 μs | 58.45 |    0.38 |  23.4375 | 1.9531 | 100.29 KB |        1.58 |
| LuceneNet_GeoBoundingBoxQuery  | Distance     | 100000        |   448.11 μs | 1.282 μs | 1.136 μs | 14.94 |    0.10 | 119.1406 | 0.9766 | 487.54 KB |        7.69 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-geo"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-geo" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-geo" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-geo" style="max-width:960px"><canvas id="chart-bench-geo" style="height:500px"></canvas></div>
<p><a href="debian-geo.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


