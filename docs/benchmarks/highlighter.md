---
title: Benchmarks - Highlighter
---

# Highlighter

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `66ba120` &nbsp;&middot;&nbsp; 24 August 2026 21:47 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                         | MaxSnippetLength | DocumentCount | Mean        | Error      | StdDev       | Ratio | RatioSD | Gen0      | Gen1   | Allocated  | Alloc Ratio |
|------------------------------- |----------------- |-------------- |------------:|-----------:|-------------:|------:|--------:|----------:|-------:|-----------:|------------:|
| **LeanCorpus_Highlight_TwoTerms**  | **100**              | **100000**        |    **66.08 μs** |   **0.378 μs** |     **0.353 μs** |  **1.00** |    **0.00** |   **11.2305** |      **-** |   **46.06 KB** |        **1.00** |
| LeanCorpus_Highlight_FiveTerms | 100              | 100000        |   167.95 μs |   1.822 μs |     1.704 μs |  2.54 |    0.03 |   10.4980 |      - |   42.97 KB |        0.93 |
| LuceneNet_Highlight_TwoTerms   | 100              | 100000        | 4,482.60 μs | 653.444 μs | 1,926.693 μs | 67.84 |   29.02 | 1257.8125 | 7.8125 | 5144.61 KB |      111.70 |
| LuceneNet_Highlight_FiveTerms  | 100              | 100000        | 4,308.09 μs | 611.924 μs | 1,804.271 μs | 65.20 |   27.18 | 1312.5000 | 7.8125 | 5389.79 KB |      117.02 |
|                                |                  |               |             |            |              |       |         |           |        |            |             |
| **LeanCorpus_Highlight_TwoTerms**  | **200**              | **100000**        |    **68.46 μs** |   **0.521 μs** |     **0.487 μs** |  **1.00** |    **0.00** |   **16.7236** |      **-** |   **68.63 KB** |        **1.00** |
| LeanCorpus_Highlight_FiveTerms | 200              | 100000        |   169.39 μs |   1.856 μs |     1.736 μs |  2.47 |    0.03 |   15.1367 |      - |   61.92 KB |        0.90 |
| LuceneNet_Highlight_TwoTerms   | 200              | 100000        | 4,249.84 μs | 614.765 μs | 1,812.648 μs | 62.08 |   26.36 | 1257.8125 | 7.8125 | 5144.61 KB |       74.96 |
| LuceneNet_Highlight_FiveTerms  | 200              | 100000        | 4,249.92 μs | 604.964 μs | 1,783.749 μs | 62.08 |   25.94 | 1312.5000 | 7.8125 | 5389.79 KB |       78.53 |
|                                |                  |               |             |            |              |       |         |           |        |            |             |
| **LeanCorpus_Highlight_TwoTerms**  | **500**              | **100000**        |    **72.88 μs** |   **0.809 μs** |     **0.676 μs** |  **1.00** |    **0.00** |   **24.1699** |      **-** |   **99.19 KB** |        **1.00** |
| LeanCorpus_Highlight_FiveTerms | 500              | 100000        |   180.33 μs |   1.915 μs |     1.791 μs |  2.47 |    0.03 |   22.7051 |      - |   92.77 KB |        0.94 |
| LuceneNet_Highlight_TwoTerms   | 500              | 100000        | 4,107.80 μs | 596.741 μs | 1,759.505 μs | 56.37 |   24.04 | 1257.8125 | 7.8125 | 5144.61 KB |       51.87 |
| LuceneNet_Highlight_FiveTerms  | 500              | 100000        | 4,335.96 μs | 605.396 μs | 1,785.025 μs | 59.50 |   24.39 | 1312.5000 | 7.8125 | 5389.79 KB |       54.34 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-highlighter"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-highlighter" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-highlighter" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-highlighter" style="max-width:960px"><canvas id="chart-bench-highlighter" style="height:500px"></canvas></div>
<p><a href="highlighter.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


