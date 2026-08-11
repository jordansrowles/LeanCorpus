---
title: Benchmarks - Indexing
---

# Indexing

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                 | DocumentCount | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0        | Gen1       | Gen2      | Allocated | Alloc Ratio |
|--------------------------------------- |-------------- |---------:|---------:|---------:|------:|--------:|------------:|-----------:|----------:|----------:|------------:|
| LeanCorpus_AddDocument_Sync            | 100000        |  7.642 s | 0.1676 s | 0.0435 s |  1.00 |    0.00 | 165000.0000 | 69000.0000 | 5000.0000 |   1.07 GB |        1.00 |
| LeanCorpus_AddDocumentAsync_Sequential | 100000        | 11.569 s | 1.0041 s | 0.1554 s |  1.51 |    0.02 | 196000.0000 | 86000.0000 | 5000.0000 |   1.31 GB |        1.22 |
| LeanCorpus_AddDocumentsAsync_Batch     | 100000        | 12.551 s | 1.1857 s | 0.1835 s |  1.64 |    0.02 | 195000.0000 | 88000.0000 | 6000.0000 |   1.29 GB |        1.21 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-index"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-index" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-index" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-index" style="max-width:960px"><canvas id="chart-bench-index" style="height:500px"></canvas></div>
<p><a href="index.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


