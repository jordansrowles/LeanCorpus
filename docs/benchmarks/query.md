---
title: Benchmarks - Term queries
---

# Term queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `66ba120` &nbsp;&middot;&nbsp; 24 August 2026 21:47 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method               | QueryTerm  | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|--------------------- |----------- |-------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_TermQuery** | **government** | **100000**        | **115.8 μs** | **0.19 μs** | **0.18 μs** |  **1.00** |  **0.8545** |      **-** |   **3.68 KB** |        **1.00** |
| LuceneNet_TermQuery  | government | 100000        | 150.6 μs | 0.28 μs | 0.26 μs |  1.30 | 11.9629 | 0.2441 |  49.96 KB |       13.58 |
|                      |            |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_TermQuery** | **people**     | **100000**        | **159.3 μs** | **0.30 μs** | **0.28 μs** |  **1.00** |  **0.7324** |      **-** |   **3.67 KB** |        **1.00** |
| LuceneNet_TermQuery  | people     | 100000        | 193.6 μs | 0.35 μs | 0.32 μs |  1.22 | 11.4746 | 0.2441 |  47.88 KB |       13.04 |
|                      |            |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_TermQuery** | **said**       | **100000**        | **692.6 μs** | **0.79 μs** | **0.74 μs** |  **1.00** |       **-** |      **-** |   **3.66 KB** |        **1.00** |
| LuceneNet_TermQuery  | said       | 100000        | 768.4 μs | 1.13 μs | 1.06 μs |  1.11 | 10.7422 |      - |  47.73 KB |       13.03 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-query"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-query" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-query" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-query" style="max-width:960px"><canvas id="chart-bench-query" style="height:500px"></canvas></div>
<p><a href="query.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


