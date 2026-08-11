---
title: Benchmarks - Block-Join (index)
---

# Block-Join (index)

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                 | BlockCount | Mean    | Error   | StdDev  | Ratio | Gen0         | Gen1        | Gen2       | Allocated | Alloc Ratio |
|----------------------- |----------- |--------:|--------:|--------:|------:|-------------:|------------:|-----------:|----------:|------------:|
| LeanLucene_IndexBlocks | 100000     | 23.70 s | 1.220 s | 0.067 s |  1.00 |  493000.0000 | 197000.0000 | 16000.0000 |   3.18 GB |        1.00 |
| LuceneNet_IndexBlocks  | 100000     | 32.55 s | 2.440 s | 0.134 s |  1.37 | 1292000.0000 |  45000.0000 |  4000.0000 |   6.29 GB |        1.98 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-blockjoin-index"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-blockjoin-index" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-blockjoin-index" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-blockjoin-index" style="max-width:960px"><canvas id="chart-bench-blockjoin-index" style="height:500px"></canvas></div>
<p><a href="blockjoin-index.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


