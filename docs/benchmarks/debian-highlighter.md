---
title: Benchmarks - Highlighter
---

# Highlighter

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `f3305a5` &nbsp;&middot;&nbsp; 16 July 2026 20:51 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                         | MaxSnippetLength | DocumentCount | Mean        | Error    | StdDev   | Ratio | RatioSD | Gen0        | Allocated  | Alloc Ratio |
|------------------------------- |----------------- |-------------- |------------:|---------:|---------:|------:|--------:|------------:|-----------:|------------:|
| **LeanCorpus_Highlight_TwoTerms**  | **100**              | **100000**        |    **45.00 ms** | **0.117 ms** | **0.109 ms** |  **1.00** |    **0.00** |  **11916.6667** |   **47.55 MB** |        **1.00** |
| LeanCorpus_Highlight_FiveTerms | 100              | 100000        |   117.76 ms | 0.264 ms | 0.247 ms |  2.62 |    0.01 |  11200.0000 |   44.68 MB |        0.94 |
| LuceneNet_Highlight_TwoTerms   | 100              | 100000        | 2,546.01 ms | 6.550 ms | 6.127 ms | 56.58 |    0.19 | 686000.0000 | 2738.15 MB |       57.58 |
| LuceneNet_Highlight_FiveTerms  | 100              | 100000        | 2,699.16 ms | 7.831 ms | 7.325 ms | 59.98 |    0.21 | 744000.0000 | 2967.98 MB |       62.41 |
|                                |                  |               |             |          |          |       |         |             |            |             |
| **LeanCorpus_Highlight_TwoTerms**  | **200**              | **100000**        |    **44.48 ms** | **0.142 ms** | **0.133 ms** |  **1.00** |    **0.00** |  **11583.3333** |   **46.51 MB** |        **1.00** |
| LeanCorpus_Highlight_FiveTerms | 200              | 100000        |   116.73 ms | 0.380 ms | 0.356 ms |  2.62 |    0.01 |   9400.0000 |    38.2 MB |        0.82 |
| LuceneNet_Highlight_TwoTerms   | 200              | 100000        | 2,598.37 ms | 9.961 ms | 8.830 ms | 58.42 |    0.26 | 686000.0000 | 2738.15 MB |       58.87 |
| LuceneNet_Highlight_FiveTerms  | 200              | 100000        | 2,720.86 ms | 7.506 ms | 7.021 ms | 61.18 |    0.23 | 744000.0000 | 2967.98 MB |       63.82 |
|                                |                  |               |             |          |          |       |         |             |            |             |
| **LeanCorpus_Highlight_TwoTerms**  | **500**              | **100000**        |    **46.60 ms** | **0.154 ms** | **0.144 ms** |  **1.00** |    **0.00** |  **16090.9091** |   **64.35 MB** |        **1.00** |
| LeanCorpus_Highlight_FiveTerms | 500              | 100000        |   121.31 ms | 0.444 ms | 0.394 ms |  2.60 |    0.01 |  14800.0000 |   59.37 MB |        0.92 |
| LuceneNet_Highlight_TwoTerms   | 500              | 100000        | 2,523.97 ms | 9.424 ms | 8.354 ms | 54.17 |    0.24 | 686000.0000 | 2738.15 MB |       42.55 |
| LuceneNet_Highlight_FiveTerms  | 500              | 100000        | 2,728.67 ms | 8.085 ms | 7.563 ms | 58.56 |    0.24 | 744000.0000 | 2967.98 MB |       46.13 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-highlighter"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-highlighter" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-highlighter" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-highlighter" style="max-width:960px"><canvas id="chart-bench-highlighter" style="height:500px"></canvas></div>
<p><a href="debian-highlighter.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


