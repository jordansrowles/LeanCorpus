---
title: Benchmarks - Term queries
---

# Term queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method               | QueryTerm  | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|--------------------- |----------- |-------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_TermQuery** | **government** | **100000**        | **103.8 μs** | **0.21 μs** | **0.18 μs** |  **1.00** |  **0.1221** |      **-** |     **880 B** |        **1.00** |
| LuceneNet_TermQuery  | government | 100000        | 149.6 μs | 0.22 μs | 0.17 μs |  1.44 | 11.9629 | 0.2441 |   51159 B |       58.14 |
|                      |            |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_TermQuery** | **people**     | **100000**        | **143.7 μs** | **0.07 μs** | **0.06 μs** |  **1.00** |       **-** |      **-** |     **872 B** |        **1.00** |
| LuceneNet_TermQuery  | people     | 100000        | 192.9 μs | 0.19 μs | 0.16 μs |  1.34 | 11.4746 | 0.2441 |   49034 B |       56.23 |
|                      |            |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_TermQuery** | **said**       | **100000**        | **643.7 μs** | **0.83 μs** | **0.77 μs** |  **1.00** |       **-** |      **-** |     **864 B** |        **1.00** |
| LuceneNet_TermQuery  | said       | 100000        | 764.0 μs | 0.50 μs | 0.42 μs |  1.19 | 10.7422 |      - |   48874 B |       56.57 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-query"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-query" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-query" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-query" style="max-width:960px"><canvas id="chart-bench-query" style="height:500px"></canvas></div>
<p><a href="query.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


