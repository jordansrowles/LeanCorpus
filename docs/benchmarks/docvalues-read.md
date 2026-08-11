---
title: Benchmarks - docvalues-read
---

# docvalues-read

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c4ff69e` &nbsp;&middot;&nbsp; 6 August 2026 20:14 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                        | DocumentCount | Mean         | Error      | StdDev    | Ratio | Gen0      | Allocated | Alloc Ratio |
|------------------------------ |-------------- |-------------:|-----------:|----------:|------:|----------:|----------:|------------:|
| **&#39;Numeric DV sequential read&#39;**  | **10000**         |   **361.643 μs** |  **4.6155 μs** | **0.7142 μs** |  **1.00** |  **152.8320** |  **640000 B** |        **1.00** |
| &#39;Numeric DV random access&#39;    | 10000         |   489.189 μs |  3.4400 μs | 0.5323 μs |  1.35 |  152.3438 |  640304 B |        1.00 |
| &#39;Sorted DV lookup&#39;            | 10000         |   446.535 μs |  0.6108 μs | 0.0945 μs |  1.23 |         - |         - |        0.00 |
| &#39;Numeric DV dense array read&#39; | 10000         |     8.666 μs |  0.0209 μs | 0.0032 μs |  0.02 |         - |         - |        0.00 |
| &#39;Sorted DV dense array read&#39;  | 10000         |     8.695 μs |  0.0162 μs | 0.0025 μs |  0.02 |         - |         - |        0.00 |
|                               |               |              |            |           |       |           |           |             |
| **&#39;Numeric DV sequential read&#39;**  | **100000**        | **3,579.130 μs** |  **6.6481 μs** | **1.0288 μs** | **1.000** | **1527.3438** | **6400000 B** |        **1.00** |
| &#39;Numeric DV random access&#39;    | 100000        | 5,461.348 μs | 34.9000 μs | 9.0634 μs | 1.526 | 1523.4375 | 6400304 B |        1.00 |
| &#39;Sorted DV lookup&#39;            | 100000        | 3,008.732 μs |  7.0595 μs | 1.0925 μs | 0.841 |         - |         - |        0.00 |
| &#39;Numeric DV dense array read&#39; | 100000        |    17.292 μs |  0.0777 μs | 0.0202 μs | 0.005 |         - |         - |        0.00 |
| &#39;Sorted DV dense array read&#39;  | 100000        |    17.362 μs |  0.0212 μs | 0.0055 μs | 0.005 |         - |         - |        0.00 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-docvalues-read"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-docvalues-read" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-docvalues-read" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-docvalues-read" style="max-width:960px"><canvas id="chart-bench-docvalues-read" style="height:500px"></canvas></div>
<p><a href="docvalues-read.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


