---
title: Benchmarks - Aggregation
---

# Aggregation

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `66ba120` &nbsp;&middot;&nbsp; 24 August 2026 21:47 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                 | DocumentCount | Mean        | Error    | StdDev   | Ratio | RatioSD | Gen0     | Gen1     | Allocated  | Alloc Ratio |
|--------------------------------------- |-------------- |------------:|---------:|---------:|------:|--------:|---------:|---------:|-----------:|------------:|
| LeanCorpus_SearchOnly                  | 100000        |    116.6 μs |  0.16 μs |  0.15 μs |  1.00 |    0.00 |   0.8545 |        - |    3.68 KB |        1.00 |
| LeanCorpus_SearchWithStats             | 100000        |  1,278.6 μs |  2.77 μs |  2.59 μs | 10.97 |    0.03 | 195.3125 |   3.9063 |  803.32 KB |      218.31 |
| LeanCorpus_SearchWithHistogram         | 100000        |  1,338.8 μs |  3.39 μs |  3.17 μs | 11.49 |    0.03 | 205.0781 |  41.0156 |   842.7 KB |      229.01 |
| LeanCorpus_SearchWithStatsAndHistogram | 100000        |  2,454.7 μs |  6.27 μs |  5.87 μs | 21.06 |    0.06 | 382.8125 |   3.9063 | 1577.88 KB |      428.81 |
| LuceneNet_TermQuery                    | 100000        |    187.1 μs |  0.43 μs |  0.38 μs |  1.61 |    0.00 |  18.3105 |   0.2441 |   75.72 KB |       20.58 |
| LuceneNet_SearchWithStats              | 100000        | 10,385.5 μs | 16.47 μs | 14.60 μs | 89.10 |    0.16 | 562.5000 | 421.8750 | 4018.06 KB |    1,091.96 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-aggregation"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-aggregation" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-aggregation" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-aggregation" style="max-width:960px"><canvas id="chart-bench-aggregation" style="height:500px"></canvas></div>
<p><a href="aggregation.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


