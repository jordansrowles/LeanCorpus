---
title: Benchmarks - Analysis parity
---

# Analysis parity

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `f3305a5` &nbsp;&middot;&nbsp; 16 July 2026 20:51 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                | Mean      | Error     | StdDev    | Ratio | Gen0   | Allocated | Alloc Ratio |
|---------------------- |----------:|----------:|----------:|------:|-------:|----------:|------------:|
| LeanCorpus_Whitespace | 30.123 μs | 0.0350 μs | 0.0328 μs |  1.00 |      - |         - |          NA |
| LuceneNet_Whitespace  | 75.064 μs | 0.0354 μs | 0.0276 μs |  2.49 | 0.7324 |    3200 B |          NA |
| LeanCorpus_Keyword    |  3.285 μs | 0.0050 μs | 0.0044 μs |  0.11 |      - |         - |          NA |
| LuceneNet_Keyword     | 11.824 μs | 0.0107 μs | 0.0089 μs |  0.39 | 0.7629 |    3200 B |          NA |
| LeanCorpus_Simple     | 40.512 μs | 0.0173 μs | 0.0135 μs |  1.34 |      - |         - |          NA |
| LuceneNet_Simple      | 81.537 μs | 0.0721 μs | 0.0602 μs |  2.71 | 0.7324 |    3200 B |          NA |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-analysis-parity"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-analysis-parity" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-analysis-parity" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-analysis-parity" style="max-width:960px"><canvas id="chart-bench-analysis-parity" style="height:500px"></canvas></div>
<p><a href="debian-analysis-parity.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


