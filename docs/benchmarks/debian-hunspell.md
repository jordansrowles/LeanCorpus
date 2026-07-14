---
title: Benchmarks - Hunspell
---

# Hunspell

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                          | Mean         | Error       | StdDev    | Gen0    | Gen1    | Allocated |
|-------------------------------- |-------------:|------------:|----------:|--------:|--------:|----------:|
| Parse_Dictionary                |     294.2 ns |     0.18 ns |   0.17 ns |  0.0420 |       - |     176 B |
| Stem_Words                      |     103.8 ns |     0.11 ns |   0.10 ns |       - |       - |         - |
| &#39;Lucene.NET HunspellStemFilter&#39; | 574,702.1 ns | 1,018.08 ns | 902.50 ns | 69.3359 | 13.6719 |  291379 B |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-hunspell"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-hunspell" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-hunspell" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-hunspell" style="max-width:960px"><canvas id="chart-bench-hunspell" style="height:500px"></canvas></div>
<p><a href="debian-hunspell.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


