---
title: Benchmarks - Light English stemmer
---

# Light English stemmer

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                        | DocumentCount | Mean        | Error    | StdDev   | Ratio | RatioSD | Gen0          | Gen1      | Allocated     | Alloc Ratio |
|------------------------------ |-------------- |------------:|---------:|---------:|------:|--------:|--------------:|----------:|--------------:|------------:|
| LightEnglish_Stem             | 100000        |    308.4 ms |  1.25 ms |  1.11 ms |  1.00 |    0.00 |             - |         - |             - |          NA |
| Porter_Stem                   | 100000        |    419.3 ms |  0.78 ms |  0.69 ms |  1.36 |    0.01 |             - |         - |      186392 B |          NA |
| &#39;Lucene.NET PorterStemFilter&#39; | 100000        | 10,025.8 ms | 35.97 ms | 30.04 ms | 32.50 |    0.15 | 11048000.0000 | 1000.0000 | 46218094536 B |          NA |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-lightenglish"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-lightenglish" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-lightenglish" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-lightenglish" style="max-width:960px"><canvas id="chart-bench-lightenglish" style="height:500px"></canvas></div>
<p><a href="debian-lightenglish.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


