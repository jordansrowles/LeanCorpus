---
title: Benchmarks - Collapse and facet
---

# Collapse and facet

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `66ba120` &nbsp;&middot;&nbsp; 25 August 2026 10:39 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                 | DocumentCount | Mean       | Error   | StdDev  | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|--------------------------------------- |-------------- |-----------:|--------:|--------:|------:|--------:|--------:|-------:|----------:|------------:|
| LeanCorpus_BaseSearch                  | 100000        |   115.7 μs | 0.10 μs | 0.09 μs |  1.00 |    0.00 |  0.8545 |      - |   3.58 KB |        1.00 |
| LeanCorpus_SearchWithCollapse          | 100000        |   543.9 μs | 1.00 μs | 0.89 μs |  4.70 |    0.01 |  0.9766 |      - |   6.81 KB |        1.90 |
| LeanCorpus_SearchWithFacets            | 100000        |   597.1 μs | 1.78 μs | 1.67 μs |  5.16 |    0.01 | 39.0625 |      - | 159.71 KB |       44.64 |
| LeanCorpus_SearchWithCollapseAndFacets | 100000        | 1,193.2 μs | 2.54 μs | 2.38 μs | 10.31 |    0.02 | 39.0625 |      - | 166.52 KB |       46.54 |
| LuceneNet_TermQuery                    | 100000        |   160.7 μs | 0.20 μs | 0.17 μs |  1.39 |    0.00 | 11.7188 | 0.2441 |  49.21 KB |       13.75 |
| LuceneNet_SearchWithCollapse           | 100000        |   335.6 μs | 0.35 μs | 0.31 μs |  2.90 |    0.00 | 14.6484 | 0.4883 |  60.41 KB |       16.88 |
| LuceneNet_SearchWithFacets             | 100000        |   403.0 μs | 0.81 μs | 0.72 μs |  3.48 |    0.01 | 17.5781 | 0.4883 |  72.16 KB |       20.17 |
| LuceneNet_SearchWithCollapseAndFacets  | 100000        |   753.6 μs | 1.60 μs | 1.42 μs |  6.51 |    0.01 | 31.2500 | 0.9766 | 132.71 KB |       37.09 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-collapse-facet"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-collapse-facet" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-collapse-facet" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-collapse-facet" style="max-width:960px"><canvas id="chart-bench-collapse-facet" style="height:500px"></canvas></div>
<p><a href="collapse-facet.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


