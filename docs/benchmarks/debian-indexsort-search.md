---
title: Benchmarks - Index-sort (search)
---

# Index-sort (search)

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `f3305a5` &nbsp;&middot;&nbsp; 16 July 2026 20:51 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                   | DocumentCount | Mean       | Error     | StdDev    | Ratio  | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|----------------------------------------- |-------------- |-----------:|----------:|----------:|-------:|--------:|--------:|-------:|----------:|------------:|
| LeanCorpus_SortedSearch_EarlyTermination | 100000        |   1.744 μs | 0.0024 μs | 0.0022 μs |   1.00 |    0.00 |  0.2041 |      - |     856 B |        1.00 |
| LeanCorpus_SortedSearch_PostSort         | 100000        | 301.042 μs | 0.3327 μs | 0.2949 μs | 172.64 |    0.27 | 34.1797 |      - |  144448 B |      168.75 |
| LuceneNet_SortedSearch                   | 100000        | 104.318 μs | 0.2953 μs | 0.2306 μs |  59.82 |    0.15 | 15.0146 | 0.2441 |   63954 B |       74.71 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-indexsort-search"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-indexsort-search" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-indexsort-search" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-indexsort-search" style="max-width:960px"><canvas id="chart-bench-indexsort-search" style="height:500px"></canvas></div>
<p><a href="debian-indexsort-search.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


