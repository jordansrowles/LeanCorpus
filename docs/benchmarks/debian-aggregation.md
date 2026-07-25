---
title: Benchmarks - Aggregation
---

# Aggregation

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `f3305a5` &nbsp;&middot;&nbsp; 16 July 2026 20:51 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                 | DocumentCount | Mean        | Error    | StdDev   | Ratio | RatioSD | Gen0     | Gen1     | Allocated | Alloc Ratio |
|--------------------------------------- |-------------- |------------:|---------:|---------:|------:|--------:|---------:|---------:|----------:|------------:|
| LeanCorpus_SearchOnly                  | 100000        |    157.0 μs |  0.19 μs |  0.17 μs |  1.00 |    0.00 |        - |        - |     720 B |        1.00 |
| LeanCorpus_SearchWithStats             | 100000        |    461.9 μs |  0.77 μs |  0.72 μs |  2.94 |    0.01 |  66.4063 |   7.3242 |  280168 B |      389.12 |
| LeanCorpus_SearchWithHistogram         | 100000        |    534.5 μs |  0.95 μs |  0.89 μs |  3.40 |    0.01 |  79.1016 |   0.9766 |  334200 B |      464.17 |
| LeanCorpus_SearchWithStatsAndHistogram | 100000        |    814.5 μs |  1.68 μs |  1.57 μs |  5.19 |    0.01 | 129.8828 |  21.4844 |  547640 B |      760.61 |
| LuceneNet_TermQuery                    | 100000        |    196.3 μs |  0.31 μs |  0.28 μs |  1.25 |    0.00 |  14.1602 |   0.4883 |   59884 B |       83.17 |
| LuceneNet_SearchWithStats              | 100000        | 10,915.1 μs | 61.44 μs | 54.46 μs | 69.51 |    0.34 | 546.8750 | 484.3750 | 4109036 B |    5,706.99 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-aggregation"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-aggregation" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-aggregation" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-aggregation" style="max-width:960px"><canvas id="chart-bench-aggregation" style="height:500px"></canvas></div>
<p><a href="debian-aggregation.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


