---
title: Benchmarks - Range queries
---

# Range queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                      | RangeWidth | DocumentCount | Mean        | Error    | StdDev   | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|---------------------------- |----------- |-------------- |------------:|---------:|---------:|------:|--------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_RangeQuery**       | **0.01**       | **100000**        |    **21.47 μs** | **0.059 μs** | **0.049 μs** |  **1.00** |    **0.00** |  **2.3499** |      **-** |   **9.54 KB** |        **1.00** |
| LuceneNet_NumericRangeQuery | 0.01       | 100000        |    86.86 μs | 0.155 μs | 0.137 μs |  4.04 |    0.01 | 30.8838 |      - | 126.55 KB |       13.27 |
|                             |            |               |             |          |          |       |         |         |        |           |             |
| **LeanCorpus_RangeQuery**       | **0.1**        | **100000**        |    **76.05 μs** | **0.904 μs** | **0.846 μs** |  **1.00** |    **0.00** |  **2.3193** |      **-** |   **9.54 KB** |        **1.00** |
| LuceneNet_NumericRangeQuery | 0.1        | 100000        |   272.94 μs | 0.529 μs | 0.495 μs |  3.59 |    0.04 | 29.7852 | 1.9531 | 122.47 KB |       12.83 |
|                             |            |               |             |          |          |       |         |         |        |           |             |
| **LeanCorpus_RangeQuery**       | **0.5**        | **100000**        |   **385.94 μs** | **3.323 μs** | **2.775 μs** |  **1.00** |    **0.00** |  **1.9531** |      **-** |   **9.41 KB** |        **1.00** |
| LuceneNet_NumericRangeQuery | 0.5        | 100000        | 1,033.19 μs | 2.070 μs | 1.835 μs |  2.68 |    0.02 | 35.1563 | 1.9531 | 145.37 KB |       15.44 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-range"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-range" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-range" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-range" style="max-width:960px"><canvas id="chart-bench-range" style="height:500px"></canvas></div>
<p><a href="debian-range.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


