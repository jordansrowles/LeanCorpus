---
title: Benchmarks - Deletion (queue)
---

# Deletion (queue)

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                  | DocumentCount | Mean     | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------ |-------------- |---------:|----------:|----------:|------:|--------:|----------:|------------:|
| LeanLucene_QueueDeletes | 100000        | 2.150 ms |  5.906 ms | 0.3237 ms |  1.00 |    0.00 |   2.96 MB |        1.00 |
| LuceneNet_QueueDeletes  | 100000        | 4.598 ms | 29.618 ms | 1.6235 ms |  2.17 |    0.72 |    2.8 MB |        0.94 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-deletion-queue"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-deletion-queue" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-deletion-queue" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-deletion-queue" style="max-width:960px"><canvas id="chart-bench-deletion-queue" style="height:500px"></canvas></div>
<p><a href="debian-deletion-queue.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


