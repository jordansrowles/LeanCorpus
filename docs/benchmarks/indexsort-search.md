---
title: Benchmarks - Index-sort (search)
---

# Index-sort (search)

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                   | DocumentCount | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0    | Gen1   | Gen2   | Allocated | Alloc Ratio |
|----------------------------------------- |-------------- |----------:|---------:|---------:|------:|--------:|--------:|-------:|-------:|----------:|------------:|
| LeanCorpus_SortedSearch_EarlyTermination | 100000        |  26.91 μs | 0.038 μs | 0.035 μs |  1.00 |    0.00 |  2.6245 |      - |      - |  10.82 KB |        1.00 |
| LeanCorpus_SortedSearch_PostSort         | 100000        | 226.74 μs | 1.199 μs | 1.122 μs |  8.43 |    0.04 | 13.4277 | 7.3242 | 7.3242 | 806.32 KB |       74.51 |
| LuceneNet_SortedSearch_FullSort          | 100000        |  96.19 μs | 0.158 μs | 0.147 μs |  3.57 |    0.01 | 17.7002 | 0.2441 |      - |  72.84 KB |        6.73 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-indexsort-search"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-indexsort-search" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-indexsort-search" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-indexsort-search" style="max-width:960px"><canvas id="chart-bench-indexsort-search" style="height:500px"></canvas></div>
<p><a href="indexsort-search.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


