---
title: Benchmarks - mlt-single-segment
---

# mlt-single-segment

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                     | DocumentCount | Mean     | Error     | StdDev    | Ratio | Gen0     | Gen1   | Allocated | Alloc Ratio |
|--------------------------- |-------------- |---------:|----------:|----------:|------:|---------:|-------:|----------:|------------:|
| &#39;LC MLT SingleSeg Scalar&#39;  | 100000        | 3.950 ms | 0.0100 ms | 0.0093 ms |  1.00 |        - |      - |  13.61 KB |        1.00 |
| &#39;Lucene.NET MLT SingleSeg&#39; | 100000        | 2.312 ms | 0.0082 ms | 0.0072 ms |  0.59 | 183.5938 | 7.8125 | 789.98 KB |       58.05 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-mlt-single-segment"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-mlt-single-segment" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-mlt-single-segment" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-mlt-single-segment" style="max-width:960px"><canvas id="chart-bench-mlt-single-segment" style="height:500px"></canvas></div>
<p><a href="debian-mlt-single-segment.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


