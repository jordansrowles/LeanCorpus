---
title: Benchmarks - Combined queries
---

# Combined queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `f3305a5` &nbsp;&middot;&nbsp; 16 July 2026 20:51 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                             | MinimumShouldMatch | DocumentCount | Mean       | Error   | StdDev  | Ratio | Gen0     | Gen1   | Allocated | Alloc Ratio |
|----------------------------------- |------------------- |-------------- |-----------:|--------:|--------:|------:|---------:|-------:|----------:|------------:|
| **LeanCorpus_CombinedFieldsQuery**     | **1**                  | **100000**        | **1,164.2 μs** | **8.47 μs** | **7.93 μs** |  **1.00** | **167.9688** | **5.8594** | **686.29 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_MultiField | 1                  | 100000        |   461.7 μs | 3.41 μs | 3.19 μs |  0.40 |   4.8828 |      - |  20.92 KB |        0.03 |
| LuceneNet_BooleanQuery_MultiField  | 1                  | 100000        |   758.9 μs | 1.42 μs | 1.33 μs |  0.65 | 113.2813 | 3.9063 |  466.7 KB |        0.68 |
|                                    |                    |               |            |         |         |       |          |        |           |             |
| **LeanCorpus_CombinedFieldsQuery**     | **2**                  | **100000**        | **1,121.3 μs** | **8.63 μs** | **8.07 μs** |  **1.00** | **167.9688** |      **-** | **686.23 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_MultiField | 2                  | 100000        |   467.3 μs | 2.04 μs | 1.90 μs |  0.42 |   4.8828 |      - |  20.92 KB |        0.03 |
| LuceneNet_BooleanQuery_MultiField  | 2                  | 100000        |   759.7 μs | 1.84 μs | 1.72 μs |  0.68 | 113.2813 | 3.9063 |  466.7 KB |        0.68 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-combined"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-combined" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-combined" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-combined" style="max-width:960px"><canvas id="chart-bench-combined" style="height:500px"></canvas></div>
<p><a href="debian-combined.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


