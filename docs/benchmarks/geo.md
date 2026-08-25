---
title: Benchmarks - Geo queries
---

# Geo queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `66ba120` &nbsp;&middot;&nbsp; 24 August 2026 21:47 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                         | GeoQueryType | DocumentCount | Mean       | Error   | StdDev  | Ratio | RatioSD | Gen0     | Gen1   | Allocated | Alloc Ratio |
|------------------------------- |------------- |-------------- |-----------:|--------:|--------:|------:|--------:|---------:|-------:|----------:|------------:|
| **LeanCorpus_GeoDistanceQuery**    | **BoundingBox**  | **100000**        |   **296.5 μs** | **0.85 μs** | **0.76 μs** |  **1.00** |    **0.00** |  **14.1602** |      **-** |  **59.69 KB** |        **1.00** |
| LeanCorpus_GeoBoundingBoxQuery | BoundingBox  | 100000        |   456.7 μs | 0.94 μs | 0.83 μs |  1.54 |    0.00 |  46.3867 |      - | 190.83 KB |        3.20 |
| LuceneNet_GeoDistanceQuery     | BoundingBox  | 100000        | 2,195.1 μs | 2.98 μs | 2.64 μs |  7.40 |    0.02 |  35.1563 |      - | 147.77 KB |        2.48 |
| LuceneNet_GeoBoundingBoxQuery  | BoundingBox  | 100000        |   618.1 μs | 1.88 μs | 1.76 μs |  2.08 |    0.01 | 161.1328 | 1.9531 | 660.55 KB |       11.07 |
|                                |              |               |            |         |         |       |         |          |        |           |             |
| **LeanCorpus_GeoDistanceQuery**    | **Distance**     | **100000**        |   **293.8 μs** | **0.82 μs** | **0.77 μs** |  **1.00** |    **0.00** |  **14.1602** |      **-** |  **59.69 KB** |        **1.00** |
| LeanCorpus_GeoBoundingBoxQuery | Distance     | 100000        |   452.8 μs | 1.12 μs | 0.99 μs |  1.54 |    0.01 |  46.3867 |      - | 190.83 KB |        3.20 |
| LuceneNet_GeoDistanceQuery     | Distance     | 100000        | 2,214.9 μs | 2.76 μs | 2.45 μs |  7.54 |    0.02 |  35.1563 |      - | 147.77 KB |        2.48 |
| LuceneNet_GeoBoundingBoxQuery  | Distance     | 100000        |   616.9 μs | 1.38 μs | 1.29 μs |  2.10 |    0.01 | 161.1328 | 1.9531 | 660.55 KB |       11.07 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-geo"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-geo" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-geo" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-geo" style="max-width:960px"><canvas id="chart-bench-geo" style="height:500px"></canvas></div>
<p><a href="geo.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


