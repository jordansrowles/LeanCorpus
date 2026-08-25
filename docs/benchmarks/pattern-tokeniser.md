---
title: Benchmarks - Pattern tokeniser
---

# Pattern tokeniser

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method              | Scenario         | Mean         | Error       | StdDev      | Ratio | RatioSD | Gen0      | Gen1   | Allocated | Alloc Ratio |
|-------------------- |----------------- |-------------:|------------:|------------:|------:|--------:|----------:|-------:|----------:|------------:|
| **LeanCorpus_Tokenise** | **comma-long**       |  **59,877.8 ns** |   **553.87 ns** |   **518.09 ns** |  **1.00** |    **0.00** |         **-** |      **-** |         **-** |          **NA** |
| LuceneNet_Tokenise  | comma-long       | 554,430.7 ns | 1,197.15 ns | 1,061.24 ns |  9.26 |    0.08 | 1091.7969 | 0.9766 | 4559840 B |          NA |
|                     |                  |              |             |             |       |         |           |        |           |             |
| **LeanCorpus_Tokenise** | **comma-short**      |     **890.0 ns** |     **0.63 ns** |     **0.56 ns** |  **1.00** |    **0.00** |         **-** |      **-** |         **-** |          **NA** |
| LuceneNet_Tokenise  | comma-short      |   4,237.3 ns |    12.05 ns |    11.27 ns |  4.76 |    0.01 |    5.0964 |      - |   21344 B |          NA |
|                     |                  |              |             |             |       |         |           |        |           |             |
| **LeanCorpus_Tokenise** | **whitespace-long**  |  **25,318.2 ns** |    **11.20 ns** |     **8.75 ns** |  **1.00** |    **0.00** |         **-** |      **-** |         **-** |          **NA** |
| LuceneNet_Tokenise  | whitespace-long  | 115,399.1 ns |   195.89 ns |   183.23 ns |  4.56 |    0.01 |  144.7754 | 0.1221 |  605960 B |          NA |
|                     |                  |              |             |             |       |         |           |        |           |             |
| **LeanCorpus_Tokenise** | **whitespace-short** |   **1,133.1 ns** |     **2.33 ns** |     **2.06 ns** |  **1.00** |    **0.00** |         **-** |      **-** |         **-** |          **NA** |
| LuceneNet_Tokenise  | whitespace-short |   4,510.2 ns |     8.92 ns |     8.35 ns |  3.98 |    0.01 |    5.1804 |      - |   21696 B |          NA |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-pattern-tokeniser"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-pattern-tokeniser" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-pattern-tokeniser" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-pattern-tokeniser" style="max-width:960px"><canvas id="chart-bench-pattern-tokeniser" style="height:500px"></canvas></div>
<p><a href="pattern-tokeniser.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


