---
title: Benchmarks - Term in set
---

# Term in set

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                         | SetSize | DocumentCount | Mean      | Error     | StdDev    | Ratio | Gen0      | Gen1     | Allocated  | Alloc Ratio |
|------------------------------- |-------- |-------------- |----------:|----------:|----------:|------:|----------:|---------:|-----------:|------------:|
| **LeanCorpus_TermInSetQuery**      | **5**       | **100000**        |  **2.875 ms** | **0.0019 ms** | **0.0016 ms** |  **1.00** |         **-** |        **-** |    **3.72 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_Should | 5       | 100000        |  1.913 ms | 0.0099 ms | 0.0092 ms |  0.67 |    5.8594 |        - |   24.66 KB |        6.63 |
| LuceneNet_BooleanQuery_Should  | 5       | 100000        |  2.031 ms | 0.0048 ms | 0.0045 ms |  0.71 |  199.2188 |  15.6250 |  827.47 KB |      222.51 |
|                                |         |               |           |           |           |       |           |          |            |             |
| **LeanCorpus_TermInSetQuery**      | **20**      | **100000**        |  **6.427 ms** | **0.0101 ms** | **0.0095 ms** |  **1.00** |         **-** |        **-** |   **11.24 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_Should | 20      | 100000        |  4.799 ms | 0.0610 ms | 0.0571 ms |  0.75 |   15.6250 |        - |   85.05 KB |        7.57 |
| LuceneNet_BooleanQuery_Should  | 20      | 100000        |  4.830 ms | 0.0099 ms | 0.0093 ms |  0.75 |  406.2500 |  15.6250 | 1704.62 KB |      151.63 |
|                                |         |               |           |           |           |       |           |          |            |             |
| **LeanCorpus_TermInSetQuery**      | **100**     | **100000**        | **12.950 ms** | **0.0219 ms** | **0.0205 ms** |  **1.00** |         **-** |        **-** |   **50.71 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_Should | 100     | 100000        | 12.144 ms | 0.1630 ms | 0.1525 ms |  0.94 |  171.8750 | 156.2500 |  999.95 KB |       19.72 |
| LuceneNet_BooleanQuery_Should  | 100     | 100000        | 11.726 ms | 0.0550 ms | 0.0515 ms |  0.91 | 1265.6250 | 406.2500 | 5961.79 KB |      117.56 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-terminset"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-terminset" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-terminset" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-terminset" style="max-width:960px"><canvas id="chart-bench-terminset" style="height:500px"></canvas></div>
<p><a href="debian-terminset.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


