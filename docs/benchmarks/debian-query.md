---
title: Benchmarks - Term queries
---

# Term queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `b2e1443` &nbsp;&middot;&nbsp; 27 July 2026 13:28 UTC &nbsp;&middot;&nbsp; 500 docs

| Method               | QueryTerm  | DocumentCount | Mean     | Error | Ratio | Allocated | Alloc Ratio |
|--------------------- |----------- |-------------- |---------:|------:|------:|----------:|------------:|
| **LeanCorpus_TermQuery** | **government** | **500**           | **27.14 ms** |    **NA** |  **1.00** |     **296 B** |        **1.00** |
| LuceneNet_TermQuery  | government | 500           | 15.55 ms |    NA |  0.57 |    7352 B |       24.84 |
|                      |            |               |          |       |       |           |             |
| **LeanCorpus_TermQuery** | **people**     | **500**           | **42.24 ms** |    **NA** |  **1.00** |     **392 B** |        **1.00** |
| LuceneNet_TermQuery  | people     | 500           | 24.93 ms |    NA |  0.59 |   11104 B |       28.33 |
|                      |            |               |          |       |       |           |             |
| **LeanCorpus_TermQuery** | **said**       | **500**           | **46.92 ms** |    **NA** |  **1.00** |     **504 B** |        **1.00** |
| LuceneNet_TermQuery  | said       | 500           | 25.33 ms |    NA |  0.54 |   11096 B |       22.02 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-query"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-query" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-query" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-query" style="max-width:960px"><canvas id="chart-bench-query" style="height:500px"></canvas></div>
<p><a href="debian-query.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


