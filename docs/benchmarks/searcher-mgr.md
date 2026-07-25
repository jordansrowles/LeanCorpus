---
title: Benchmarks - Searcher manager
---

# Searcher manager

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                 | DocumentCount | Mean          | Error      | StdDev     | Median        | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|--------------------------------------- |-------------- |--------------:|-----------:|-----------:|--------------:|------:|--------:|-------:|----------:|------------:|
| &#39;LeanCorpus acquire, search, release&#39;  | 100000        | 103,587.79 ns | 107.878 ns | 100.909 ns | 103,558.20 ns | 1.000 |  0.1221 |      - |     929 B |        1.00 |
| &#39;LeanCorpus lease, search, release&#39;    | 100000        | 103,695.07 ns |  40.044 ns |  31.264 ns | 103,692.05 ns | 1.001 |  0.1221 |      - |     993 B |        1.07 |
| &#39;Lucene.NET acquire, search, release&#39;  | 100000        | 147,947.02 ns | 414.786 ns | 387.992 ns | 147,949.78 ns | 1.428 | 11.9629 | 0.2441 |   51217 B |       55.13 |
| &#39;LeanCorpus acquire and release&#39;       | 100000        |      24.45 ns |   0.013 ns |   0.031 ns |      24.44 ns | 0.000 |       - |      - |         - |        0.00 |
| &#39;LeanCorpus lease acquire and release&#39; | 100000        |      29.74 ns |   0.081 ns |   0.076 ns |      29.76 ns | 0.000 |  0.0153 |      - |      64 B |        0.07 |
| &#39;Lucene.NET acquire and release&#39;       | 100000        |      30.13 ns |   0.020 ns |   0.016 ns |      30.14 ns | 0.000 |       - |      - |         - |        0.00 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-searcher-mgr"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-searcher-mgr" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-searcher-mgr" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-searcher-mgr" style="max-width:960px"><canvas id="chart-bench-searcher-mgr" style="height:500px"></canvas></div>
<p><a href="searcher-mgr.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


