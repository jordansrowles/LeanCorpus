---
title: Benchmarks - Collapse and facet
---

# Collapse and facet

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                 | DocumentCount | Mean       | Error   | StdDev  | Ratio | RatioSD | Gen0    | Gen1    | Gen2   | Allocated | Alloc Ratio |
|--------------------------------------- |-------------- |-----------:|--------:|--------:|------:|--------:|--------:|--------:|-------:|----------:|------------:|
| LeanCorpus_BaseSearch                  | 100000        |   109.7 μs | 0.08 μs | 0.07 μs |  1.00 |    0.00 |  0.1221 |       - |      - |     704 B |        1.00 |
| LeanCorpus_SearchWithCollapse          | 100000        |   552.5 μs | 0.31 μs | 0.24 μs |  5.04 |    0.00 |       - |       - |      - |    4016 B |        5.70 |
| LeanCorpus_SearchWithFacets            | 100000        |   728.9 μs | 2.85 μs | 2.67 μs |  6.65 |    0.02 | 76.1719 | 15.6250 | 0.9766 |  418861 B |      594.97 |
| LeanCorpus_SearchWithCollapseAndFacets | 100000        | 1,297.2 μs | 1.03 μs | 0.86 μs | 11.83 |    0.01 | 76.1719 |       - |      - |  422872 B |      600.67 |
| LuceneNet_TermQuery                    | 100000        |   154.0 μs | 0.27 μs | 0.24 μs |  1.40 |    0.00 | 11.7188 |  0.2441 |      - |   50392 B |       71.58 |
| LuceneNet_SearchWithCollapse           | 100000        |   328.8 μs | 0.38 μs | 0.30 μs |  3.00 |    0.00 | 14.6484 |  0.4883 |      - |   61863 B |       87.87 |
| LuceneNet_SearchWithFacets             | 100000        |   414.9 μs | 0.36 μs | 0.30 μs |  3.78 |    0.00 | 17.5781 |  0.4883 |      - |   73894 B |      104.96 |
| LuceneNet_SearchWithCollapseAndFacets  | 100000        |   751.4 μs | 1.10 μs | 0.98 μs |  6.85 |    0.01 | 31.2500 |  0.9766 |      - |  135894 B |      193.03 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-collapse-facet"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-collapse-facet" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-collapse-facet" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-collapse-facet" style="max-width:960px"><canvas id="chart-bench-collapse-facet" style="height:500px"></canvas></div>
<p><a href="collapse-facet.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


