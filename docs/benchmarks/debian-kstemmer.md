---
title: Benchmarks - KStemmer
---

# KStemmer

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `f3305a5` &nbsp;&middot;&nbsp; 16 July 2026 20:51 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                   | DocumentCount | Mean    | Error    | StdDev   | Ratio | Gen0       | Allocated | Alloc Ratio |
|------------------------- |-------------- |--------:|---------:|---------:|------:|-----------:|----------:|------------:|
| LeanCorpus_KStem_Analyse | 100000        | 1.266 s | 0.0020 s | 0.0017 s |  1.00 |          - |   2.29 MB |        1.00 |
| LuceneNet_KStem_Analyse  | 100000        | 1.730 s | 0.0022 s | 0.0018 s |  1.37 | 88000.0000 | 351.28 MB |      153.47 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-kstemmer"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-kstemmer" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-kstemmer" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-kstemmer" style="max-width:960px"><canvas id="chart-bench-kstemmer" style="height:500px"></canvas></div>
<p><a href="debian-kstemmer.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


