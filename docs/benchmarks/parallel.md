---
title: Benchmarks - Parallel search
---

# Parallel search

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `66ba120` &nbsp;&middot;&nbsp; 25 August 2026 10:31 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                         | SegmentCount | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0     | Gen1    | Allocated | Alloc Ratio |
|------------------------------- |------------- |-------------- |---------:|--------:|--------:|------:|---------:|--------:|----------:|------------:|
| **&#39;LeanCorpus phrase sequential&#39;** | **4**            | **100000**        | **573.6 μs** | **1.45 μs** | **1.29 μs** |  **1.00** |   **4.8828** |       **-** |  **22.17 KB** |        **1.00** |
| &#39;LeanCorpus phrase parallel&#39;   | 4            | 100000        | 395.1 μs | 2.47 μs | 2.31 μs |  0.69 |   6.3477 |       - |  25.69 KB |        1.16 |
| &#39;Lucene.NET phrase sequential&#39; | 4            | 100000        | 341.5 μs | 0.75 μs | 0.70 μs |  0.60 |  75.6836 | 13.6719 | 310.49 KB |       14.00 |
|                                |              |               |          |         |         |       |          |         |           |             |
| **&#39;LeanCorpus phrase sequential&#39;** | **8**            | **100000**        | **565.5 μs** | **1.07 μs** | **0.89 μs** |  **1.00** |   **7.8125** |       **-** |  **34.05 KB** |        **1.00** |
| &#39;LeanCorpus phrase parallel&#39;   | 8            | 100000        | 389.5 μs | 2.69 μs | 2.52 μs |  0.69 |   9.2773 |       - |   38.1 KB |        1.12 |
| &#39;Lucene.NET phrase sequential&#39; | 8            | 100000        | 351.9 μs | 1.17 μs | 1.09 μs |  0.62 | 117.1875 | 19.5313 | 480.88 KB |       14.12 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-parallel"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-parallel" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-parallel" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-parallel" style="max-width:960px"><canvas id="chart-bench-parallel" style="height:500px"></canvas></div>
<p><a href="parallel.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


