---
title: Benchmarks - Term queries
---

# Term queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method               | QueryTerm  | DocumentCount | Mean      | Error    | StdDev   | Ratio | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------------- |----------- |-------------- |----------:|---------:|---------:|------:|-------:|-------:|----------:|------------:|
| **LeanCorpus_TermQuery** | **government** | **100000**        | **104.72 μs** | **0.153 μs** | **0.143 μs** |  **1.00** | **0.1221** |      **-** |     **720 B** |        **1.00** |
| LuceneNet_TermQuery  | government | 100000        |  97.53 μs | 0.215 μs | 0.202 μs |  0.93 | 5.2490 | 0.1221 |   22583 B |       31.37 |
|                      |            |               |           |          |          |       |        |        |           |             |
| **LeanCorpus_TermQuery** | **people**     | **100000**        |  **26.22 μs** | **0.031 μs** | **0.028 μs** |  **1.00** | **0.1526** |      **-** |     **712 B** |        **1.00** |
| LuceneNet_TermQuery  | people     | 100000        |  41.55 μs | 0.190 μs | 0.178 μs |  1.58 | 5.1270 | 0.0610 |   21787 B |       30.60 |
|                      |            |               |           |          |          |       |        |        |           |             |
| **LeanCorpus_TermQuery** | **said**       | **100000**        | **639.46 μs** | **0.833 μs** | **0.780 μs** |  **1.00** |      **-** |      **-** |     **704 B** |        **1.00** |
| LuceneNet_TermQuery  | said       | 100000        | 518.50 μs | 0.355 μs | 0.277 μs |  0.81 | 4.8828 |      - |   21317 B |       30.28 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-query"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-query" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-query" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-query" style="max-width:960px"><canvas id="chart-bench-query" style="height:500px"></canvas></div>
<p><a href="debian-query.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


