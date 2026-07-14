---
title: Benchmarks - Phrase queries
---

# Phrase queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                 | PhraseType     | DocumentCount | Mean       | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|----------------------- |--------------- |-------------- |-----------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_PhraseQuery** | **ExactThreeWord** | **100000**        |   **347.5 μs** | **1.30 μs** | **1.22 μs** |  **1.00** | **11.7188** |      **-** |  **47.55 KB** |        **1.00** |
| LuceneNet_PhraseQuery  | ExactThreeWord | 100000        |   319.9 μs | 0.62 μs | 0.58 μs |  0.92 | 30.7617 | 1.4648 | 128.39 KB |        2.70 |
|                        |                |               |            |         |         |       |         |        |           |             |
| **LeanCorpus_PhraseQuery** | **ExactTwoWord**   | **100000**        |   **266.5 μs** | **1.33 μs** | **1.25 μs** |  **1.00** |  **8.3008** |      **-** |  **34.28 KB** |        **1.00** |
| LuceneNet_PhraseQuery  | ExactTwoWord   | 100000        |   261.3 μs | 0.55 μs | 0.52 μs |  0.98 | 25.8789 | 0.9766 | 106.74 KB |        3.11 |
|                        |                |               |            |         |         |       |         |        |           |             |
| **LeanCorpus_PhraseQuery** | **SlopTwoWord**    | **100000**        |   **767.6 μs** | **9.27 μs** | **8.67 μs** |  **1.00** |  **8.7891** |      **-** |  **38.48 KB** |        **1.00** |
| LuceneNet_PhraseQuery  | SlopTwoWord    | 100000        | 1,023.3 μs | 0.88 μs | 0.78 μs |  1.33 | 11.7188 | 1.9531 |  51.84 KB |        1.35 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-phrase"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-phrase" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-phrase" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-phrase" style="max-width:960px"><canvas id="chart-bench-phrase" style="height:500px"></canvas></div>
<p><a href="debian-phrase.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


