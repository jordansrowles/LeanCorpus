---
title: Benchmarks - Gutenberg index
---

# Gutenberg index

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `66ba120` &nbsp;&middot;&nbsp; 24 August 2026 21:47 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                    | Mean       | Error    | StdDev   | Ratio | RatioSD | Gen0       | Gen1       | Gen2      | Allocated | Alloc Ratio |
|-------------------------- |-----------:|---------:|---------:|------:|--------:|-----------:|-----------:|----------:|----------:|------------:|
| LeanCorpus_Standard_Index |   783.6 ms | 241.5 ms | 62.73 ms |  1.00 |    0.00 | 20000.0000 | 10000.0000 | 2000.0000 | 132.35 MB |        1.00 |
| LeanCorpus_English_Index  |   973.9 ms | 115.8 ms | 30.07 ms |  1.25 |    0.09 | 36000.0000 | 13000.0000 | 3000.0000 | 221.54 MB |        1.67 |
| LuceneNet_Index           | 1,373.7 ms | 117.6 ms | 30.53 ms |  1.76 |    0.13 | 42000.0000 |  3000.0000 |         - | 208.13 MB |        1.57 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-gutenberg-index"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-gutenberg-index" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-gutenberg-index" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-gutenberg-index" style="max-width:960px"><canvas id="chart-bench-gutenberg-index" style="height:500px"></canvas></div>
<p><a href="gutenberg-index.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


