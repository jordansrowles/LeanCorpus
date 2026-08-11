---
title: Benchmarks - Term queries
---

# Term queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method               | QueryTerm  | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|--------------------- |----------- |-------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_TermQuery** | **government** | **100000**        | **109.3 μs** | **0.09 μs** | **0.07 μs** |  **1.00** |  **0.1221** |      **-** |     **808 B** |        **1.00** |
| LuceneNet_TermQuery  | government | 100000        | 151.0 μs | 0.25 μs | 0.22 μs |  1.38 | 11.9629 | 0.2441 |   51159 B |       63.32 |
|                      |            |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_TermQuery** | **people**     | **100000**        | **154.9 μs** | **0.21 μs** | **0.19 μs** |  **1.00** |       **-** |      **-** |     **800 B** |        **1.00** |
| LuceneNet_TermQuery  | people     | 100000        | 193.1 μs | 0.15 μs | 0.12 μs |  1.25 | 11.4746 | 0.2441 |   49034 B |       61.29 |
|                      |            |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_TermQuery** | **said**       | **100000**        | **686.9 μs** | **0.84 μs** | **0.79 μs** |  **1.00** |       **-** |      **-** |     **792 B** |        **1.00** |
| LuceneNet_TermQuery  | said       | 100000        | 763.3 μs | 0.63 μs | 0.53 μs |  1.11 | 10.7422 |      - |   48874 B |       61.71 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-query"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-query" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-query" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-query" style="max-width:960px"><canvas id="chart-bench-query" style="height:500px"></canvas></div>
<p><a href="query.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


