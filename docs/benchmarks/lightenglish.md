---
title: Benchmarks - Light English stemmer
---

# Light English stemmer

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                        | DocumentCount | Mean        | Error    | StdDev   | Ratio | RatioSD | Gen0          | Gen1      | Allocated      | Alloc Ratio |
|------------------------------ |-------------- |------------:|---------:|---------:|------:|--------:|--------------:|----------:|---------------:|------------:|
| LightEnglish_Stem             | 100000        |    895.8 ms |  4.32 ms |  3.83 ms |  1.00 |    0.00 |             - |         - |              - |          NA |
| Porter_Stem                   | 100000        |  1,063.4 ms |  0.49 ms |  0.41 ms |  1.19 |    0.00 |             - |         - |              - |          NA |
| &#39;Lucene.NET PorterStemFilter&#39; | 100000        | 32,503.5 ms | 87.03 ms | 77.15 ms | 36.28 |    0.17 | 34081000.0000 | 3000.0000 | 142556150184 B |          NA |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-lightenglish"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-lightenglish" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-lightenglish" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-lightenglish" style="max-width:960px"><canvas id="chart-bench-lightenglish" style="height:500px"></canvas></div>
<p><a href="lightenglish.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


