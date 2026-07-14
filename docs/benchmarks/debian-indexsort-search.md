---
title: Benchmarks - Index-sort (search)
---

# Index-sort (search)

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                   | DocumentCount | Mean      | Error    | StdDev   | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|----------------------------------------- |-------------- |----------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| LeanCorpus_SortedSearch_EarlyTermination | 100000        | 147.38 μs | 0.362 μs | 0.321 μs |  1.00 | 18.3105 |      - |  75.44 KB |        1.00 |
| LeanCorpus_SortedSearch_PostSort         | 100000        | 145.83 μs | 0.231 μs | 0.205 μs |  0.99 | 18.3105 |      - |  75.44 KB |        1.00 |
| LuceneNet_SortedSearch                   | 100000        |  85.41 μs | 0.162 μs | 0.135 μs |  0.58 | 14.7705 | 0.2441 |  61.08 KB |        0.81 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-indexsort-search"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-indexsort-search" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-indexsort-search" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-indexsort-search" style="max-width:960px"><canvas id="chart-bench-indexsort-search" style="height:500px"></canvas></div>
<p><a href="debian-indexsort-search.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


