---
title: Benchmarks - Query cache
---

# Query cache

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `66ba120` &nbsp;&middot;&nbsp; 24 August 2026 21:47 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                  | DocumentCount | Mean         | Error       | StdDev      | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|---------------------------------------- |-------------- |-------------:|------------:|------------:|------:|--------:|--------:|-------:|----------:|------------:|
| LeanCorpus_NoCache                      | 100000        | 115,745.6 ns |   158.96 ns |   148.69 ns | 1.000 |    0.00 |  0.8545 |      - |    3768 B |        1.00 |
| LeanCorpus_WithCache                    | 100000        |     346.1 ns |     6.75 ns |     8.04 ns | 0.003 |    0.00 |  0.1183 |      - |     496 B |        0.13 |
| &#39;Cache enabled, cacheable BooleanQuery&#39; | 100000        |     857.5 ns |     1.29 ns |     1.21 ns | 0.007 |    0.00 |  0.2556 |      - |    1072 B |        0.28 |
| &#39;Cache enabled, BooleanQuery misses&#39;    | 100000        | 433,750.3 ns | 2,969.15 ns | 2,479.37 ns | 3.747 |    0.02 |  4.3945 | 0.4883 |   21939 B |        5.82 |
| &#39;Cache disabled, BooleanQuery&#39;          | 100000        | 429,745.5 ns | 5,022.65 ns | 4,452.45 ns | 3.713 |    0.04 |  4.8828 |      - |   20744 B |        5.51 |
| LuceneNet_TermQuery                     | 100000        | 148,363.5 ns |   337.49 ns |   315.69 ns | 1.282 |    0.00 | 11.9629 | 0.2441 |   51119 B |       13.57 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-query-cache"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-query-cache" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-query-cache" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-query-cache" style="max-width:960px"><canvas id="chart-bench-query-cache" style="height:500px"></canvas></div>
<p><a href="query-cache.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


