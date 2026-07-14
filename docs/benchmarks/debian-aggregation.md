---
title: Benchmarks - Aggregation
---

# Aggregation

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `dfecfdd` &nbsp;&middot;&nbsp; 12 July 2026 18:24 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                 | DocumentCount | Mean        | Error     | StdDev    | Ratio | RatioSD | Gen0     | Gen1     | Allocated | Alloc Ratio |
|--------------------------------------- |-------------- |------------:|----------:|----------:|------:|--------:|---------:|---------:|----------:|------------:|
| LeanCorpus_SearchOnly                  | 100000        |    88.48 μs |  0.108 μs |  0.096 μs |  1.00 |    0.00 |   0.1221 |        - |     720 B |        1.00 |
| LeanCorpus_SearchWithStats             | 100000        |   251.46 μs |  0.330 μs |  0.292 μs |  2.84 |    0.00 |  35.1563 |        - |  147792 B |      205.27 |
| LeanCorpus_SearchWithHistogram         | 100000        |   282.57 μs |  0.762 μs |  0.713 μs |  3.19 |    0.01 |  41.9922 |   4.3945 |  176928 B |      245.73 |
| LeanCorpus_SearchWithStatsAndHistogram | 100000        |   439.92 μs |  0.693 μs |  0.579 μs |  4.97 |    0.01 |  69.3359 |        - |  290784 B |      403.87 |
| LuceneNet_TermQuery                    | 100000        |   130.70 μs |  0.065 μs |  0.051 μs |  1.48 |    0.00 |  13.6719 |   0.2441 |   58325 B |       81.01 |
| LuceneNet_SearchWithStats              | 100000        | 8,254.27 μs | 25.074 μs | 20.938 μs | 93.29 |    0.25 | 562.5000 | 437.5000 | 4084036 B |    5,672.27 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-aggregation"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-aggregation" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-aggregation" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-aggregation" style="max-width:960px"><canvas id="chart-bench-aggregation" style="height:500px"></canvas></div>
<p><a href="debian-aggregation.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


