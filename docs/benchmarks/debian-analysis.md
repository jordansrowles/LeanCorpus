---
title: Benchmarks - Analysis
---

# Analysis

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method             | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0       | Allocated   | Alloc Ratio |
|------------------- |-------------- |---------:|--------:|--------:|------:|-----------:|------------:|------------:|
| LeanCorpus_Analyse | 100000        | 303.9 ms | 0.76 ms | 0.71 ms |  1.00 |          - |           - |          NA |
| LuceneNet_Analyse  | 100000        | 785.4 ms | 2.85 ms | 2.52 ms |  2.58 | 52000.0000 | 219439896 B |          NA |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-analysis"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-analysis" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-analysis" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-analysis" style="max-width:960px"><canvas id="chart-bench-analysis" style="height:500px"></canvas></div>
<p><a href="debian-analysis.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


