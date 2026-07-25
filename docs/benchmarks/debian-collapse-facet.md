---
title: Benchmarks - Collapse and facet
---

# Collapse and facet

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `f3305a5` &nbsp;&middot;&nbsp; 16 July 2026 20:51 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                 | DocumentCount | Mean        | Error     | StdDev   | Ratio  | RatioSD | Gen0      | Gen1     | Gen2   | Allocated | Alloc Ratio |
|--------------------------------------- |-------------- |------------:|----------:|---------:|-------:|--------:|----------:|---------:|-------:|----------:|------------:|
| LeanCorpus_BaseSearch                  | 100000        |    157.7 μs |   0.19 μs |  0.17 μs |   1.00 |    0.00 |         - |        - |      - |     616 B |        1.00 |
| LeanCorpus_SearchWithCollapse          | 100000        |    597.8 μs |   0.36 μs |  0.28 μs |   3.79 |    0.00 |         - |        - |      - |    3936 B |        6.39 |
| LeanCorpus_SearchWithFacets            | 100000        |    875.1 μs |   3.45 μs |  3.22 μs |   5.55 |    0.02 |   89.8438 |  15.6250 | 0.9766 |  473627 B |      768.88 |
| LeanCorpus_SearchWithCollapseAndFacets | 100000        |  1,509.0 μs |   2.11 μs |  1.87 μs |   9.57 |    0.01 |   87.8906 |  11.7188 |      - |  477560 B |      775.26 |
| LuceneNet_TermQuery                    | 100000        |    166.1 μs |   0.22 μs |  0.19 μs |   1.05 |    0.00 |    7.3242 |   0.2441 |      - |   31299 B |       50.81 |
| LuceneNet_SearchWithCollapse           | 100000        | 27,283.8 μs | 107.47 μs | 95.27 μs | 173.05 |    0.61 | 1000.0000 | 750.0000 |      - | 6643364 B |   10,784.68 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-collapse-facet"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-collapse-facet" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-collapse-facet" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-collapse-facet" style="max-width:960px"><canvas id="chart-bench-collapse-facet" style="height:500px"></canvas></div>
<p><a href="debian-collapse-facet.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


