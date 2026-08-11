---
title: Benchmarks - Combined queries
---

# Combined queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                             | MinimumShouldMatch | DocumentCount | Mean       | Error   | StdDev  | Ratio | Gen0     | Gen1   | Allocated | Alloc Ratio |
|----------------------------------- |------------------- |-------------- |-----------:|--------:|--------:|------:|---------:|-------:|----------:|------------:|
| **LeanCorpus_CombinedFieldsQuery**     | **1**                  | **100000**        | **2,365.2 μs** | **3.50 μs** | **3.27 μs** |  **1.00** | **117.1875** |      **-** | **486.99 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_MultiField | 1                  | 100000        |   537.5 μs | 4.79 μs | 4.24 μs |  0.23 |   4.8828 |      - |  21.14 KB |        0.04 |
| LuceneNet_BooleanQuery_MultiField  | 1                  | 100000        |   671.3 μs | 2.62 μs | 2.45 μs |  0.28 | 186.5234 | 3.9063 | 771.68 KB |        1.58 |
|                                    |                    |               |            |         |         |       |          |        |           |             |
| **LeanCorpus_CombinedFieldsQuery**     | **2**                  | **100000**        | **2,335.3 μs** | **3.91 μs** | **3.66 μs** |  **1.00** | **117.1875** |      **-** | **486.99 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_MultiField | 2                  | 100000        |   543.4 μs | 5.92 μs | 5.25 μs |  0.23 |   4.8828 |      - |  21.14 KB |        0.04 |
| LuceneNet_BooleanQuery_MultiField  | 2                  | 100000        |   672.5 μs | 1.64 μs | 1.54 μs |  0.29 | 187.5000 | 3.9063 | 771.69 KB |        1.58 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-combined"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-combined" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-combined" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-combined" style="max-width:960px"><canvas id="chart-bench-combined" style="height:500px"></canvas></div>
<p><a href="combined.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


