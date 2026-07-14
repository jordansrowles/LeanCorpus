---
title: Benchmarks - KStemmer
---

# KStemmer

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                   | DocumentCount | Mean       | Error   | StdDev  | Ratio | Gen0       | Allocated | Alloc Ratio |
|------------------------- |-------------- |-----------:|--------:|--------:|------:|-----------:|----------:|------------:|
| LeanCorpus_KStem_Analyse | 100000        |   726.3 ms | 1.83 ms | 1.62 ms |  1.00 |          - |   2.29 MB |        1.00 |
| LuceneNet_KStem_Analyse  | 100000        | 1,002.0 ms | 6.85 ms | 5.72 ms |  1.38 | 52000.0000 | 211.25 MB |       92.30 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-kstemmer"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-kstemmer" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-kstemmer" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-kstemmer" style="max-width:960px"><canvas id="chart-bench-kstemmer" style="height:500px"></canvas></div>
<p><a href="debian-kstemmer.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


