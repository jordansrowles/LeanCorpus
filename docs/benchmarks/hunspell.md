---
title: Benchmarks - Hunspell
---

# Hunspell

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                          | Mean         | Error       | StdDev      | Gen0    | Gen1    | Allocated |
|-------------------------------- |-------------:|------------:|------------:|--------:|--------:|----------:|
| Parse_Dictionary                |     295.0 ns |     0.49 ns |     0.46 ns |  0.0420 |       - |     176 B |
| Stem_Words                      |     100.9 ns |     0.10 ns |     0.10 ns |       - |       - |         - |
| &#39;Lucene.NET HunspellStemFilter&#39; | 584,429.2 ns | 1,219.11 ns | 1,140.36 ns | 69.3359 | 13.6719 |  291376 B |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-hunspell"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-hunspell" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-hunspell" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-hunspell" style="max-width:960px"><canvas id="chart-bench-hunspell" style="height:500px"></canvas></div>
<p><a href="hunspell.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


