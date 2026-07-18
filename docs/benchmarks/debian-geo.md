---
title: Benchmarks - Geo queries
---

# Geo queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `f3305a5` &nbsp;&middot;&nbsp; 16 July 2026 20:51 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                         | GeoQueryType | DocumentCount | Mean        | Error    | StdDev   | Ratio | RatioSD | Gen0     | Gen1   | Allocated | Alloc Ratio |
|------------------------------- |------------- |-------------- |------------:|---------:|---------:|------:|--------:|---------:|-------:|----------:|------------:|
| **LeanCorpus_GeoDistanceQuery**    | **BoundingBox**  | **100000**        |    **30.31 μs** | **0.171 μs** | **0.160 μs** |  **1.00** |    **0.00** |  **15.8386** |      **-** |  **63.38 KB** |        **1.00** |
| LeanCorpus_GeoBoundingBoxQuery | BoundingBox  | 100000        |    82.37 μs | 0.747 μs | 0.699 μs |  2.72 |    0.03 |  50.1709 | 0.4883 | 194.86 KB |        3.07 |
| LuceneNet_GeoDistanceQuery     | BoundingBox  | 100000        | 2,027.94 μs | 4.237 μs | 3.756 μs | 66.91 |    0.36 |  27.3438 |      - | 114.83 KB |        1.81 |
| LuceneNet_GeoBoundingBoxQuery  | BoundingBox  | 100000        |   493.01 μs | 0.509 μs | 0.476 μs | 16.27 |    0.08 | 133.3008 | 0.4883 |  544.9 KB |        8.60 |
|                                |              |               |             |          |          |       |         |          |        |           |             |
| **LeanCorpus_GeoDistanceQuery**    | **Distance**     | **100000**        |    **30.64 μs** | **0.212 μs** | **0.198 μs** |  **1.00** |    **0.00** |  **15.8081** |      **-** |  **63.38 KB** |        **1.00** |
| LeanCorpus_GeoBoundingBoxQuery | Distance     | 100000        |    80.84 μs | 0.180 μs | 0.150 μs |  2.64 |    0.02 |  50.2930 |      - | 194.87 KB |        3.07 |
| LuceneNet_GeoDistanceQuery     | Distance     | 100000        | 2,033.81 μs | 1.409 μs | 1.100 μs | 66.37 |    0.42 |  27.3438 |      - | 114.83 KB |        1.81 |
| LuceneNet_GeoBoundingBoxQuery  | Distance     | 100000        |   492.68 μs | 0.752 μs | 0.667 μs | 16.08 |    0.10 | 133.3008 | 0.4883 |  544.9 KB |        8.60 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-geo"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-geo" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-geo" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-geo" style="max-width:960px"><canvas id="chart-bench-geo" style="height:500px"></canvas></div>
<p><a href="debian-geo.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


