---
title: Benchmarks - tokenbudget
---

# tokenbudget

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c4ff69e` &nbsp;&middot;&nbsp; 6 August 2026 20:14 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                               | DocumentCount | Mean    | Error    | StdDev   | Ratio | RatioSD | Gen0        | Gen1       | Gen2      | Allocated  | Alloc Ratio |
|------------------------------------- |-------------- |--------:|---------:|---------:|------:|--------:|------------:|-----------:|----------:|-----------:|------------:|
| LeanCorpus_Index_NoBudget            | 100000        | 7.813 s | 0.0505 s | 0.0472 s |  1.00 |    0.00 | 170000.0000 | 71000.0000 | 6000.0000 | 1116.06 MB |        1.00 |
| LeanCorpus_Index_WithBudget_Truncate | 100000        | 5.694 s | 0.0394 s | 0.0349 s |  0.73 |    0.01 | 134000.0000 | 54000.0000 | 5000.0000 |  876.48 MB |        0.79 |
| LeanCorpus_Index_WithBudget_Reject   | 100000        |      NA |       NA |       NA |     ? |       ? |          NA |         NA |        NA |         NA |           ? |

Benchmarks with issues:
  TokenBudgetBenchmarks.LeanCorpus_Index_WithBudget_Reject: DefaultJob [DocumentCount=100000]

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-tokenbudget"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-tokenbudget" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-tokenbudget" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-tokenbudget" style="max-width:960px"><canvas id="chart-bench-tokenbudget" style="height:500px"></canvas></div>
<p><a href="tokenbudget.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


