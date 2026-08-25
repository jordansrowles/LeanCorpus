---
title: Benchmarks - Term in set
---

# Term in set

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `66ba120` &nbsp;&middot;&nbsp; 25 August 2026 10:26 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                         | SetSize | DocumentCount | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0      | Gen1     | Gen2    | Allocated  | Alloc Ratio |
|------------------------------- |-------- |-------------- |----------:|----------:|----------:|------:|--------:|----------:|---------:|--------:|-----------:|------------:|
| **LeanCorpus_TermInSetQuery**      | **5**       | **100000**        |  **1.544 ms** | **0.0036 ms** | **0.0032 ms** |  **1.00** |    **0.00** |         **-** |        **-** |       **-** |    **6.85 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_Should | 5       | 100000        |  1.990 ms | 0.0122 ms | 0.0102 ms |  1.29 |    0.01 |    7.8125 |        - |       - |   33.67 KB |        4.91 |
| LuceneNet_BooleanQuery_Should  | 5       | 100000        |  2.067 ms | 0.0084 ms | 0.0079 ms |  1.34 |    0.01 |  199.2188 |  15.6250 |       - |  827.47 KB |      120.77 |
|                                |         |               |           |           |           |       |         |           |          |         |            |             |
| **LeanCorpus_TermInSetQuery**      | **20**      | **100000**        |  **3.300 ms** | **0.0034 ms** | **0.0030 ms** |  **1.00** |    **0.00** |    **3.9063** |        **-** |       **-** |   **21.21 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_Should | 20      | 100000        |  5.182 ms | 0.0400 ms | 0.0374 ms |  1.57 |    0.01 |   23.4375 |        - |       - |  117.91 KB |        5.56 |
| LuceneNet_BooleanQuery_Should  | 20      | 100000        |  4.926 ms | 0.0113 ms | 0.0106 ms |  1.49 |    0.00 |  406.2500 |  15.6250 |       - | 1704.62 KB |       80.37 |
|                                |         |               |           |           |           |       |         |           |          |         |            |             |
| **LeanCorpus_TermInSetQuery**      | **100**     | **100000**        |  **6.655 ms** | **0.0093 ms** | **0.0078 ms** |  **1.00** |    **0.00** |   **23.4375** |        **-** |       **-** |   **96.85 KB** |        **1.00** |
| LeanCorpus_BooleanQuery_Should | 100     | 100000        | 12.849 ms | 0.0828 ms | 0.0775 ms |  1.93 |    0.01 |  203.1250 | 171.8750 |       - | 1160.97 KB |       11.99 |
| LuceneNet_BooleanQuery_Should  | 100     | 100000        | 12.034 ms | 0.1185 ms | 0.1108 ms |  1.81 |    0.02 | 1265.6250 | 406.2500 | 15.6250 | 5961.34 KB |       61.55 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-terminset"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-terminset" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-terminset" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-terminset" style="max-width:960px"><canvas id="chart-bench-terminset" style="height:500px"></canvas></div>
<p><a href="terminset.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


