---
title: Benchmarks - Gutenberg search
---

# Gutenberg search

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                     | SearchTerm | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------------------- |----------- |---------:|---------:|---------:|------:|--------:|-------:|-------:|----------:|------------:|
| **LeanCorpus_Standard_Search** | **death**      | **11.86 μs** | **0.025 μs** | **0.022 μs** |  **1.00** |    **0.00** | **0.1221** |      **-** |     **544 B** |        **1.00** |
| LeanCorpus_English_Search  | death      | 11.77 μs | 0.029 μs | 0.027 μs |  0.99 |    0.00 | 0.1221 |      - |     544 B |        1.00 |
| LuceneNet_Search           | death      | 23.58 μs | 0.193 μs | 0.181 μs |  1.99 |    0.02 | 2.6550 | 0.0305 |   11231 B |       20.65 |
|                            |            |          |          |          |       |         |        |        |           |             |
| **LeanCorpus_Standard_Search** | **love**       | **15.47 μs** | **0.041 μs** | **0.039 μs** |  **1.00** |    **0.00** | **0.1221** |      **-** |     **536 B** |        **1.00** |
| LeanCorpus_English_Search  | love       | 19.80 μs | 0.055 μs | 0.051 μs |  1.28 |    0.00 | 0.1221 |      - |     536 B |        1.00 |
| LuceneNet_Search           | love       | 30.18 μs | 0.064 μs | 0.050 μs |  1.95 |    0.01 | 2.6245 | 0.0305 |   11175 B |       20.85 |
|                            |            |          |          |          |       |         |        |        |           |             |
| **LeanCorpus_Standard_Search** | **man**        | **40.35 μs** | **0.092 μs** | **0.082 μs** |  **1.00** |    **0.00** | **0.1221** |      **-** |     **536 B** |        **1.00** |
| LeanCorpus_English_Search  | man        | 40.38 μs | 0.051 μs | 0.048 μs |  1.00 |    0.00 | 0.1221 |      - |     536 B |        1.00 |
| LuceneNet_Search           | man        | 46.19 μs | 0.103 μs | 0.096 μs |  1.14 |    0.00 | 2.6245 | 0.0610 |   11038 B |       20.59 |
|                            |            |          |          |          |       |         |        |        |           |             |
| **LeanCorpus_Standard_Search** | **night**      | **25.94 μs** | **0.135 μs** | **0.120 μs** |  **1.00** |    **0.00** | **0.1221** |      **-** |     **544 B** |        **1.00** |
| LeanCorpus_English_Search  | night      | 26.51 μs | 0.041 μs | 0.034 μs |  1.02 |    0.00 | 0.1221 |      - |     544 B |        1.00 |
| LuceneNet_Search           | night      | 34.55 μs | 0.050 μs | 0.047 μs |  1.33 |    0.01 | 2.6245 | 0.0610 |   11223 B |       20.63 |
|                            |            |          |          |          |       |         |        |        |           |             |
| **LeanCorpus_Standard_Search** | **sea**        | **13.35 μs** | **0.016 μs** | **0.014 μs** |  **1.00** |    **0.00** | **0.1221** |      **-** |     **536 B** |        **1.00** |
| LeanCorpus_English_Search  | sea        | 14.37 μs | 0.034 μs | 0.032 μs |  1.08 |    0.00 | 0.1221 |      - |     536 B |        1.00 |
| LuceneNet_Search           | sea        | 25.60 μs | 0.092 μs | 0.081 μs |  1.92 |    0.01 | 2.6550 | 0.0305 |   11271 B |       21.03 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-gutenberg-search"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-gutenberg-search" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-gutenberg-search" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-gutenberg-search" style="max-width:960px"><canvas id="chart-bench-gutenberg-search" style="height:500px"></canvas></div>
<p><a href="gutenberg-search.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


