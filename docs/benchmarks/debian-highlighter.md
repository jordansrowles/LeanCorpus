---
title: Benchmarks - Highlighter
---

# Highlighter

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                         | MaxSnippetLength | DocumentCount | Mean        | Error      | StdDev       | Ratio | RatioSD | Gen0      | Gen1   | Allocated  | Alloc Ratio |
|------------------------------- |----------------- |-------------- |------------:|-----------:|-------------:|------:|--------:|----------:|-------:|-----------:|------------:|
| **LeanCorpus_Highlight_TwoTerms**  | **100**              | **100000**        |    **65.55 μs** |   **0.521 μs** |     **0.487 μs** |  **1.00** |    **0.00** |   **11.2305** |      **-** |   **46.06 KB** |        **1.00** |
| LeanCorpus_Highlight_FiveTerms | 100              | 100000        |   164.55 μs |   1.812 μs |     1.695 μs |  2.51 |    0.03 |   10.4980 |      - |   42.97 KB |        0.93 |
| LuceneNet_Highlight_TwoTerms   | 100              | 100000        | 4,120.40 μs | 602.865 μs | 1,777.562 μs | 62.86 |   27.00 | 1257.8125 | 7.8125 | 5144.61 KB |      111.70 |
| LuceneNet_Highlight_FiveTerms  | 100              | 100000        | 4,264.90 μs | 604.739 μs | 1,783.087 μs | 65.07 |   27.08 | 1312.5000 | 7.8125 | 5389.79 KB |      117.02 |
|                                |                  |               |             |            |              |       |         |           |        |            |             |
| **LeanCorpus_Highlight_TwoTerms**  | **200**              | **100000**        |    **68.69 μs** |   **0.506 μs** |     **0.474 μs** |  **1.00** |    **0.00** |   **16.7236** |      **-** |   **68.63 KB** |        **1.00** |
| LeanCorpus_Highlight_FiveTerms | 200              | 100000        |   168.12 μs |   1.709 μs |     1.598 μs |  2.45 |    0.03 |   14.8926 |      - |   61.78 KB |        0.90 |
| LuceneNet_Highlight_TwoTerms   | 200              | 100000        | 4,105.15 μs | 598.287 μs | 1,764.063 μs | 59.76 |   25.56 | 1257.8125 | 7.8125 | 5144.61 KB |       74.96 |
| LuceneNet_Highlight_FiveTerms  | 200              | 100000        | 4,279.34 μs | 607.417 μs | 1,790.983 μs | 62.30 |   25.95 | 1312.5000 | 7.8125 | 5389.79 KB |       78.53 |
|                                |                  |               |             |            |              |       |         |           |        |            |             |
| **LeanCorpus_Highlight_TwoTerms**  | **500**              | **100000**        |    **71.12 μs** |   **0.580 μs** |     **0.543 μs** |  **1.00** |    **0.00** |   **24.1699** |      **-** |   **99.19 KB** |        **1.00** |
| LeanCorpus_Highlight_FiveTerms | 500              | 100000        |   176.34 μs |   1.981 μs |     1.853 μs |  2.48 |    0.03 |   22.7051 |      - |   92.77 KB |        0.94 |
| LuceneNet_Highlight_TwoTerms   | 500              | 100000        | 4,112.40 μs | 601.027 μs | 1,772.143 μs | 57.83 |   24.81 | 1257.8125 | 7.8125 | 5144.61 KB |       51.87 |
| LuceneNet_Highlight_FiveTerms  | 500              | 100000        | 4,247.37 μs | 604.068 μs | 1,781.108 μs | 59.72 |   24.93 | 1312.5000 | 7.8125 | 5389.79 KB |       54.34 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-highlighter"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-highlighter" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-highlighter" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-highlighter" style="max-width:960px"><canvas id="chart-bench-highlighter" style="height:500px"></canvas></div>
<p><a href="debian-highlighter.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


