---
title: Benchmarks - Gutenberg search
---

# Gutenberg search

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `f3305a5` &nbsp;&middot;&nbsp; 16 July 2026 20:51 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                     | SearchTerm | Mean       | Error    | StdDev   | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------------------- |----------- |-----------:|---------:|---------:|------:|--------:|-------:|-------:|----------:|------------:|
| **LeanCorpus_Standard_Search** | **death**      |   **325.6 ns** |  **0.28 ns** |  **0.24 ns** |  **1.00** |    **0.00** | **0.0648** |      **-** |     **272 B** |        **1.00** |
| LeanCorpus_English_Search  | death      |   324.3 ns |  0.49 ns |  0.43 ns |  1.00 |    0.00 | 0.0648 |      - |     272 B |        1.00 |
| LuceneNet_Search           | death      | 9,037.5 ns | 58.32 ns | 51.70 ns | 27.76 |    0.15 | 3.3569 | 0.0305 |   14123 B |       51.92 |
|                            |            |            |          |          |       |         |        |        |           |             |
| **LeanCorpus_Standard_Search** | **love**       |   **325.7 ns** |  **0.37 ns** |  **0.33 ns** |  **1.00** |    **0.00** | **0.0629** |      **-** |     **264 B** |        **1.00** |
| LeanCorpus_English_Search  | love       |   324.2 ns |  0.62 ns |  0.55 ns |  1.00 |    0.00 | 0.0629 |      - |     264 B |        1.00 |
| LuceneNet_Search           | love       | 9,400.9 ns | 78.99 ns | 61.67 ns | 28.86 |    0.18 | 3.3264 | 0.0153 |   14010 B |       53.07 |
|                            |            |            |          |          |       |         |        |        |           |             |
| **LeanCorpus_Standard_Search** | **man**        |   **325.3 ns** |  **0.82 ns** |  **0.68 ns** |  **1.00** |    **0.00** | **0.0629** |      **-** |     **264 B** |        **1.00** |
| LeanCorpus_English_Search  | man        |   325.9 ns |  0.51 ns |  0.45 ns |  1.00 |    0.00 | 0.0629 |      - |     264 B |        1.00 |
| LuceneNet_Search           | man        | 9,400.5 ns | 60.73 ns | 56.81 ns | 28.90 |    0.18 | 3.3264 | 0.0153 |   14010 B |       53.07 |
|                            |            |            |          |          |       |         |        |        |           |             |
| **LeanCorpus_Standard_Search** | **night**      |   **322.1 ns** |  **0.39 ns** |  **0.35 ns** |  **1.00** |    **0.00** | **0.0648** |      **-** |     **272 B** |        **1.00** |
| LeanCorpus_English_Search  | night      |   329.5 ns |  0.23 ns |  0.18 ns |  1.02 |    0.00 | 0.0648 |      - |     272 B |        1.00 |
| LuceneNet_Search           | night      | 9,523.8 ns | 12.40 ns | 10.99 ns | 29.57 |    0.05 | 3.3264 | 0.0153 |   14027 B |       51.57 |
|                            |            |            |          |          |       |         |        |        |           |             |
| **LeanCorpus_Standard_Search** | **sea**        |   **321.8 ns** |  **0.45 ns** |  **0.42 ns** |  **1.00** |    **0.00** | **0.0629** |      **-** |     **264 B** |        **1.00** |
| LeanCorpus_English_Search  | sea        |   324.8 ns |  0.26 ns |  0.22 ns |  1.01 |    0.00 | 0.0629 |      - |     264 B |        1.00 |
| LuceneNet_Search           | sea        | 9,376.0 ns | 63.69 ns | 56.46 ns | 29.14 |    0.17 | 3.3264 | 0.0153 |   14010 B |       53.07 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-gutenberg-search"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-gutenberg-search" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-gutenberg-search" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-gutenberg-search" style="max-width:960px"><canvas id="chart-bench-gutenberg-search" style="height:500px"></canvas></div>
<p><a href="debian-gutenberg-search.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


