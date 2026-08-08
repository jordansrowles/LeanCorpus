---
title: Benchmarks - Collapse and facet
---

# Collapse and facet

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                 | DocumentCount | Mean       | Error   | StdDev  | Ratio | RatioSD | Gen0    | Gen1    | Gen2   | Allocated | Alloc Ratio |
|--------------------------------------- |-------------- |-----------:|--------:|--------:|------:|--------:|--------:|--------:|-------:|----------:|------------:|
| LeanCorpus_BaseSearch                  | 100000        |   107.0 μs | 0.13 μs | 0.12 μs |  1.00 |    0.00 |  0.1221 |       - |      - |     776 B |        1.00 |
| LeanCorpus_SearchWithCollapse          | 100000        |   585.7 μs | 0.94 μs | 0.88 μs |  5.48 |    0.01 |  0.9766 |       - |      - |    4096 B |        5.28 |
| LeanCorpus_SearchWithFacets            | 100000        |   739.0 μs | 1.41 μs | 1.10 μs |  6.91 |    0.01 | 76.1719 | 15.6250 | 0.9766 |  418939 B |      539.87 |
| LeanCorpus_SearchWithCollapseAndFacets | 100000        | 1,652.8 μs | 2.18 μs | 1.94 μs | 15.45 |    0.02 | 76.1719 |  7.8125 |      - |  423032 B |      545.14 |
| LuceneNet_TermQuery                    | 100000        |   159.1 μs | 0.23 μs | 0.22 μs |  1.49 |    0.00 | 11.7188 |  0.2441 |      - |   50392 B |       64.94 |
| LuceneNet_SearchWithCollapse           | 100000        |   329.5 μs | 0.70 μs | 0.66 μs |  3.08 |    0.01 | 14.6484 |  0.4883 |      - |   61863 B |       79.72 |
| LuceneNet_SearchWithFacets             | 100000        |   401.3 μs | 0.24 μs | 0.20 μs |  3.75 |    0.00 | 17.5781 |  0.4883 |      - |   73894 B |       95.22 |
| LuceneNet_SearchWithCollapseAndFacets  | 100000        |   753.4 μs | 1.31 μs | 1.16 μs |  7.04 |    0.01 | 31.2500 |  0.9766 |      - |  135899 B |      175.13 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-collapse-facet"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-collapse-facet" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-collapse-facet" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-collapse-facet" style="max-width:960px"><canvas id="chart-bench-collapse-facet" style="height:500px"></canvas></div>
<p><a href="debian-collapse-facet.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


