---
title: Benchmarks - Block-Join (search)
---

# Block-Join (search)

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `f3305a5` &nbsp;&middot;&nbsp; 16 July 2026 20:51 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                           | BlockCount | Mean     | Error     | StdDev    | Ratio | Gen0    | Allocated | Alloc Ratio |
|--------------------------------- |----------- |---------:|----------:|----------:|------:|--------:|----------:|------------:|
| LeanLucene_BlockJoinQuery        | 100000     | 2.230 ms | 0.0017 ms | 0.0013 ms |  1.00 |       - |   3.16 KB |        1.00 |
| LuceneNet_ToParentBlockJoinQuery | 100000     | 3.737 ms | 0.0076 ms | 0.0071 ms |  1.68 | 11.7188 |  56.76 KB |       17.94 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-blockjoin-search"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-blockjoin-search" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-blockjoin-search" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-blockjoin-search" style="max-width:960px"><canvas id="chart-bench-blockjoin-search" style="height:500px"></canvas></div>
<p><a href="debian-blockjoin-search.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


