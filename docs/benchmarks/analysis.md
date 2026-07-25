---
title: Benchmarks - Analysis
---

# Analysis

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method             | DocumentCount | Mean       | Error   | StdDev  | Ratio | Gen0        | Allocated   | Alloc Ratio |
|------------------- |-------------- |-----------:|--------:|--------:|------:|------------:|------------:|------------:|
| LeanCorpus_Analyse | 100000        |   896.9 ms | 2.94 ms | 2.75 ms |  1.00 |           - |           - |          NA |
| LuceneNet_Analyse  | 100000        | 2,180.5 ms | 1.99 ms | 1.86 ms |  2.43 | 144000.0000 | 605284312 B |          NA |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-analysis"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-analysis" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-analysis" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-analysis" style="max-width:960px"><canvas id="chart-bench-analysis" style="height:500px"></canvas></div>
<p><a href="analysis.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


