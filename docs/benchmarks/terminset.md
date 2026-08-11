---
title: Benchmarks - Term in set
---

# Term in set

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                         | SetSize | DocumentCount | Mean      | Error     | StdDev    | Ratio | Gen0      | Gen1     | Allocated  | Alloc Ratio |
|------------------------------- |-------- |-------------- |----------:|----------:|----------:|------:|----------:|---------:|-----------:|------------:|
| **LeanCorpus_TermInSetQuery**      | **5**       | **100000**        |  **2.828 ms** | **0.0008 ms** | **0.0007 ms** |  **1.00** |         **-** |        **-** |    **3.34 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_Should | 5       | 100000        |  1.939 ms | 0.0197 ms | 0.0184 ms |  0.69 |    3.9063 |        - |   24.29 KB |        7.28 |
| LuceneNet_BooleanQuery_Should  | 5       | 100000        |  2.057 ms | 0.0040 ms | 0.0038 ms |  0.73 |  199.2188 |  15.6250 |  827.47 KB |      248.05 |
|                                |         |               |           |           |           |       |           |          |            |             |
| **LeanCorpus_TermInSetQuery**      | **20**      | **100000**        |  **6.336 ms** | **0.0025 ms** | **0.0019 ms** |  **1.00** |         **-** |        **-** |    **9.69 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_Should | 20      | 100000        |  4.929 ms | 0.0541 ms | 0.0506 ms |  0.78 |   15.6250 |        - |   82.94 KB |        8.56 |
| LuceneNet_BooleanQuery_Should  | 20      | 100000        |  4.844 ms | 0.0129 ms | 0.0108 ms |  0.76 |  406.2500 |  15.6250 | 1704.66 KB |      175.96 |
|                                |         |               |           |           |           |       |           |          |            |             |
| **LeanCorpus_TermInSetQuery**      | **100**     | **100000**        | **12.875 ms** | **0.0122 ms** | **0.0109 ms** |  **1.00** |         **-** |        **-** |   **42.98 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_Should | 100     | 100000        | 12.345 ms | 0.1276 ms | 0.1194 ms |  0.96 |  171.8750 | 156.2500 |     989 KB |       23.01 |
| LuceneNet_BooleanQuery_Should  | 100     | 100000        | 11.754 ms | 0.0447 ms | 0.0418 ms |  0.91 | 1265.6250 | 406.2500 | 5961.79 KB |      138.70 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-terminset"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-terminset" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-terminset" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-terminset" style="max-width:960px"><canvas id="chart-bench-terminset" style="height:500px"></canvas></div>
<p><a href="terminset.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


