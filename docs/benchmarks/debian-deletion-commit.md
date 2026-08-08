---
title: Benchmarks - Deletion (commit)
---

# Deletion (commit)

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                   | DocumentCount | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0      | Allocated | Alloc Ratio |
|------------------------- |-------------- |---------:|---------:|---------:|------:|--------:|----------:|----------:|------------:|
| LeanLucene_CommitDeletes | 100000        | 157.5 ms | 237.0 ms | 12.99 ms |  1.00 |    0.00 |         - |  17.96 MB |        1.00 |
| LuceneNet_CommitDeletes  | 100000        | 183.1 ms | 467.8 ms | 25.64 ms |  1.17 |    0.16 | 4000.0000 |  19.24 MB |        1.07 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-deletion-commit"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-deletion-commit" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-deletion-commit" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-deletion-commit" style="max-width:960px"><canvas id="chart-bench-deletion-commit" style="height:500px"></canvas></div>
<p><a href="debian-deletion-commit.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


