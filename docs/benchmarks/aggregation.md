---
title: Benchmarks - Aggregation
---

# Aggregation

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                 | DocumentCount | Mean        | Error    | StdDev   | Ratio | RatioSD | Gen0     | Gen1     | Allocated | Alloc Ratio |
|--------------------------------------- |-------------- |------------:|---------:|---------:|------:|--------:|---------:|---------:|----------:|------------:|
| LeanCorpus_SearchOnly                  | 100000        |    110.5 μs |  0.12 μs |  0.10 μs |  1.00 |    0.00 |   0.1221 |        - |     808 B |        1.00 |
| LeanCorpus_SearchWithStats             | 100000        |    368.4 μs |  0.55 μs |  0.51 μs |  3.33 |    0.01 |  53.7109 |   5.8594 |  225400 B |      278.96 |
| LeanCorpus_SearchWithHistogram         | 100000        |    417.5 μs |  0.66 μs |  0.58 μs |  3.78 |    0.01 |  63.4766 |   0.4883 |  265720 B |      328.86 |
| LeanCorpus_SearchWithStatsAndHistogram | 100000        |    651.4 μs |  1.16 μs |  1.09 μs |  5.89 |    0.01 | 100.5859 |        - |  424312 B |      525.14 |
| LuceneNet_TermQuery                    | 100000        |    189.8 μs |  0.48 μs |  0.40 μs |  1.72 |    0.00 |  18.3105 |   0.2441 |   77541 B |       95.97 |
| LuceneNet_SearchWithStats              | 100000        | 10,198.1 μs | 15.82 μs | 14.03 μs | 92.28 |    0.15 | 562.5000 | 421.8750 | 4114497 B |    5,092.20 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-aggregation"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-aggregation" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-aggregation" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-aggregation" style="max-width:960px"><canvas id="chart-bench-aggregation" style="height:500px"></canvas></div>
<p><a href="aggregation.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


