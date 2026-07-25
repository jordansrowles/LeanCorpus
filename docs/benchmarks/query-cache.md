---
title: Benchmarks - Query cache
---

# Query cache

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                  | DocumentCount | Mean         | Error       | StdDev      | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|---------------------------------------- |-------------- |-------------:|------------:|------------:|------:|--------:|--------:|-------:|----------:|------------:|
| LeanCorpus_NoCache                      | 100000        | 103,385.2 ns |   263.41 ns |   233.51 ns | 1.000 |    0.00 |  0.1221 |      - |     880 B |        1.00 |
| LeanCorpus_WithCache                    | 100000        |     242.4 ns |     0.28 ns |     0.25 ns | 0.002 |    0.00 |  0.1183 |      - |     496 B |        0.56 |
| &#39;Cache enabled, cacheable BooleanQuery&#39; | 100000        |     689.9 ns |     0.52 ns |     0.43 ns | 0.007 |    0.00 |  0.2518 |      - |    1056 B |        1.20 |
| &#39;Cache enabled, BooleanQuery misses&#39;    | 100000        | 420,382.8 ns | 5,288.31 ns | 4,946.69 ns | 4.066 |    0.05 |  3.4180 | 0.4883 |   17633 B |       20.04 |
| &#39;Cache disabled, BooleanQuery&#39;          | 100000        | 425,095.9 ns | 5,197.87 ns | 4,862.09 ns | 4.112 |    0.05 |  3.9063 |      - |   16525 B |       18.78 |
| LuceneNet_TermQuery                     | 100000        | 148,049.8 ns |   453.40 ns |   401.93 ns | 1.432 |    0.00 | 11.9629 | 0.2441 |   51119 B |       58.09 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-query-cache"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-query-cache" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-query-cache" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-query-cache" style="max-width:960px"><canvas id="chart-bench-query-cache" style="height:500px"></canvas></div>
<p><a href="query-cache.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


