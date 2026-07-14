---
title: Benchmarks - Query cache
---

# Query cache

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                            | DocumentCount | Mean        | Error       | StdDev      | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|---------------------------------- |-------------- |------------:|------------:|------------:|------:|--------:|-------:|-------:|----------:|------------:|
| LeanCorpus_NoCache                | 100000        | 86,418.6 ns |    73.05 ns |    57.03 ns | 1.000 |    0.00 | 0.1221 |      - |     720 B |        1.00 |
| LeanCorpus_WithCache              | 100000        |    255.5 ns |     0.47 ns |     0.41 ns | 0.003 |    0.00 | 0.1183 |      - |     496 B |        0.69 |
| LeanCorpus_WithCache_BooleanQuery | 100000        |    778.1 ns |     1.06 ns |     0.94 ns | 0.009 |    0.00 | 0.2518 |      - |    1056 B |        1.47 |
| LeanCorpus_NoCache_BooleanQuery   | 100000        | 93,250.2 ns | 1,908.54 ns | 5,597.41 ns | 1.079 |    0.06 | 3.7842 |      - |   15927 B |       22.12 |
| LuceneNet_TermQuery               | 100000        | 90,093.0 ns |   272.24 ns |   254.65 ns | 1.043 |    0.00 | 5.3711 | 0.1221 |   22543 B |       31.31 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-query-cache"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-query-cache" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-query-cache" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-query-cache" style="max-width:960px"><canvas id="chart-bench-query-cache" style="height:500px"></canvas></div>
<p><a href="debian-query-cache.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


