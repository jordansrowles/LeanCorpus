---
title: Benchmarks - Highlighter
---

# Highlighter

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                         | MaxSnippetLength | DocumentCount | Mean        | Error      | StdDev       | Ratio | RatioSD | Gen0      | Gen1   | Allocated  | Alloc Ratio |
|------------------------------- |----------------- |-------------- |------------:|-----------:|-------------:|------:|--------:|----------:|-------:|-----------:|------------:|
| **LeanCorpus_Highlight_TwoTerms**  | **100**              | **100000**        |    **66.04 μs** |   **0.494 μs** |     **0.462 μs** |  **1.00** |    **0.00** |   **11.2305** |      **-** |   **46.06 KB** |        **1.00** |
| LeanCorpus_Highlight_FiveTerms | 100              | 100000        |   163.52 μs |   1.735 μs |     1.623 μs |  2.48 |    0.03 |   10.4980 |      - |   42.97 KB |        0.93 |
| LuceneNet_Highlight_TwoTerms   | 100              | 100000        | 4,098.63 μs | 597.856 μs | 1,762.791 μs | 62.06 |   26.57 | 1257.8125 | 7.8125 | 5144.61 KB |      111.70 |
| LuceneNet_Highlight_FiveTerms  | 100              | 100000        | 4,329.47 μs | 614.158 μs | 1,810.858 μs | 65.56 |   27.30 | 1312.5000 | 7.8125 | 5389.79 KB |      117.02 |
|                                |                  |               |             |            |              |       |         |           |        |            |             |
| **LeanCorpus_Highlight_TwoTerms**  | **200**              | **100000**        |    **68.40 μs** |   **0.526 μs** |     **0.492 μs** |  **1.00** |    **0.00** |   **16.7236** |      **-** |   **68.63 KB** |        **1.00** |
| LeanCorpus_Highlight_FiveTerms | 200              | 100000        |   168.04 μs |   1.872 μs |     1.751 μs |  2.46 |    0.03 |   15.1367 |      - |   61.92 KB |        0.90 |
| LuceneNet_Highlight_TwoTerms   | 200              | 100000        | 4,084.84 μs | 598.591 μs | 1,764.959 μs | 59.72 |   25.69 | 1257.8125 | 7.8125 | 5144.61 KB |       74.96 |
| LuceneNet_Highlight_FiveTerms  | 200              | 100000        | 4,288.33 μs | 610.927 μs | 1,801.333 μs | 62.70 |   26.22 | 1312.5000 | 7.8125 | 5389.79 KB |       78.53 |
|                                |                  |               |             |            |              |       |         |           |        |            |             |
| **LeanCorpus_Highlight_TwoTerms**  | **500**              | **100000**        |    **71.29 μs** |   **1.065 μs** |     **0.996 μs** |  **1.00** |    **0.00** |   **24.1699** |      **-** |   **99.19 KB** |        **1.00** |
| LeanCorpus_Highlight_FiveTerms | 500              | 100000        |   177.75 μs |   1.818 μs |     1.700 μs |  2.49 |    0.04 |   22.7051 |      - |   92.77 KB |        0.94 |
| LuceneNet_Highlight_TwoTerms   | 500              | 100000        | 4,107.10 μs | 599.290 μs | 1,767.019 μs | 57.62 |   24.69 | 1257.8125 | 7.8125 | 5144.61 KB |       51.87 |
| LuceneNet_Highlight_FiveTerms  | 500              | 100000        | 4,241.03 μs | 602.192 μs | 1,775.577 μs | 59.50 |   24.81 | 1312.5000 |      - | 5389.79 KB |       54.34 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-highlighter"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-highlighter" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-highlighter" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-highlighter" style="max-width:960px"><canvas id="chart-bench-highlighter" style="height:500px"></canvas></div>
<p><a href="highlighter.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


