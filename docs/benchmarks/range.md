---
title: Benchmarks - Range queries
---

# Range queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                      | RangeWidth | DocumentCount | Mean        | Error    | StdDev   | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|---------------------------- |----------- |-------------- |------------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_RangeQuery**       | **0.01**       | **100000**        |    **28.37 μs** | **0.038 μs** | **0.035 μs** |  **1.00** |  **0.6714** |      **-** |   **2.82 KB** |        **1.00** |
| LuceneNet_NumericRangeQuery | 0.01       | 100000        |   100.23 μs | 0.185 μs | 0.173 μs |  3.53 | 36.8652 |      - | 150.79 KB |       53.47 |
|                             |            |               |             |          |          |       |         |        |           |             |
| **LeanCorpus_RangeQuery**       | **0.1**        | **100000**        |   **132.90 μs** | **0.184 μs** | **0.154 μs** |  **1.00** |  **0.4883** |      **-** |   **2.82 KB** |        **1.00** |
| LuceneNet_NumericRangeQuery | 0.1        | 100000        |   295.83 μs | 0.566 μs | 0.529 μs |  2.23 | 35.1563 |      - | 144.45 KB |       51.22 |
|                             |            |               |             |          |          |       |         |        |           |             |
| **LeanCorpus_RangeQuery**       | **0.5**        | **100000**        |   **578.25 μs** | **0.356 μs** | **0.278 μs** |  **1.00** |       **-** |      **-** |   **2.82 KB** |        **1.00** |
| LuceneNet_NumericRangeQuery | 0.5        | 100000        | 1,044.82 μs | 1.896 μs | 1.681 μs |  1.81 | 41.0156 | 1.9531 | 172.22 KB |       61.06 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-range"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-range" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-range" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-range" style="max-width:960px"><canvas id="chart-bench-range" style="height:500px"></canvas></div>
<p><a href="range.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


