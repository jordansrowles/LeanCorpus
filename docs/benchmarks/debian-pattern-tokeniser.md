---
title: Benchmarks - Pattern tokeniser
---

# Pattern tokeniser

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method              | Scenario         | Mean         | Error     | StdDev    | Ratio | RatioSD | Gen0      | Gen1   | Allocated | Alloc Ratio |
|-------------------- |----------------- |-------------:|----------:|----------:|------:|--------:|----------:|-------:|----------:|------------:|
| **LeanCorpus_Tokenise** | **comma-long**       |  **63,071.3 ns** | **392.16 ns** | **366.83 ns** |  **1.00** |    **0.00** |         **-** |      **-** |         **-** |          **NA** |
| LuceneNet_Tokenise  | comma-long       | 531,132.3 ns | 894.79 ns | 747.19 ns |  8.42 |    0.05 | 1091.7969 | 0.9766 | 4559840 B |          NA |
|                     |                  |              |           |           |       |         |           |        |           |             |
| **LeanCorpus_Tokenise** | **comma-short**      |     **888.4 ns** |   **0.40 ns** |   **0.31 ns** |  **1.00** |    **0.00** |         **-** |      **-** |         **-** |          **NA** |
| LuceneNet_Tokenise  | comma-short      |   4,211.1 ns |  16.51 ns |  13.79 ns |  4.74 |    0.02 |    5.0964 |      - |   21344 B |          NA |
|                     |                  |              |           |           |       |         |           |        |           |             |
| **LeanCorpus_Tokenise** | **whitespace-long**  |  **24,914.4 ns** |  **11.68 ns** |   **9.12 ns** |  **1.00** |    **0.00** |         **-** |      **-** |         **-** |          **NA** |
| LuceneNet_Tokenise  | whitespace-long  | 100,857.1 ns | 104.53 ns |  92.66 ns |  4.05 |    0.00 |  144.7754 | 0.1221 |  605960 B |          NA |
|                     |                  |              |           |           |       |         |           |        |           |             |
| **LeanCorpus_Tokenise** | **whitespace-short** |   **1,124.9 ns** |   **1.03 ns** |   **0.91 ns** |  **1.00** |    **0.00** |         **-** |      **-** |         **-** |          **NA** |
| LuceneNet_Tokenise  | whitespace-short |   4,501.6 ns |   6.64 ns |   5.54 ns |  4.00 |    0.01 |    5.1804 |      - |   21696 B |          NA |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-pattern-tokeniser"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-pattern-tokeniser" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-pattern-tokeniser" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-pattern-tokeniser" style="max-width:960px"><canvas id="chart-bench-pattern-tokeniser" style="height:500px"></canvas></div>
<p><a href="debian-pattern-tokeniser.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


