---
title: Benchmarks - Range queries
---

# Range queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                      | RangeWidth | DocumentCount | Mean        | Error    | StdDev   | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|---------------------------- |----------- |-------------- |------------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_RangeQuery**       | **0.01**       | **100000**        |    **32.07 μs** | **0.067 μs** | **0.063 μs** |  **1.00** |  **0.6714** |      **-** |   **2.98 KB** |        **1.00** |
| LuceneNet_NumericRangeQuery | 0.01       | 100000        |    98.82 μs | 0.234 μs | 0.207 μs |  3.08 | 36.8652 |      - | 150.79 KB |       50.53 |
|                             |            |               |             |          |          |       |         |        |           |             |
| **LeanCorpus_RangeQuery**       | **0.1**        | **100000**        |   **693.97 μs** | **0.305 μs** | **0.238 μs** |  **1.00** |       **-** |      **-** |   **2.98 KB** |        **1.00** |
| LuceneNet_NumericRangeQuery | 0.1        | 100000        |   294.98 μs | 0.386 μs | 0.342 μs |  0.43 | 35.1563 | 0.9766 | 144.45 KB |       48.40 |
|                             |            |               |             |          |          |       |         |        |           |             |
| **LeanCorpus_RangeQuery**       | **0.5**        | **100000**        |   **762.98 μs** | **1.210 μs** | **1.072 μs** |  **1.00** |       **-** |      **-** |   **2.98 KB** |        **1.00** |
| LuceneNet_NumericRangeQuery | 0.5        | 100000        | 1,046.61 μs | 3.182 μs | 2.820 μs |  1.37 | 41.0156 | 1.9531 | 172.22 KB |       57.71 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-range"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-range" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-range" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-range" style="max-width:960px"><canvas id="chart-bench-range" style="height:500px"></canvas></div>
<p><a href="range.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


