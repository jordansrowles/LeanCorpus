---
title: Benchmarks - Stemmer
---

# Stemmer

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                     | DocumentCount | Mean       | Error   | StdDev  | Ratio | Gen0       | Allocated | Alloc Ratio |
|--------------------------- |-------------- |-----------:|--------:|--------:|------:|-----------:|----------:|------------:|
| LeanCorpus_StemmedAnalyser | 100000        |   768.3 ms | 1.48 ms | 1.38 ms |  1.00 |          - |   2.29 MB |        1.00 |
| LuceneNet_EnglishAnalyzer  | 100000        | 1,192.0 ms | 1.70 ms | 1.59 ms |  1.55 | 51000.0000 | 206.02 MB |       90.01 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-stemmer"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-stemmer" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-stemmer" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-stemmer" style="max-width:960px"><canvas id="chart-bench-stemmer" style="height:500px"></canvas></div>
<p><a href="debian-stemmer.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


