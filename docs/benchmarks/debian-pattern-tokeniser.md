---
title: Benchmarks - Pattern tokeniser
---

# Pattern tokeniser

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `f3305a5` &nbsp;&middot;&nbsp; 16 July 2026 20:51 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method              | Scenario         | Mean         | Error       | StdDev      | Ratio | RatioSD | Gen0      | Gen1   | Allocated | Alloc Ratio |
|-------------------- |----------------- |-------------:|------------:|------------:|------:|--------:|----------:|-------:|----------:|------------:|
| **LeanCorpus_Tokenise** | **comma-long**       |  **52,477.1 ns** |    **43.12 ns** |    **40.34 ns** |  **1.00** |    **0.00** |         **-** |      **-** |         **-** |          **NA** |
| LuceneNet_Tokenise  | comma-long       | 527,129.5 ns | 1,115.22 ns | 1,043.18 ns | 10.04 |    0.02 | 1091.7969 | 0.9766 | 4559840 B |          NA |
|                     |                  |              |             |             |       |         |           |        |           |             |
| **LeanCorpus_Tokenise** | **comma-short**      |     **894.6 ns** |     **0.92 ns** |     **0.86 ns** |  **1.00** |    **0.00** |         **-** |      **-** |         **-** |          **NA** |
| LuceneNet_Tokenise  | comma-short      |   4,218.6 ns |    11.11 ns |    10.39 ns |  4.72 |    0.01 |    5.0964 |      - |   21344 B |          NA |
|                     |                  |              |             |             |       |         |           |        |           |             |
| **LeanCorpus_Tokenise** | **whitespace-long**  |  **25,220.6 ns** |     **7.69 ns** |     **6.00 ns** |  **1.00** |    **0.00** |         **-** |      **-** |         **-** |          **NA** |
| LuceneNet_Tokenise  | whitespace-long  | 102,097.9 ns |   553.67 ns |   517.91 ns |  4.05 |    0.02 |  144.7754 | 0.1221 |  605960 B |          NA |
|                     |                  |              |             |             |       |         |           |        |           |             |
| **LeanCorpus_Tokenise** | **whitespace-short** |   **1,135.9 ns** |     **1.75 ns** |     **1.55 ns** |  **1.00** |    **0.00** |         **-** |      **-** |         **-** |          **NA** |
| LuceneNet_Tokenise  | whitespace-short |   4,455.7 ns |     9.66 ns |     9.04 ns |  3.92 |    0.01 |    5.1804 |      - |   21696 B |          NA |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-pattern-tokeniser"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-pattern-tokeniser" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-pattern-tokeniser" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-pattern-tokeniser" style="max-width:960px"><canvas id="chart-bench-pattern-tokeniser" style="height:500px"></canvas></div>
<p><a href="debian-pattern-tokeniser.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


