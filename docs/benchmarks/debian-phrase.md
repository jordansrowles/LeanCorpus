---
title: Benchmarks - Phrase queries
---

# Phrase queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `f3305a5` &nbsp;&middot;&nbsp; 16 July 2026 20:51 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                 | PhraseType     | DocumentCount | Mean       | Error    | StdDev   | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|----------------------- |--------------- |-------------- |-----------:|---------:|---------:|------:|--------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_PhraseQuery** | **ExactThreeWord** | **100000**        |   **564.6 μs** |  **6.95 μs** |  **6.50 μs** |  **1.00** |    **0.00** | **15.6250** |      **-** |  **62.62 KB** |        **1.00** |
| LuceneNet_PhraseQuery  | ExactThreeWord | 100000        |   557.9 μs |  1.10 μs |  1.03 μs |  0.99 |    0.01 | 44.9219 | 2.9297 | 188.92 KB |        3.02 |
|                        |                |               |            |          |          |       |         |         |        |           |             |
| **LeanCorpus_PhraseQuery** | **ExactTwoWord**   | **100000**        |   **476.9 μs** |  **3.18 μs** |  **2.97 μs** |  **1.00** |    **0.00** | **10.7422** |      **-** |  **44.59 KB** |        **1.00** |
| LuceneNet_PhraseQuery  | ExactTwoWord   | 100000        |   478.0 μs |  0.50 μs |  0.44 μs |  1.00 |    0.01 | 38.0859 | 0.9766 | 157.44 KB |        3.53 |
|                        |                |               |            |          |          |       |         |         |        |           |             |
| **LeanCorpus_PhraseQuery** | **SlopTwoWord**    | **100000**        | **1,303.9 μs** | **25.37 μs** | **27.15 μs** |  **1.00** |    **0.00** | **13.6719** |      **-** |  **53.71 KB** |        **1.00** |
| LuceneNet_PhraseQuery  | SlopTwoWord    | 100000        | 1,961.0 μs |  1.26 μs |  0.98 μs |  1.50 |    0.03 | 15.6250 |      - |  73.71 KB |        1.37 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-phrase"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-phrase" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-phrase" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-phrase" style="max-width:960px"><canvas id="chart-bench-phrase" style="height:500px"></canvas></div>
<p><a href="debian-phrase.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


