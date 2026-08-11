---
title: Benchmarks - merge
---

# merge

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c4ff69e` &nbsp;&middot;&nbsp; 7 August 2026 08:52 UTC &nbsp;&middot;&nbsp; 20,000 docs

| Method                             | DocumentCount | SegmentCount | Mean       | Error      | StdDev    | Median     | Ratio | RatioSD | Gen0       | Gen1       | Allocated | Alloc Ratio |
|----------------------------------- |-------------- |------------- |-----------:|-----------:|----------:|-----------:|------:|--------:|-----------:|-----------:|----------:|------------:|
| **&#39;Merge plain text segments&#39;**        | **1000**          | **5**            |   **127.8 ms** |   **159.9 ms** |  **41.53 ms** |   **126.8 ms** |  **1.00** |    **0.00** |  **4000.0000** |  **1000.0000** |  **23.34 MB** |        **1.00** |
| &#39;Merge segments with HNSW vectors&#39; | 1000          | 5            |   323.0 ms | 1,107.3 ms | 287.57 ms |   190.0 ms |  2.79 |    2.57 |  5000.0000 |  1000.0000 |  30.87 MB |        1.32 |
|                                    |               |              |            |            |           |            |       |         |            |            |           |             |
| **&#39;Merge plain text segments&#39;**        | **1000**          | **20**           |   **145.2 ms** |   **155.2 ms** |  **40.30 ms** |   **130.1 ms** |  **1.00** |    **0.00** |  **7000.0000** |  **1000.0000** |  **34.23 MB** |        **1.00** |
| &#39;Merge segments with HNSW vectors&#39; | 1000          | 20           |   352.6 ms | 1,299.1 ms | 337.37 ms |   213.3 ms |  2.55 |    2.33 |  8000.0000 |  1000.0000 |  42.65 MB |        1.25 |
|                                    |               |              |            |            |           |            |       |         |            |            |           |             |
| **&#39;Merge plain text segments&#39;**        | **10000**         | **5**            |   **331.1 ms** |   **524.0 ms** | **136.07 ms** |   **286.2 ms** |  **1.00** |    **0.00** | **27000.0000** |  **3000.0000** | **134.23 MB** |        **1.00** |
| &#39;Merge segments with HNSW vectors&#39; | 10000         | 5            | 1,863.6 ms |   678.2 ms | 176.12 ms | 1,796.2 ms |  6.22 |    1.78 | 46000.0000 |  8000.0000 | 231.47 MB |        1.72 |
|                                    |               |              |            |            |           |            |       |         |            |            |           |             |
| **&#39;Merge plain text segments&#39;**        | **10000**         | **20**           |   **427.9 ms** |   **637.3 ms** | **165.50 ms** |   **382.4 ms** |  **1.00** |    **0.00** | **43000.0000** |  **4000.0000** | **199.91 MB** |        **1.00** |
| &#39;Merge segments with HNSW vectors&#39; | 10000         | 20           | 2,209.8 ms |   763.3 ms | 198.24 ms | 2,139.0 ms |  5.69 |    1.68 | 65000.0000 | 10000.0000 | 309.63 MB |        1.55 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-merge"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-merge" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-merge" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-merge" style="max-width:960px"><canvas id="chart-bench-merge" style="height:500px"></canvas></div>
<p><a href="merge.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


