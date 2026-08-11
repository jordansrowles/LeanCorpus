---
title: Benchmarks - Parallel search
---

# Parallel search

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                         | SegmentCount | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0     | Gen1    | Allocated | Alloc Ratio |
|------------------------------- |------------- |-------------- |---------:|--------:|--------:|------:|---------:|--------:|----------:|------------:|
| **&#39;LeanCorpus phrase sequential&#39;** | **4**            | **100000**        | **780.4 μs** | **0.43 μs** | **0.33 μs** |  **1.00** |   **4.8828** |       **-** |  **20.06 KB** |        **1.00** |
| &#39;LeanCorpus phrase parallel&#39;   | 4            | 100000        | 532.6 μs | 9.29 μs | 8.69 μs |  0.68 |   5.8594 |       - |  23.58 KB |        1.18 |
| &#39;Lucene.NET phrase sequential&#39; | 4            | 100000        | 332.1 μs | 0.65 μs | 0.61 μs |  0.43 |  75.6836 | 13.6719 | 310.49 KB |       15.48 |
|                                |              |               |          |         |         |       |          |         |           |             |
| **&#39;LeanCorpus phrase sequential&#39;** | **8**            | **100000**        | **769.7 μs** | **1.15 μs** | **1.02 μs** |  **1.00** |   **6.8359** |       **-** |  **30.67 KB** |        **1.00** |
| &#39;LeanCorpus phrase parallel&#39;   | 8            | 100000        | 507.3 μs | 7.88 μs | 7.37 μs |  0.66 |   7.8125 |       - |  34.73 KB |        1.13 |
| &#39;Lucene.NET phrase sequential&#39; | 8            | 100000        | 347.4 μs | 0.66 μs | 0.62 μs |  0.45 | 117.1875 | 19.5313 | 480.88 KB |       15.68 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-parallel"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-parallel" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-parallel" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-parallel" style="max-width:960px"><canvas id="chart-bench-parallel" style="height:500px"></canvas></div>
<p><a href="parallel.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


