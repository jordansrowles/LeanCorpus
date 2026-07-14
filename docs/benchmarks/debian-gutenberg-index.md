---
title: Benchmarks - Gutenberg index
---

# Gutenberg index

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                    | Mean     | Error     | StdDev   | Ratio | RatioSD | Gen0      | Gen1     | Gen2     | Allocated | Alloc Ratio |
|-------------------------- |---------:|----------:|---------:|------:|--------:|----------:|---------:|---------:|----------:|------------:|
| LeanCorpus_Standard_Index | 137.2 ms |  17.19 ms |  2.66 ms |  1.00 |    0.00 |  750.0000 | 750.0000 | 500.0000 |   4.71 MB |        1.00 |
| LeanCorpus_English_Index  | 151.8 ms |  61.84 ms |  9.57 ms |  1.11 |    0.07 |  750.0000 | 750.0000 | 500.0000 |   4.71 MB |        1.00 |
| LuceneNet_Index           | 659.5 ms | 212.44 ms | 55.17 ms |  4.81 |    0.38 | 1000.0000 |        - |        - |   4.69 MB |        1.00 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-gutenberg-index"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-gutenberg-index" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-gutenberg-index" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-gutenberg-index" style="max-width:960px"><canvas id="chart-bench-gutenberg-index" style="height:500px"></canvas></div>
<p><a href="debian-gutenberg-index.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


