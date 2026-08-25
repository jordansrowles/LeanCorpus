---
title: Benchmarks - Range queries
---

# Range queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `66ba120` &nbsp;&middot;&nbsp; 24 August 2026 21:47 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                      | RangeWidth | DocumentCount | Mean       | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|---------------------------- |----------- |-------------- |-----------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_RangeQuery**       | **0.01**       | **100000**        |   **250.8 μs** | **0.68 μs** | **0.60 μs** |  **1.00** |  **0.4883** |      **-** |   **3.77 KB** |        **1.00** |
| LuceneNet_NumericRangeQuery | 0.01       | 100000        |   102.2 μs | 0.16 μs | 0.15 μs |  0.41 | 36.8652 |      - | 150.79 KB |       40.04 |
|                             |            |               |            |         |         |       |         |        |           |             |
| **LeanCorpus_RangeQuery**       | **0.1**        | **100000**        |   **708.0 μs** | **1.30 μs** | **1.22 μs** |  **1.00** |       **-** |      **-** |   **3.77 KB** |        **1.00** |
| LuceneNet_NumericRangeQuery | 0.1        | 100000        |   298.3 μs | 0.55 μs | 0.49 μs |  0.42 | 35.1563 | 0.9766 | 144.45 KB |       38.36 |
|                             |            |               |            |         |         |       |         |        |           |             |
| **LeanCorpus_RangeQuery**       | **0.5**        | **100000**        | **2,846.9 μs** | **3.67 μs** | **3.06 μs** |  **1.00** |       **-** |      **-** |   **3.77 KB** |        **1.00** |
| LuceneNet_NumericRangeQuery | 0.5        | 100000        | 1,050.3 μs | 2.32 μs | 2.06 μs |  0.37 | 41.0156 | 1.9531 | 172.22 KB |       45.73 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-range"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-range" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-range" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-range" style="max-width:960px"><canvas id="chart-bench-range" style="height:500px"></canvas></div>
<p><a href="range.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


