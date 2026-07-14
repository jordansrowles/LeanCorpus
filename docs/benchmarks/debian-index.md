---
title: Benchmarks - Indexing
---

# Indexing

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                    | DocumentCount | Mean    | Error    | StdDev   | Ratio | Gen0        | Gen1       | Gen2      | Allocated  | Alloc Ratio |
|-------------------------- |-------------- |--------:|---------:|---------:|------:|------------:|-----------:|----------:|-----------:|------------:|
| LeanCorpus_IndexDocuments | 100000        | 6.845 s | 0.1711 s | 0.0265 s |  1.00 |  86000.0000 | 42000.0000 | 9000.0000 |  925.71 MB |        1.00 |
| LuceneNet_IndexDocuments  | 100000        | 4.610 s | 0.3021 s | 0.0467 s |  0.67 | 273000.0000 |  8000.0000 | 1000.0000 | 1256.33 MB |        1.36 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-index"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-index" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-index" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-index" style="max-width:960px"><canvas id="chart-bench-index" style="height:500px"></canvas></div>
<p><a href="debian-index.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


