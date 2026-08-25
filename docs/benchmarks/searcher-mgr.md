---
title: Benchmarks - Searcher manager
---

# Searcher manager

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `66ba120` &nbsp;&middot;&nbsp; 24 August 2026 21:47 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                 | DocumentCount | Mean          | Error      | StdDev     | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|--------------------------------------- |-------------- |--------------:|-----------:|-----------:|------:|--------:|-------:|----------:|------------:|
| &#39;LeanCorpus acquire, search, release&#39;  | 100000        | 116,184.41 ns | 132.665 ns | 117.604 ns | 1.000 |  0.8545 |      - |    3884 B |        1.00 |
| &#39;LeanCorpus lease, search, release&#39;    | 100000        | 117,471.04 ns | 202.182 ns | 189.122 ns | 1.011 |  0.8545 |      - |    4070 B |        1.05 |
| &#39;Lucene.NET acquire, search, release&#39;  | 100000        | 149,510.65 ns | 554.870 ns | 519.026 ns | 1.287 | 11.9629 | 0.2441 |   51112 B |       13.16 |
| &#39;LeanCorpus acquire and release&#39;       | 100000        |      32.72 ns |   0.069 ns |   0.065 ns | 0.000 |       - |      - |         - |        0.00 |
| &#39;LeanCorpus lease acquire and release&#39; | 100000        |     194.61 ns |   0.610 ns |   0.571 ns | 0.002 |  0.0439 |      - |     184 B |        0.05 |
| &#39;Lucene.NET acquire and release&#39;       | 100000        |      30.36 ns |   0.055 ns |   0.049 ns | 0.000 |       - |      - |         - |        0.00 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-searcher-mgr"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-searcher-mgr" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-searcher-mgr" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-searcher-mgr" style="max-width:960px"><canvas id="chart-bench-searcher-mgr" style="height:500px"></canvas></div>
<p><a href="searcher-mgr.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


