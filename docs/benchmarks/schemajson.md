---
title: Benchmarks - Schema and JSON
---

# Schema and JSON

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `66ba120` &nbsp;&middot;&nbsp; 24 August 2026 21:47 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                      | DocumentCount | Mean       | Error    | StdDev   | Ratio | Gen0        | Gen1       | Gen2      | Allocated  | Alloc Ratio |
|---------------------------- |-------------- |-----------:|---------:|---------:|------:|------------:|-----------:|----------:|-----------:|------------:|
| LeanCorpus_Index_NoSchema   | 100000        | 8,048.2 ms | 85.39 ms | 75.70 ms |  1.00 | 163000.0000 | 68000.0000 | 2000.0000 | 1102.34 MB |        1.00 |
| LeanCorpus_Index_WithSchema | 100000        | 8,132.1 ms | 38.57 ms | 36.08 ms |  1.01 | 165000.0000 | 68000.0000 | 3000.0000 | 1106.18 MB |        1.00 |
| LeanCorpus_JsonMapping      | 100000        |   419.5 ms |  2.12 ms |  1.98 ms |  0.05 |  52000.0000 |          - |         - |  219.01 MB |        0.20 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-schemajson"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-schemajson" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-schemajson" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-schemajson" style="max-width:960px"><canvas id="chart-bench-schemajson" style="height:500px"></canvas></div>
<p><a href="schemajson.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


