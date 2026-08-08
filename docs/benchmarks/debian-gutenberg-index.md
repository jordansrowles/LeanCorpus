---
title: Benchmarks - Gutenberg index
---

# Gutenberg index

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                    | Mean       | Error     | StdDev   | Ratio | RatioSD | Gen0       | Gen1       | Gen2      | Allocated | Alloc Ratio |
|-------------------------- |-----------:|----------:|---------:|------:|--------:|-----------:|-----------:|----------:|----------:|------------:|
| LeanCorpus_Standard_Index |   711.7 ms |  73.67 ms | 19.13 ms |  1.00 |    0.00 | 20000.0000 | 10000.0000 | 2000.0000 | 137.09 MB |        1.00 |
| LeanCorpus_English_Index  |   729.1 ms |  20.96 ms |  3.24 ms |  1.02 |    0.03 | 17000.0000 |  8000.0000 | 1000.0000 |  122.3 MB |        0.89 |
| LuceneNet_Index           | 1,517.5 ms | 340.46 ms | 88.42 ms |  2.13 |    0.12 | 42000.0000 |  3000.0000 |         - | 208.13 MB |        1.52 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-gutenberg-index"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-gutenberg-index" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-gutenberg-index" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-gutenberg-index" style="max-width:960px"><canvas id="chart-bench-gutenberg-index" style="height:500px"></canvas></div>
<p><a href="debian-gutenberg-index.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


