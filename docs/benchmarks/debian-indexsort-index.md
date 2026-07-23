---
title: Benchmarks - Index-sort (index)
---

# Index-sort (index)

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `f3305a5` &nbsp;&middot;&nbsp; 16 July 2026 20:51 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                    | DocumentCount | Mean     | Error    | StdDev   | Ratio | Gen0        | Gen1       | Gen2       | Allocated  | Alloc Ratio |
|-------------------------- |-------------- |---------:|---------:|---------:|------:|------------:|-----------:|-----------:|-----------:|------------:|
| LeanCorpus_Index_Unsorted | 100000        |  9.858 s | 0.3258 s | 0.0846 s |  1.00 | 116000.0000 | 55000.0000 | 11000.0000 |  916.69 MB |        1.00 |
| LeanCorpus_Index_Sorted   | 100000        | 10.357 s | 0.2243 s | 0.0583 s |  1.05 | 116000.0000 | 54000.0000 | 10000.0000 |  940.25 MB |        1.03 |
| LuceneNet_Index_Unsorted  | 100000        |  7.318 s | 0.1792 s | 0.0465 s |  0.74 | 588000.0000 | 65000.0000 |  7000.0000 | 3212.81 MB |        3.50 |
| LuceneNet_Index_Sorted    | 100000        |  6.368 s | 0.1895 s | 0.0492 s |  0.65 | 486000.0000 | 39000.0000 |  5000.0000 |  2613.7 MB |        2.85 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-indexsort-index"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-indexsort-index" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-indexsort-index" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-indexsort-index" style="max-width:960px"><canvas id="chart-bench-indexsort-index" style="height:500px"></canvas></div>
<p><a href="debian-indexsort-index.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


