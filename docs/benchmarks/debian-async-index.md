---
title: Benchmarks - Async index
---

# Async index

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                 | DocumentCount | Mean    | Error    | StdDev   | Ratio | RatioSD | Gen0       | Gen1       | Gen2      | Allocated | Alloc Ratio |
|--------------------------------------- |-------------- |--------:|---------:|---------:|------:|--------:|-----------:|-----------:|----------:|----------:|------------:|
| LeanCorpus_AddDocument_Sync            | 100000        | 6.594 s | 0.3230 s | 0.0839 s |  1.00 |    0.00 | 80000.0000 | 39000.0000 | 7000.0000 | 902.85 MB |        1.00 |
| LeanCorpus_AddDocumentAsync_Sequential | 100000        | 8.012 s | 0.4363 s | 0.1133 s |  1.22 |    0.02 | 84000.0000 | 41000.0000 | 6000.0000 | 930.39 MB |        1.03 |
| LeanCorpus_AddDocumentsAsync_Batch     | 100000        | 7.991 s | 0.6809 s | 0.1768 s |  1.21 |    0.03 | 83000.0000 | 40000.0000 | 6000.0000 | 927.77 MB |        1.03 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-async-index"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-async-index" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-async-index" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-async-index" style="max-width:960px"><canvas id="chart-bench-async-index" style="height:500px"></canvas></div>
<p><a href="debian-async-index.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


