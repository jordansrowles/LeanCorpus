---
title: Benchmarks - More like this
---

# More like this

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                   | DocumentCount | Mean     | Error     | StdDev    | Median   | Ratio | RatioSD | Gen0     | Gen1    | Allocated | Alloc Ratio |
|----------------------------------------- |-------------- |---------:|----------:|----------:|---------:|------:|--------:|---------:|--------:|----------:|------------:|
| &#39;LeanCorpus MLT Scalar (DefaultParams)&#39;  | 100000        | 2.376 ms | 0.0471 ms | 0.0908 ms | 2.348 ms |  1.00 |    0.00 |  27.3438 |  7.8125 | 117.83 KB |        1.00 |
| &#39;LeanCorpus MLT Scalar (HighMinDocFreq)&#39; | 100000        | 2.348 ms | 0.0466 ms | 0.1268 ms | 2.311 ms |  0.99 |    0.06 |   7.8125 |       - |  37.55 KB |        0.32 |
| &#39;LeanCorpus MLT Scalar (NoBoost)&#39;        | 100000        | 2.347 ms | 0.0430 ms | 0.0742 ms | 2.334 ms |  0.99 |    0.05 |  27.3438 |  3.9063 | 117.07 KB |        0.99 |
| &#39;LeanCorpus MLT WAND (DefaultParams)&#39;    | 100000        | 2.597 ms | 0.0518 ms | 0.0636 ms | 2.591 ms |  1.09 |    0.05 |  46.8750 | 11.7188 | 198.43 KB |        1.68 |
| LuceneNet_MoreLikeThis_DefaultParams     | 100000        | 4.191 ms | 0.0545 ms | 0.0484 ms | 4.186 ms |  1.77 |    0.07 | 851.5625 | 23.4375 | 3569.3 KB |       30.29 |
| LuceneNet_MoreLikeThis_HighMinDocFreq    | 100000        | 3.378 ms | 0.0060 ms | 0.0056 ms | 3.378 ms |  1.42 |    0.05 | 281.2500 | 11.7188 | 1183.6 KB |       10.05 |
| LuceneNet_MoreLikeThis_NoBoost           | 100000        | 4.193 ms | 0.0330 ms | 0.0275 ms | 4.182 ms |  1.77 |    0.07 | 835.9375 | 23.4375 | 3569.1 KB |       30.29 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-mlt"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-mlt" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-mlt" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-mlt" style="max-width:960px"><canvas id="chart-bench-mlt" style="height:500px"></canvas></div>
<p><a href="mlt.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


