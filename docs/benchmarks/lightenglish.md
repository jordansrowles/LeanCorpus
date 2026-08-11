---
title: Benchmarks - Light English stemmer
---

# Light English stemmer

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method            | DocumentCount | Mean       | Error   | StdDev  | Ratio | Allocated | Alloc Ratio |
|------------------ |-------------- |-----------:|--------:|--------:|------:|----------:|------------:|
| LightEnglish_Stem | 100000        |   994.2 ms | 1.84 ms | 1.72 ms |  1.00 |         - |          NA |
| Porter_Stem       | 100000        | 1,051.5 ms | 1.26 ms | 1.05 ms |  1.06 |         - |          NA |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-lightenglish"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-lightenglish" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-lightenglish" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-lightenglish" style="max-width:960px"><canvas id="chart-bench-lightenglish" style="height:500px"></canvas></div>
<p><a href="lightenglish.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


