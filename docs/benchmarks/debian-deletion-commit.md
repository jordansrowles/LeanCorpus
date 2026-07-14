---
title: Benchmarks - Deletion (commit)
---

# Deletion (commit)

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                   | DocumentCount | Mean     | Error    | StdDev  | Ratio | RatioSD | Gen0      | Gen1      | Allocated | Alloc Ratio |
|------------------------- |-------------- |---------:|---------:|--------:|------:|--------:|----------:|----------:|----------:|------------:|
| LeanLucene_CommitDeletes | 100000        | 405.2 ms | 150.0 ms | 8.22 ms |  1.00 |    0.00 | 3000.0000 | 1000.0000 |  81.98 MB |        1.00 |
| LuceneNet_CommitDeletes  | 100000        | 153.7 ms | 118.2 ms | 6.48 ms |  0.38 |    0.02 | 4000.0000 |         - |  18.81 MB |        0.23 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-deletion-commit"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-deletion-commit" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-deletion-commit" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-deletion-commit" style="max-width:960px"><canvas id="chart-bench-deletion-commit" style="height:500px"></canvas></div>
<p><a href="debian-deletion-commit.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


