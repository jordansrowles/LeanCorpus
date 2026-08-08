---
title: Benchmarks - Schema and JSON
---

# Schema and JSON

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                      | DocumentCount | Mean       | Error    | StdDev   | Ratio | Gen0        | Gen1       | Gen2      | Allocated  | Alloc Ratio |
|---------------------------- |-------------- |-----------:|---------:|---------:|------:|------------:|-----------:|----------:|-----------:|------------:|
| LeanCorpus_Index_NoSchema   | 100000        | 7,672.1 ms | 70.06 ms | 62.10 ms |  1.00 | 166000.0000 | 67000.0000 | 2000.0000 | 1116.03 MB |        1.00 |
| LeanCorpus_Index_WithSchema | 100000        | 7,721.3 ms | 70.11 ms | 65.58 ms |  1.01 | 167000.0000 | 66000.0000 | 2000.0000 | 1119.85 MB |        1.00 |
| LeanCorpus_JsonMapping      | 100000        |   412.0 ms |  2.05 ms |  1.82 ms |  0.05 |  52000.0000 |          - |         - |  219.01 MB |        0.20 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-schemajson"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-schemajson" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-schemajson" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-schemajson" style="max-width:960px"><canvas id="chart-bench-schemajson" style="height:500px"></canvas></div>
<p><a href="debian-schemajson.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


