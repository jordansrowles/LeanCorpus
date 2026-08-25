---
title: Benchmarks - Analysis parity
---

# Analysis parity

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                | Mean      | Error     | StdDev    | Ratio | Gen0   | Allocated | Alloc Ratio |
|---------------------- |----------:|----------:|----------:|------:|-------:|----------:|------------:|
| LeanCorpus_Whitespace | 29.955 μs | 0.0200 μs | 0.0188 μs |  1.00 |      - |         - |          NA |
| LuceneNet_Whitespace  | 73.723 μs | 0.0820 μs | 0.0727 μs |  2.46 | 0.7324 |    3200 B |          NA |
| LeanCorpus_Keyword    |  3.256 μs | 0.0034 μs | 0.0030 μs |  0.11 |      - |         - |          NA |
| LuceneNet_Keyword     | 11.801 μs | 0.0106 μs | 0.0094 μs |  0.39 | 0.7629 |    3200 B |          NA |
| LeanCorpus_Simple     | 40.526 μs | 0.0212 μs | 0.0165 μs |  1.35 |      - |         - |          NA |
| LuceneNet_Simple      | 83.016 μs | 0.0410 μs | 0.0320 μs |  2.77 | 0.7324 |    3200 B |          NA |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-analysis-parity"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-analysis-parity" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-analysis-parity" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-analysis-parity" style="max-width:960px"><canvas id="chart-bench-analysis-parity" style="height:500px"></canvas></div>
<p><a href="analysis-parity.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


