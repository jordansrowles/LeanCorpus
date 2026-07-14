---
title: Benchmarks - Multi-phrase
---

# Multi-phrase

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                      | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|---------------------------- |-------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| LeanCorpus_MultiPhraseQuery | 100000        | 370.8 μs | 1.41 μs | 1.32 μs |  1.00 | 16.1133 |      - |  66.21 KB |        1.00 |
| LuceneNet_MultiPhraseQuery  | 100000        | 768.7 μs | 1.25 μs | 1.17 μs |  2.07 | 36.1328 | 1.9531 | 153.78 KB |        2.32 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-multiphrase"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-multiphrase" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-multiphrase" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-multiphrase" style="max-width:960px"><canvas id="chart-bench-multiphrase" style="height:500px"></canvas></div>
<p><a href="debian-multiphrase.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


