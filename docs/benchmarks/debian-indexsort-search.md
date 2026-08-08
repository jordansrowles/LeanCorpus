---
title: Benchmarks - Index-sort (search)
---

# Index-sort (search)

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                   | DocumentCount | Mean       | Error     | StdDev    | Ratio  | RatioSD | Gen0    | Gen1   | Gen2   | Allocated | Alloc Ratio |
|----------------------------------------- |-------------- |-----------:|----------:|----------:|-------:|--------:|--------:|-------:|-------:|----------:|------------:|
| LeanCorpus_SortedSearch_EarlyTermination | 100000        |   2.714 μs | 0.0046 μs | 0.0043 μs |   1.00 |    0.00 |  0.2174 |      - |      - |     912 B |        1.00 |
| LeanCorpus_SortedSearch_PostSort         | 100000        | 337.122 μs | 3.2705 μs | 2.8992 μs | 124.23 |    1.05 | 38.0859 | 9.2773 | 9.2773 |  920881 B |    1,009.74 |
| LuceneNet_SortedSearch                   | 100000        | 125.674 μs | 0.2549 μs | 0.2384 μs |  46.31 |    0.11 | 20.0195 | 0.2441 |      - |   84677 B |       92.85 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-indexsort-search"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-indexsort-search" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-indexsort-search" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-indexsort-search" style="max-width:960px"><canvas id="chart-bench-indexsort-search" style="height:500px"></canvas></div>
<p><a href="debian-indexsort-search.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


