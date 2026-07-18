---
title: Benchmarks - Multi-phrase
---

# Multi-phrase

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `f3305a5` &nbsp;&middot;&nbsp; 16 July 2026 20:51 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                      | DocumentCount | Mean       | Error   | StdDev  | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|---------------------------- |-------------- |-----------:|--------:|--------:|------:|--------:|--------:|-------:|----------:|------------:|
| LeanCorpus_MultiPhraseQuery | 100000        |   612.6 μs | 6.28 μs | 5.57 μs |  1.00 |    0.00 | 21.4844 |      - |  87.49 KB |        1.00 |
| LuceneNet_MultiPhraseQuery  | 100000        | 1,341.2 μs | 0.53 μs | 0.44 μs |  2.19 |    0.02 | 52.7344 | 3.9063 | 221.77 KB |        2.53 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-multiphrase"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-multiphrase" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-multiphrase" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-multiphrase" style="max-width:960px"><canvas id="chart-bench-multiphrase" style="height:500px"></canvas></div>
<p><a href="debian-multiphrase.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


