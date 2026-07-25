---
title: Benchmarks - Parallel indexing
---

# Parallel indexing

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `f3305a5` &nbsp;&middot;&nbsp; 16 July 2026 20:51 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                 | SegmentCount | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|--------------------------------------- |------------- |-------------- |---------:|--------:|--------:|------:|-------:|----------:|------------:|
| **LeanCorpus_SequentialSearch**            | **4**            | **100000**        | **169.0 μs** | **0.22 μs** | **0.21 μs** |  **1.00** |      **-** |     **600 B** |        **1.00** |
| LeanCorpus_ParallelSearch              | 4            | 100000        | 170.7 μs | 0.26 μs | 0.23 μs |  1.01 |      - |     600 B |        1.00 |
| LeanCorpus_ParallelSearch_BooleanQuery | 4            | 100000        | 314.7 μs | 2.68 μs | 2.50 μs |  1.86 | 2.4414 |   11638 B |       19.40 |
| &#39;Lucene.NET sequential search&#39;         | 4            | 100000        | 149.5 μs | 0.19 μs | 0.16 μs |  0.88 | 8.7891 |   37216 B |       62.03 |
|                                        |              |               |          |         |         |       |        |           |             |
| **LeanCorpus_SequentialSearch**            | **8**            | **100000**        | **172.6 μs** | **0.26 μs** | **0.24 μs** |  **1.00** |      **-** |     **672 B** |        **1.00** |
| LeanCorpus_ParallelSearch              | 8            | 100000        | 172.7 μs | 0.26 μs | 0.25 μs |  1.00 |      - |     672 B |        1.00 |
| LeanCorpus_ParallelSearch_BooleanQuery | 8            | 100000        | 147.6 μs | 2.55 μs | 2.26 μs |  0.86 | 3.9063 |   16360 B |       24.35 |
| &#39;Lucene.NET sequential search&#39;         | 8            | 100000        | 150.6 μs | 0.30 μs | 0.26 μs |  0.87 | 8.7891 |   37216 B |       55.38 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-parallel"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-parallel" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-parallel" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-parallel" style="max-width:960px"><canvas id="chart-bench-parallel" style="height:500px"></canvas></div>
<p><a href="debian-parallel.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


