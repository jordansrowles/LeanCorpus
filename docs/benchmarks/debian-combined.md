---
title: Benchmarks - Combined queries
---

# Combined queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                             | MinimumShouldMatch | DocumentCount | Mean       | Error    | StdDev  | Ratio | Gen0     | Gen1    | Allocated | Alloc Ratio |
|----------------------------------- |------------------- |-------------- |-----------:|---------:|--------:|------:|---------:|--------:|----------:|------------:|
| **LeanCorpus_CombinedFieldsQuery**     | **1**                  | **100000**        | **2,366.8 μs** |  **4.89 μs** | **4.57 μs** |  **1.00** | **117.1875** | **11.7188** | **487.61 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_MultiField | 1                  | 100000        |   528.8 μs | 10.04 μs | 8.38 μs |  0.22 |   4.8828 |       - |  21.43 KB |        0.04 |
| LuceneNet_BooleanQuery_MultiField  | 1                  | 100000        |   670.4 μs |  1.86 μs | 1.74 μs |  0.28 | 187.5000 |  3.9063 | 771.69 KB |        1.58 |
|                                    |                    |               |            |          |         |       |          |         |           |             |
| **LeanCorpus_CombinedFieldsQuery**     | **2**                  | **100000**        | **2,384.8 μs** |  **3.58 μs** | **3.34 μs** |  **1.00** | **117.1875** | **11.7188** | **487.61 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_MultiField | 2                  | 100000        |   526.8 μs |  7.09 μs | 6.64 μs |  0.22 |   4.8828 |       - |  21.43 KB |        0.04 |
| LuceneNet_BooleanQuery_MultiField  | 2                  | 100000        |   670.9 μs |  2.21 μs | 2.07 μs |  0.28 | 186.5234 |  4.8828 | 771.69 KB |        1.58 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-combined"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-combined" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-combined" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-combined" style="max-width:960px"><canvas id="chart-bench-combined" style="height:500px"></canvas></div>
<p><a href="debian-combined.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


