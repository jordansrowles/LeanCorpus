---
title: Benchmarks - More like this
---

# More like this

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                   | DocumentCount | Mean     | Error     | StdDev    | Ratio | RatioSD | Gen0     | Gen1    | Allocated  | Alloc Ratio |
|----------------------------------------- |-------------- |---------:|----------:|----------:|------:|--------:|---------:|--------:|-----------:|------------:|
| &#39;LeanCorpus MLT Scalar (DefaultParams)&#39;  | 100000        | 2.350 ms | 0.0466 ms | 0.0498 ms |  1.00 |    0.00 |  27.3438 |  7.8125 |  113.93 KB |        1.00 |
| &#39;LeanCorpus MLT Scalar (HighMinDocFreq)&#39; | 100000        | 2.455 ms | 0.0489 ms | 0.1161 ms |  1.05 |    0.05 |   7.8125 |       - |   36.86 KB |        0.32 |
| &#39;LeanCorpus MLT Scalar (NoBoost)&#39;        | 100000        | 2.422 ms | 0.0478 ms | 0.0977 ms |  1.03 |    0.05 |  27.3438 |  7.8125 |  115.12 KB |        1.01 |
| &#39;LeanCorpus MLT WAND (DefaultParams)&#39;    | 100000        | 2.741 ms | 0.0450 ms | 0.0399 ms |  1.17 |    0.03 |  46.8750 | 11.7188 |  197.96 KB |        1.74 |
| LuceneNet_MoreLikeThis_DefaultParams     | 100000        | 4.142 ms | 0.0651 ms | 0.0609 ms |  1.76 |    0.04 | 835.9375 | 23.4375 | 3568.78 KB |       31.32 |
| LuceneNet_MoreLikeThis_HighMinDocFreq    | 100000        | 3.351 ms | 0.0075 ms | 0.0070 ms |  1.43 |    0.03 | 285.1563 | 15.6250 | 1183.48 KB |       10.39 |
| LuceneNet_MoreLikeThis_NoBoost           | 100000        | 4.149 ms | 0.0705 ms | 0.0659 ms |  1.77 |    0.05 | 851.5625 | 23.4375 |  3569.3 KB |       31.33 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-mlt"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-mlt" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-mlt" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-mlt" style="max-width:960px"><canvas id="chart-bench-mlt" style="height:500px"></canvas></div>
<p><a href="mlt.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


