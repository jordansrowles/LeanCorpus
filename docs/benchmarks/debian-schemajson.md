---
title: Benchmarks - Schema and JSON
---

# Schema and JSON

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                      | DocumentCount | Mean       | Error     | StdDev    | Ratio | RatioSD | Gen0       | Gen1       | Gen2      | Allocated | Alloc Ratio |
|---------------------------- |-------------- |-----------:|----------:|----------:|------:|--------:|-----------:|-----------:|----------:|----------:|------------:|
| LeanCorpus_Index_NoSchema   | 100000        | 6,615.7 ms |  85.03 ms |  71.01 ms |  1.00 |    0.00 | 81000.0000 | 38000.0000 | 4000.0000 | 925.71 MB |        1.00 |
| LeanCorpus_Index_WithSchema | 100000        | 6,856.5 ms | 110.01 ms | 112.97 ms |  1.04 |    0.02 | 82000.0000 | 38000.0000 | 4000.0000 | 929.53 MB |        1.00 |
| LeanCorpus_JsonMapping      | 100000        |   198.0 ms |   0.46 ms |   0.43 ms |  0.03 |    0.00 | 29666.6667 |          - |         - | 119.13 MB |        0.13 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-schemajson"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-schemajson" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-schemajson" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-schemajson" style="max-width:960px"><canvas id="chart-bench-schemajson" style="height:500px"></canvas></div>
<p><a href="debian-schemajson.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


