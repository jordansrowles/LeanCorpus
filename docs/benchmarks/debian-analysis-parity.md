---
title: Benchmarks - Analysis parity
---

# Analysis parity

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                | Mean      | Error     | StdDev    | Ratio | Gen0   | Allocated | Alloc Ratio |
|---------------------- |----------:|----------:|----------:|------:|-------:|----------:|------------:|
| LeanCorpus_Whitespace | 29.385 μs | 0.0231 μs | 0.0205 μs |  1.00 |      - |         - |          NA |
| LuceneNet_Whitespace  | 79.380 μs | 0.0554 μs | 0.0463 μs |  2.70 | 0.7324 |    3200 B |          NA |
| LeanCorpus_Keyword    |  3.169 μs | 0.0029 μs | 0.0027 μs |  0.11 |      - |         - |          NA |
| LuceneNet_Keyword     | 11.796 μs | 0.0234 μs | 0.0207 μs |  0.40 | 0.7629 |    3200 B |          NA |
| LeanCorpus_Simple     | 40.546 μs | 0.0149 μs | 0.0116 μs |  1.38 |      - |         - |          NA |
| LuceneNet_Simple      | 86.833 μs | 0.0638 μs | 0.0566 μs |  2.95 | 0.7324 |    3200 B |          NA |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-analysis-parity"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-analysis-parity" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-analysis-parity" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-analysis-parity" style="max-width:960px"><canvas id="chart-bench-analysis-parity" style="height:500px"></canvas></div>
<p><a href="debian-analysis-parity.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


