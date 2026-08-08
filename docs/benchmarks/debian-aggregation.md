---
title: Benchmarks - Aggregation
---

# Aggregation

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                 | DocumentCount | Mean        | Error    | StdDev   | Ratio | RatioSD | Gen0     | Gen1     | Allocated | Alloc Ratio |
|--------------------------------------- |-------------- |------------:|---------:|---------:|------:|--------:|---------:|---------:|----------:|------------:|
| LeanCorpus_SearchOnly                  | 100000        |    103.8 μs |  0.16 μs |  0.15 μs |  1.00 |    0.00 |   0.1221 |        - |     880 B |        1.00 |
| LeanCorpus_SearchWithStats             | 100000        |    424.9 μs |  0.58 μs |  0.52 μs |  4.09 |    0.01 |  53.7109 |   1.9531 |  225480 B |      256.23 |
| LeanCorpus_SearchWithHistogram         | 100000        |    408.6 μs |  0.39 μs |  0.32 μs |  3.94 |    0.01 |  63.4766 |        - |  265800 B |      302.05 |
| LeanCorpus_SearchWithStatsAndHistogram | 100000        |    652.4 μs |  1.03 μs |  0.96 μs |  6.29 |    0.01 | 100.5859 |        - |  424392 B |      482.26 |
| LuceneNet_TermQuery                    | 100000        |    187.8 μs |  0.49 μs |  0.43 μs |  1.81 |    0.00 |  18.3105 |   0.2441 |   77541 B |       88.11 |
| LuceneNet_SearchWithStats              | 100000        | 10,089.9 μs | 14.12 μs | 11.79 μs | 97.23 |    0.17 | 562.5000 | 421.8750 | 4114497 B |    4,675.56 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-aggregation"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-aggregation" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-aggregation" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-aggregation" style="max-width:960px"><canvas id="chart-bench-aggregation" style="height:500px"></canvas></div>
<p><a href="debian-aggregation.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


