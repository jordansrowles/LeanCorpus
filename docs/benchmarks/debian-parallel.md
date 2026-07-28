---
title: Benchmarks - Parallel search
---

# Parallel search

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                         | SegmentCount | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0     | Gen1    | Allocated | Alloc Ratio |
|------------------------------- |------------- |-------------- |---------:|--------:|--------:|------:|---------:|--------:|----------:|------------:|
| **&#39;LeanCorpus phrase sequential&#39;** | **4**            | **100000**        | **773.8 μs** | **0.96 μs** | **0.80 μs** |  **1.00** |   **4.8828** |       **-** |  **20.09 KB** |        **1.00** |
| &#39;LeanCorpus phrase parallel&#39;   | 4            | 100000        | 530.8 μs | 6.14 μs | 5.74 μs |  0.69 |   5.8594 |       - |  23.61 KB |        1.18 |
| &#39;Lucene.NET phrase sequential&#39; | 4            | 100000        | 332.3 μs | 0.54 μs | 0.50 μs |  0.43 |  75.6836 | 13.6719 | 310.49 KB |       15.45 |
|                                |              |               |          |         |         |       |          |         |           |             |
| **&#39;LeanCorpus phrase sequential&#39;** | **8**            | **100000**        | **760.7 μs** | **1.18 μs** | **1.04 μs** |  **1.00** |   **6.8359** |       **-** |  **30.75 KB** |        **1.00** |
| &#39;LeanCorpus phrase parallel&#39;   | 8            | 100000        | 500.5 μs | 6.21 μs | 5.81 μs |  0.66 |   7.8125 |       - |  34.81 KB |        1.13 |
| &#39;Lucene.NET phrase sequential&#39; | 8            | 100000        | 348.3 μs | 0.62 μs | 0.55 μs |  0.46 | 117.1875 | 19.5313 | 480.88 KB |       15.64 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-parallel"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-parallel" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-parallel" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-parallel" style="max-width:960px"><canvas id="chart-bench-parallel" style="height:500px"></canvas></div>
<p><a href="debian-parallel.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


