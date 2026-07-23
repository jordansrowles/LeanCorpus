---
title: Benchmarks - Block-Join (index)
---

# Block-Join (index)

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `f3305a5` &nbsp;&middot;&nbsp; 16 July 2026 20:51 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                 | BlockCount | Mean    | Error   | StdDev  | Ratio | Gen0        | Gen1        | Gen2       | Allocated | Alloc Ratio |
|----------------------- |----------- |--------:|--------:|--------:|------:|------------:|------------:|-----------:|----------:|------------:|
| LeanLucene_IndexBlocks | 100000     | 25.89 s | 4.535 s | 0.249 s |  1.00 | 322000.0000 | 155000.0000 | 30000.0000 |   2.52 GB |        1.00 |
| LuceneNet_IndexBlocks  | 100000     | 17.13 s | 0.630 s | 0.035 s |  0.66 | 747000.0000 |  21000.0000 |  2000.0000 |   3.44 GB |        1.36 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-blockjoin-index"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-blockjoin-index" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-blockjoin-index" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-blockjoin-index" style="max-width:960px"><canvas id="chart-bench-blockjoin-index" style="height:500px"></canvas></div>
<p><a href="debian-blockjoin-index.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


