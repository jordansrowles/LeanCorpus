---
title: Benchmarks - Searcher manager
---

# Searcher manager

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                   | DocumentCount | Mean     | Error    | StdDev   | Ratio | Gen0   | Allocated | Alloc Ratio |
|----------------------------------------- |-------------- |---------:|---------:|---------:|------:|-------:|----------:|------------:|
| LeanCorpus_SearcherManager_AcquireSearch | 100000        | 87.32 μs | 0.142 μs | 0.133 μs |  1.00 | 0.1221 |     822 B |        1.00 |
| LeanCorpus_SearcherManager_AcquireLease  | 100000        | 86.83 μs | 0.197 μs | 0.184 μs |  0.99 | 0.1221 |     784 B |        0.95 |
| LuceneNet_SearcherManager_AcquireSearch  | 100000        | 89.84 μs | 0.199 μs | 0.186 μs |  1.03 | 6.2256 |   26534 B |       32.28 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-searcher-mgr"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-searcher-mgr" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-searcher-mgr" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-searcher-mgr" style="max-width:960px"><canvas id="chart-bench-searcher-mgr" style="height:500px"></canvas></div>
<p><a href="debian-searcher-mgr.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


