---
title: Benchmarks - More like this
---

# More like this

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `f3305a5` &nbsp;&middot;&nbsp; 16 July 2026 20:51 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                   | DocumentCount | Mean       | Error    | StdDev   | Ratio | RatioSD | Gen0     | Gen1    | Allocated  | Alloc Ratio |
|----------------------------------------- |-------------- |-----------:|---------:|---------:|------:|--------:|---------:|--------:|-----------:|------------:|
| &#39;LeanCorpus MLT Scalar (DefaultParams)&#39;  | 100000        | 1,671.6 μs | 19.26 μs | 18.02 μs |  1.00 |    0.00 |  29.2969 |       - |  116.62 KB |        1.00 |
| &#39;LeanCorpus MLT Scalar (HighMinDocFreq)&#39; | 100000        |   314.3 μs |  2.42 μs |  2.27 μs |  0.19 |    0.00 |   5.8594 |       - |   24.23 KB |        0.21 |
| &#39;LeanCorpus MLT Scalar (NoBoost)&#39;        | 100000        | 1,681.0 μs | 14.96 μs | 13.26 μs |  1.01 |    0.01 |  29.2969 |  3.9063 |  118.15 KB |        1.01 |
| &#39;LeanCorpus MLT WAND (DefaultParams)&#39;    | 100000        | 2,271.6 μs | 41.85 μs | 39.15 μs |  1.36 |    0.03 |  50.7813 | 15.6250 |   202.9 KB |        1.74 |
| LuceneNet_MoreLikeThis_DefaultParams     | 100000        | 2,829.0 μs | 12.03 μs | 11.25 μs |  1.69 |    0.02 | 429.6875 | 15.6250 | 1835.42 KB |       15.74 |
| LuceneNet_MoreLikeThis_HighMinDocFreq    | 100000        |   604.6 μs |  1.18 μs |  1.05 μs |  0.36 |    0.00 | 145.5078 |  2.9297 |  606.89 KB |        5.20 |
| LuceneNet_MoreLikeThis_NoBoost           | 100000        | 2,845.2 μs |  7.24 μs |  6.77 μs |  1.70 |    0.02 | 433.5938 | 11.7188 | 1835.61 KB |       15.74 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-mlt"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-mlt" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-mlt" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-mlt" style="max-width:960px"><canvas id="chart-bench-mlt" style="height:500px"></canvas></div>
<p><a href="debian-mlt.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


