---
title: Benchmarks - Searcher manager
---

# Searcher manager

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                 | DocumentCount | Mean          | Error      | StdDev     | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|--------------------------------------- |-------------- |--------------:|-----------:|-----------:|------:|--------:|-------:|----------:|------------:|
| &#39;LeanCorpus acquire, search, release&#39;  | 100000        | 109,881.78 ns |  73.779 ns |  69.013 ns | 1.000 |  0.1221 |      - |     857 B |        1.00 |
| &#39;LeanCorpus lease, search, release&#39;    | 100000        | 110,979.34 ns |  59.136 ns |  46.169 ns | 1.010 |  0.2441 |      - |    1041 B |        1.21 |
| &#39;Lucene.NET acquire, search, release&#39;  | 100000        | 147,885.78 ns | 472.237 ns | 394.339 ns | 1.346 | 11.9629 | 0.2441 |   51112 B |       59.64 |
| &#39;LeanCorpus acquire and release&#39;       | 100000        |      32.51 ns |   0.028 ns |   0.023 ns | 0.000 |       - |      - |         - |        0.00 |
| &#39;LeanCorpus lease acquire and release&#39; | 100000        |     196.84 ns |   0.368 ns |   0.326 ns | 0.002 |  0.0439 |      - |     184 B |        0.21 |
| &#39;Lucene.NET acquire and release&#39;       | 100000        |      30.17 ns |   0.021 ns |   0.019 ns | 0.000 |       - |      - |         - |        0.00 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-searcher-mgr"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-searcher-mgr" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-searcher-mgr" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-searcher-mgr" style="max-width:960px"><canvas id="chart-bench-searcher-mgr" style="height:500px"></canvas></div>
<p><a href="searcher-mgr.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


