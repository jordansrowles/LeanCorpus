---
title: Benchmarks - Regexp queries
---

# Regexp queries

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `66ba120` &nbsp;&middot;&nbsp; 24 August 2026 21:47 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                 | Pattern    | DocumentCount | Mean        | Error    | StdDev   | Ratio | Gen0     | Gen1   | Allocated  | Alloc Ratio |
|----------------------- |----------- |-------------- |------------:|---------:|---------:|------:|---------:|-------:|-----------:|------------:|
| **LeanCorpus_RegexpQuery** | **.*nation.*** | **100000**        | **38,577.6 μs** | **53.01 μs** | **49.59 μs** |  **1.00** |        **-** |      **-** |   **86.84 KB** |        **1.00** |
| LuceneNet_RegexpQuery  | .*nation.* | 100000        | 28,599.7 μs | 34.20 μs | 30.32 μs |  0.74 | 312.5000 |      - | 1342.77 KB |       15.46 |
|                        |            |               |             |          |          |       |          |        |            |             |
| **LeanCorpus_RegexpQuery** | **gov.*ment**  | **100000**        |    **248.6 μs** |  **0.57 μs** |  **0.54 μs** |  **1.00** |  **11.2305** |      **-** |   **46.52 KB** |        **1.00** |
| LuceneNet_RegexpQuery  | gov.*ment  | 100000        |    417.5 μs |  1.24 μs |  1.16 μs |  1.68 |  89.8438 | 0.9766 |  369.09 KB |        7.93 |
|                        |            |               |             |          |          |       |          |        |            |             |
| **LeanCorpus_RegexpQuery** | **mark.***     | **100000**        |    **491.6 μs** |  **0.92 μs** |  **0.86 μs** |  **1.00** |  **21.4844** |      **-** |   **88.58 KB** |        **1.00** |
| LuceneNet_RegexpQuery  | mark.*     | 100000        |    431.6 μs |  0.52 μs |  0.48 μs |  0.88 |  40.5273 | 0.4883 |  166.42 KB |        1.88 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-regexp"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-regexp" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-regexp" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-regexp" style="max-width:960px"><canvas id="chart-bench-regexp" style="height:500px"></canvas></div>
<p><a href="regexp.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


