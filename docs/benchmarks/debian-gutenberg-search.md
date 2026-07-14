---
title: Benchmarks - Gutenberg search
---

# Gutenberg search

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                     | SearchTerm | Mean        | Error    | StdDev   | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------------------- |----------- |------------:|---------:|---------:|------:|--------:|-------:|-------:|----------:|------------:|
| **LeanCorpus_Standard_Search** | **death**      |    **331.1 ns** |  **0.45 ns** |  **0.35 ns** |  **1.00** |    **0.00** | **0.0648** |      **-** |     **272 B** |        **1.00** |
| LeanCorpus_English_Search  | death      |    326.1 ns |  0.86 ns |  0.67 ns |  0.98 |    0.00 | 0.0648 |      - |     272 B |        1.00 |
| LuceneNet_Search           | death      | 12,445.9 ns | 20.23 ns | 18.93 ns | 37.59 |    0.07 | 4.1351 | 0.0153 |   17358 B |       63.82 |
|                            |            |             |          |          |       |         |        |        |           |             |
| **LeanCorpus_Standard_Search** | **love**       |    **322.5 ns** |  **0.44 ns** |  **0.39 ns** |  **1.00** |    **0.00** | **0.0629** |      **-** |     **264 B** |        **1.00** |
| LeanCorpus_English_Search  | love       |    323.2 ns |  0.59 ns |  0.55 ns |  1.00 |    0.00 | 0.0629 |      - |     264 B |        1.00 |
| LuceneNet_Search           | love       | 11,614.6 ns | 64.11 ns | 59.97 ns | 36.02 |    0.18 | 4.1046 | 0.0305 |   17246 B |       65.33 |
|                            |            |             |          |          |       |         |        |        |           |             |
| **LeanCorpus_Standard_Search** | **man**        |    **321.9 ns** |  **0.30 ns** |  **0.25 ns** |  **1.00** |    **0.00** | **0.0629** |      **-** |     **264 B** |        **1.00** |
| LeanCorpus_English_Search  | man        |    321.3 ns |  0.42 ns |  0.33 ns |  1.00 |    0.00 | 0.0629 |      - |     264 B |        1.00 |
| LuceneNet_Search           | man        | 11,128.8 ns |  8.99 ns |  7.50 ns | 34.57 |    0.03 | 4.1046 | 0.0153 |   17246 B |       65.33 |
|                            |            |             |          |          |       |         |        |        |           |             |
| **LeanCorpus_Standard_Search** | **night**      |    **323.5 ns** |  **0.42 ns** |  **0.37 ns** |  **1.00** |    **0.00** | **0.0648** |      **-** |     **272 B** |        **1.00** |
| LeanCorpus_English_Search  | night      |    324.3 ns |  0.41 ns |  0.38 ns |  1.00 |    0.00 | 0.0648 |      - |     272 B |        1.00 |
| LuceneNet_Search           | night      | 12,605.8 ns | 33.21 ns | 31.06 ns | 38.97 |    0.10 | 4.1199 | 0.0305 |   17262 B |       63.46 |
|                            |            |             |          |          |       |         |        |        |           |             |
| **LeanCorpus_Standard_Search** | **sea**        |    **372.1 ns** |  **0.52 ns** |  **0.49 ns** |  **1.00** |    **0.00** | **0.0629** |      **-** |     **264 B** |        **1.00** |
| LeanCorpus_English_Search  | sea        |    322.2 ns |  0.49 ns |  0.41 ns |  0.87 |    0.00 | 0.0629 |      - |     264 B |        1.00 |
| LuceneNet_Search           | sea        | 11,342.2 ns |  8.85 ns |  6.91 ns | 30.48 |    0.04 | 4.1046 | 0.0153 |   17246 B |       65.33 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-gutenberg-search"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-gutenberg-search" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-gutenberg-search" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-gutenberg-search" style="max-width:960px"><canvas id="chart-bench-gutenberg-search" style="height:500px"></canvas></div>
<p><a href="debian-gutenberg-search.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


