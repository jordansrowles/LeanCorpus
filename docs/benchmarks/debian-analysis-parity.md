---
title: Benchmarks - Analysis parity
---

# Analysis parity

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                | Mean      | Error     | StdDev    | Ratio | Gen0   | Allocated | Alloc Ratio |
|---------------------- |----------:|----------:|----------:|------:|-------:|----------:|------------:|
| LeanCorpus_Whitespace | 29.528 μs | 0.0309 μs | 0.0289 μs |  1.00 |      - |         - |          NA |
| LuceneNet_Whitespace  | 74.205 μs | 0.0567 μs | 0.0502 μs |  2.51 | 0.7324 |    3200 B |          NA |
| LeanCorpus_Keyword    |  3.254 μs | 0.0018 μs | 0.0015 μs |  0.11 |      - |         - |          NA |
| LuceneNet_Keyword     | 12.045 μs | 0.0095 μs | 0.0084 μs |  0.41 | 0.7629 |    3200 B |          NA |
| LeanCorpus_Simple     | 41.679 μs | 0.0137 μs | 0.0107 μs |  1.41 |      - |         - |          NA |
| LuceneNet_Simple      | 82.249 μs | 0.0820 μs | 0.0727 μs |  2.79 | 0.7324 |    3200 B |          NA |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-analysis-parity"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-analysis-parity" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-analysis-parity" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-analysis-parity" style="max-width:960px"><canvas id="chart-bench-analysis-parity" style="height:500px"></canvas></div>
<p><a href="debian-analysis-parity.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


