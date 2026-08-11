---
title: Benchmarks - Deletion (commit)
---

# Deletion (commit)

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                   | DocumentCount | Mean     | Error     | StdDev   | Ratio | RatioSD | Gen0      | Allocated | Alloc Ratio |
|------------------------- |-------------- |---------:|----------:|---------:|------:|--------:|----------:|----------:|------------:|
| LeanLucene_CommitDeletes | 100000        | 138.9 ms |  92.26 ms |  5.06 ms |  1.00 |    0.00 |         - |  17.89 MB |        1.00 |
| LuceneNet_CommitDeletes  | 100000        | 174.1 ms | 237.77 ms | 13.03 ms |  1.25 |    0.09 | 4000.0000 |  19.24 MB |        1.08 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-deletion-commit"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-deletion-commit" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-deletion-commit" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-deletion-commit" style="max-width:960px"><canvas id="chart-bench-deletion-commit" style="height:500px"></canvas></div>
<p><a href="deletion-commit.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


