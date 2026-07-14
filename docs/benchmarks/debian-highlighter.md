---
title: Benchmarks - Highlighter
---

# Highlighter

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                         | MaxSnippetLength | DocumentCount | Mean        | Error    | StdDev   | Ratio | RatioSD | Gen0        | Allocated  | Alloc Ratio |
|------------------------------- |----------------- |-------------- |------------:|---------:|---------:|------:|--------:|------------:|-----------:|------------:|
| **LeanCorpus_Highlight_TwoTerms**  | **100**              | **100000**        |    **34.58 ms** | **0.068 ms** | **0.064 ms** |  **1.00** |    **0.00** |  **12200.0000** |   **48.85 MB** |        **1.00** |
| LeanCorpus_Highlight_FiveTerms | 100              | 100000        |    81.02 ms | 0.204 ms | 0.191 ms |  2.34 |    0.01 |  11857.1429 |   47.31 MB |        0.97 |
| LuceneNet_Highlight_TwoTerms   | 100              | 100000        | 1,600.65 ms | 1.789 ms | 1.494 ms | 46.29 |    0.09 | 447000.0000 | 1785.08 MB |       36.54 |
| LuceneNet_Highlight_FiveTerms  | 100              | 100000        | 1,715.85 ms | 5.137 ms | 4.805 ms | 49.62 |    0.16 | 498000.0000 | 1988.44 MB |       40.70 |
|                                |                  |               |             |          |          |       |         |             |            |             |
| **LeanCorpus_Highlight_TwoTerms**  | **200**              | **100000**        |    **31.30 ms** | **0.021 ms** | **0.016 ms** |  **1.00** |    **0.00** |   **7000.0000** |   **28.02 MB** |        **1.00** |
| LeanCorpus_Highlight_FiveTerms | 200              | 100000        |    79.55 ms | 0.177 ms | 0.165 ms |  2.54 |    0.01 |   5857.1429 |   23.58 MB |        0.84 |
| LuceneNet_Highlight_TwoTerms   | 200              | 100000        | 1,624.96 ms | 2.936 ms | 2.603 ms | 51.92 |    0.08 | 447000.0000 | 1785.08 MB |       63.70 |
| LuceneNet_Highlight_FiveTerms  | 200              | 100000        | 1,736.09 ms | 3.821 ms | 3.574 ms | 55.47 |    0.11 | 498000.0000 | 1988.44 MB |       70.95 |
|                                |                  |               |             |          |          |       |         |             |            |             |
| **LeanCorpus_Highlight_TwoTerms**  | **500**              | **100000**        |    **32.13 ms** | **0.073 ms** | **0.065 ms** |  **1.00** |    **0.00** |   **9375.0000** |    **37.5 MB** |        **1.00** |
| LeanCorpus_Highlight_FiveTerms | 500              | 100000        |    81.69 ms | 0.148 ms | 0.131 ms |  2.54 |    0.01 |   8714.2857 |   34.87 MB |        0.93 |
| LuceneNet_Highlight_TwoTerms   | 500              | 100000        | 1,586.41 ms | 2.980 ms | 2.642 ms | 49.37 |    0.12 | 447000.0000 | 1785.08 MB |       47.61 |
| LuceneNet_Highlight_FiveTerms  | 500              | 100000        | 1,760.23 ms | 4.129 ms | 3.862 ms | 54.78 |    0.16 | 498000.0000 | 1988.44 MB |       53.03 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-highlighter"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-highlighter" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-highlighter" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-highlighter" style="max-width:960px"><canvas id="chart-bench-highlighter" style="height:500px"></canvas></div>
<p><a href="debian-highlighter.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


