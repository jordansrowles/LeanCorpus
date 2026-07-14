---
title: Benchmarks - Block-Join (index)
---

# Block-Join (index)

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                 | BlockCount | Mean    | Error   | StdDev  | Ratio | Gen0        | Gen1        | Gen2       | Allocated | Alloc Ratio |
|----------------------- |----------- |--------:|--------:|--------:|------:|------------:|------------:|-----------:|----------:|------------:|
| LeanLucene_IndexBlocks | 100000     | 21.61 s | 5.456 s | 0.299 s |  1.00 | 268000.0000 | 129000.0000 | 28000.0000 |   2.53 GB |        1.00 |
| LuceneNet_IndexBlocks  | 100000     | 13.95 s | 1.751 s | 0.096 s |  0.65 | 784000.0000 |  19000.0000 |  2000.0000 |   3.51 GB |        1.38 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-blockjoin-index"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-blockjoin-index" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-blockjoin-index" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-blockjoin-index" style="max-width:960px"><canvas id="chart-bench-blockjoin-index" style="height:500px"></canvas></div>
<p><a href="debian-blockjoin-index.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


