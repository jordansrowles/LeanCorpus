---
title: Benchmarks - Gutenberg index
---

# Gutenberg index

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                    | Mean       | Error     | StdDev   | Ratio | RatioSD | Gen0       | Gen1       | Gen2      | Allocated | Alloc Ratio |
|-------------------------- |-----------:|----------:|---------:|------:|--------:|-----------:|-----------:|----------:|----------:|------------:|
| LeanCorpus_Standard_Index |   714.7 ms |  74.27 ms | 19.29 ms |  1.00 |    0.00 | 20000.0000 | 10000.0000 | 2000.0000 | 137.08 MB |        1.00 |
| LeanCorpus_English_Index  |   736.9 ms |  12.80 ms |  1.98 ms |  1.03 |    0.03 | 17000.0000 |  8000.0000 | 1000.0000 | 122.29 MB |        0.89 |
| LuceneNet_Index           | 1,412.0 ms | 133.93 ms | 34.78 ms |  1.98 |    0.07 | 42000.0000 |  3000.0000 |         - | 208.13 MB |        1.52 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-gutenberg-index"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-gutenberg-index" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-gutenberg-index" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-gutenberg-index" style="max-width:960px"><canvas id="chart-bench-gutenberg-index" style="height:500px"></canvas></div>
<p><a href="gutenberg-index.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


