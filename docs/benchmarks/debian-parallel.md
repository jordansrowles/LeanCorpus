---
title: Benchmarks - Parallel indexing
---

# Parallel indexing

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                 | SegmentCount | DocumentCount | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------------------------- |------------- |-------------- |----------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_SequentialSearch**            | **4**            | **100000**        | **102.21 μs** | **0.467 μs** | **0.437 μs** |  **1.00** |    **0.00** | **0.1221** |     **600 B** |        **1.00** |
| LeanCorpus_ParallelSearch              | 4            | 100000        | 101.84 μs | 0.223 μs | 0.208 μs |  1.00 |    0.00 | 0.1221 |     600 B |        1.00 |
| LeanCorpus_ParallelSearch_BooleanQuery | 4            | 100000        | 140.68 μs | 2.807 μs | 6.616 μs |  1.38 |    0.06 | 2.6855 |   11719 B |       19.53 |
| &#39;Lucene.NET sequential search&#39;         | 4            | 100000        |  86.88 μs | 0.124 μs | 0.116 μs |  0.85 |    0.00 | 6.2256 |   26432 B |       44.05 |
|                                        |              |               |           |          |          |       |         |        |           |             |
| **LeanCorpus_SequentialSearch**            | **8**            | **100000**        |  **96.90 μs** | **0.075 μs** | **0.066 μs** |  **1.00** |    **0.00** | **0.1221** |     **672 B** |        **1.00** |
| LeanCorpus_ParallelSearch              | 8            | 100000        | 101.55 μs | 0.120 μs | 0.112 μs |  1.05 |    0.00 | 0.1221 |     672 B |        1.00 |
| LeanCorpus_ParallelSearch_BooleanQuery | 8            | 100000        | 164.96 μs | 3.276 μs | 6.467 μs |  1.70 |    0.07 | 3.6621 |   15688 B |       23.35 |
| &#39;Lucene.NET sequential search&#39;         | 8            | 100000        |  86.98 μs | 0.203 μs | 0.189 μs |  0.90 |    0.00 | 6.2256 |   26432 B |       39.33 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-parallel"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-parallel" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-parallel" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-parallel" style="max-width:960px"><canvas id="chart-bench-parallel" style="height:500px"></canvas></div>
<p><a href="debian-parallel.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


