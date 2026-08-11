---
title: Benchmarks - Term-vector highlighter
---

# Term-vector highlighter

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                 | DocumentCount | Mean        | Error    | StdDev   | Gen0      | Gen1    | Allocated  |
|--------------------------------------- |-------------- |------------:|---------:|---------:|----------:|--------:|-----------:|
| LeanCorpus_HybridHighlighter_NoOffsets | 100000        |   137.00 μs | 0.264 μs | 0.234 μs |   20.2637 |       - |   83.02 KB |
| LeanCorpus_Highlighter                 | 100000        |    99.63 μs | 0.184 μs | 0.172 μs |   15.2588 |       - |   62.66 KB |
| LuceneNet_Highlighter                  | 100000        |   129.04 μs | 0.284 μs | 0.252 μs |   56.3965 |       - |  230.47 KB |
| LeanCorpus_TermVectorHighlighter       | 100000        |   533.13 μs | 0.887 μs | 0.741 μs |   29.2969 |       - |  119.69 KB |
| LuceneNet_FastVectorHighlighter        | 100000        | 8,024.86 μs | 4.510 μs | 3.521 μs | 1101.5625 | 15.6250 | 4545.97 KB |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-tv-highlighter"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-tv-highlighter" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-tv-highlighter" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-tv-highlighter" style="max-width:960px"><canvas id="chart-bench-tv-highlighter" style="height:500px"></canvas></div>
<p><a href="tv-highlighter.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


