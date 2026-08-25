---
title: Benchmarks - Index-sort (search)
---

# Index-sort (search)

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `66ba120` &nbsp;&middot;&nbsp; 24 August 2026 21:47 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                   | DocumentCount | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0    | Gen1    | Gen2   | Allocated | Alloc Ratio |
|----------------------------------------- |-------------- |----------:|---------:|---------:|------:|--------:|--------:|--------:|-------:|----------:|------------:|
| LeanCorpus_SortedSearch_EarlyTermination | 100000        |  72.71 μs | 0.154 μs | 0.144 μs |  1.00 |    0.00 |  9.2773 |       - |      - |  38.01 KB |        1.00 |
| LeanCorpus_SortedSearch_PostSort         | 100000        | 557.85 μs | 1.147 μs | 1.016 μs |  7.67 |    0.02 | 57.6172 | 11.7188 | 8.7891 | 984.51 KB |       25.90 |
| LuceneNet_SortedSearch_FullSort          | 100000        | 104.36 μs | 1.901 μs | 1.779 μs |  1.44 |    0.02 | 17.7002 |  0.2441 |      - |  72.84 KB |        1.92 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-indexsort-search"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-indexsort-search" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-indexsort-search" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-indexsort-search" style="max-width:960px"><canvas id="chart-bench-indexsort-search" style="height:500px"></canvas></div>
<p><a href="indexsort-search.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


