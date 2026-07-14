---
title: Benchmarks - Combined queries
---

# Combined queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                             | MinimumShouldMatch | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0     | Gen1   | Allocated | Alloc Ratio |
|----------------------------------- |------------------- |-------------- |---------:|--------:|--------:|------:|---------:|-------:|----------:|------------:|
| **LeanCorpus_CombinedFieldsQuery**     | **1**                  | **100000**        | **634.2 μs** | **3.96 μs** | **3.70 μs** |  **1.00** |  **94.7266** |      **-** | **385.93 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_MultiField | 1                  | 100000        | 280.7 μs | 1.08 μs | 1.01 μs |  0.44 |   4.8828 |      - |  20.15 KB |        0.05 |
| LuceneNet_BooleanQuery_MultiField  | 1                  | 100000        | 474.8 μs | 0.92 μs | 0.86 μs |  0.75 | 111.8164 | 2.4414 | 462.68 KB |        1.20 |
|                                    |                    |               |          |         |         |       |          |        |           |             |
| **LeanCorpus_CombinedFieldsQuery**     | **2**                  | **100000**        | **632.7 μs** | **1.98 μs** | **1.76 μs** |  **1.00** |  **93.7500** | **5.8594** |    **386 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_MultiField | 2                  | 100000        | 276.3 μs | 2.85 μs | 2.66 μs |  0.44 |   4.8828 |      - |  20.15 KB |        0.05 |
| LuceneNet_BooleanQuery_MultiField  | 2                  | 100000        | 480.6 μs | 1.18 μs | 0.98 μs |  0.76 | 111.8164 | 2.4414 | 462.68 KB |        1.20 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-combined"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-combined" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-combined" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-combined" style="max-width:960px"><canvas id="chart-bench-combined" style="height:500px"></canvas></div>
<p><a href="debian-combined.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


