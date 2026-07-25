---
title: Benchmarks - Gutenberg search
---

# Gutenberg search

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                     | SearchTerm | Mean     | Error    | StdDev   | Ratio | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------------------- |----------- |---------:|---------:|---------:|------:|-------:|-------:|----------:|------------:|
| **LeanCorpus_Standard_Search** | **death**      | **11.39 μs** | **0.016 μs** | **0.014 μs** |  **1.00** | **0.1221** |      **-** |     **552 B** |        **1.00** |
| LeanCorpus_English_Search  | death      | 11.04 μs | 0.021 μs | 0.020 μs |  0.97 | 0.1221 |      - |     552 B |        1.00 |
| LuceneNet_Search           | death      | 22.21 μs | 0.023 μs | 0.020 μs |  1.95 | 2.6550 | 0.0305 |   11231 B |       20.35 |
|                            |            |          |          |          |       |        |        |           |             |
| **LeanCorpus_Standard_Search** | **love**       | **14.71 μs** | **0.033 μs** | **0.029 μs** |  **1.00** | **0.1221** |      **-** |     **544 B** |        **1.00** |
| LeanCorpus_English_Search  | love       | 19.08 μs | 0.060 μs | 0.053 μs |  1.30 | 0.1221 |      - |     544 B |        1.00 |
| LuceneNet_Search           | love       | 29.38 μs | 0.067 μs | 0.062 μs |  2.00 | 2.6245 | 0.0305 |   11175 B |       20.54 |
|                            |            |          |          |          |       |        |        |           |             |
| **LeanCorpus_Standard_Search** | **man**        | **38.37 μs** | **0.078 μs** | **0.073 μs** |  **1.00** | **0.1221** |      **-** |     **544 B** |        **1.00** |
| LeanCorpus_English_Search  | man        | 38.08 μs | 0.047 μs | 0.041 μs |  0.99 | 0.1221 |      - |     544 B |        1.00 |
| LuceneNet_Search           | man        | 46.51 μs | 0.052 μs | 0.048 μs |  1.21 | 2.6245 | 0.0610 |   11038 B |       20.29 |
|                            |            |          |          |          |       |        |        |           |             |
| **LeanCorpus_Standard_Search** | **night**      | **23.98 μs** | **0.039 μs** | **0.034 μs** |  **1.00** | **0.1221** |      **-** |     **552 B** |        **1.00** |
| LeanCorpus_English_Search  | night      | 25.15 μs | 0.040 μs | 0.037 μs |  1.05 | 0.1221 |      - |     552 B |        1.00 |
| LuceneNet_Search           | night      | 34.76 μs | 0.056 μs | 0.052 μs |  1.45 | 2.6245 | 0.0610 |   11223 B |       20.33 |
|                            |            |          |          |          |       |        |        |           |             |
| **LeanCorpus_Standard_Search** | **sea**        | **12.65 μs** | **0.016 μs** | **0.014 μs** |  **1.00** | **0.1221** |      **-** |     **544 B** |        **1.00** |
| LeanCorpus_English_Search  | sea        | 13.73 μs | 0.011 μs | 0.010 μs |  1.08 | 0.1221 |      - |     544 B |        1.00 |
| LuceneNet_Search           | sea        | 26.52 μs | 0.083 μs | 0.074 μs |  2.10 | 2.6550 | 0.0305 |   11271 B |       20.72 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-gutenberg-search"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-gutenberg-search" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-gutenberg-search" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-gutenberg-search" style="max-width:960px"><canvas id="chart-bench-gutenberg-search" style="height:500px"></canvas></div>
<p><a href="gutenberg-search.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


