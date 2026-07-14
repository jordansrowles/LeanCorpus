---
title: Benchmarks - Suggester
---

# Suggester

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                 | DocumentCount | Mean     | Error     | StdDev    | Ratio | RatioSD | Gen0      | Gen1     | Allocated  | Alloc Ratio |
|----------------------- |-------------- |---------:|----------:|----------:|------:|--------:|----------:|---------:|-----------:|------------:|
| LeanCorpus_DidYouMean  | 100000        | 1.671 ms | 0.0044 ms | 0.0039 ms |  1.00 |    0.00 |    1.9531 |        - |   12.87 KB |        1.00 |
| LeanCorpus_SpellIndex  | 100000        | 1.691 ms | 0.0017 ms | 0.0015 ms |  1.01 |    0.00 |    1.9531 |        - |   11.15 KB |        0.87 |
| LuceneNet_SpellChecker | 100000        | 8.461 ms | 0.0211 ms | 0.0197 ms |  5.06 |    0.02 | 1281.2500 | 109.3750 | 5246.59 KB |      407.75 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-suggester"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-suggester" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-suggester" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-suggester" style="max-width:960px"><canvas id="chart-bench-suggester" style="height:500px"></canvas></div>
<p><a href="debian-suggester.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


