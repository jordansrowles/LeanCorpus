---
title: Benchmarks - More like this
---

# More like this

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `66ba120` &nbsp;&middot;&nbsp; 24 August 2026 21:47 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                   | DocumentCount | Mean     | Error     | StdDev    | Ratio | RatioSD | Gen0     | Gen1    | Allocated  | Alloc Ratio |
|----------------------------------------- |-------------- |---------:|----------:|----------:|------:|--------:|---------:|--------:|-----------:|------------:|
| &#39;LeanCorpus MLT Scalar (DefaultParams)&#39;  | 100000        | 2.584 ms | 0.0500 ms | 0.0535 ms |  1.00 |    0.00 |  39.0625 |  7.8125 |  155.21 KB |        1.00 |
| &#39;LeanCorpus MLT Scalar (HighMinDocFreq)&#39; | 100000        | 2.511 ms | 0.0495 ms | 0.0813 ms |  0.97 |    0.04 |  11.7188 |       - |   53.11 KB |        0.34 |
| &#39;LeanCorpus MLT Scalar (NoBoost)&#39;        | 100000        | 2.585 ms | 0.0515 ms | 0.0704 ms |  1.00 |    0.03 |  39.0625 |  7.8125 |   156.6 KB |        1.01 |
| &#39;LeanCorpus MLT WAND (DefaultParams)&#39;    | 100000        | 2.839 ms | 0.0339 ms | 0.0317 ms |  1.10 |    0.03 |  58.5938 | 15.6250 |  237.63 KB |        1.53 |
| LuceneNet_MoreLikeThis_DefaultParams     | 100000        | 4.234 ms | 0.0591 ms | 0.0524 ms |  1.64 |    0.04 | 835.9375 | 23.4375 |  3569.1 KB |       23.00 |
| LuceneNet_MoreLikeThis_HighMinDocFreq    | 100000        | 3.328 ms | 0.0069 ms | 0.0064 ms |  1.29 |    0.03 | 281.2500 | 11.7188 |  1183.6 KB |        7.63 |
| LuceneNet_MoreLikeThis_NoBoost           | 100000        | 4.239 ms | 0.0684 ms | 0.0640 ms |  1.64 |    0.04 | 835.9375 | 23.4375 | 3568.66 KB |       22.99 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-mlt"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-mlt" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-mlt" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-mlt" style="max-width:960px"><canvas id="chart-bench-mlt" style="height:500px"></canvas></div>
<p><a href="mlt.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


