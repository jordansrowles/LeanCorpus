---
title: Benchmarks - Index-sort (index)
---

# Index-sort (index)

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                    | DocumentCount | Mean    | Error    | StdDev   | Ratio | Gen0        | Gen1       | Gen2      | Allocated | Alloc Ratio |
|-------------------------- |-------------- |--------:|---------:|---------:|------:|------------:|-----------:|----------:|----------:|------------:|
| LeanCorpus_Index_Unsorted | 100000        | 8.063 s | 0.1659 s | 0.0431 s |  1.00 | 178000.0000 | 72000.0000 | 6000.0000 |   1.18 GB |        1.00 |
| LeanCorpus_Index_Sorted   | 100000        | 8.550 s | 0.1288 s | 0.0334 s |  1.06 | 182000.0000 | 72000.0000 | 5000.0000 |    1.2 GB |        1.02 |
| LuceneNet_Index_Unsorted  | 100000        | 9.908 s | 0.0200 s | 0.0031 s |  1.23 | 648000.0000 | 67000.0000 | 4000.0000 |   3.64 GB |        3.10 |
| LuceneNet_Index_Sorted    | 100000        | 9.358 s | 0.1665 s | 0.0432 s |  1.16 | 598000.0000 | 59000.0000 | 3000.0000 |   3.29 GB |        2.80 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-indexsort-index"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-indexsort-index" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-indexsort-index" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-indexsort-index" style="max-width:960px"><canvas id="chart-bench-indexsort-index" style="height:500px"></canvas></div>
<p><a href="indexsort-index.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


