---
title: Benchmarks - Pattern tokeniser
---

# Pattern tokeniser

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method              | Scenario         | Mean         | Error       | StdDev      | Ratio | RatioSD | Gen0      | Gen1   | Allocated | Alloc Ratio |
|-------------------- |----------------- |-------------:|------------:|------------:|------:|--------:|----------:|-------:|----------:|------------:|
| **LeanCorpus_Tokenise** | **comma-long**       |  **51,007.6 ns** |    **77.08 ns** |    **72.10 ns** |  **1.00** |    **0.00** |         **-** |      **-** |         **-** |          **NA** |
| LuceneNet_Tokenise  | comma-long       | 535,546.6 ns | 2,530.36 ns | 2,366.90 ns | 10.50 |    0.05 | 1091.7969 | 0.9766 | 4559840 B |          NA |
|                     |                  |              |             |             |       |         |           |        |           |             |
| **LeanCorpus_Tokenise** | **comma-short**      |     **892.2 ns** |     **0.65 ns** |     **0.61 ns** |  **1.00** |    **0.00** |         **-** |      **-** |         **-** |          **NA** |
| LuceneNet_Tokenise  | comma-short      |   4,144.1 ns |    16.12 ns |    15.08 ns |  4.64 |    0.02 |    5.0964 |      - |   21344 B |          NA |
|                     |                  |              |             |             |       |         |           |        |           |             |
| **LeanCorpus_Tokenise** | **whitespace-long**  |  **25,194.7 ns** |    **12.04 ns** |     **9.40 ns** |  **1.00** |    **0.00** |         **-** |      **-** |         **-** |          **NA** |
| LuceneNet_Tokenise  | whitespace-long  | 102,233.8 ns |   225.20 ns |   210.65 ns |  4.06 |    0.01 |  144.7754 | 0.1221 |  605960 B |          NA |
|                     |                  |              |             |             |       |         |           |        |           |             |
| **LeanCorpus_Tokenise** | **whitespace-short** |   **1,126.2 ns** |     **1.56 ns** |     **1.38 ns** |  **1.00** |    **0.00** |         **-** |      **-** |         **-** |          **NA** |
| LuceneNet_Tokenise  | whitespace-short |   4,518.3 ns |     8.98 ns |     8.40 ns |  4.01 |    0.01 |    5.1804 |      - |   21696 B |          NA |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-pattern-tokeniser"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-pattern-tokeniser" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-pattern-tokeniser" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-pattern-tokeniser" style="max-width:960px"><canvas id="chart-bench-pattern-tokeniser" style="height:500px"></canvas></div>
<p><a href="debian-pattern-tokeniser.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


