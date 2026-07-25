---
title: Benchmarks - Gutenberg index
---

# Gutenberg index

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `f3305a5` &nbsp;&middot;&nbsp; 16 July 2026 20:51 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                    | Mean     | Error     | StdDev   | Ratio | RatioSD | Gen0     | Gen1     | Gen2     | Allocated | Alloc Ratio |
|-------------------------- |---------:|----------:|---------:|------:|--------:|---------:|---------:|---------:|----------:|------------:|
| LeanCorpus_Standard_Index | 159.1 ms |   9.38 ms |  1.45 ms |  1.00 |    0.00 | 500.0000 | 500.0000 | 500.0000 |    3.6 MB |        1.00 |
| LeanCorpus_English_Index  | 151.1 ms | 107.97 ms | 16.71 ms |  0.95 |    0.09 | 500.0000 | 500.0000 | 500.0000 |    3.6 MB |        1.00 |
| LuceneNet_Index           | 649.1 ms | 180.26 ms | 46.81 ms |  4.08 |    0.27 |        - |        - |        - |   2.66 MB |        0.74 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-gutenberg-index"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-gutenberg-index" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-gutenberg-index" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-gutenberg-index" style="max-width:960px"><canvas id="chart-bench-gutenberg-index" style="height:500px"></canvas></div>
<p><a href="debian-gutenberg-index.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


