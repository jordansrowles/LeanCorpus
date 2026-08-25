---
title: Benchmarks - mlt-single-segment
---

# mlt-single-segment

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `66ba120` &nbsp;&middot;&nbsp; 24 August 2026 21:47 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                     | DocumentCount | Mean     | Error     | StdDev    | Ratio | Gen0     | Gen1   | Allocated | Alloc Ratio |
|--------------------------- |-------------- |---------:|----------:|----------:|------:|---------:|-------:|----------:|------------:|
| &#39;LC MLT SingleSeg Scalar&#39;  | 100000        | 4.037 ms | 0.0108 ms | 0.0101 ms |  1.00 |        - |      - |  17.82 KB |        1.00 |
| &#39;Lucene.NET MLT SingleSeg&#39; | 100000        | 2.362 ms | 0.0090 ms | 0.0084 ms |  0.59 | 183.5938 | 7.8125 | 789.98 KB |       44.33 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-mlt-single-segment"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-mlt-single-segment" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-mlt-single-segment" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-mlt-single-segment" style="max-width:960px"><canvas id="chart-bench-mlt-single-segment" style="height:500px"></canvas></div>
<p><a href="mlt-single-segment.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


