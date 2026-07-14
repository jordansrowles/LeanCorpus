---
title: Benchmarks - Index-sort (index)
---

# Index-sort (index)

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                    | DocumentCount | Mean    | Error    | StdDev   | Ratio | RatioSD | Gen0        | Gen1       | Gen2       | Allocated  | Alloc Ratio |
|-------------------------- |-------------- |--------:|---------:|---------:|------:|--------:|------------:|-----------:|-----------:|-----------:|------------:|
| LeanCorpus_Index_Unsorted | 100000        | 8.372 s | 0.4566 s | 0.1186 s |  1.00 |    0.00 |  94000.0000 | 46000.0000 | 10000.0000 | 1012.38 MB |        1.00 |
| LeanCorpus_Index_Sorted   | 100000        | 8.767 s | 0.4813 s | 0.0745 s |  1.05 |    0.02 |  97000.0000 | 48000.0000 | 12000.0000 | 1038.03 MB |        1.03 |
| LuceneNet_Index_Unsorted  | 100000        | 6.050 s | 0.0724 s | 0.0188 s |  0.72 |    0.01 | 590000.0000 | 56000.0000 |  6000.0000 |  3135.1 MB |        3.10 |
| LuceneNet_Index_Sorted    | 100000        | 5.228 s | 0.0539 s | 0.0140 s |  0.62 |    0.01 | 504000.0000 | 43000.0000 |  5000.0000 | 2623.93 MB |        2.59 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-indexsort-index"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-indexsort-index" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-indexsort-index" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-indexsort-index" style="max-width:960px"><canvas id="chart-bench-indexsort-index" style="height:500px"></canvas></div>
<p><a href="debian-indexsort-index.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


