---
title: Benchmarks - Suggester
---

# Suggester

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                 | DocumentCount | Mean     | Error     | StdDev    | Ratio | Gen0      | Gen1     | Allocated  | Alloc Ratio |
|----------------------- |-------------- |---------:|----------:|----------:|------:|----------:|---------:|-----------:|------------:|
| LeanCorpus_DidYouMean  | 100000        | 4.513 ms | 0.0052 ms | 0.0040 ms |  1.00 |         - |        - |   24.91 KB |        1.00 |
| LeanCorpus_SpellIndex  | 100000        | 4.534 ms | 0.0151 ms | 0.0134 ms |  1.00 |         - |        - |    23.2 KB |        0.93 |
| LuceneNet_SpellChecker | 100000        | 9.658 ms | 0.0160 ms | 0.0142 ms |  2.14 | 1296.8750 | 140.6250 | 5351.46 KB |      214.80 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-suggester"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-suggester" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-suggester" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-suggester" style="max-width:960px"><canvas id="chart-bench-suggester" style="height:500px"></canvas></div>
<p><a href="suggester.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


