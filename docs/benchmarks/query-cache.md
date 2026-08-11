---
title: Benchmarks - Query cache
---

# Query cache

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                  | DocumentCount | Mean         | Error       | StdDev      | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|---------------------------------------- |-------------- |-------------:|------------:|------------:|------:|--------:|--------:|-------:|----------:|------------:|
| LeanCorpus_NoCache                      | 100000        | 109,547.5 ns |   123.95 ns |   115.94 ns | 1.000 |    0.00 |  0.1221 |      - |     808 B |        1.00 |
| LeanCorpus_WithCache                    | 100000        |     264.4 ns |     0.16 ns |     0.13 ns | 0.002 |    0.00 |  0.1183 |      - |     496 B |        0.61 |
| &#39;Cache enabled, cacheable BooleanQuery&#39; | 100000        |     735.9 ns |     1.10 ns |     0.98 ns | 0.007 |    0.00 |  0.2556 |      - |    1072 B |        1.33 |
| &#39;Cache enabled, BooleanQuery misses&#39;    | 100000        | 429,402.6 ns | 4,504.12 ns | 4,213.16 ns | 3.920 |    0.04 |  3.4180 | 0.4883 |   17577 B |       21.75 |
| &#39;Cache disabled, BooleanQuery&#39;          | 100000        | 431,509.0 ns | 3,557.72 ns | 3,327.89 ns | 3.939 |    0.03 |  3.9063 |      - |   16406 B |       20.30 |
| LuceneNet_TermQuery                     | 100000        | 146,954.6 ns |   171.63 ns |   143.32 ns | 1.341 |    0.00 | 11.9629 | 0.2441 |   51119 B |       63.27 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-query-cache"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-query-cache" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-query-cache" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-query-cache" style="max-width:960px"><canvas id="chart-bench-query-cache" style="height:500px"></canvas></div>
<p><a href="query-cache.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


