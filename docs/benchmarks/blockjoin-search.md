---
title: Benchmarks - Block-Join (search)
---

# Block-Join (search)

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                           | BlockCount | Mean     | Error     | StdDev    | Ratio | Gen0    | Allocated | Alloc Ratio |
|--------------------------------- |----------- |---------:|----------:|----------:|------:|--------:|----------:|------------:|
| LeanLucene_BlockJoinQuery        | 100000     | 1.579 ms | 0.0020 ms | 0.0018 ms |  1.00 |       - |   2.23 KB |        1.00 |
| LuceneNet_ToParentBlockJoinQuery | 100000     | 1.998 ms | 0.0066 ms | 0.0061 ms |  1.27 | 11.7188 |  48.14 KB |       21.62 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-blockjoin-search"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-blockjoin-search" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-blockjoin-search" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-blockjoin-search" style="max-width:960px"><canvas id="chart-bench-blockjoin-search" style="height:500px"></canvas></div>
<p><a href="blockjoin-search.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


