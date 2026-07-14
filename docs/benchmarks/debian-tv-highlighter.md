---
title: Benchmarks - Term-vector highlighter
---

# Term-vector highlighter

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                 | DocumentCount | Mean        | Error    | StdDev   | Gen0     | Gen1   | Allocated  |
|--------------------------------------- |-------------- |------------:|---------:|---------:|---------:|-------:|-----------:|
| LeanCorpus_HybridHighlighter_NoOffsets | 100000        |   114.04 μs | 0.116 μs | 0.103 μs |  19.5313 |      - |   80.04 KB |
| LeanCorpus_Highlighter                 | 100000        |    76.94 μs | 0.131 μs | 0.122 μs |  14.5264 |      - |   59.68 KB |
| LuceneNet_Highlighter                  | 100000        |   127.12 μs | 0.585 μs | 0.519 μs |  56.3965 |      - |  230.47 KB |
| LeanCorpus_TermVectorHighlighter       | 100000        |   425.40 μs | 0.753 μs | 0.704 μs |  28.3203 |      - |  116.71 KB |
| LuceneNet_FastVectorHighlighter        | 100000        | 6,245.68 μs | 5.691 μs | 5.045 μs | 695.3125 | 7.8125 | 2870.16 KB |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-tv-highlighter"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-tv-highlighter" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-tv-highlighter" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-tv-highlighter" style="max-width:960px"><canvas id="chart-bench-tv-highlighter" style="height:500px"></canvas></div>
<p><a href="debian-tv-highlighter.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


