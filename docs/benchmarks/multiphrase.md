---
title: Benchmarks - Multi-phrase
---

# Multi-phrase

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                      | DocumentCount | Mean     | Error     | StdDev    | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|---------------------------- |-------------- |---------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| LeanCorpus_MultiPhraseQuery | 100000        | 1.089 ms | 0.0015 ms | 0.0014 ms |  1.00 | 17.5781 |      - |  78.53 KB |        1.00 |
| LuceneNet_MultiPhraseQuery  | 100000        | 1.113 ms | 0.0014 ms | 0.0012 ms |  1.02 | 87.8906 | 1.9531 | 371.22 KB |        4.73 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-multiphrase"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-multiphrase" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-multiphrase" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-multiphrase" style="max-width:960px"><canvas id="chart-bench-multiphrase" style="height:500px"></canvas></div>
<p><a href="multiphrase.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


