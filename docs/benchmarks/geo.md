---
title: Benchmarks - Geo queries
---

# Geo queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                         | GeoQueryType | DocumentCount | Mean        | Error    | StdDev   | Ratio | RatioSD | Gen0     | Gen1   | Allocated | Alloc Ratio |
|------------------------------- |------------- |-------------- |------------:|---------:|---------:|------:|--------:|---------:|-------:|----------:|------------:|
| **LeanCorpus_GeoDistanceQuery**    | **BoundingBox**  | **100000**        |    **68.19 μs** | **0.084 μs** | **0.070 μs** |  **1.00** |    **0.00** |  **14.4043** |      **-** |  **58.91 KB** |        **1.00** |
| LeanCorpus_GeoBoundingBoxQuery | BoundingBox  | 100000        |   166.58 μs | 0.368 μs | 0.326 μs |  2.44 |    0.01 |  46.3867 |      - | 190.05 KB |        3.23 |
| LuceneNet_GeoDistanceQuery     | BoundingBox  | 100000        | 2,206.16 μs | 1.007 μs | 0.841 μs | 32.35 |    0.03 |  35.1563 |      - | 147.77 KB |        2.51 |
| LuceneNet_GeoBoundingBoxQuery  | BoundingBox  | 100000        |   614.84 μs | 1.186 μs | 1.109 μs |  9.02 |    0.02 | 161.1328 | 1.9531 | 660.55 KB |       11.21 |
|                                |              |               |             |          |          |       |         |          |        |           |             |
| **LeanCorpus_GeoDistanceQuery**    | **Distance**     | **100000**        |    **68.07 μs** | **0.315 μs** | **0.294 μs** |  **1.00** |    **0.00** |  **14.4043** |      **-** |  **58.91 KB** |        **1.00** |
| LeanCorpus_GeoBoundingBoxQuery | Distance     | 100000        |   166.49 μs | 0.277 μs | 0.259 μs |  2.45 |    0.01 |  46.3867 |      - | 190.05 KB |        3.23 |
| LuceneNet_GeoDistanceQuery     | Distance     | 100000        | 2,188.99 μs | 3.067 μs | 2.869 μs | 32.16 |    0.14 |  35.1563 |      - | 147.77 KB |        2.51 |
| LuceneNet_GeoBoundingBoxQuery  | Distance     | 100000        |   615.62 μs | 0.822 μs | 0.729 μs |  9.04 |    0.04 | 161.1328 | 1.9531 | 660.55 KB |       11.21 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-geo"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-geo" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-geo" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-geo" style="max-width:960px"><canvas id="chart-bench-geo" style="height:500px"></canvas></div>
<p><a href="geo.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


