---
title: Benchmarks - Async index
---

# Async index

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `f3305a5` &nbsp;&middot;&nbsp; 16 July 2026 20:51 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                 | DocumentCount | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0        | Gen1       | Gen2      | Allocated | Alloc Ratio |
|--------------------------------------- |-------------- |---------:|---------:|---------:|------:|--------:|------------:|-----------:|----------:|----------:|------------:|
| LeanCorpus_AddDocument_Sync            | 100000        |  7.800 s | 0.1097 s | 0.0170 s |  1.00 |    0.00 |  99000.0000 | 45000.0000 | 6000.0000 | 806.21 MB |        1.00 |
| LeanCorpus_AddDocumentAsync_Sequential | 100000        |  9.813 s | 0.4972 s | 0.1291 s |  1.26 |    0.02 | 104000.0000 | 46000.0000 | 6000.0000 |  839.4 MB |        1.04 |
| LeanCorpus_AddDocumentsAsync_Batch     | 100000        | 10.807 s | 0.4268 s | 0.1108 s |  1.39 |    0.01 | 106000.0000 | 47000.0000 | 8000.0000 | 833.46 MB |        1.03 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-async-index"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-async-index" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-async-index" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-async-index" style="max-width:960px"><canvas id="chart-bench-async-index" style="height:500px"></canvas></div>
<p><a href="debian-async-index.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


