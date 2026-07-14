---
title: Benchmarks - More like this
---

# More like this

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `23d347f` &nbsp;&middot;&nbsp; 12 July 2026 19:05 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                      | DocumentCount | Mean         | Error       | StdDev      | Ratio | RatioSD | Gen0    | Gen1    | Allocated | Alloc Ratio |
|-------------------------------------------- |-------------- |-------------:|------------:|------------:|------:|--------:|--------:|--------:|----------:|------------:|
| LeanCorpus_MoreLikeThisQuery_DefaultParams  | 100000        | 7,365.209 μs |  44.8233 μs |  39.7347 μs | 1.000 |    0.00 |  7.8125 |       - |  45.04 KB |        1.00 |
| LeanCorpus_MoreLikeThisQuery_HighMinDocFreq | 100000        |     1.196 μs |   0.0061 μs |   0.0054 μs | 0.000 |    0.00 |  0.7267 |       - |   2.97 KB |        0.07 |
| LeanCorpus_MoreLikeThisQuery_NoBoost        | 100000        | 7,526.499 μs | 148.1284 μs | 212.4413 μs | 1.022 |    0.03 |       - |       - |  45.04 KB |        1.00 |
| LuceneNet_MoreLikeThis_DefaultParams        | 100000        |    61.666 μs |   0.6214 μs |   0.5813 μs | 0.008 |    0.00 | 57.2510 | 12.6953 | 234.27 KB |        5.20 |
| LuceneNet_MoreLikeThis_HighMinDocFreq       | 100000        |    61.667 μs |   0.4776 μs |   0.4468 μs | 0.008 |    0.00 | 57.2510 | 12.6953 | 234.27 KB |        5.20 |
| LuceneNet_MoreLikeThis_NoBoost              | 100000        |    62.098 μs |   0.6713 μs |   0.6279 μs | 0.008 |    0.00 | 57.2510 | 12.6953 | 234.27 KB |        5.20 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-mlt"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-mlt" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-mlt" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-mlt" style="max-width:960px"><canvas id="chart-bench-mlt" style="height:500px"></canvas></div>
<p><a href="debian-mlt.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


