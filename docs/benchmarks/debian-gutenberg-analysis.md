---
title: Benchmarks - Gutenberg analysis
---

# Gutenberg analysis

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                      | Mean     | Error   | StdDev  | Ratio | Allocated | Alloc Ratio |
|---------------------------- |---------:|--------:|--------:|------:|----------:|------------:|
| LeanCorpus_Standard_Analyse | 138.5 μs | 0.33 μs | 0.31 μs |  1.00 |         - |          NA |
| LeanCorpus_English_Analyse  | 297.4 μs | 0.27 μs | 0.24 μs |  2.15 |         - |          NA |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-gutenberg-analysis"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-gutenberg-analysis" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-gutenberg-analysis" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-gutenberg-analysis" style="max-width:960px"><canvas id="chart-bench-gutenberg-analysis" style="height:500px"></canvas></div>
<p><a href="debian-gutenberg-analysis.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


