---
title: Benchmarks - Gutenberg search
---

# Gutenberg search

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `66ba120` &nbsp;&middot;&nbsp; 24 August 2026 21:47 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                     | SearchTerm | Mean     | Error    | StdDev   | Ratio | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------------------- |----------- |---------:|---------:|---------:|------:|-------:|-------:|----------:|------------:|
| **LeanCorpus_Standard_Search** | **death**      | **13.11 μs** | **0.018 μs** | **0.017 μs** |  **1.00** | **0.2594** |      **-** |   **1.11 KB** |        **1.00** |
| LeanCorpus_English_Search  | death      | 12.98 μs | 0.029 μs | 0.026 μs |  0.99 | 0.2594 |      - |   1.11 KB |        1.00 |
| LuceneNet_Search           | death      | 22.00 μs | 0.216 μs | 0.191 μs |  1.68 | 2.6550 | 0.0305 |  10.97 KB |        9.89 |
|                            |            |          |          |          |       |        |        |           |             |
| **LeanCorpus_Standard_Search** | **love**       | **16.46 μs** | **0.037 μs** | **0.035 μs** |  **1.00** | **0.2441** |      **-** |    **1.1 KB** |        **1.00** |
| LeanCorpus_English_Search  | love       | 20.99 μs | 0.029 μs | 0.027 μs |  1.28 | 0.2441 |      - |    1.1 KB |        1.00 |
| LuceneNet_Search           | love       | 28.29 μs | 0.050 μs | 0.047 μs |  1.72 | 2.6245 | 0.0305 |  10.91 KB |        9.91 |
|                            |            |          |          |          |       |        |        |           |             |
| **LeanCorpus_Standard_Search** | **man**        | **41.15 μs** | **0.085 μs** | **0.080 μs** |  **1.00** | **0.2441** |      **-** |    **1.1 KB** |        **1.00** |
| LeanCorpus_English_Search  | man        | 41.97 μs | 0.051 μs | 0.045 μs |  1.02 | 0.2441 |      - |    1.1 KB |        1.00 |
| LuceneNet_Search           | man        | 47.96 μs | 0.068 μs | 0.064 μs |  1.17 | 2.6245 | 0.0610 |  10.78 KB |        9.79 |
|                            |            |          |          |          |       |        |        |           |             |
| **LeanCorpus_Standard_Search** | **night**      | **27.18 μs** | **0.045 μs** | **0.038 μs** |  **1.00** | **0.2441** |      **-** |   **1.11 KB** |        **1.00** |
| LeanCorpus_English_Search  | night      | 28.37 μs | 0.065 μs | 0.054 μs |  1.04 | 0.2441 |      - |   1.11 KB |        1.00 |
| LuceneNet_Search           | night      | 35.38 μs | 0.102 μs | 0.091 μs |  1.30 | 2.6245 | 0.0610 |  10.96 KB |        9.88 |
|                            |            |          |          |          |       |        |        |           |             |
| **LeanCorpus_Standard_Search** | **sea**        | **14.68 μs** | **0.060 μs** | **0.056 μs** |  **1.00** | **0.2594** |      **-** |    **1.1 KB** |        **1.00** |
| LeanCorpus_English_Search  | sea        | 15.66 μs | 0.018 μs | 0.015 μs |  1.07 | 0.2441 |      - |    1.1 KB |        1.00 |
| LuceneNet_Search           | sea        | 26.19 μs | 0.158 μs | 0.148 μs |  1.78 | 2.6550 | 0.0305 |  11.01 KB |        9.99 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-gutenberg-search"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-gutenberg-search" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-gutenberg-search" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-gutenberg-search" style="max-width:960px"><canvas id="chart-bench-gutenberg-search" style="height:500px"></canvas></div>
<p><a href="gutenberg-search.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


