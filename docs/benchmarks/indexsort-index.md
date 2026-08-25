---
title: Benchmarks - Index-sort (index)
---

# Index-sort (index)

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `66ba120` &nbsp;&middot;&nbsp; 24 August 2026 21:47 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                    | DocumentCount | Mean     | Error    | StdDev   | Ratio | Gen0        | Gen1       | Gen2      | Allocated | Alloc Ratio |
|-------------------------- |-------------- |---------:|---------:|---------:|------:|------------:|-----------:|----------:|----------:|------------:|
| LeanCorpus_Index_Unsorted | 100000        |  8.466 s | 0.2076 s | 0.0321 s |  1.00 | 175000.0000 | 71000.0000 | 5000.0000 |   1.15 GB |        1.00 |
| LeanCorpus_Index_Sorted   | 100000        |  8.886 s | 0.3504 s | 0.0542 s |  1.05 | 180000.0000 | 73000.0000 | 7000.0000 |   1.17 GB |        1.02 |
| LuceneNet_Index_Unsorted  | 100000        | 10.034 s | 0.1028 s | 0.0267 s |  1.19 | 649000.0000 | 74000.0000 | 4000.0000 |   3.64 GB |        3.18 |
| LuceneNet_Index_Sorted    | 100000        |  9.338 s | 0.1485 s | 0.0386 s |  1.10 | 592000.0000 | 59000.0000 | 3000.0000 |   3.29 GB |        2.87 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-indexsort-index"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-indexsort-index" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-indexsort-index" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-indexsort-index" style="max-width:960px"><canvas id="chart-bench-indexsort-index" style="height:500px"></canvas></div>
<p><a href="indexsort-index.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


