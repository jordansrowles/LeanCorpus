---
title: Benchmarks - Index-sort (index)
---

# Index-sort (index)

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                    | DocumentCount | Mean    | Error    | StdDev   | Ratio | Gen0        | Gen1       | Gen2      | Allocated | Alloc Ratio |
|-------------------------- |-------------- |--------:|---------:|---------:|------:|------------:|-----------:|----------:|----------:|------------:|
| LeanCorpus_Index_Unsorted | 100000        | 8.077 s | 0.1343 s | 0.0349 s |  1.00 | 177000.0000 | 73000.0000 | 5000.0000 |   1.18 GB |        1.00 |
| LeanCorpus_Index_Sorted   | 100000        | 8.142 s | 0.2027 s | 0.0526 s |  1.01 | 177000.0000 | 73000.0000 | 5000.0000 |   1.18 GB |        1.00 |
| LuceneNet_Index_Unsorted  | 100000        | 9.850 s | 0.1062 s | 0.0164 s |  1.22 | 646000.0000 | 75000.0000 | 4000.0000 |   3.64 GB |        3.10 |
| LuceneNet_Index_Sorted    | 100000        | 9.298 s | 0.0798 s | 0.0207 s |  1.15 | 593000.0000 | 62000.0000 | 3000.0000 |   3.29 GB |        2.80 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-indexsort-index"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-indexsort-index" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-indexsort-index" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-indexsort-index" style="max-width:960px"><canvas id="chart-bench-indexsort-index" style="height:500px"></canvas></div>
<p><a href="indexsort-index.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


